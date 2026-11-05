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
        /// For a Bind: the Tier of effect to produce — "a caster may always produce a lower-Tier
        /// effect of either kind instead of their own" (Core Rules §11). Null for the spell's own.
        /// </summary>
        public Tier? BindAs { get; init; }

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

        /// <summary>
        /// The Shape it's cast in, where the caster names one: "Shape is free, and chosen in the moment"
        /// (Core Rules §11, "Spell Shape"). A spell over many may go as a Volume, a Line or a Cone; null
        /// casts it in the Shape it was written with.
        /// </summary>
        public SpellShape? Shape { get; init; }

        /// <summary>Where a Line or a Cone starts — its point of origin, priced by its distance like any other.</summary>
        public GridPosition? Origin { get; init; }

        /// <summary>Which of the eight ways a Line or a Cone is aimed, one square's step.</summary>
        public (int X, int Y)? Aim { get; init; }

        /// <summary>A Utility spell on the ground: +1 dries or firms it a grade, −1 fouls it — "in either direction" (Core Rules §9).</summary>
        public int GroundShift { get; init; } = 1;
    }

    /// <summary>
    /// What the rules make of a <see cref="SpellCast"/> before anything is paid or rolled: whether it
    /// can be cast at all, and if so where it lands, what it costs, what it rolls and who it catches.
    /// </summary>
    /// <summary>
    /// Where a cast would land and whom it would catch, or why it can't go there — the part of a
    /// <see cref="CastPlan"/> that doesn't turn on how it is paid for.
    /// </summary>
    public sealed record CastPlacement(string? Refusal, GridPosition Origin, SpellRange Range, List<GridPosition> Squares, List<Combatant> Caught, MotionKind? Motion)
    {
        public static CastPlacement Refused(string why) => new(why, default, SpellRange.Close, [], [], null);
    }

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
    /// <summary>The three kinds of Utility spell a fight can see (Core Rules §11).</summary>
    public enum UtilityKind
    {
        /// <summary>Moves light, weather or ground, and lasts on the ground it was put down on.</summary>
        Lever,

        /// <summary>Sees a distant place as it is now, from a viewing point that lasts the scene.</summary>
        Scrying,

        /// <summary>Reveals a present-tense truth: what carries active magic, and what is a lie.</summary>
        Sensing,
    }

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

        /// <summary>How many squares a Size buys a Line: 4, 9 or 16.</summary>
        public static int Length(SpellSize size) => size switch
        {
            SpellSize.Small => 4,
            SpellSize.Medium => 9,
            _ => 16,
        };

        /// <summary>The Shape <paramref name="cast"/> goes out in: the one named, else the one written, else a Ray.</summary>
        public static SpellShape ShapeOf(SpellCast cast) =>
            cast.Spell.Range == SpellRange.Touch ? SpellShape.None : cast.Shape ?? cast.Spell.Shape ?? SpellShape.Ray;

        /// <summary>
        /// Why <paramref name="spell"/> can't go out as <paramref name="shape"/>, or null if it can: a spell
        /// over many takes any of the three area Shapes — they cover the same squares' worth, strung
        /// differently — while one aimed at one creature is a Ray, and one at Touch has none.
        /// </summary>
        public static string? ShapeRefusal(SpellDefinition spell, SpellShape shape)
        {
            if (spell.Range == SpellRange.Touch || spell.Shape == SpellShape.None)
            {
                return shape == SpellShape.None ? null : "A Touch spell has no Shape: it's laid on a body.";
            }

            var area = shape is SpellShape.Volume or SpellShape.Line or SpellShape.Cone;
            return spell.Target == SpellTarget.Many
                ? area ? null : $"{spell.Name} is cast over many: a Volume, a Line or a Cone."
                : shape == SpellShape.Ray ? null : $"{spell.Name} is cast at one: a Ray.";
        }

        /// <summary>
        /// A Line: a straight run one square wide from the origin, as long as the Size allows, along a
        /// row, a column or a diagonal — corner to corner on the diagonal. It is aimed, not filled, so a
        /// wall stops it there.
        /// </summary>
        public static List<GridPosition> Line(GridPosition origin, (int X, int Y) aim, SpellSize size, Func<GridPosition, bool> isWall)
        {
            var squares = new List<GridPosition>();
            var at = origin;
            for (var i = 0; i < Length(size) && !isWall(at); i++)
            {
                squares.Add(at);
                at = new GridPosition(at.X + aim.X, at.Y + aim.Y);
            }

            return squares;
        }

        /// <summary>
        /// A Cone: one square wide at the origin and widening as it goes — rows of 1 and 3 at Small, 1, 3
        /// and 5 at Medium, 1, 3, 5 and 7 at Large. Along a diagonal it's the staircase triangle with its
        /// point at the origin: rows of 2 and 1, 4 to 1, or 5 to 1 — 3, 10 or 15 squares. Aimed, so
        /// whatever a wall hides from the origin it doesn't reach.
        /// </summary>
        public static List<GridPosition> Cone(GridPosition origin, (int X, int Y) aim, SpellSize size, Func<GridPosition, bool> isWall)
        {
            var squares = new List<GridPosition>();
            if (aim.X != 0 && aim.Y != 0)
            {
                var n = size switch { SpellSize.Small => 2, SpellSize.Medium => 4, _ => 5 };
                for (var i = 0; i < n; i++)
                {
                    for (var j = 0; i + j < n; j++)
                    {
                        squares.Add(new GridPosition(origin.X + aim.X * i, origin.Y + aim.Y * j));
                    }
                }
            }
            else
            {
                var rows = size switch { SpellSize.Small => 2, SpellSize.Medium => 3, _ => 4 };
                var across = (X: aim.Y, Y: aim.X);
                for (var k = 0; k < rows; k++)
                {
                    for (var w = -k; w <= k; w++)
                    {
                        squares.Add(new GridPosition(origin.X + aim.X * k + across.X * w, origin.Y + aim.Y * k + across.Y * w));
                    }
                }
            }

            return squares.Where(sq => !isWall(sq) && (sq == origin || Sight.Clear(origin, sq, isWall))).ToList();
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
        /// The lever a Utility spell moves, read from what it does: the ground, the weather (warmth or
        /// chill), or light. Null for one that touches none — a Scrying, a Sensing, a purified flask.
        /// </summary>
        public static Lever? LeverOf(SpellDefinition spell)
        {
            if (spell.SpellType != SpellType.Utility || UtilityKindOf(spell) != UtilityKind.Lever)
            {
                return null;
            }

            var text = SpellArea.TextOf(spell);
            return text.Contains("ground") || text.Contains("mud") || text.Contains("mire") || text.Contains("footing")
                ? Lever.Ground
                : text.Contains("warm") || text.Contains("chill") || text.Contains("cold") || text.Contains("heat")
                    ? Lever.Exposure
                    : text.Contains("light") || text.Contains("glow") || text.Contains("shine")
                        ? Lever.Light
                        : null;
        }

        /// <summary>
        /// What a Utility spell is for (Core Rules §11, "Utility Spells"): a Scrying spell, which sees a
        /// distant place as it is now; a Sensing spell, which reveals a present-tense truth; or one that
        /// moves a lever of the scene. Read from its name and notes, which say "scry" or "sense".
        /// </summary>
        public static UtilityKind? UtilityKindOf(SpellDefinition spell)
        {
            if (spell.SpellType != SpellType.Utility)
            {
                return null;
            }

            var text = SpellArea.TextOf(spell);
            return text.Contains("scry") ? UtilityKind.Scrying
                : text.Contains("sensing") || text.Contains("senses") || text.Contains("detect") ? UtilityKind.Sensing
                : UtilityKind.Lever;
        }

        /// <summary>
        /// Which Motion a spell is, read from what it does: one that moves the target is a Shift,
        /// one that hastens is a Quicken, and anything else that grips a body is a Bind.
        /// </summary>
        public static MotionKind MotionOf(SpellDefinition spell)
        {
            var text = SpellArea.TextOf(spell);
            return text.Contains("hasten") || text.Contains("quicken") ? MotionKind.Quicken
                : text.Contains("moves the target") || text.Contains("shift") || text.Contains("shove") || text.Contains("hurl") ? MotionKind.Shift
                : MotionKind.Bind;
        }

        /// <summary>
        /// A spell's name and notes, lower-cased, which the helpers here read what it does from — made
        /// once for each spell, which is never rewritten in play, since a brain asks on every square.
        /// </summary>
        internal static string TextOf(SpellDefinition spell) => Texts.GetValue(spell, static s => $"{s.Name} {s.Notes}".ToLowerInvariant());

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<SpellDefinition, string> Texts = new();

        /// <summary>
        /// The element an Elemental spell works, read from what it is: flame and fire, stone and
        /// earth, wind and gale, water. Null for a spell that names none.
        /// </summary>
        public static Element? ElementOf(SpellDefinition spell)
        {
            var text = SpellArea.TextOf(spell);
            return text.Contains("flame") || text.Contains("fire") || text.Contains("conflagration") ? Element.Fire
                : text.Contains("stone") || text.Contains("earth") ? Element.Earth
                : text.Contains("wind") || text.Contains("gale") || text.Contains("air") ? Element.Air
                : text.Contains("water") ? Element.Water
                : null;
        }

        /// <summary>
        /// The one Control effect a spell is written to produce, where it names one — a statblock's
        /// "Withering Glare (Control - Fear)", a catalogue entry that "applies the Compulsion effect" —
        /// or null where the caster picks on a success, as the rules' own framework has it.
        /// </summary>
        public static ControlEffect? FixedEffectOf(SpellDefinition spell)
        {
            var text = SpellArea.TextOf(spell);
            return text.Contains("control - fear") || text.Contains("applies frightened") ? ControlEffect.Fear
                : text.Contains("control - confusion") || text.Contains("confusion effect") ? ControlEffect.Confusion
                : text.Contains("control - compulsion") || text.Contains("compulsion effect") ? ControlEffect.Compulsion
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
