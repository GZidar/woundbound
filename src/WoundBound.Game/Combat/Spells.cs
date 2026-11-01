using WoundBound.Rules.Enums;
using WoundBound.Rules.Models;

namespace WoundBound.Game.Combat
{
    /// <summary>
    /// The three kinds of Motion spell (Core Rules §11, "Motion Spells"): <see cref="Bind"/> denies a
    /// target their own movement, <see cref="Shift"/> imposes movement on them, <see cref="Quicken"/>
    /// lends them speed. A spell doesn't record which it is, so it is read from what it does.
    /// </summary>
    public enum MotionKind
    {
        Bind,
        Shift,
        Quicken,
    }

    /// <summary>
    /// What a Control spell does to whoever it lands on — the caster's pick, on a success (Core Rules
    /// §11, "Control Spells").
    /// </summary>
    public enum ControlEffect
    {
        /// <summary>Frightened: gives ground a band a turn, with Disadvantage while the source can be seen.</summary>
        Fear,

        /// <summary>Confused: its action goes on attacking a creature in reach, chosen at random — allies count.</summary>
        Confusion,

        /// <summary>Compelled: carries out the caster's one directive on each of its turns.</summary>
        Compulsion,
    }

    /// <summary>
    /// One casting, as the caster declares it (Core Rules §11): the spell, where it goes, and the
    /// choices made in the same breath — what pays, whether it is Sustained, which Threshold a Buff
    /// or Bane moves. Range is not chosen: it is read off where the point of origin falls.
    /// </summary>
    /// <param name="Spell">One of the caster's own spells.</param>
    public sealed record SpellCast(SpellDefinition Spell)
    {
        /// <summary>The creature a Ray or a Touch spell goes into.</summary>
        public Combatant? Target { get; init; }

        /// <summary>
        /// For a Volume: the block's corner with the least X and Y. The block is always a block
        /// (Core Rules §11, "Placing a Volume"), and the point of origin is taken as the square of it
        /// nearest the caster — the cheapest place the origin could be, and anywhere in the block will do.
        /// </summary>
        public GridPosition? Corner { get; init; }

        /// <summary>The Threshold a Buff or a Bane moves — the caster's choice. Null takes the spell's own.</summary>
        public ThresholdType? Threshold { get; init; }

        /// <summary>The track a Heal restores, chosen freely (Core Rules §11, "Heal / Harm Spells"). Null restores Body.</summary>
        public WoundType? Restores { get; init; }

        /// <summary>Pay an Aether or Divine spell's cost out of Body instead (Core Rules §11, "Bleeding for Power").</summary>
        public bool PayInBody { get; init; }

        /// <summary>Pay 1 Stamina instead of 1 wound of the cost — a Cerebral caster, once a round (Core Rules §3).</summary>
        public bool Overclock { get; init; }

        /// <summary>
        /// Let a Mind or Soul cost that would reach 0 hold the track at 1 and take the excess from
        /// Body instead (Core Rules §4, "Backlash on a spend").
        /// </summary>
        public bool Backlash { get; init; }

        /// <summary>Pay 1 Body before the roll for Advantage on it — a Body-secondary caster, once a round (Core Rules §3).</summary>
        public bool Sacrifice { get; init; }

        /// <summary>Declared Sustained, to be held past the start of the caster's next turn for Stamina (Core Rules §11).</summary>
        public bool Sustain { get; init; }

        /// <summary>
        /// The fire, water or earth an Elemental spell draws on (Core Rules §14, "A carried or built
        /// source"), spent whole: <see cref="FuelSource.Torch"/> or <see cref="FuelSource.Lantern"/>
        /// in the caster's hand — or, with <see cref="TapAt"/>, a torch burning on the ground there;
        /// <see cref="FuelSource.Campfire"/> for a built fire at <see cref="TapAt"/>. None pays from
        /// the pack after whatever the place offers.
        /// </summary>
        public FuelSource Tap { get; init; } = FuelSource.None;

        /// <summary>Where the tapped source is, when it isn't in the caster's hand.</summary>
        public GridPosition? TapAt { get; init; }

        /// <summary>What a Control spell does to those it lands on. Fear unless chosen otherwise.</summary>
        public ControlEffect Effect { get; init; } = ControlEffect.Fear;

        /// <summary>
        /// A Compulsion's directive: "attack that guard" names who; null is "drop your weapon and
        /// kneel" — no action, no move, for as long as it holds.
        /// </summary>
        public Combatant? Directed { get; init; }

        /// <summary>A Shift's straight line, one square's step — any of the eight ways. Null drives the target straight away from the caster.</summary>
        public (int X, int Y)? Direction { get; init; }

        /// <summary>How far a Shift carries them, up to what its Tier allows. Null goes as far as it can.</summary>
        public int? Squares { get; init; }
    }

    /// <summary>
    /// What the rules make of a <see cref="SpellCast"/> before anything is paid or rolled: whether it
    /// can be cast at all, and if so where it lands, what it costs, what it rolls and who it catches.
    /// </summary>
    public sealed record CastPlan(
        string? Refusal,
        GridPosition Origin = default,
        SpellRange Range = SpellRange.Close,
        int Cost = 0,
        WoundType? Track = null,
        Ability CastAbility = Ability.Intellect,
        int CastBonus = 0,
        IReadOnlyList<GridPosition>? Squares = null,
        IReadOnlyList<Combatant>? Caught = null,
        MotionKind? Motion = null)
    {
        public bool CanCast => Refusal is null;

        /// <summary>The wounds the cast will cost once Overclock has paid its share.</summary>
        public int WoundsPaid { get; init; }

        /// <summary>How an Elemental spell's Fuel is paid: ambient presence, the tapped source, the pack. Null for Pure Magic.</summary>
        public Rules.Models.FuelPayment? Fuel { get; init; }

        /// <summary>Every square the spell covers — one for a Ray, none for a Touch.</summary>
        public IReadOnlyList<GridPosition> Area => Squares ?? [];

        /// <summary>Everybody the spell catches.</summary>
        public IReadOnlyList<Combatant> Targets => Caught ?? [];

        public static CastPlan Refused(string why) => new(why);
    }

    /// <summary>The ground a spell covers (Core Rules §11, "Spell Shape").</summary>
    public static class SpellArea
    {
        /// <summary>How many squares across a Volume of <paramref name="size"/> is: 2, 3 or 4.</summary>
        public static int Across(SpellSize size) => size switch
        {
            SpellSize.Small => 2,
            SpellSize.Medium => 3,
            _ => 4,
        };

        /// <summary>The block's squares, its least corner at <paramref name="corner"/>.</summary>
        public static IEnumerable<GridPosition> Block(GridPosition corner, SpellSize size)
        {
            var across = Across(size);
            for (var dx = 0; dx < across; dx++)
            {
                for (var dy = 0; dy < across; dy++)
                {
                    yield return new GridPosition(corner.X + dx, corner.Y + dy);
                }
            }
        }

        /// <summary>
        /// The squares a Volume reaches: "trace from the point of origin through open space, staying
        /// inside the block. Anything you can reach that way is caught" — a wall with no way past
        /// shields what is behind it, and nothing else does.
        /// </summary>
        public static List<GridPosition> Reached(GridPosition corner, SpellSize size, GridPosition origin, Func<GridPosition, bool> isWall)
        {
            var block = Block(corner, size).ToHashSet();
            var reached = new List<GridPosition>();
            var seen = new HashSet<GridPosition> { origin };
            var frontier = new Queue<GridPosition>();
            frontier.Enqueue(origin);
            while (frontier.TryDequeue(out var at))
            {
                if (isWall(at))
                {
                    continue;
                }

                reached.Add(at);
                for (var dx = -1; dx <= 1; dx++)
                {
                    for (var dy = -1; dy <= 1; dy++)
                    {
                        var next = new GridPosition(at.X + dx, at.Y + dy);
                        if (block.Contains(next) && seen.Add(next))
                        {
                            frontier.Enqueue(next);
                        }
                    }
                }
            }

            return reached;
        }

        /// <summary>Every corner that puts a block of <paramref name="size"/> over <paramref name="square"/>.</summary>
        public static IEnumerable<GridPosition> CornersCovering(GridPosition square, SpellSize size)
        {
            var across = Across(size);
            for (var dx = 0; dx < across; dx++)
            {
                for (var dy = 0; dy < across; dy++)
                {
                    yield return new GridPosition(square.X - dx, square.Y - dy);
                }
            }
        }

        /// <summary>
        /// Which Motion a spell is, read from what it does: one that moves the target is a Shift,
        /// one that hastens is a Quicken, and anything else that grips a body is a Bind.
        /// </summary>
        public static MotionKind MotionOf(SpellDefinition spell)
        {
            var text = $"{spell.Name} {spell.Notes}".ToLowerInvariant();
            return text.Contains("hasten") || text.Contains("quicken") ? MotionKind.Quicken
                : text.Contains("moves the target") || text.Contains("shift") || text.Contains("shove") || text.Contains("hurl") ? MotionKind.Shift
                : MotionKind.Bind;
        }

        /// <summary>
        /// The element an Elemental spell works, read from what it is: flame and fire, stone and
        /// earth, wind and gale, water. Null for a spell that names none.
        /// </summary>
        public static Element? ElementOf(SpellDefinition spell)
        {
            var text = $"{spell.Name} {spell.Notes}".ToLowerInvariant();
            return text.Contains("flame") || text.Contains("fire") || text.Contains("conflagration") ? Element.Fire
                : text.Contains("stone") || text.Contains("earth") ? Element.Earth
                : text.Contains("wind") || text.Contains("gale") || text.Contains("air") ? Element.Air
                : text.Contains("water") ? Element.Water
                : null;
        }

        /// <summary>How far a Shift carries its target, by Tier: Short, Middle, Long — 4, 8 or 16 squares (Core Rules §11).</summary>
        public static int ShiftSquares(Tier tier) => tier switch
        {
            Tier.T1 => 4,
            Tier.T2 => 8,
            _ => 16,
        };
    }
}
