namespace WoundBound.Game.Combat
{
    /// <summary>
    /// What a place looks like, as against what it does (the user's request, 2026-11-08: trees,
    /// bushes, rocks, grass, water). The rest of <see cref="Setting"/> says what a square does — Full
    /// or Partial cover, Broken ground, a wall, water — and this says what it is: a tree or a crate
    /// stack, a bush or a barrel, grass or a dirt road. The rules never read it.
    ///
    /// <para>All of it is optional. Anything a scene leaves unsaid is worked out from the
    /// <see cref="Biome"/> and what the square does (<see cref="Figures.SceneryPlan"/>), and a prop
    /// that doesn't fit its square's footprint — a bush named on Full cover — is put right, so the
    /// board never lies about cover.</para>
    /// </summary>
    public sealed record Scenery
    {
        /// <summary>The kind of country, which decides what unnamed cover and ground look like.</summary>
        public Biome Biome { get; init; } = Biome.Meadow;

        /// <summary>What stands on a wall or cover square, where the scene names it.</summary>
        public IReadOnlyDictionary<GridPosition, Prop> Props { get; init; } = new Dictionary<GridPosition, Prop>();

        /// <summary>What the ground is, where the scene names it — a road, a yard of flagstones, mud.</summary>
        public IReadOnlyDictionary<GridPosition, Surface> Surfaces { get; init; } = new Dictionary<GridPosition, Surface>();

        /// <summary>
        /// High ground whose sides drop sheer — a rock face, a wall-walk — rather than banking down
        /// (the default). Either way the rules are the same: a square is raised or it isn't.
        /// </summary>
        public IReadOnlySet<GridPosition> Cliffs { get; init; } = new HashSet<GridPosition>();

        /// <summary>
        /// A battlemap picture to draw the board on, where the scene has one (the user, 2026-11-08): the
        /// art is the ground, and the props are still drawn over it so what can be hidden behind,
        /// broken or climbed is plain to see.
        /// </summary>
        public Backdrop? Backdrop { get; init; }

        /// <summary>The default: open meadow, everything worked out.</summary>
        public static Scenery Meadow { get; } = new();

        /// <summary>A surface over the given rectangles, inclusive, for writing a map.</summary>
        public static Dictionary<GridPosition, Surface> SurfaceOf(Surface surface, params (int X0, int Y0, int X1, int Y1)[] areas) =>
            Setting.Squares(areas).ToDictionary(sq => sq, _ => surface);

        /// <summary>A prop over the given rectangles, inclusive, for writing a map.</summary>
        public static Dictionary<GridPosition, Prop> PropOf(Prop prop, params (int X0, int Y0, int X1, int Y1)[] areas) =>
            Setting.Squares(areas).ToDictionary(sq => sq, _ => prop);

        /// <summary>Several maps as one; a later one wins a square named twice.</summary>
        public static Dictionary<GridPosition, T> Merge<T>(params IReadOnlyDictionary<GridPosition, T>[] maps)
        {
            var merged = new Dictionary<GridPosition, T>();
            foreach (var map in maps)
            {
                foreach (var (square, value) in map)
                {
                    merged[square] = value;
                }
            }

            return merged;
        }
    }

    /// <summary>
    /// A battlemap image and how its grid lies on the board: square (0, 0)'s top-left corner is at
    /// (<paramref name="OriginX"/>, <paramref name="OriginY"/>) in the image's pixels, and a square
    /// is <paramref name="PixelsPerSquare"/> across.
    /// </summary>
    public sealed record Backdrop(string Path, float OriginX, float OriginY, float PixelsPerSquare);

    /// <summary>The kind of country a fight is in.</summary>
    public enum Biome
    {
        /// <summary>Open grassland: trees and bushes for cover.</summary>
        Meadow,

        /// <summary>Under trees: moss and leaf litter, trees and undergrowth.</summary>
        Woodland,

        /// <summary>On a road or in a yard: dirt and gravel, crates and barrels.</summary>
        Road,

        /// <summary>Marsh: mud, reeds and standing water, scrubby trees.</summary>
        Fen,

        /// <summary>Houses and greens: flagstones, fences, timber and plaster.</summary>
        Village,

        /// <summary>Hills and broken stone: boulders, rocks, rock faces.</summary>
        Rocky,
    }

    /// <summary>What the ground is.</summary>
    public enum Surface
    {
        Grass,
        LongGrass,
        Moss,
        Leaves,
        Dirt,
        Road,
        Gravel,
        Mud,
        Sand,
        Flagstone,
        Stone,
    }

    /// <summary>
    /// What stands on a square that is solid — a wall or a piece of cover. Each is tall or low
    /// (<see cref="Props.IsTall"/>): Full cover and walls are drawn tall, Partial cover low.
    /// </summary>
    public enum Prop
    {
        // Tall: Full cover, or walls.
        Tree,
        Pine,
        Birch,
        CrateStack,
        Boulder,
        Pillar,
        StoneWall,
        HutWall,
        RockFace,

        // Low: Partial cover.
        Bush,
        Hedge,
        Rock,
        Crate,
        Barrel,
        Cart,
        Fence,
        LowWall,
        Log,
        Stump,
        Reeds,
    }

    /// <summary>What each <see cref="Prop"/> is like.</summary>
    public static class Props
    {
        /// <summary>Whether it stands taller than a person crouching — Full cover or a wall — rather than waist-high.</summary>
        public static bool IsTall(this Prop prop) => prop <= Prop.RockFace;

        /// <summary>Whether it is built — a wall, a hut — rather than cover something hides behind.</summary>
        public static bool IsWallLike(this Prop prop) => prop is Prop.StoneWall or Prop.HutWall or Prop.RockFace or Prop.Pillar;
    }
}
