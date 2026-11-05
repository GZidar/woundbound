namespace WoundBound.Game.Combat
{
    /// <summary>
    /// The ground a creature stands on: a square block, <c>across</c> squares on a side, its
    /// top-left corner at the creature's position (GM Reference §9: Large occupies 2×2, Huge 3×3).
    /// </summary>
    public static class Footprints
    {
        /// <summary>How many squares across a creature of <paramref name="size"/> stands.</summary>
        public static int AcrossFor(WoundBound.Rules.Enums.AdversarySize size) => size switch
        {
            WoundBound.Rules.Enums.AdversarySize.Large => 2,
            WoundBound.Rules.Enums.AdversarySize.Huge => 3,
            _ => 1,
        };

        public static IEnumerable<GridPosition> At(GridPosition corner, int across)
        {
            for (var dx = 0; dx < across; dx++)
            {
                for (var dy = 0; dy < across; dy++)
                {
                    yield return new GridPosition(corner.X + dx, corner.Y + dy);
                }
            }
        }

        /// <summary>Whether any square of the block at <paramref name="corner"/> is in <paramref name="squares"/>.</summary>
        public static bool AnyIn(GridPosition corner, int across, IReadOnlySet<GridPosition> squares)
        {
            for (var dx = 0; dx < across; dx++)
            {
                for (var dy = 0; dy < across; dy++)
                {
                    if (squares.Contains(new GridPosition(corner.X + dx, corner.Y + dy)))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public static bool Contains(GridPosition corner, int across, GridPosition square) =>
            square.X >= corner.X && square.X < corner.X + across && square.Y >= corner.Y && square.Y < corner.Y + across;

        /// <summary>
        /// Squares between the nearest squares of two blocks, a diagonal step counting one — the
        /// ordinary count (Core Rules §9, "Counting the squares") taken from the closest edge.
        /// </summary>
        public static int Distance(GridPosition a, int acrossA, GridPosition b, int acrossB)
        {
            var dx = Gap(a.X, acrossA, b.X, acrossB);
            var dy = Gap(a.Y, acrossA, b.Y, acrossB);
            return Math.Max(dx, dy);
        }

        private static int Gap(int a, int sizeA, int b, int sizeB) =>
            Math.Max(0, Math.Max(b - (a + sizeA - 1), a - (b + sizeB - 1)));
    }
}
