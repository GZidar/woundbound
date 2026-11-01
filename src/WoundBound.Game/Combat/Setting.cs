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

        public Terrain GroundAt(GridPosition square) => Ground.TryGetValue(square, out var grade) ? grade : Terrain.Firm;

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
            (Ground.Count > 0 ? $", ground {string.Join(" and ", Ground.Values.Distinct().Select(g => g.ToString()))} in places" : string.Empty);
    }
}
