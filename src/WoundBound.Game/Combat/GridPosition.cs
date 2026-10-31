namespace WoundBound.Game.Combat
{
    /// <summary>A square on the battle grid.</summary>
    public readonly record struct GridPosition(int X, int Y)
    {
        /// <summary>
        /// Squares between two positions, counting a diagonal step as one (Core Rules §9,
        /// "Counting the squares"). The slice has no walls, so the shortest route is a straight one.
        /// </summary>
        public int DistanceTo(GridPosition other) => Math.Max(Math.Abs(X - other.X), Math.Abs(Y - other.Y));

        public override string ToString() => $"({X},{Y})";
    }
}
