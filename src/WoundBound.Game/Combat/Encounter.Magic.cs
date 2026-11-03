using WoundBound.Game.Combat.Brains;
using WoundBound.Rules.Calculation;
using WoundBound.Rules.Catalog;
using WoundBound.Rules.Enums;
using WoundBound.Rules.Models;

namespace WoundBound.Game.Combat
{
    /// <summary>
    /// A spell still doing something to somebody: a Buff or Bane on a Threshold, or a Bind or a
    /// Quicken on their pace. Each lasts until the start of its caster's next turn, unless declared
    /// Sustained and paid for (Core Rules §11, "Duration and the Caster", "Sustaining a Spell").
    /// </summary>
    public sealed record SpellInPlay(Combatant Caster, Combatant Target, string Spell, SpellType Type, bool Sustained,
        ThresholdType? Threshold, int Amount, CharacterCondition? Condition)
    {
        /// <summary>A willing target accepts a held spell, and isn't rolled against while it does.</summary>
        public bool Accepts => Target.Side == Caster.Side;
    }

    public sealed partial class Encounter
    {
        private sealed class SpellEffect
        {
            public required Combatant Caster { get; init; }
            public required Combatant Target { get; init; }
            public required SpellDefinition Spell { get; init; }
            public ThresholdModifier? Modifier { get; init; }
            public CharacterCondition? Condition { get; init; }
            public bool Sustained { get; init; }
            public int CastBonus { get; init; }
            public bool Accepts => Target.Side == Caster.Side;
        }

        private readonly List<SpellEffect> spellEffects = new();

        /// <summary>Every spell still at work on the board, by whom and on whom.</summary>
        public IReadOnlyList<SpellInPlay> SpellsInPlay => spellEffects
            .Select(e => new SpellInPlay(e.Caster, e.Target, e.Spell.Name, e.Spell.SpellType, e.Sustained,
                e.Modifier?.Threshold, e.Modifier?.Amount ?? 0, e.Condition))
            .ToList();

        /// <summary>What <paramref name="caster"/> casting <paramref name="cast"/> from where they stand would come to, or why it can't be cast.</summary>
        public CastPlan PlanCast(Combatant caster, SpellCast cast) => PlanCastFrom(caster, caster.Position, cast, checkTurn: true);

        /// <summary>
        /// As <see cref="PlanCast"/>, as though <paramref name="caster"/> stood on <paramref name="from"/>.
        /// Leaving out the turn — the action and the footing — lets a brain weigh a casting it
        /// would make after a move.
        /// </summary>
        internal CastPlan PlanCastFrom(Combatant caster, GridPosition from, SpellCast cast, bool checkTurn)
        {
            var spell = cast.Spell;
            var build = caster.Build;
            if (!caster.Spells.Contains(spell))
            {
                return CastPlan.Refused($"{spell.Name} isn't one of {caster.Name}'s spells.");
            }

            if (checkTurn)
            {
                if (caster.IsOutOfFight)
                {
                    return CastPlan.Refused($"{caster.Name} is in no state to.");
                }

                if (caster.ActionsRemaining < 1)
                {
                    return CastPlan.Refused("Casting takes an action, and there's none left.");
                }

                if (caster.NeedsFooting)
                {
                    return CastPlan.Refused(FootingFirst);
                }
            }

            if (spell.SpellType == SpellType.Control && HasControlActive(caster))
            {
                return CastPlan.Refused($"{caster.Name} already has a Control spell holding somebody — only one at a time.");
            }


            // Which access pays for it, and what it rolls (Core Rules §11, "Pure Magic", "Bleeding for Power").
            if (caster.MagicAccess.FirstOrDefault(a => a.Source == spell.Source) is not { } access)
            {
                return CastPlan.Refused($"{caster.Name} has no access to {SpellCalculator.SourceLabel(spell.Source)}.");
            }

            if ((int)spell.SpellTier > (int)access.MaxSpellTier)
            {
                return CastPlan.Refused($"{spell.Name} is {spell.SpellTier}, past the {access.MaxSpellTier} {caster.Name} can cast.");
            }

            // Elemental Magic: the trained element only, and never a Heal (Core Rules §11, "Elemental Magic").
            Element? element = null;
            if (spell.Source == MagicSource.ElementalMagic)
            {
                if ((SpellArea.ElementOf(spell) ?? caster.Casting?.Element) is not { } worked)
                {
                    return CastPlan.Refused($"{spell.Name} doesn't say which element it works.");
                }

                if (access.TrainedElements.Count > 0 && !access.TrainedElements.Contains(worked))
                {
                    return CastPlan.Refused($"{caster.Name} isn't trained in {worked}.");
                }

                if (spell.SpellType == SpellType.Heal)
                {
                    return CastPlan.Refused("Only Blood Magic can heal — no element can close a wound.");
                }

                if (spell.SpellType is SpellType.Control or SpellType.Illusion)
                {
                    return CastPlan.Refused($"Elemental Magic can't make a {spell.SpellType}: it reaches into a mind, and channelled elemental force can't (Core Rules §11).");
                }

                element = worked;
            }
            else if (cast.Tap != FuelSource.None)
            {
                return CastPlan.Refused("Only Elemental Magic is paid in Fuel.");
            }

            WoundType? track;
            Ability ability;
            if (element is not null)
            {
                if (cast.PayInBody)
                {
                    return CastPlan.Refused("Elemental Magic is paid in Fuel — there's no blood to bleed into it.");
                }

                // A creature made of what it casts pays a piece of itself, in Body (GM Reference §9).
                (track, ability) = (build is null && caster.Casting is { OfItsElement: true } ? WoundType.Body : null, access.CastAbility);
            }
            else if (spell.Source == MagicSource.BloodMagic)
            {
                (track, ability) = (WoundType.Body, access.CastAbility);
            }
            else if (cast.PayInBody)
            {
                if (!access.CanBleedForPower || access.BleedCastAbility is not { } bleed)
                {
                    return CastPlan.Refused($"{caster.Name} can't pay for {spell.Name} in Body.");
                }

                (track, ability) = (WoundType.Body, bleed);
            }
            else
            {
                (track, ability) = (access.NativeCostTrack ?? WoundType.Body, access.CastAbility);
            }

            // A character rolls Tier + the stat; a foe rolls its Caster trait's bonus, or its Tier's
            // baseline when it pays a Divine or Aether spell in blood (GM Reference §9, "On Casters").
            var castBonus = build is not null
                ? CharacterCalculator.CastBonus(build, ability)
                : track == WoundType.Body && spell.Source != MagicSource.BloodMagic && spell.Source == caster.Casting?.Source
                    ? caster.StatblockBleedBonus
                    : caster.StatblockCastBonus;

            // Where it lands (Core Rules §11, "Spell Shape", "Placing a Volume").
            var touch = spell.Range == SpellRange.Touch || spell.Shape == SpellShape.None;
            var shape = SpellArea.ShapeOf(cast);
            if (spell.SpellType != SpellType.Illusion && SpellArea.ShapeRefusal(spell, shape) is { } wrongShape)
            {
                return CastPlan.Refused(wrongShape);
            }

            if (spell.SpellType == SpellType.Utility && UtilityRefusal(spell, shape) is { } noUtility)
            {
                return CastPlan.Refused(noUtility);
            }

            GridPosition origin;
            List<GridPosition> squares;
            List<Combatant> caught;
            if (spell.SpellType == SpellType.Illusion)
            {
                var placed = PlaceIllusion(caster, from, cast);
                if (placed.Refusal is { } nowhere)
                {
                    return CastPlan.Refused(nowhere);
                }

                (origin, squares, caught, touch) = (placed.Origin, placed.Squares, placed.Caught, placed.Touch);
            }
            else if (touch || shape == SpellShape.Ray)
            {
                if (cast.Target is not { } target)
                {
                    return CastPlan.Refused($"{spell.Name} needs somebody to go into.");
                }

                if (target.HasFled || !target.OccupiesSquare)
                {
                    return CastPlan.Refused($"{target.Name} isn't on the board.");
                }

                if (touch && caster.DistanceFrom(from, target) > 1)
                {
                    return CastPlan.Refused($"{spell.Name} is cast at Touch — {target.Name} has to be in reach of a hand.");
                }

                origin = target.Position;
                squares = touch ? [] : [origin];
                caught = [target];
            }
            else if (shape is SpellShape.Line or SpellShape.Cone)
            {
                if (cast.Origin is not { } start || cast.Aim is not { } aim || (aim.X == 0 && aim.Y == 0) || Math.Abs(aim.X) > 1 || Math.Abs(aim.Y) > 1)
                {
                    return CastPlan.Refused($"A {shape} needs a point of origin and one of the eight ways to aim it.");
                }

                if (Setting.IsWall(start))
                {
                    return CastPlan.Refused("The point of origin is inside a wall.");
                }

                origin = start;
                squares = shape == SpellShape.Line
                    ? SpellArea.Line(start, aim, spell.Size, Setting.IsWall)
                    : SpellArea.Cone(start, aim, spell.Size, Setting.IsWall);
                var covered = squares.ToHashSet();

                // Aimed away from the hand that casts it: the caster isn't caught in their own Line or Cone.
                caught = combatants.Where(c => c != caster && c.OccupiesSquare && c.Footprint.Any(covered.Contains)).ToList();
                // An area effect catches a mount as well as its rider (Core Rules §9, "Mounts").
                caught.AddRange(Steeds.Where(m => covered.Contains(m.Position)));
            }
            else if (shape == SpellShape.Volume)
            {
                if (cast.Corner is not { } corner)
                {
                    return CastPlan.Refused($"{spell.Name} needs a place for its block.");
                }

                var block = SpellArea.Block(corner, spell.Size).Where(sq => !Setting.IsWall(sq)).ToList();
                if (block.Count == 0)
                {
                    return CastPlan.Refused("There's no open ground in that block to put the spell into.");
                }

                origin = block.OrderBy(sq => from.DistanceTo(sq)).ThenBy(sq => Math.Abs(sq.X - from.X) + Math.Abs(sq.Y - from.Y)).First();
                squares = SpellArea.Reached(corner, spell.Size, origin, Setting.IsWall);
                var covered = squares.ToHashSet();
                caught = combatants.Where(c => c.OccupiesSquare && c.Footprint.Any(covered.Contains)).ToList();
                caught.AddRange(Steeds.Where(m => covered.Contains(m.Position)));
            }
            else
            {
                return CastPlan.Refused($"{shape} spells aren't in the game yet.");
            }

            var distance = from.DistanceTo(origin);
            if (!touch)
            {
                if (distance > LongRangeSquares)
                {
                    return CastPlan.Refused($"Beyond Long — the point of origin is {distance} squares off.");
                }

                if (distance > 1 && !LineOpen(from, origin))
                {
                    return CastPlan.Refused("Something solid is in the way.");
                }

                var seen = environment is not { } tables || RangeBandCalculator.BandForSquares(bands, distance) is not { } band
                    || (caught.Count == 1 && squares.Count == 1
                        ? CanBeTargeted(caught[0], origin, distance)
                        : EnvironmentCalculator.AllowsTargeting(tables, VisibilityAtSquare(origin), band));
                if (!seen)
                {
                    return CastPlan.Refused("Too dark to see where it would go — nothing can be targeted beyond Close at Visibility None.");
                }
            }

            var range = touch ? SpellRange.Touch : RangeBandCalculator.BandForSquares(bands, distance) switch
            {
                DistanceBand.Middle => SpellRange.Middle,
                DistanceBand.Long => SpellRange.Long,
                DistanceBand.Short => SpellRange.Short,
                _ => SpellRange.Close,
            };

            // Who it works on, by Type (Core Rules §11).
            MotionKind? motion = spell.SpellType == SpellType.Motion ? SpellArea.MotionOf(spell) : null;
            var willingOnly = spell.SpellType == SpellType.Buff || motion == MotionKind.Quicken;

            // Cover (Core Rules §9): a Ray, Line or Cone is aimed, and full cover can't be targeted by
            // it; a Volume fills the space and flows round, so it ignores cover. A spell laid on the
            // willing, a Heal or a Utility isn't an attack at anybody.
            if (!touch && shape != SpellShape.Volume && !willingOnly
                && spell.SpellType is SpellType.Harm or SpellType.Control or SpellType.Bane or SpellType.Motion
                && Setting.CoverPieces.Count > 0)
            {
                var exposed = caught.Where(c => c.Side == caster.Side || CoverFrom(from, c) != Cover.Full).ToList();
                if (exposed.Count == 0 && caught.Count > 0)
                {
                    return CastPlan.Refused(caught.Count == 1
                        ? $"{caught[0].Name} is behind full cover from here — only a Volume would reach them."
                        : "Everybody it would catch is behind full cover from here.");
                }

                caught = exposed;
            }
            if (willingOnly)
            {
                caught = caught.Where(c => c.Side == caster.Side && !c.IsOutOfFight).ToList();
                if (caught.Count == 0)
                {
                    return CastPlan.Refused($"A {spell.SpellType} is laid on a willing target, and nobody willing is there.");
                }
            }

            switch (spell.SpellType)
            {
                case SpellType.Utility:
                    // It works on the ground, not on whoever happens to be standing there.
                    caught = [];
                    break;

                case SpellType.Illusion:
                    // It works on whoever sees it, which is a matter for the moment it stands.
                    break;

                case SpellType.Heal:
                    var patient = caught[0];
                    if (patient == caster)
                    {
                        return CastPlan.Refused("A Heal can never target its own caster — blood given closes someone else's wound.");
                    }

                    if (patient.BodyStatus == BodyStatus.Dead)
                    {
                        return CastPlan.Refused($"{patient.Name} is beyond healing.");
                    }

                    var mends = cast.Restores ?? spell.DamageType ?? WoundType.Body;
                    if (patient.Track(mends) is var (now, max) && now >= max)
                    {
                        return CastPlan.Refused($"{patient.Name} carries no {mends} wound.");
                    }

                    break;

                case SpellType.Buff or SpellType.Bane:
                    var moves = cast.Threshold ?? spell.Threshold ?? ThresholdType.Dodge;
                    if (moves == ThresholdType.Fortitude)
                    {
                        // Nothing with no living body has a Fortitude for it to move (GM Reference §9).
                        caught = caught.Where(c => c.BaseFortitudeThreshold is not null).ToList();
                        if (caught.Count == 0)
                        {
                            return CastPlan.Refused("No living body there for Fortitude to measure.");
                        }
                    }

                    break;

                case SpellType.Control:
                    if (SpellArea.FixedEffectOf(spell) is { } written && written != cast.Effect)
                    {
                        return CastPlan.Refused($"{spell.Name} is written to cause {written}, not {cast.Effect}.");
                    }

                    caught = caught.Where(c => c.ResolveThreshold is not null && !c.IsOutOfFight).ToList();
                    if (cast.Effect == ControlEffect.Compulsion && cast.Directed is { } directed && (directed.HasFled || !directed.OccupiesSquare))
                    {
                        return CastPlan.Refused($"{directed.Name} isn't there to be attacked.");
                    }

                    break;

                case SpellType.Motion when motion == MotionKind.Bind && cast.BindAs is { } lower && lower > spell.SpellTier:
                    return CastPlan.Refused($"A {spell.SpellTier} Bind can't produce a {lower} effect — only its own, or a lower one.");
            }

            // A Swarm is moved, held or hurried only as a whole: a Motion spell must cover every square
            // of it, or it does nothing to the mass (the user's ruling, 2026-11-02).
            var swarmUnmoved = spell.SpellType == SpellType.Motion
                ? caught.FirstOrDefault(c => c.IsSwarm && !c.Footprint.All(squares.Contains))
                : null;
            if (swarmUnmoved is not null)
            {
                caught = caught.Where(c => c != swarmUnmoved && !(c.IsSwarm && !c.Footprint.All(squares.Contains))).ToList();
            }

            if (caught.Count == 0 && spell.SpellType is not (SpellType.Utility or SpellType.Illusion))
            {
                return CastPlan.Refused(swarmUnmoved is not null
                    ? $"{spell.Name} has to cover the whole of {swarmUnmoved.Name} to move it — a part of a Swarm can't be moved."
                    : $"{spell.Name} would catch nobody there.");
            }

            // What it costs, and whether it can be paid (Core Rules §11, "Spell Wound Cost"; §4).
            var cost = SpellCalculator.SpellCost(spell.SpellTier, spell.Size, range);
            var wounds = cost;
            if (cast.Overclock)
            {
                if (element is not null)
                {
                    return CastPlan.Refused("Overclock pays a wound of a spell's cost, and Elemental Magic costs Fuel, not wounds.");
                }

                if (!caster.HasOverclock)
                {
                    return CastPlan.Refused("Only a Cerebral character can Overclock.");
                }

                if (caster.HasOverclockedThisRound)
                {
                    return CastPlan.Refused("Already Overclocked this round.");
                }

                if (caster.Stamina < 1 || cost < 1)
                {
                    return CastPlan.Refused(cost < 1 ? "Nothing to Overclock — it costs no wounds." : "No Stamina to Overclock with.");
                }

                wounds--;
            }

            var rolls = spell.SpellType is SpellType.Harm or SpellType.Control or SpellType.Illusion || (spell.SpellType == SpellType.Motion && motion != MotionKind.Quicken);
            if (cast.Sacrifice && SacrificeRefusal(caster) is { } noSacrifice)
            {
                return CastPlan.Refused(noSacrifice);
            }

            if (cast.Sacrifice && !rolls)
            {
                return CastPlan.Refused($"{spell.Name} rolls nothing to land, so Sacrifice has nothing to improve.");
            }

            if (cast.Sustain && (!SpellCalculator.CanBeSustained(spell.SpellType) || motion == MotionKind.Shift))
            {
                return CastPlan.Refused($"A {(motion == MotionKind.Shift ? "Shift" : spell.SpellType.ToString())} is over the moment it lands — there's nothing to Sustain.");
            }

            var sacrificed = cast.Sacrifice ? 1 : 0;
            FuelPayment? fuel = null;
            if (element is { } fuelled && track is null)
            {
                if (FuelRefusal(caster, from, fuelled, cast) is { } noSource)
                {
                    return CastPlan.Refused(noSource);
                }

                fuel = build is not null
                    ? InventoryCalculator.PlanFuelPayment(build, fuelled, cost, AmbientAt(fuelled, from), cast.Tap)
                    : StatblockFuelPayment(caster, fuelled, cost, AmbientAt(fuelled, from), cast.Tap);
                if (!fuel.IsFullyPaid)
                {
                    return CastPlan.Refused($"{spell.Name} needs {cost} {fuelled} Fuel, and that leaves it {fuel.Shortfall} short.");
                }

                wounds = 0;
            }

            var payRefusal = track is not { } paying
                ? PlayStateCalculator.SpendRefusal(WoundType.Body, caster.Body, caster.Body, sacrificed, false)
                : paying == WoundType.Body
                    ? PlayStateCalculator.SpendRefusal(WoundType.Body, caster.Body, caster.Body, wounds + sacrificed, false)
                    : PlayStateCalculator.SpendRefusal(WoundType.Body, caster.Body, caster.Body, sacrificed, false)
                      ?? PlayStateCalculator.SpendRefusal(paying, caster.Track(paying).Current, caster.Body - sacrificed, wounds, cast.Backlash);
            if (payRefusal is not null)
            {
                return CastPlan.Refused(payRefusal);
            }

            return new CastPlan(null, origin, range, cost, track, ability, castBonus, squares, caught, motion) { WoundsPaid = wounds, Fuel = fuel };
        }

        /// <summary>Why <paramref name="combatant"/> can't Sacrifice now, or null if they can (Core Rules §3, "Sacrifice").</summary>
        public static string? SacrificeRefusal(Combatant combatant) =>
            !combatant.HasSacrifice ? "Only a character with Body as their Secondary Wound can Sacrifice."
            : combatant.HasSacrificedThisRound ? "Already Sacrificed this round."
            : !PlayStateCalculator.CanSpendBody(combatant.Body, 1) ? "At 1 Body there's nothing left to give."
            : null;

        /// <summary>Casts <paramref name="cast"/> with the active character's action (Core Rules §9, "Magic").</summary>
        public bool TryCast(SpellCast cast)
        {
            if (Phase != EncounterPhase.PartyTurn || Active is not { } caster || PlanCast(caster, cast) is not { CanCast: true } plan)
            {
                return false;
            }

            Cast(caster, cast, plan, () => CheckForEnd());
            return true;
        }

        /// <param name="free">Paid by a free-Buff Opportunity bonus: no wounds (GM Reference §9).</param>
        private void Cast(Combatant caster, SpellCast cast, CastPlan plan, Action then, bool free = false)
        {
            var spell = cast.Spell;
            caster.ActionsRemaining--;
            caster.HasCastSpell = true;

            // Paid before anything is rolled; a cost never cancels the thing it paid for (Core Rules §4).
            var paid = new List<string>();
            if (cast.Overclock)
            {
                caster.Stamina--;
                caster.HasOverclockedThisRound = true;
                paid.Add("1 Stamina (Overclock)");
            }

            if (free && plan.WoundsPaid > 0)
            {
                paid.Add("no wounds (the Opportunity pays)");
            }
            else if (plan.WoundsPaid > 0)
            {
                var track = plan.Track!.Value;
                if (track == WoundType.Body)
                {
                    caster.Body -= plan.WoundsPaid;
                }
                else
                {
                    var current = caster.Track(track).Current;
                    PlayStateCalculator.TrySpendMindOrSoul(current, caster.Body, plan.WoundsPaid, cast.Backlash, out var left, out var body);
                    SetTrack(caster, track, left);
                    if (body < caster.Body)
                    {
                        paid.Add($"{caster.Body - body} Body in Backlash");
                    }

                    caster.Body = body;
                }

                caster.TendEligible[track] = true;
                paid.Insert(0, $"{plan.WoundsPaid} {track}");
            }

            if (plan.Fuel is { } fuel)
            {
                if (caster.Build is { } payer)
                {
                    InventoryCalculator.PayFuelCost(payer, fuel);
                }
                else
                {
                    caster.StatblockFuel -= fuel.FromCarried;
                }

                var parts = new List<string>();
                if (fuel.FromAmbient > 0)
                {
                    parts.Add($"{fuel.FromAmbient} from the {fuel.Element} around them");
                }

                if (fuel.SourceSpent > 0)
                {
                    parts.Add($"{fuel.FromSource} from {Tap(caster, cast)}");
                }

                if (fuel.FromCarried > 0)
                {
                    parts.Add($"{fuel.FromCarried} {fuel.Element} Fuel from the pack");
                }

                paid.Add(parts.Count == 0 ? "no Fuel" : string.Join(", ", parts));
            }

            if (cast.Sacrifice)
            {
                caster.Body--;
                caster.HasSacrificedThisRound = true;
                caster.TendEligible[WoundType.Body] = true;
                paid.Add("1 Body (Sacrifice, for Advantage)");
            }

            if (cast.Sustain && caster.Sustaining is not null)
            {
                ReleaseHeld(caster, "to hold a new one");
            }

            var where = plan.Range == SpellRange.Touch ? $"with a hand on {plan.Targets[0].Name}"
                : plan.Area.Count > 1 ? $"as a {SpellArea.ShapeOf(cast).ToString().ToLowerInvariant()} over {plan.Area.Count} squares at {plan.Range}"
                : plan.Targets.Count == 0 ? $"on {plan.Origin}, {plan.Range}"
                : $"at {plan.Targets[0].Name}, {plan.Range}";
            Write($"{caster.Name} casts {spell.Name} {where} — {(paid.Count == 0 ? "free" : string.Join(", ", paid))}" +
                  (cast.Sustain ? ", declared Sustained." : "."));

            // Casting a spell at somebody out of full cover shows the caster (Core Rules §9, "Popping out").
            if (spell.SpellType is SpellType.Harm or SpellType.Control or SpellType.Bane or SpellType.Motion)
            {
                ShowFromCover(caster, plan.Targets.Where(t => t.Side != caster.Side));
            }

            void Finish()
            {
                if (caster.IsDazed)
                {
                    Write($"{caster.Name} has spent the last of their {(caster.IsStunned ? "Mind, and is Stunned" : "Soul, and is Numbed")}.");
                }

                then();
            }

            // A spell into a phantom goes through nothing and lands on nothing; one flying through a
            // phantom wall calls it into question.
            if (plan.Range != SpellRange.Touch)
            {
                ShotThroughWalls(caster, caster.Position, plan.Origin);
            }

            if (plan.Targets.Count == 1 && plan.Targets[0].IsPhantom)
            {
                PassesThroughPhantom(caster, plan.Targets[0], spell.Name);
                Finish();
                return;
            }

            switch (spell.SpellType)
            {
                case SpellType.Harm:
                    CastHarm(caster, cast, plan, Finish);
                    return;
                case SpellType.Heal:
                    CastHeal(caster, cast, plan);
                    Finish();
                    return;
                case SpellType.Control:
                    CastControl(caster, cast, plan, Finish);
                    return;
                case SpellType.Utility:
                    Work(caster, cast, plan);
                    Finish();
                    return;
                case SpellType.Illusion:
                    CastIllusion(caster, cast, plan);
                    Finish();
                    return;
                case SpellType.Buff or SpellType.Bane:
                    foreach (var target in plan.Targets)
                    {
                        LayOn(caster, cast, plan, target);
                    }

                    Finish();
                    return;
                default:
                    CastMotion(caster, cast, plan, Finish);
                    return;
            }
        }

        private static void SetTrack(Combatant c, WoundType track, int value)
        {
            switch (track)
            {
                case WoundType.Body:
                    c.Body = value;
                    break;
                case WoundType.Mind:
                    c.Mind = value;
                    break;
                default:
                    c.Soul = value;
                    break;
            }
        }

        /// <summary>
        /// The dice a Heal or Harm rolls — d6 for one target, d4 for many, one die a Tier — plus Grace;
        /// on a critical, the dice's maximum and one more die (Core Rules §10, §11).
        /// </summary>
        private int SpellDice(Combatant caster, SpellDefinition spell, bool critical)
        {
            var count = SpellCalculator.HealHarmDiceCount(spell.SpellTier);
            var sides = DiceNotation.ParseSides(SpellCalculator.HealHarmDieSize(spell.Target));
            var rolled = critical
                ? count * sides + dice.Roll(sides)
                : Enumerable.Range(0, count).Sum(_ => dice.Roll(sides));
            return Math.Max(0, rolled + (caster.Build is { } build ? SpellCalculator.GraceBonus(build, spell) : 0));
        }

        /// <summary>
        /// A Harm spell: one roll for each target it catches, each against that target's own Dodge
        /// Threshold — which they may raise after seeing it, as against any attack — and each
        /// standing alone for criticals. A double one wounds the caster once per casting, of the
        /// spell's own damage type (Core Rules §11, "Resolving a spell against several targets").
        /// </summary>
        private void CastHarm(Combatant caster, SpellCast cast, CastPlan plan, Action then)
        {
            var spell = cast.Spell;
            var type = spell.DamageType ?? WoundType.Body;
            var (helped, harried) = TakeAid(caster);
            var backfired = false;
            var targets = new Queue<Combatant>(plan.Targets);

            void Next()
            {
                if (!targets.TryDequeue(out var target))
                {
                    if (backfired)
                    {
                        Write($"{caster.Name}'s working turns on them — a double one, and a {type} wound of their own.");
                        Wound(caster, type, 1, direct: true, Done);
                        return;
                    }

                    Done();
                    return;
                }

                if (!target.OccupiesSquare)
                {
                    Next();
                    return;
                }

                var threshold = target.DodgeThreshold + VisibilityDodgeModifier(target)
                                + CoverDodgeModifier(caster.Position, target, SpellArea.ShapeOf(cast) == SpellShape.Volume || spell.Range == SpellRange.Touch);
                target.HasBeenAttackedSinceTurnStart = true;
                var advantage = cast.Sacrifice || helped || target.IsDefenceless;
                var disadvantage = AttacksAtDisadvantage(caster) || harried;
                var roll = RollAgainst(plan.CastBonus, threshold, advantage, disadvantage, caster);
                var expected = SpellCalculator.HealHarmDiceCount(spell.SpellTier) * (DiceNotation.ParseSides(SpellCalculator.HealHarmDieSize(spell.Target)) + 1) / 2.0
                               + (caster.Build is { } b ? SpellCalculator.GraceBonus(b, spell) : 0);
                var shieldGain = target.InHandShield is { } shield && target.CoveredBy.All(c => c.Bearer != target) && !target.IsOutOfFight
                    ? Math.Max(0, InventoryCalculator.ShieldDodgeThresholdBonus(shield.Weight) - target.ShieldBonus)
                    : 0;

                void Land(int boost, string? boostedBy)
                {
                    var final = boost > 0 ? Reread(roll, threshold + boost) : roll;
                    var line = $"{spell.Name} at {target.Name}: {DescribeRoll(final)} vs DT {final.Threshold}" + (boost > 0 ? $" (+{boost} {boostedBy})" : string.Empty);
                    if (final.Success != true)
                    {
                        // A double that misses — rolled so, or Riposted into it — is a critical failure.
                        backfired |= final.IsCritical;
                        Write(line + (final.IsCritical && roll.Success == true ? " — Riposte! The critical becomes a critical failure."
                            : final.IsCritical ? " — critical failure!" : " — miss."));
                        Next();
                        return;
                    }

                    // Only an area over the whole of a Swarm gets the dice; anything less is a wound
                    // a square covered, and rolls nothing (GM Reference §3).
                    var partial = target.IsSwarm ? SwarmCoverage(target, plan) : null;
                    var amount = partial ?? SpellDice(caster, spell, final.IsCritical);
                    if (partial is { } covered)
                    {
                        line += $" — covers {covered} of its {target.Across * target.Across} squares";
                    }

                    Write(line + (final.IsCritical ? $" — critical! {amount} {type}." : $" — hit, {amount} {type}."));
                    Wound(target, type, amount, direct: false, Next);
                }

                if (!target.IsOutOfFight && shieldGain > 0 && BoostWouldTurn(roll, threshold, shieldGain))
                {
                    OfferReaction(new PendingReaction(ReactionKind.RaiseShield, target, caster, roll, threshold,
                            $"{caster.Name}'s {spell.Name} rolls {roll.Total} against {target.Name}'s DT {threshold}. Raising the shield now (1 Stamina, +{shieldGain}, and it stays up) would make it miss.",
                            Bonus: shieldGain, ExpectedDamage: expected, DamageType: type),
                        raised =>
                        {
                            if (raised)
                            {
                                RaiseShield(target, target, reactive: true);
                            }

                            Land(raised ? shieldGain : 0, "shield raised");
                        });
                    return;
                }

                if (!target.IsOutOfFight && BoostWouldTurn(roll, threshold))
                {
                    OfferReaction(new PendingReaction(ReactionKind.PanicDodge, target, caster, roll, threshold,
                            $"{caster.Name}'s {spell.Name} rolls {roll.Total} against {target.Name}'s DT {threshold}. A panic dodge (1 Stamina, +2) would make it miss.", ExpectedDamage: expected, DamageType: type),
                        dodged => Land(dodged ? ThresholdModifierCalculator.ReactionBonus : 0, "panic dodge"));
                    return;
                }

                Land(0, null);
            }

            void Done()
            {
                CheckHolds();
                then();
            }

            Next();
        }

        /// <summary>
        /// What a Harm spell costs a Swarm when it doesn't blanket the whole mass: 1 wound for a Ray
        /// or Touch (a single point), 1 per square for an area that covers part of it. Null when the
        /// area covers every square — then the rolled damage lands in full (GM Reference §3).
        /// </summary>
        internal static int? SwarmCoverage(Combatant swarm, CastPlan plan)
        {
            var covered = swarm.Footprint.Count(plan.Area.Contains);
            return covered >= swarm.Across * swarm.Across ? null : Math.Max(1, covered);
        }

        /// <summary>
        /// Wounds of any type landing on <paramref name="target"/>. Body is soaked by armour unless
        /// <paramref name="direct"/>; Mind and Soul never are, and may be Backlashed onto Body instead
        /// of dropping the track to 0 (Core Rules §4). A Minion hit on Mind or Soul simply goes down
        /// (GM Reference §3).
        /// </summary>
        private void Wound(Combatant target, WoundType type, int amount, bool direct, Action then)
        {
            if (amount <= 0)
            {
                then();
                return;
            }

            if (target.IsSwarm && type != WoundType.Body)
            {
                Write($"{target.Name} has no single mind or spirit for {type} damage to find (GM Reference §3).");
                then();
                return;
            }

            if (type != WoundType.Body && target.IsSteed)
            {
                // A mount is Body, Dodge and Stability, and nothing more (Core Rules §9, "Mounts").
                Write($"{target.Name} is a mount — there's no {type} in it for the spell to find.");
                then();
                return;
            }

            if (type != WoundType.Body && (target.IsBoundNotAlive || type == WoundType.Mind && target.IsMindImmune))
            {
                Write(target.IsBoundNotAlive
                    ? $"{target.Name} is Bound, Not Alive: there is no mind or spirit in it for {type} damage to find (GM Reference §9)."
                    : $"{target.Name} has no mind left in it for Mind damage to mean anything (Deathless Purpose).");
                then();
                return;
            }

            var soaked = type == WoundType.Body && !direct ? Math.Min(amount, InventoryCalculator.ArmourAbsorbedAgainst(target.Armour, WoundType.Body, isDirect: false)) : 0;
            Shelter(target, amount - soaked, type, left => Land(target, type, left + soaked, soaked, then));
        }

        /// <summary>The wounds left once Shelter has had its say, landing: Body less what armour took, Mind or Soul with Backlash on offer.</summary>
        private void Land(Combatant target, WoundType type, int amount, int absorbed, Action then)
        {
            if (amount - absorbed <= 0)
            {
                then();
                return;
            }

            if (type == WoundType.Body)
            {
                TakeBodyWounds(target, amount - absorbed);
                Write($"{target.Name}: {amount} Body" + (absorbed > 0 ? $", armour absorbs {absorbed}" : string.Empty) +
                      (target.IsOutOfFight ? " — out of the fight." : $" — {target.Body}/{target.BodyMax} Body."));
                then();
                return;
            }

            // A Minion's Mind and Soul are pinned at 1: a point drops it Stunned or Numbed — out of
            // the fight, lying there to be questioned — unless it Backlashes, and the Body that
            // costs is a hit that removes it (GM Reference §3, "Minion Rule").
            target.TendEligible[type] = true;
            var current = target.Track(type).Current;
            var excess = amount - (current - 1);
            void Apply(bool backlash)
            {
                var (track, body) = PlayStateCalculator.ApplyMindOrSoulDamage(current, target.Body, amount, backlash);
                SetTrack(target, type, track);
                if (body != target.Body)
                {
                    target.TendEligible[WoundType.Body] = true;
                    target.Body = target.IsMinion ? 0 : body;
                    target.HasBeenDropped |= target.Body <= 0;
                }

                var (now, max) = target.Track(type);
                Write($"{target.Name}: {amount} {type}" +
                      (backlash ? $", Backlashed — held at 1, {excess} Body instead ({target.Body}/{target.BodyMax})." : $" — {now}/{max} {type}.") +
                      (target.IsOutOfFight && !backlash ? target.IsDazed ? $" {target.Name} is {(type == WoundType.Mind ? "Stunned" : "Numbed")}." : " Out of the fight." : string.Empty) +
                      (backlash && target.IsOutOfFight ? $" {target.Name} is out of the fight." : string.Empty));
                then();
            }

            if (current > 0 && current - amount <= 0 && target.Body > 0)
            {
                OfferReaction(new PendingReaction(ReactionKind.Backlash, target, target, null, 0,
                        $"{amount} {type} would take {target.Name} to 0 {type} — {(type == WoundType.Mind ? "Stunned" : "Numbed")}. " +
                        $"Backlash holds them at 1 and puts the excess, {excess}, on Body instead, Direct.",
                        ImpactWounds: excess),
                    Apply);
                return;
            }

            Apply(false);
        }

        /// <summary>A Heal: rolls nothing to land; its dice, plus Grace, back onto the chosen track (Core Rules §11).</summary>
        private void CastHeal(Combatant caster, SpellCast cast, CastPlan plan)
        {
            var patient = plan.Targets[0];
            var track = cast.Restores ?? cast.Spell.DamageType ?? WoundType.Body;
            var amount = SpellDice(caster, cast.Spell, critical: false);
            var wasDown = patient.IsOutOfFight;
            var (before, max) = patient.Track(track);
            SetTrack(patient, track, PlayStateCalculator.ApplyHeal(before, amount, max));
            if (track == WoundType.Body && patient.Body > 0)
            {
                patient.IsStable = false;
            }

            var (now, _) = patient.Track(track);
            Write($"{cast.Spell.Name} restores {amount} {track} to {patient.Name}: {now}/{max}." +
                  (wasDown && !patient.IsOutOfFight ? $" {patient.Name} is back in the fight!" : string.Empty));
        }

        /// <summary>A Buff or a Bane takes hold without a roll: its Tier's bonus or penalty to the chosen Threshold (Core Rules §11).</summary>
        private void LayOn(Combatant caster, SpellCast cast, CastPlan plan, Combatant target)
        {
            var spell = cast.Spell;
            var threshold = cast.Threshold ?? spell.Threshold ?? ThresholdType.Dodge;
            var amount = spell.SpellType == SpellType.Buff
                ? ThresholdModifierCalculator.BuffAmount(spell.SpellTier)
                : ThresholdModifierCalculator.BaneAmount(spell.SpellTier);
            var modifier = new ThresholdModifier
            {
                Threshold = threshold,
                Amount = amount,
                Source = spell.Name,
                Duration = ThresholdModifierDuration.UntilCastersTurn,
                Caster = caster.Name,
                Sustained = cast.Sustain,
            };
            target.ThresholdModifiers = ThresholdModifierCalculator.Add(target.ThresholdModifiers, modifier);
            Hold(caster, cast, plan, target, modifier, condition: null);
            Write($"{target.Name}: {(amount > 0 ? "+" : "−")}{Math.Abs(amount)} {threshold} until the start of {caster.Name}'s next turn — {ThresholdLine(target, threshold)}.");
        }

        private static string ThresholdLine(Combatant c, ThresholdType threshold) => threshold switch
        {
            ThresholdType.Dodge => $"DT {c.DodgeThreshold}",
            ThresholdType.Stability => $"ST {c.StabilityThreshold}",
            _ => $"RT {c.ResolveThreshold}",
        };

        /// <summary>What a Bind does at each Tier (Core Rules §11, "Motion Spells"): Slowed, Stuck, Frozen.</summary>
        public static CharacterCondition BindEffect(Tier tier) => tier switch
        {
            Tier.T1 => CharacterCondition.Slowed,
            Tier.T2 => CharacterCondition.Stuck,
            _ => CharacterCondition.Frozen,
        };

        private static string BindMeaning(CharacterCondition bound) => bound switch
        {
            CharacterCondition.Stuck => "they can't change square, but may act freely",
            CharacterCondition.Frozen => "they may neither move nor act",
            _ => "one Speed slower",
        };

        private void Hold(Combatant caster, SpellCast cast, CastPlan plan, Combatant target, ThresholdModifier? modifier, CharacterCondition? condition)
        {
            spellEffects.Add(new SpellEffect
            {
                Caster = caster,
                Target = target,
                Spell = cast.Spell,
                Modifier = modifier,
                Condition = condition,
                Sustained = cast.Sustain,
                CastBonus = plan.CastBonus,
            });

            if (cast.Sustain)
            {
                caster.Sustaining = cast.Spell.Name;
            }
        }

        /// <summary>
        /// A Motion spell, rolled against each unwilling target's Stability Threshold — which they
        /// may raise by standing firm — and laid on a willing one without a roll. A Shift is Forced
        /// Movement in a straight line, which the target may still arrest; a Bind leaves them Slowed,
        /// a Quicken Hastened, until the start of the caster's next turn (Core Rules §11).
        /// </summary>
        private void CastMotion(Combatant caster, SpellCast cast, CastPlan plan, Action then)
        {
            var spell = cast.Spell;
            var kind = plan.Motion!.Value;
            var (helped, harried) = TakeAid(caster);
            var targets = new Queue<Combatant>(plan.Targets);

            void Next()
            {
                if (!targets.TryDequeue(out var target))
                {
                    CheckHolds();
                    then();
                    return;
                }

                if (!target.OccupiesSquare)
                {
                    Next();
                    return;
                }

                void Takes(string line)
                {
                    switch (kind)
                    {
                        case MotionKind.Shift when target.IsRiding && target.Side != caster.Side:
                            // A Shift landed on a rider unhorses them, rather than carrying horse and all (Core Rules §9, "Mounts").
                            Write(line);
                            Unhorse(target, $"{caster.Name}'s {spell.Name}");
                            Next();
                            return;
                        case MotionKind.Shift:
                            var step = cast.Direction ?? AwayFrom(caster.Position, target.Position);
                            var squares = Math.Min(cast.Squares ?? int.MaxValue, SpellArea.ShiftSquares(spell.SpellTier));
                            Write(line);
                            ForcedMove(target, step, squares, $"{caster.Name}'s {spell.Name}", caster, meetHazards: true, Next);
                            return;
                        case MotionKind.Bind:
                            var bound = BindEffect(cast.BindAs ?? spell.SpellTier);
                            Write(line + $" {target.Name} is {bound} until the start of {caster.Name}'s next turn — {BindMeaning(bound)}.");
                            target.SetCondition(bound, true);
                            Hold(caster, cast, plan, target, modifier: null, bound);
                            Next();
                            return;
                        default:
                            Write(line + $" {target.Name} is Hastened until the start of {caster.Name}'s next turn.");
                            target.SetCondition(CharacterCondition.Hastened, true);
                            Hold(caster, cast, plan, target, modifier: null, CharacterCondition.Hastened);
                            Next();
                            return;
                    }
                }

                if (target.Side == caster.Side || kind == MotionKind.Quicken)
                {
                    Takes($"{spell.Name} on {target.Name}, who is willing — nothing to roll.");
                    return;
                }

                var threshold = target.StabilityThreshold;
                var unshaken = kind == MotionKind.Shift && (target.IsUnshakable || target.IsSolo);
                var roll = RollAgainst(plan.CastBonus, threshold, cast.Sacrifice || helped || target.IsDefenceless, AttacksAtDisadvantage(caster) || harried || unshaken, caster);

                void Resolve(bool stoodFirm)
                {
                    var final = stoodFirm ? Reread(roll, threshold + ThresholdModifierCalculator.ReactionBonus) : roll;
                    var line = $"{spell.Name} at {target.Name}: {DescribeRoll(final)} vs ST {final.Threshold}" + (stoodFirm ? " (+2 standing firm)" : string.Empty);
                    if (final.Success != true)
                    {
                        Write(line + " — it doesn't take.");
                        Next();
                        return;
                    }

                    Takes(line + " —");
                }

                if (!target.IsOutOfFight && BoostWouldTurn(roll, threshold))
                {
                    OfferReaction(new PendingReaction(ReactionKind.StandFirm, target, caster, roll, threshold,
                        $"{caster.Name}'s {spell.Name} rolls {roll.Total} against {target.Name}'s ST {threshold}. Standing firm (1 Stamina, +2 ST) would stop it."), Resolve);
                    return;
                }

                Resolve(false);
            }

            Next();
        }

        private static (int X, int Y) AwayFrom(GridPosition from, GridPosition at)
        {
            var step = (Math.Sign(at.X - from.X), Math.Sign(at.Y - from.Y));
            return step == (0, 0) ? (1, 0) : step;
        }

        /// <summary>
        /// The start of <paramref name="caster"/>'s turn: everything they laid on that lasted "until the
        /// start of the caster's next turn" ends, but for what they declared Sustained — which waits on
        /// whether they pay. A caster with no turn to take can't pay, and lets it go.
        /// </summary>
        private void ExpireSpellsAtTurnStart(Combatant caster)
        {
            foreach (var effect in spellEffects.Where(e => e.Caster == caster && !e.Sustained).ToList())
            {
                End(effect, $"{effect.Spell.Name} on {effect.Target.Name} ends as {caster.Name}'s turn begins.");
            }

            if (caster.Sustaining is not null && (caster.IsOutOfFight || caster.Stamina <= 0))
            {
                ReleaseHeld(caster, "with nothing to pay for it");
            }
        }

        /// <summary>
        /// Offers <paramref name="caster"/> the Sustain payment, if they are holding a spell: 1 Stamina,
        /// a tactical spend, and every target fighting it rechecked. Unpaid, it ends here.
        /// </summary>
        private void OfferSustain(Combatant caster, Action then)
        {
            if (caster.Sustaining is not { } name)
            {
                then();
                return;
            }

            var held = spellEffects.Where(e => e.Caster == caster && e.Sustained).ToList();
            OfferReaction(new PendingReaction(ReactionKind.Sustain, caster, caster, null, 0,
                    $"{caster.Name}'s {name} would end now, holding {string.Join(", ", held.Select(e => e.Target.Name))}. " +
                    "Sustaining it (1 Stamina, a tactical spend) keeps it until their next turn; anyone fighting it is rechecked."),
                paid =>
                {
                    if (!paid)
                    {
                        ReleaseHeld(caster, "and lets it go");
                        then();
                        return;
                    }

                    Write($"{caster.Name} holds {name} for another round ({caster.Stamina} Stamina left).");
                    Recheck(new Queue<SpellEffect>(held.Where(e => !e.Accepts)), then);
                });
        }

        /// <summary>
        /// Each held target fighting the spell, rechecked: the cast total again against a Bind's
        /// Stability, or against what a Bane lowers at its value without the Bane; the target may spend
        /// Stamina for +2 against it, as against the cast. A failed recheck frees that target and no other.
        /// </summary>
        private void Recheck(Queue<SpellEffect> fighting, Action then)
        {
            if (!fighting.TryDequeue(out var effect))
            {
                then();
                return;
            }

            var target = effect.Target;
            var caster = effect.Caster;
            ThresholdType checks;
            int threshold;
            if (effect.Modifier is { } bane)
            {
                checks = bane.Threshold;
                var without = checks switch
                {
                    ThresholdType.Dodge => target.DodgeThreshold,
                    ThresholdType.Stability => target.StabilityThreshold,
                    _ => target.ResolveThreshold ?? 0,
                };
                threshold = without - bane.Amount;
            }
            else
            {
                checks = ThresholdType.Stability;
                threshold = target.StabilityThreshold;
            }

            var roll = RollAgainst(effect.CastBonus, threshold, advantage: false, disadvantage: false);

            void Resolve(bool boosted)
            {
                var final = boosted ? Reread(roll, threshold + ThresholdModifierCalculator.ReactionBonus) : roll;
                var label = checks switch { ThresholdType.Dodge => "DT", ThresholdType.Stability => "ST", _ => "RT" };
                if (final.Success == true)
                {
                    Write($"{effect.Spell.Name} rechecked against {target.Name}: {DescribeRoll(final)} vs {label} {final.Threshold} — it holds.");
                }
                else
                {
                    End(effect, $"{effect.Spell.Name} rechecked against {target.Name}: {DescribeRoll(final)} vs {label} {final.Threshold} — {target.Name} throws it off.");
                }

                Recheck(fighting, then);
            }

            if (!target.IsOutOfFight && BoostWouldTurn(roll, threshold))
            {
                var kind = checks switch
                {
                    ThresholdType.Dodge => ReactionKind.PanicDodge,
                    ThresholdType.Resolve => ReactionKind.BraceResolve,
                    _ => ReactionKind.StandFirm,
                };
                OfferReaction(new PendingReaction(kind, target, caster, roll, threshold,
                    $"{caster.Name}'s held {effect.Spell.Name} rechecks at {roll.Total} against {target.Name}'s {threshold}. 1 Stamina for +2 would throw it off."), Resolve);
                return;
            }

            Resolve(false);
        }

        /// <summary>Lets go of everything <paramref name="caster"/> is Sustaining.</summary>
        private void ReleaseHeld(Combatant caster, string how)
        {
            var name = caster.Sustaining;
            foreach (var effect in spellEffects.Where(e => e.Caster == caster && e.Sustained).ToList())
            {
                End(effect, null);
            }

            caster.Sustaining = null;
            if (name is not null)
            {
                Write($"{caster.Name} stops holding {name}, {how}.");
            }
        }

        private void End(SpellEffect effect, string? line)
        {
            spellEffects.Remove(effect);
            var target = effect.Target;
            if (effect.Modifier is { } modifier)
            {
                target.ThresholdModifiers = ThresholdModifierCalculator.Remove(target.ThresholdModifiers, modifier.Id);
            }

            if (effect.Condition is { } condition && !spellEffects.Any(e => e.Target == target && e.Condition == condition))
            {
                target.SetCondition(condition, false);
            }

            if (effect.Sustained && !spellEffects.Any(e => e.Caster == effect.Caster && e.Sustained))
            {
                effect.Caster.Sustaining = null;
            }

            if (line is not null)
            {
                Write(line);
            }
        }

        /// <summary>
        /// A held spell is anchored to its caster: it ends at once if they fall unconscious, start
        /// dying, or leave (Core Rules §11). What lasts only until their next turn runs to the round's
        /// end if they have no next turn to start.
        /// </summary>
        private void EndSpellsOfFallenCasters(bool roundEnd)
        {
            EndWorkingsOfFallenCasters();
            AgeIllusions(roundPassed: false);
            foreach (var effect in spellEffects.ToList())
            {
                var caster = effect.Caster;
                var gone = caster.IsUnconscious || caster.HasFled || caster.Body <= 0 || (roundEnd && caster.IsOutOfFight);
                if (gone && (effect.Sustained || roundEnd))
                {
                    End(effect, $"{effect.Spell.Name} on {effect.Target.Name} ends with {caster.Name} out of the fight.");
                }
            }
        }
    }
}
