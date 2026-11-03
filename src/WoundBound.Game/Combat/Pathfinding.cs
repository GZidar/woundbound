namespace WoundBound.Game.Combat
{
    /// <summary>How a move to a square would go: what it costs, the squares it crosses, and whether any of them must be swum.</summary>
    /// <param name="Path">Every square crossed, where the mover stands first and the destination last.</param>
    public sealed record MovePlan(int Cost, IReadOnlyList<GridPosition> Path, bool SwimsDeepWater);

    /// <summary>
    /// The shortest route the ground allows (Core Rules §9, "Counting the squares"): around walls,
    /// pillars and anything else solid, never through them, a diagonal step counting one. An
    /// enemy's square is never open, nor is an ally's with an enemy in reach of it; any other ally's
    /// may be passed through, but not stopped in. A move is priced by its length's band, and one
    /// more where it begins, ends or passes through Broken ground — so where a dry way round costs
    /// less than wading straight across, the dry way is the one taken.
    /// </summary>
    internal sealed class MovementMap
    {
        private readonly Dictionary<GridPosition, MovePlan> plans = new();

        /// <param name="from">Where the mover stands.</param>
        /// <param name="radius">How far, in squares of route, to look.</param>
        /// <param name="isWall">Solid.</param>
        /// <param name="isClosed">A square nobody on the mover's side may pass through.</param>
        /// <param name="canStop">A square the mover may end on.</param>
        /// <param name="isBroken">Broken ground, water included.</param>
        /// <param name="isDeep">Deep water, which must be swum.</param>
        /// <param name="price">The band cost of a route this many squares long, or null beyond reach.</param>
        public MovementMap(GridPosition from, int radius, Func<GridPosition, bool> isWall, Func<GridPosition, bool> isClosed,
            Func<GridPosition, bool> canStop, Func<GridPosition, bool> isBroken, Func<GridPosition, bool> isDeep,
            Func<int, int?> price, int brokenTax)
        {
            // Two layers: routes that have touched no Broken ground yet, and routes that have.
            var startsBroken = isBroken(from);
            var length = new Dictionary<(GridPosition Square, bool Taxed), int> { [(from, startsBroken)] = 0 };
            var parent = new Dictionary<(GridPosition, bool), (GridPosition, bool)>();
            var queue = new Queue<(GridPosition Square, bool Taxed)>();
            queue.Enqueue((from, startsBroken));

            while (queue.TryDequeue(out var at))
            {
                var steps = length[at];
                if (steps >= radius)
                {
                    continue;
                }

                foreach (var next in Neighbours(at.Square, isWall))
                {
                    if (isClosed(next))
                    {
                        continue;
                    }

                    var state = (next, at.Taxed || isBroken(next));
                    if (length.ContainsKey(state))
                    {
                        continue;
                    }

                    length[state] = steps + 1;
                    parent[state] = at;
                    queue.Enqueue(state);
                }
            }

            foreach (var ((square, taxed), steps) in length)
            {
                if (square == from || !canStop(square) || price(steps) is not { } band)
                {
                    continue;
                }

                var cost = band + (taxed ? brokenTax : 0);
                if (plans.TryGetValue(square, out var known) && known.Cost <= cost)
                {
                    continue;
                }

                var path = new List<GridPosition> { square };
                var walk = (square, taxed);
                while (parent.TryGetValue(walk, out var back))
                {
                    path.Add(back.Item1);
                    walk = back;
                }

                path.Reverse();
                plans[square] = new MovePlan(cost, path, path.Skip(1).Any(isDeep));
            }
        }

        public MovePlan? To(GridPosition square) => plans.TryGetValue(square, out var plan) ? plan : null;

        public IEnumerable<KeyValuePair<GridPosition, MovePlan>> All => plans;

        /// <summary>The eight squares around <paramref name="at"/>, but not a diagonal squeezed between two walls.</summary>
        private static IEnumerable<GridPosition> Neighbours(GridPosition at, Func<GridPosition, bool> isWall)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0)
                    {
                        continue;
                    }

                    var next = new GridPosition(at.X + dx, at.Y + dy);
                    if (isWall(next))
                    {
                        continue;
                    }

                    if (dx != 0 && dy != 0 && isWall(new GridPosition(at.X + dx, at.Y)) && isWall(new GridPosition(at.X, at.Y + dy)))
                    {
                        continue;
                    }

                    yield return next;
                }
            }
        }
    }

    /// <summary>Whether anything solid stands between two squares.</summary>
    internal static class Sight
    {
        /// <summary>
        /// Whether the straight line between the centres of <paramref name="from"/> and
        /// <paramref name="to"/> passes through no wall — the squares at either end don't count.
        /// The rules count distance "around walls … never through them"; a shot or a light is held
        /// to the same.
        /// </summary>
        public static bool Clear(GridPosition from, GridPosition to, Func<GridPosition, bool> isWall) =>
            !Between(from, to).Any(isWall);

        /// <summary>
        /// The squares the straight line between the centres of <paramref name="from"/> and
        /// <paramref name="to"/> passes through, nearest <paramref name="from"/> first — the squares
        /// at either end left out.
        /// </summary>
        public static IEnumerable<GridPosition> Between(GridPosition from, GridPosition to)
        {
            var steps = Math.Max(Math.Abs(to.X - from.X), Math.Abs(to.Y - from.Y)) * 4;
            GridPosition? last = null;
            for (var i = 1; i < steps; i++)
            {
                var t = (double)i / steps;
                var square = new GridPosition(
                    (int)Math.Round(from.X + (to.X - from.X) * t, MidpointRounding.AwayFromZero),
                    (int)Math.Round(from.Y + (to.Y - from.Y) * t, MidpointRounding.AwayFromZero));
                if (square != from && square != to && square != last)
                {
                    last = square;
                    yield return square;
                }
            }
        }
    }
}
