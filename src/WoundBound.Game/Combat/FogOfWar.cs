namespace WoundBound.Game.Combat
{
    /// <summary>How much of a square one side knows.</summary>
    public enum FogState
    {
        /// <summary>Never seen: nothing is known of it, not even the ground.</summary>
        Unseen,

        /// <summary>Seen before: the ground, walls and cover are remembered, but not who stands there now.</summary>
        Remembered,

        /// <summary>In sight now.</summary>
        Seen,
    }

    /// <summary>
    /// The fog of war for one side (the user's direction, 2026-11-04): what it sees now, what it has
    /// seen before and remembers the lie of, and what it has never seen. A creature of the other side
    /// is shown only while it is seen — a remembered square is ground, not a lookout. Presentation
    /// only: the rules already stop anybody targeting what they can't see.
    /// </summary>
    public sealed class FogOfWar
    {
        private readonly Encounter encounter;
        private readonly HashSet<GridPosition> explored = new();
        private IReadOnlySet<GridPosition> seen = new HashSet<GridPosition>();

        public FogOfWar(Encounter encounter, Side side = Side.Party)
        {
            this.encounter = encounter;
            Side = side;
            Update();
        }

        public Side Side { get; }

        /// <summary>The squares in sight now.</summary>
        public IReadOnlySet<GridPosition> Seen => seen;

        /// <summary>Every square ever in sight, those in sight now included.</summary>
        public IReadOnlySet<GridPosition> Explored => explored;

        /// <summary>
        /// Looks again, after anything that might change what's seen — a move, a light lit or let go,
        /// someone going down. True where anything changed.
        /// </summary>
        public bool Update()
        {
            var now = encounter.SightOf(Side);
            var before = explored.Count;
            explored.UnionWith(now);
            var changed = explored.Count != before || !now.SetEquals(seen);
            seen = now;
            return changed;
        }

        public FogState StateOf(GridPosition square) =>
            seen.Contains(square) ? FogState.Seen : explored.Contains(square) ? FogState.Remembered : FogState.Unseen;

        /// <summary>
        /// Whether <paramref name="creature"/> is shown: one of the side's own, or seen now by one of
        /// them — or, lying out of the fight, on ground in sight now.
        /// </summary>
        public bool Shows(Combatant creature) =>
            creature.Side == Side
            || (creature.OccupiesSquare ? encounter.SideSees(Side, creature) : !creature.HasFled && seen.Contains(creature.Position));

        /// <summary>The smallest rectangle around everything explored, for keeping the camera over known ground; null while nothing is.</summary>
        public (int X0, int Y0, int X1, int Y1)? ExploredBounds =>
            explored.Count == 0 ? null : (explored.Min(s => s.X), explored.Min(s => s.Y), explored.Max(s => s.X), explored.Max(s => s.Y));
    }
}
