using WoundBound.Rules.Enums;

namespace WoundBound.Game.Combat
{
    /// <summary>
    /// Where a fight happens, as the three environmental levers describe it (Core Rules §9,
    /// "Environment"). Visibility and Exposure belong to the scene; Terrain belongs to the map,
    /// square by square, since "a single fight may hold all three grades at once" — anywhere left
    /// unmarked is Firm (GM Reference §13). A GM who says nothing gets the neutral grade on all
    /// three: <see cref="Daylight"/>.
    /// </summary>
    public sealed record Setting
    {
        public string Name { get; init; } = "Daylight";

        /// <summary>
        /// The scene's Visibility before any light: Good, Poor or None. In an Unlit scene this is
        /// None, and the lights burning in it raise it around themselves.
        /// </summary>
        public Visibility Visibility { get; init; } = Visibility.Good;

        /// <summary>Why it's hard to see — no light, which light answers, or something in the way, which it doesn't. Null at Good.</summary>
        public VisibilityCause? Cause { get; init; }

        public Exposure Exposure { get; init; } = Exposure.Normal;

        /// <summary>The ground, where it isn't Firm.</summary>
        public IReadOnlyDictionary<GridPosition, Terrain> Ground { get; init; } = new Dictionary<GridPosition, Terrain>();

        /// <summary>Built or sited fires — the only light that holds a grade out to Long.</summary>
        public IReadOnlySet<GridPosition> Fires { get; init; } = new HashSet<GridPosition>();

        /// <summary>
        /// How much of each element the place offers an Elemental caster (Core Rules §14, "Ambient
        /// presence") — a breeze or a gale, loose soil or bedrock, a building alight or a forge in
        /// use. Water is also read from the water on the map.
        /// </summary>
        public IReadOnlyDictionary<Rules.Enums.Element, Rules.Enums.AmbientPresence> Elements { get; init; } =
            new Dictionary<Rules.Enums.Element, Rules.Enums.AmbientPresence>();

        /// <summary>Walls, pillars and anything else solid: nobody stands in them, moves through them, or sees or shoots through them.</summary>
        public IReadOnlySet<GridPosition> Walls { get; init; } = new HashSet<GridPosition>();

        /// <summary>Water, by how deep it is. Any of it is Broken ground; deep water must be swum.</summary>
        public IReadOnlyDictionary<GridPosition, WaterDepth> Water { get; init; } = new Dictionary<GridPosition, WaterDepth>();

        /// <summary>What swimming through deep water is rolled against: Athletics, at this Difficulty.</summary>
        public int SwimDifficulty { get; init; } = 18;

        /// <summary>Hazards written into the place (GM Reference §13).</summary>
        public IReadOnlyList<Hazard> Hazards { get; init; } = [];

        /// <summary>The ground underfoot: water is Broken, whatever lies under it; anywhere unmarked is Firm.</summary>
        public Terrain GroundAt(GridPosition square) =>
            Water.ContainsKey(square) ? Terrain.Broken : Ground.TryGetValue(square, out var grade) ? grade : Terrain.Firm;

        public bool IsWall(GridPosition square) => Walls.Contains(square);

        /// <summary>
        /// <see cref="IsWall"/> as one delegate, made once for each setting: the line-of-sight and
        /// spell-shape tests are asked it on every square a brain weighs. Held outside the record, so a
        /// copy made with <c>with</c> never inherits a test of the old one's walls.
        /// </summary>
        public Func<GridPosition, bool> WallTest => WallTests.GetValue(this, static s => s.IsWall);

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Setting, Func<GridPosition, bool>> WallTests = new();

        /// <summary>
        /// Cover (Core Rules §9, "Cover"): the solid things on the map a creature crouches behind —
        /// a low wall, a cart, a barrel (Partial), a pillar or a stack of crates taller than a man
        /// (Full). Marked where they stand, like the ground. Nobody stands in or walks through one.
        /// Unlike a <see cref="Walls">wall</see>, nothing here stops a shot or a look outright:
        /// what it does is give cover to whoever stands right behind it.
        /// </summary>
        public IReadOnlyDictionary<GridPosition, Cover> CoverPieces { get; init; } = new Dictionary<GridPosition, Cover>();

        /// <summary>The cover a piece on <paramref name="square"/> gives, or None where there is none.</summary>
        public Cover CoverAt(GridPosition square) => CoverPieces.TryGetValue(square, out var grade) ? grade : Cover.None;

        /// <summary>
        /// The cover a creature <paramref name="across"/> squares wide, standing on <paramref name="at"/>,
        /// has against an attack from <paramref name="from"/> (Core Rules §9, "Cover"): the best piece on
        /// the line that stands right beside it.
        /// </summary>
        public Cover CoverAgainst(GridPosition from, GridPosition at, int across = 1)
        {
            if (CoverPieces.Count == 0)
            {
                return Cover.None;
            }

            var stands = Footprints.At(at, across).ToList();
            return Rules.Calculation.CoverCalculator.Best(Sight.Between(from, at)
                .Where(sq => !stands.Contains(sq) && stands.Any(s => s.DistanceTo(sq) <= 1))
                .Select(CoverAt));
        }

        /// <summary>
        /// Whether a line between two squares is open: no wall on it, and no full piece of cover that
        /// stands beside neither end — a pillar in the middle of the room blocks a look across it,
        /// where one beside the archer is only leant round.
        /// </summary>
        public bool LineOpen(GridPosition from, GridPosition to) =>
            (Walls.Count == 0 || Sight.Clear(from, to, WallTest))
            && (CoverPieces.Count == 0
                || !Sight.Between(from, to).Any(sq => CoverAt(sq) == Cover.Full && sq.DistanceTo(from) > 1 && sq.DistanceTo(to) > 1));

        /// <summary>Whether anything solid stands on <paramref name="square"/> — a wall or a piece of cover: nobody stands in it or moves through it.</summary>
        public bool IsSolid(GridPosition square) => Walls.Contains(square) || CoverPieces.ContainsKey(square);

        /// <summary>Cover pieces of one grade over the given rectangles, inclusive, for writing a map.</summary>
        public static Dictionary<GridPosition, Cover> CoverOf(Cover grade, params (int X0, int Y0, int X1, int Y1)[] areas) =>
            Patches(Terrain.Firm, areas).Keys.ToDictionary(sq => sq, _ => grade);

        public bool IsDeepWater(GridPosition square) => Water.TryGetValue(square, out var depth) && depth == WaterDepth.Deep;

        /// <summary>The hazard covering <paramref name="square"/>, if any.</summary>
        public Hazard? HazardAt(GridPosition square) => Hazards.FirstOrDefault(h => h.Squares.Contains(square));

        /// <summary>The squares of the given rectangles, inclusive.</summary>
        public static HashSet<GridPosition> Squares(params (int X0, int Y0, int X1, int Y1)[] areas) => Patches(Terrain.Firm, areas).Keys.ToHashSet();

        /// <summary>Hard to see for want of light, which light can answer.</summary>
        public bool IsUnlit => Visibility != Visibility.Good && (Cause ?? VisibilityCause.Unlit) == VisibilityCause.Unlit;

        public static Setting Daylight { get; } = new();

        /// <summary>Ground of one grade over the given rectangles, inclusive, for writing a map.</summary>
        public static Dictionary<GridPosition, Terrain> Patches(Terrain grade, params (int X0, int Y0, int X1, int Y1)[] areas)
        {
            var ground = new Dictionary<GridPosition, Terrain>();
            foreach (var (x0, y0, x1, y1) in areas)
            {
                for (var x = Math.Min(x0, x1); x <= Math.Max(x0, x1); x++)
                {
                    for (var y = Math.Min(y0, y1); y <= Math.Max(y0, y1); y++)
                    {
                        ground[new GridPosition(x, y)] = grade;
                    }
                }
            }

            return ground;
        }

        /// <summary>The scene in a line, for the screen and the log.</summary>
        public string Describe() =>
            $"{Name}: Visibility {(Visibility == Visibility.Good ? "Good" : $"{Visibility} ({Cause ?? VisibilityCause.Unlit})")}, " +
            $"Exposure {Exposure}" +
            (Ground.Count > 0 ? $", ground {string.Join(" and ", Ground.Values.Distinct().Select(g => g.ToString()))} in places" : string.Empty) +
            (Water.Count > 0 ? ", water" : string.Empty) +
            (CoverPieces.Count > 0 ? $", cover ({string.Join(" and ", CoverPieces.Values.Distinct().Order().Select(g => g.ToString().ToLowerInvariant()))})" : string.Empty) +
            string.Concat(Hazards.Select(h => $", {h.Name}"));
    }

    /// <summary>How deep water is: Shallow is wading, ankle to knee; Deep is waist-deep and over, and has to be swum.</summary>
    public enum WaterDepth
    {
        Shallow,
        Deep,
    }

    /// <summary>One way of beating a hazard: a skill, at a Difficulty (GM Reference §13 — "name the skill and the Difficulty").</summary>
    public sealed record HazardCheck(string Skill, int Difficulty);

    /// <summary>
    /// A hazard written into a place, the way GM Reference §13 builds them: something to beat —
    /// any of its <see cref="Avoid"/> checks, whichever the creature is best at — and a clear effect
    /// on a failure. Met whenever a creature enters its ground, by its own feet or by force.
    /// Noticing it first (the Detect check) isn't modelled: everyone knows where it is.
    /// </summary>
    public sealed record Hazard(string Name, string Description, IReadOnlySet<GridPosition> Squares, IReadOnlyList<HazardCheck> Avoid, HazardEffect Effect);

    /// <summary>What a hazard does to whoever fails against it.</summary>
    public abstract record HazardEffect
    {
        private HazardEffect()
        {
        }

        /// <summary>
        /// Swept <paramref name="Squares"/> along (<paramref name="Dx"/>, <paramref name="Dy"/>) —
        /// Forced Movement — and, with <paramref name="LosesFooting"/>, an action spent regaining
        /// their footing before they can act normally again.
        /// </summary>
        public sealed record Swept(int Squares, int Dx, int Dy, bool LosesFooting) : HazardEffect;
    }
}
