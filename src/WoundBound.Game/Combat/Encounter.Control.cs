using WoundBound.Rules.Calculation;
using WoundBound.Rules.Enums;
using WoundBound.Rules.Models;

namespace WoundBound.Game.Combat
{
    /// <summary>A Control spell holding somebody: by whom, what it makes them do, and for how much longer.</summary>
    public sealed record ControlInPlay(Combatant Caster, Combatant Target, string Spell, ControlEffect Effect, Combatant? Directed, int RoundsLeft);

    /// <summary>
    /// Control spells (Core Rules §11, "Control Spells"): rolled against Resolve, and on a success
    /// Fear, Confusion or Compulsion — standing effects that take over the turns of whoever they
    /// hold, last by Tier, and are rechecked for free at the end of each of the caster's turns.
    /// </summary>
    public sealed partial class Encounter
    {
        private sealed class ControlHold
        {
            public required Combatant Caster { get; init; }
            public required Combatant Target { get; init; }
            public required SpellDefinition Spell { get; init; }
            public required ControlEffect Effect { get; init; }
            public Combatant? Directed { get; init; }
            public required int CastBonus { get; init; }
            public int RoundsLeft { get; set; }
        }

        private readonly List<ControlHold> controls = new();

        /// <summary>Every Control spell holding somebody.</summary>
        public IReadOnlyList<ControlInPlay> ControlsInPlay => controls
            .Select(c => new ControlInPlay(c.Caster, c.Target, c.Spell.Name, c.Effect, c.Directed, c.RoundsLeft))
            .ToList();

        /// <summary>Whether a Control spell is holding <paramref name="combatant"/>.</summary>
        public bool IsControlled(Combatant combatant) => controls.Any(c => c.Target == combatant);

        /// <summary>Whether <paramref name="caster"/> has a Control spell active — they may only have one (Core Rules §11).</summary>
        public bool HasControlActive(Combatant caster) => controls.Any(c => c.Caster == caster);

        private static CharacterCondition ConditionFor(ControlEffect effect) => effect switch
        {
            ControlEffect.Fear => CharacterCondition.Frightened,
            ControlEffect.Confusion => CharacterCondition.Confused,
            _ => CharacterCondition.Compelled,
        };

        /// <summary>
        /// A Control spell: one roll for each creature it catches, against their Resolve Threshold,
        /// which they may raise by 2 for Stamina after seeing it. A Solo has Advantage on its Resolve
        /// check, so the cast rolls at Disadvantage against one (GM Reference §3, "Solo Resilience").
        /// </summary>
        private void CastControl(Combatant caster, SpellCast cast, CastPlan plan, Action then)
        {
            var spell = cast.Spell;
            var (helped, harried) = TakeAid(caster);
            var targets = new Queue<Combatant>(plan.Targets);

            void Next()
            {
                if (!targets.TryDequeue(out var target))
                {
                    then();
                    return;
                }

                if (!target.OccupiesSquare || target.ResolveThreshold is not { } threshold)
                {
                    Next();
                    return;
                }

                var roll = RollAgainst(plan.CastBonus, threshold, cast.Sacrifice || helped || target.IsDefenceless,
                    AttacksAtDisadvantage(caster) || harried || target.Role == AdversaryRole.Solo, caster);

                void Resolve(bool braced)
                {
                    var final = braced ? Reread(roll, threshold + ThresholdModifierCalculator.ReactionBonus) : roll;
                    Witness(target, ThresholdType.Resolve, final);
                    var line = $"{spell.Name} at {target.Name}: {DescribeRoll(final)} vs RT {final.Threshold}" + (braced ? " (+2 steeling themselves)" : string.Empty);
                    if (final.Success != true)
                    {
                        Write(line + " — their will holds.");
                        Next();
                        return;
                    }

                    var hold = new ControlHold
                    {
                        Caster = caster,
                        Target = target,
                        Spell = spell,
                        Effect = cast.Effect,
                        Directed = cast.Effect == ControlEffect.Compulsion ? cast.Directed : null,
                        CastBonus = plan.CastBonus,
                        RoundsLeft = SpellCalculator.ControlIllusionDurationRounds(spell.SpellTier),
                    };
                    controls.Add(hold);
                    target.SetCondition(ConditionFor(cast.Effect), true);
                    Write(line + " — " + cast.Effect switch
                    {
                        ControlEffect.Fear => $"{target.Name} is Frightened of {caster.Name}.",
                        ControlEffect.Confusion => $"{target.Name} is Confused.",
                        _ => $"{target.Name} is Compelled: " + (hold.Directed is { } who ? $"\"attack {who.Name}\"." : "\"drop your weapon and kneel\"."),
                    } + $" It holds for up to {hold.RoundsLeft} rounds.");
                    Next();
                }

                if (!target.IsOutOfFight && BoostWouldTurn(roll, threshold))
                {
                    OfferReaction(new PendingReaction(ReactionKind.BraceResolve, target, caster, roll, threshold,
                        $"{caster.Name}'s {spell.Name} rolls {roll.Total} against {target.Name}'s RT {threshold}. Steeling themselves (1 Stamina, +2 RT) would shrug it off."), Resolve);
                    return;
                }

                Resolve(false);
            }

            Next();
        }

        private void EndControl(ControlHold hold, string? line)
        {
            controls.Remove(hold);
            var condition = ConditionFor(hold.Effect);
            if (!controls.Any(c => c.Target == hold.Target && ConditionFor(c.Effect) == condition))
            {
                hold.Target.SetCondition(condition, false);
            }

            if (line is not null)
            {
                Write(line);
            }
        }

        /// <summary>
        /// The end of <paramref name="caster"/>'s turn: each Control spell of theirs rechecked for
        /// free — the cast total again against the target's Resolve as it stands now, which the
        /// target may raise for Stamina. A failed recheck ends it there (Core Rules §11).
        /// </summary>
        private void RecheckControls(Combatant caster, Action then)
        {
            var due = new Queue<ControlHold>(controls.Where(c => c.Caster == caster));

            void Next()
            {
                if (!due.TryDequeue(out var hold))
                {
                    then();
                    return;
                }

                var target = hold.Target;
                if (!controls.Contains(hold) || target.ResolveThreshold is not { } threshold)
                {
                    Next();
                    return;
                }

                var roll = RollAgainst(hold.CastBonus, threshold, advantage: false, disadvantage: target.Role == AdversaryRole.Solo);

                void Resolve(bool braced)
                {
                    var final = braced ? Reread(roll, threshold + ThresholdModifierCalculator.ReactionBonus) : roll;
                    Witness(target, ThresholdType.Resolve, final);
                    var line = $"{hold.Spell.Name} rechecked against {target.Name}: {DescribeRoll(final)} vs RT {final.Threshold}";
                    if (final.Success == true)
                    {
                        Write(line + " — it holds.");
                    }
                    else
                    {
                        EndControl(hold, line + $" — {target.Name} throws it off.");
                    }

                    Next();
                }

                if (!target.IsOutOfFight && BoostWouldTurn(roll, threshold))
                {
                    OfferReaction(new PendingReaction(ReactionKind.BraceResolve, target, caster, roll, threshold,
                        $"{caster.Name}'s {hold.Spell.Name} rechecks at {roll.Total} against {target.Name}'s RT {threshold}. Steeling themselves (1 Stamina, +2 RT) would throw it off."), Resolve);
                    return;
                }

                Resolve(false);
            }

            Next();
        }

        /// <summary>A round gone by on every Control spell: those that have run their Tier's length lapse.</summary>
        private void AgeControls()
        {
            foreach (var hold in controls.ToList())
            {
                hold.RoundsLeft--;
                if (hold.RoundsLeft <= 0)
                {
                    EndControl(hold, $"{hold.Spell.Name} on {hold.Target.Name} runs its course.");
                }
            }
        }

        /// <summary>A Control spell is anchored to its caster: it ends at once if they fall unconscious, start dying, or leave.</summary>
        private void EndControlsOfFallenCasters()
        {
            foreach (var hold in controls.Where(c => c.Caster.IsUnconscious || c.Caster.HasFled || c.Caster.Body <= 0 || c.Target.HasFled || (c.Target.IsOutOfFight && !c.Target.OccupiesSquare)).ToList())
            {
                EndControl(hold, hold.Target.HasFled || !hold.Target.OccupiesSquare ? null : $"{hold.Spell.Name} on {hold.Target.Name} ends with {hold.Caster.Name} out of the fight.");
            }
        }

        /// <summary>
        /// The start of a held creature's turn, once it is set up: the Control spell takes it over —
        /// a Compulsion first, then a Confusion, then a Fear. <paramref name="resume"/> hands what is
        /// left of the turn back to whoever plays the creature; <paramref name="endTurn"/> ends it.
        /// </summary>
        private void TakeControlledTurn(Combatant creature, Action resume, Action endTurn)
        {
            var hold = controls.Where(c => c.Target == creature).OrderBy(c => c.Effect switch
            {
                ControlEffect.Compulsion => 0,
                ControlEffect.Confusion => 1,
                _ => 2,
            }).FirstOrDefault();
            if (hold is null || creature.IsOutOfFight)
            {
                resume();
                return;
            }

            switch (hold.Effect)
            {
                case ControlEffect.Compulsion:
                    Compelled(creature, hold, endTurn);
                    return;
                case ControlEffect.Confusion:
                    Confused(creature, resume, endTurn);
                    return;
                default:
                    Frightened(creature, hold, resume, endTurn);
                    return;
            }
        }

        /// <summary>
        /// Frightened (Core Rules §9, "Conditions"): end the turn at least a band further from the
        /// source than it began — its own route, at what the move costs, drawing the blade on the
        /// way out. At Long it leaves the Challenge. Cornered, it gets as far as it can, and the
        /// turn is spent. Where the source can't be seen at all, the fear ends.
        /// </summary>
        private void Frightened(Combatant creature, ControlHold hold, Action resume, Action endTurn)
        {
            var source = hold.Caster;
            var distance = creature.DistanceTo(source);
            if (environment is { } tables && RangeBandCalculator.BandForSquares(bands, distance) is { } seenBand
                && !EnvironmentCalculator.AllowsTargeting(tables, VisibilityOf(source), seenBand))
            {
                EndControl(hold, $"{creature.Name} can't see {source.Name} any more in the dark, and the fear goes out of it.");
                resume();
                return;
            }

            var band = RangeBandCalculator.BandForSquares(bands, distance);
            if (band is null or DistanceBand.Long)
            {
                creature.HasFled = true;
                Release(creature, "lets go to run");
                EndControl(hold, null);
                Write($"{creature.Name}, Frightened at Long, flees the scene and takes no further part.");
                endTurn();
                return;
            }

            var squares = CanMoveItself(creature) ? SquaresWithin(creature, MovesAvailable(creature)) : new Dictionary<GridPosition, int>();
            var further = squares
                .Where(o => RangeBandCalculator.BandForSquares(bands, creature.DistanceFrom(o.Key, source)) is not { } b || b > band)
                .OrderBy(o => o.Value).ThenByDescending(o => creature.DistanceFrom(o.Key, source))
                .Select(o => (Square: (GridPosition?)o.Key, Cost: o.Value))
                .FirstOrDefault();

            if (further.Square is { } away)
            {
                Write($"{creature.Name} is Frightened of {source.Name}, and gives ground.");
                Move(creature, away, further.Cost, () =>
                {
                    creature.HasWithdrawnThisTurn = true;
                    if (creature.IsOutOfFight)
                    {
                        endTurn();
                        return;
                    }

                    resume();
                });
                return;
            }

            // Cornered: as far from the source as it can get, and the turn is spent regardless.
            var best = squares.Where(o => creature.DistanceFrom(o.Key, source) > distance)
                .OrderByDescending(o => creature.DistanceFrom(o.Key, source)).ThenBy(o => o.Value)
                .Select(o => (Square: (GridPosition?)o.Key, Cost: o.Value))
                .FirstOrDefault();
            Write($"{creature.Name} is Frightened of {source.Name}, and has nowhere to give ground to.");
            if (best.Square is { } edge)
            {
                Move(creature, edge, best.Cost, endTurn);
                return;
            }

            endTurn();
        }

        /// <summary>
        /// Confused: a creature within reach is chosen at random — allies count — and its action goes
        /// on attacking it. Nothing in reach, and the turn is wasted (Core Rules §11).
        /// </summary>
        private void Confused(Combatant creature, Action resume, Action endTurn)
        {
            var inReach = combatants
                .Where(o => o != creature && o.OccupiesSquare && (!o.IsOutOfFight || o.IsDefenceless) && creature.CanReach(o))
                .ToList();
            if (inReach.Count == 0 || creature.ActionsRemaining < 1)
            {
                Write($"{creature.Name} is Confused, with nobody in reach — the turn is wasted.");
                endTurn();
                return;
            }

            var victim = inReach[dice.Roll(inReach.Count) - 1];
            var plan = PlanAttackWith(creature, creature.MeleeAttack, creature.Position, victim);
            if (!plan.CanAttack)
            {
                Write($"{creature.Name} is Confused, and turns on {victim.Name}, but can't get a blow in — the turn is wasted.");
                endTurn();
                return;
            }

            Write($"{creature.Name} is Confused, and turns on {victim.Name}.");
            ResolveAttack(creature, victim, asReaction: false, opportunityBonus: false, _ =>
            {
                if (CheckForEnd() || creature.IsOutOfFight)
                {
                    endTurn();
                    return;
                }

                resume();
            }, creature.MeleeAttack);
        }

        /// <summary>
        /// Compelled: the directive, carried out again on each of its turns — "drop your weapon and
        /// kneel" takes the whole turn; "attack that guard" closes on them if it must, and strikes.
        /// </summary>
        private void Compelled(Combatant creature, ControlHold hold, Action endTurn)
        {
            if (hold.Directed is not { } quarry)
            {
                Write($"{creature.Name} is Compelled, and kneels, weapon dropped.");
                endTurn();
                return;
            }

            if (!quarry.OccupiesSquare || quarry.HasFled)
            {
                Write($"{creature.Name} is Compelled to attack {quarry.Name}, who is gone — the turn is wasted.");
                endTurn();
                return;
            }

            void Strike()
            {
                var plan = PlanAttack(creature, quarry);
                if (creature.ActionsRemaining < 1 || !plan.CanAttack)
                {
                    Write($"{creature.Name} is Compelled to attack {quarry.Name}, and can't reach them.");
                    endTurn();
                    return;
                }

                Write($"{creature.Name} is Compelled, and goes for {quarry.Name}.");
                ResolveAttack(creature, quarry, asReaction: false, opportunityBonus: false, _ => endTurn());
            }

            if (PlanAttack(creature, quarry).CanAttack)
            {
                Strike();
                return;
            }

            if (CanMoveItself(creature) && view.StrikeSquare(creature, quarry, 1) is { } square && MoveCostTo(creature, square) is { } cost)
            {
                Move(creature, square, cost, () =>
                {
                    if (creature.IsOutOfFight)
                    {
                        endTurn();
                        return;
                    }

                    Strike();
                });
                return;
            }

            Write($"{creature.Name} is Compelled to attack {quarry.Name}, and can't reach them.");
            endTurn();
        }
    }
}
