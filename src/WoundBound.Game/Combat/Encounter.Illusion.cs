using WoundBound.Rules.Calculation;
using WoundBound.Rules.Enums;
using WoundBound.Rules.Models;

namespace WoundBound.Game.Combat
{
    /// <summary>The three categories of sense an Illusion can engage, one per Tier (Core Rules §11, "Illusion Spells").</summary>
    [Flags]
    public enum Senses
    {
        None = 0,
        Sight = 1,
        Sound = 2,
        /// <summary>Touch, smell and taste together.</summary>
        Substance = 4,
    }

    /// <summary>
    /// What an Illusion is made to be, for a fight — the four the game knows (the user's ruling,
    /// 2026-11-02). It deals no damage and compels nobody: it only misinforms.
    /// </summary>
    public enum IllusionForm
    {
        /// <summary>A figure standing where none is — the caster's likeness, or anyone they can picture.</summary>
        Figure,
        /// <summary>A wall, a barricade, a fall of rock: something solid where the ground is open.</summary>
        Wall,
        /// <summary>Help coming for the caster's side — lanterns at the bend, a name called, a war-chant.</summary>
        Reinforcements,
        /// <summary>An injury the target feels and hasn't taken.</summary>
        Wound,
    }

    /// <summary>
    /// An Illusion standing on the board: its Deceit is the cast roll, fixed and never rerolled; it
    /// lasts by Tier, as a Control does, with nothing rechecking it; and a creature that has seen
    /// through it is never fooled by it again (Core Rules §11, "Illusion Spells").
    /// </summary>
    public sealed class Illusion
    {
        public required Combatant Caster { get; init; }
        public required string Spell { get; init; }
        public required IllusionForm Form { get; init; }
        public required Senses Senses { get; init; }
        public required int Deceit { get; init; }
        public required IReadOnlySet<GridPosition> Squares { get; init; }
        public int RoundsLeft { get; internal set; }

        /// <summary>A Figure's phantom body: a shadow creature, never in the fight's roster, for whoever is taken in to see.</summary>
        public Combatant? Phantom { get; init; }

        /// <summary>A Wound's sufferer, and how many wounds it believes it has taken.</summary>
        public Combatant? Subject { get; init; }
        public int FeltWounds { get; init; }

        internal HashSet<Combatant> SeenThrough { get; } = new();

        /// <summary>Whether <paramref name="creature"/> has seen it for what it is.</summary>
        public bool IsSeenThroughBy(Combatant creature) => SeenThrough.Contains(creature);
    }

    public sealed partial class Encounter
    {
        private readonly List<Illusion> illusions = new();

        /// <summary>Every Illusion still standing.</summary>
        public IReadOnlyList<Illusion> Illusions => illusions;

        /// <summary>
        /// Whether <paramref name="creature"/> is taken in by <paramref name="illusion"/>: it is on the
        /// other side from the caster (who are in on it), hasn't seen through it, and can perceive it —
        /// a Sight-only illusion is worth nothing where it stands at Visibility None (Core Rules §9,
        /// "What the Levers Touch Elsewhere"). A Wound is felt only by its subject.
        /// </summary>
        public bool Fools(Illusion illusion, Combatant creature)
        {
            if (creature.Side == illusion.Caster.Side || illusion.SeenThrough.Contains(creature) || creature.IsOutOfFight)
            {
                return false;
            }

            if (illusion.Form == IllusionForm.Wound)
            {
                return creature == illusion.Subject;
            }

            if (illusion.Senses == Senses.Sight && illusion.Squares.All(sq => VisibilityAtSquare(sq) == Visibility.None))
            {
                return false;
            }

            return illusion.Squares.Any(sq => creature.DistanceTo(sq) <= LongRangeSquares);
        }

        /// <summary>The phantom figures <paramref name="creature"/> takes for real.</summary>
        public IEnumerable<Combatant> PhantomsSeenBy(Combatant creature) =>
            illusions.Where(i => i.Phantom is not null && Fools(i, creature)).Select(i => i.Phantom!);

        /// <summary>Squares <paramref name="creature"/> believes solid: phantom walls it is taken in by.</summary>
        public bool BelievesWall(Combatant creature, GridPosition square) =>
            illusions.Any(i => i.Form == IllusionForm.Wall && i.Squares.Contains(square) && Fools(i, creature));

        /// <summary>The Body <paramref name="creature"/> believes it has: its own, less any wound it feels and hasn't taken.</summary>
        public int BelievedBody(Combatant creature) =>
            creature.Body - illusions.Where(i => i.Form == IllusionForm.Wound && Fools(i, creature)).Sum(i => i.FeltWounds);

        private bool HasIllusionActive(Combatant caster) => illusions.Any(i => i.Caster == caster);

        /// <summary>What a spell's senses are, read from what it does: a figure seen, a voice heard, a wound felt.</summary>
        public static Senses SensesOf(SpellDefinition spell)
        {
            var text = $"{spell.Name} {spell.Notes}".ToLowerInvariant();
            var senses = Senses.None;
            if (text.Contains("figure") || text.Contains("likeness") || text.Contains("double") || text.Contains("image")
                || text.Contains("wall") || text.Contains("lantern") || text.Contains("sight") || text.Contains("seen"))
            {
                senses |= Senses.Sight;
            }

            if (text.Contains("voice") || text.Contains("sound") || text.Contains("hear") || text.Contains("chant") || text.Contains("scream"))
            {
                senses |= Senses.Sound;
            }

            if (text.Contains("flesh") || text.Contains("feel") || text.Contains("texture") || text.Contains("smell"))
            {
                senses |= Senses.Substance;
            }

            return senses;
        }

        /// <summary>What a spell is made to be, read from what it does.</summary>
        public static IllusionForm? FormOf(SpellDefinition spell)
        {
            var text = $"{spell.Name} {spell.Notes}".ToLowerInvariant();
            return text.Contains("wound") || text.Contains("injury") ? IllusionForm.Wound
                : text.Contains("wall") || text.Contains("barricade") || text.Contains("rockfall") ? IllusionForm.Wall
                : text.Contains("figure") || text.Contains("likeness") || text.Contains("double") ? IllusionForm.Figure
                : text.Contains("voice") || text.Contains("lantern") || text.Contains("chant") || text.Contains("reinforce") ? IllusionForm.Reinforcements
                : null;
        }

        /// <summary>
        /// Where an Illusion goes: a figure on one open square, a wall or a host of reinforcements over
        /// the block of its Size at <see cref="SpellCast.Corner"/>, a wound into somebody within reach of
        /// a hand. Size sets the footprint, Range how far off it is put (Core Rules §11).
        /// </summary>
        private (string? Refusal, GridPosition Origin, List<GridPosition> Squares, List<Combatant> Caught, bool Touch) PlaceIllusion(
            Combatant caster, GridPosition from, SpellCast cast)
        {
            var spell = cast.Spell;
            if (HasIllusionActive(caster))
            {
                return ($"{caster.Name} already has an Illusion standing — only one at a time.", default, [], [], false);
            }

            if (FormOf(spell) is not { } form)
            {
                return ($"{spell.Name} isn't one of the things an Illusion can be in a fight here: a figure, a wall, reinforcements or a wound.", default, [], [], false);
            }

            var senses = SensesOf(spell);
            var needs = form switch
            {
                IllusionForm.Figure or IllusionForm.Wall => Senses.Sight,
                IllusionForm.Wound => Senses.Substance,
                _ => Senses.Sight | Senses.Sound,
            };
            if ((senses & needs) == 0)
            {
                return ($"A {form.ToString().ToLowerInvariant()} has to be {(needs == (Senses.Sight | Senses.Sound) ? "seen or heard" : needs == Senses.Sight ? "seen" : "felt")}, and {spell.Name} isn't.", default, [], [], false);
            }

            if (form == IllusionForm.Wound)
            {
                if (cast.Target is not { } subject || subject.Side == caster.Side || !subject.OccupiesSquare || subject.IsOutOfFight)
                {
                    return ($"{spell.Name} is written into the flesh of a foe within reach of a hand.", default, [], [], false);
                }

                if (caster.DistanceFrom(from, subject) > 1)
                {
                    return ($"{spell.Name} is cast at Touch — {subject.Name} has to be in reach of a hand.", default, [], [], false);
                }

                return (null, subject.Position, [], [subject], true);
            }

            if (cast.Corner is not { } corner)
            {
                return ($"{spell.Name} needs a place to stand.", default, [], [], false);
            }

            if (form == IllusionForm.Figure && caster.Build is null && caster.Statblock is null)
            {
                return ($"{caster.Name} has no likeness to put up.", default, [], [], false);
            }

            var squares = form == IllusionForm.Figure
                ? [corner]
                : SpellArea.Block(corner, spell.Size).Where(sq => !Setting.IsWall(sq) && At(sq) is null).ToList();
            if (squares.Count == 0 || squares.Any(sq => Setting.IsWall(sq) || At(sq) is not null))
            {
                return ("There's no open ground there for it to stand on.", default, [], [], false);
            }

            var origin = squares.OrderBy(sq => from.DistanceTo(sq)).First();
            return (null, origin, squares, [], false);
        }

        /// <summary>
        /// Puts the Illusion up: the cast roll is its Deceit (2d12 + Tier + cast ability, Core Rules
        /// §11). A felt wound is reckoned as the Harm it imitates would roll.
        /// </summary>
        private void CastIllusion(Combatant caster, SpellCast cast, CastPlan plan)
        {
            var spell = cast.Spell;
            var form = FormOf(spell)!.Value;
            var (helped, harried) = TakeAid(caster);
            var roll = RollAgainst(plan.CastBonus, 0, cast.Sacrifice || helped, AttacksAtDisadvantage(caster) || harried, caster);
            var deceit = roll.Total;

            Combatant? phantom = null;
            if (form == IllusionForm.Figure)
            {
                // In the likeness of its caster: a character's sheet, or a creature's statblock — on the
                // caster's own side, so the taken-in see one more of the enemy, a rival's too.
                phantom = caster.Build is { } build
                    ? CombatantFactory.FromCharacter(build, plan.Area[0], caster.Side)
                    : CombatantFactory.FromAdversary(caster.Statblock!, caster.Name, plan.Area[0]);
                // It goes by the name of whoever it is the likeness of: that's who the taken-in see.
                phantom.IsPhantom = true;
            }

            var subject = form == IllusionForm.Wound ? plan.Targets[0] : null;
            var felt = form == IllusionForm.Wound ? SpellDice(caster, spell, critical: false) : 0;
            var illusion = new Illusion
            {
                Caster = caster,
                Spell = spell.Name,
                Form = form,
                Senses = SensesOf(spell),
                Deceit = deceit,
                Squares = plan.Area.ToHashSet(),
                RoundsLeft = SpellCalculator.ControlIllusionDurationRounds(spell.SpellTier),
                Phantom = phantom,
                Subject = subject,
                FeltWounds = felt,
            };
            illusions.Add(illusion);

            Write($"{spell.Name}: {DescribeRoll(roll)} — Deceit {deceit}. " + form switch
            {
                IllusionForm.Figure => $"A figure in {caster.Name}'s likeness stands at {plan.Area[0]}.",
                IllusionForm.Wall => $"A wall stands over {plan.Area.Count} squares where there is none.",
                IllusionForm.Reinforcements => "Help seems to be coming.",
                _ => $"{subject!.Name} feels {felt} wound{(felt == 1 ? string.Empty : "s")} that {(felt == 1 ? "isn't" : "aren't")} there.",
            });

            if (form == IllusionForm.Reinforcements)
            {
                BreakTheWavering(illusion);
            }

            CheckMorale();
        }

        /// <summary>
        /// Help arriving breaks a taken-in foe only if it was already one step from breaking — one
        /// more ally down, or one more wound — and leaves anyone steadier where they were (the user's
        /// ruling, 2026-11-02).
        /// </summary>
        private void BreakTheWavering(Illusion illusion)
        {
            foreach (var creature in Fighting(Side.Adversaries == illusion.Caster.Side ? Side.Party : Side.Adversaries)
                         .Where(c => !c.IsBroken && c.Morale is not null && Fools(illusion, c)).ToList())
            {
                var morale = creature.Morale!;
                if (morale.IsWavering(creature, AlliesDown(creature), BelievedBody(creature)))
                {
                    creature.IsBroken = true;
                    Write($"{creature.Name} sees help coming for the other side, and that's enough ({morale.Trait}) — it breaks, and looks for a way out.");
                }
            }
        }

        private int AlliesDown(Combatant creature) =>
            combatants.Count(c => c.Side == creature.Side && c != creature && c.IsOutOfFight && !c.HasFled && !c.HasSurrendered);

        /// <summary>
        /// <paramref name="creature"/> sees through <paramref name="illusion"/> — and every other
        /// creature still taken in that saw what happened gets a free Search check against it,
        /// since its reality has just been called into question (the user's ruling, 2026-11-02).
        /// </summary>
        private void Exposed(Illusion illusion, Combatant? creature, GridPosition where, string what)
        {
            if (creature is not null && illusion.SeenThrough.Add(creature))
            {
                Write($"{what} — {creature.Name} sees {illusion.Spell} for what it is.");
            }
            else
            {
                Write($"{what}.");
            }

            foreach (var witness in combatants.Where(c => c != creature && c.OccupiesSquare && Fools(illusion, c)
                         && c.DistanceTo(where) <= MiddleRangeSquares()
                         && (Setting.Walls.Count == 0 || Sight.Clear(c.Position, where, Setting.IsWall))).ToList())
            {
                BeliefCheck(illusion, witness, free: true);
            }

            CheckMorale();
        }

        private int MiddleRangeSquares() => bands.Bands.Where(b => b.Name == DistanceBand.Middle).Select(b => b.MaxSquares).DefaultIfEmpty(8).First();

        /// <summary>
        /// A Search check against the Deceit: on a success the searcher perceives the Illusion for
        /// what it is, and everyone else remains taken in (Core Rules §11).
        /// </summary>
        private bool BeliefCheck(Illusion illusion, Combatant searcher, bool free)
        {
            var roll = RollAgainst(searcher.SkillBonus("Search"), illusion.Deceit, false, false, searcher);
            var sees = roll.Total >= illusion.Deceit;
            Write($"{searcher.Name} {(free ? "looks again at" : "spends an action testing")} {illusion.Spell}: Search {DescribeRoll(roll)} against Deceit {illusion.Deceit} — " +
                  (sees ? "sees it for what it is." : "still taken in."));
            if (sees)
            {
                illusion.SeenThrough.Add(searcher);
            }

            return sees;
        }

        /// <summary>Spends <paramref name="searcher"/>'s action testing <paramref name="illusion"/>.</summary>
        internal bool Search(Combatant searcher, Illusion illusion)
        {
            searcher.ActionsRemaining--;
            var sees = BeliefCheck(illusion, searcher, free: false);
            CheckMorale();
            return sees;
        }

        /// <summary>Why <paramref name="searcher"/> can't test <paramref name="illusion"/> now, or null.</summary>
        public string? SearchRefusal(Combatant searcher, Illusion illusion) =>
            !illusions.Contains(illusion) ? "That illusion is gone."
            : searcher.ActionsRemaining < 1 ? "Testing an illusion takes an action, and there's none left."
            : !Fools(illusion, searcher) ? $"{searcher.Name} isn't taken in by it."
            : null;

        /// <summary>A blow, a shot or a spell at a phantom: it passes straight through, and the action is spent on nothing.</summary>
        private bool PassesThroughPhantom(Combatant attacker, Combatant target, string with)
        {
            var illusion = target.IsPhantom ? illusions.FirstOrDefault(i => i.Phantom == target) : null;
            if (illusion is null)
            {
                return false;
            }

            Exposed(illusion, attacker, target.Position, $"{attacker.Name}'s {with} passes straight through {target.Name} — a phantom, {illusion.Spell}");
            return true;
        }

        /// <summary>A creature walking into a phantom wall or figure: it can't survive being walked through, so the walker sees through it.</summary>
        private void TouchIllusions(Combatant mover, IReadOnlyList<GridPosition> path, GridPosition reached)
        {
            var walked = path.TakeWhile(sq => sq != reached).Append(reached).ToList();
            foreach (var illusion in illusions.Where(i => i.Form is IllusionForm.Wall or IllusionForm.Figure && Fools(i, mover)).ToList())
            {
                if (walked.Where(illusion.Squares.Contains).Select(sq => (GridPosition?)sq).FirstOrDefault() is { } touched)
                {
                    Exposed(illusion, mover, touched, $"{mover.Name} walks straight into {(illusion.Form == IllusionForm.Wall ? "the wall" : "the figure")} and through it");
                }
            }
        }

        /// <summary>A shot or a spell flying through a phantom wall, seen by those who think it solid.</summary>
        private void ShotThroughWalls(Combatant shooter, GridPosition from, GridPosition to)
        {
            foreach (var illusion in illusions.Where(i => i.Form == IllusionForm.Wall).ToList())
            {
                if (!Sight.Clear(from, to, illusion.Squares.Contains))
                {
                    Exposed(illusion, Fools(illusion, shooter) ? shooter : null, illusion.Squares.First(),
                        $"{shooter.Name}'s shot flies clean through {illusion.Spell}");
                }
            }
        }

        /// <summary>A round gone by on every Illusion; those that have run their Tier's length fade, as do those of a fallen caster.</summary>
        private void AgeIllusions(bool roundPassed)
        {
            foreach (var illusion in illusions.ToList())
            {
                var caster = illusion.Caster;
                if (caster.IsUnconscious || caster.HasFled || caster.Body <= 0)
                {
                    illusions.Remove(illusion);
                    Write($"{illusion.Spell} fades with {caster.Name}.");
                    continue;
                }

                if (roundPassed && --illusion.RoundsLeft <= 0)
                {
                    illusions.Remove(illusion);
                    Write($"{illusion.Spell} runs its course and fades.");
                }
            }
        }
    }
}
