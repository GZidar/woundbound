using WoundBound.Rules.Calculation;
using WoundBound.Rules.Enums;

namespace WoundBound.Game.Combat
{
    /// <summary>
    /// What a creature can see, by the same two tests the rules put on a shot (Core Rules §9): a
    /// line, which walls and a full piece of cover in the middle of the room break; and light, since
    /// "at Visibility None nothing past Close can be seen". Poor is still seen — only shot at harder.
    /// Nightvision sees in the dark. Obscured scenes hold their grade evenly; Unlit ones are read from
    /// the light to the thing looked at, so a torchbearer is seen from across the field while the
    /// dark beyond their torch stays dark. This is what the fog of war is drawn from.
    /// </summary>
    public sealed partial class Encounter
    {
        /// <summary>Whether <paramref name="viewer"/> can see <paramref name="square"/> — the ground itself, lit as nobody carrying a light would be.</summary>
        public bool CanSee(Combatant viewer, GridPosition square) =>
            CanSee(viewer, square, Footprints.At(square, 1), () => VisibilityAtSquare(square));

        /// <summary>Whether <paramref name="viewer"/> can see <paramref name="target"/>, lit as it is — by its own light, and everyone else's.</summary>
        public bool CanSee(Combatant viewer, Combatant target) =>
            viewer == target
            || (target.OccupiesSquare && CanSee(viewer, target.Position, Footprints.At(target.Position, target.Across).ToList(), () => VisibilityOf(target)));

        /// <summary>Whether anybody of <paramref name="side"/> still in the fight can see <paramref name="target"/>; a side always knows where its own are.</summary>
        public bool SideSees(Side side, Combatant target) =>
            target.Side == side || combatants.Any(c => c.Side == side && Looks(c) && CanSee(c, target));

        /// <summary>Every square on the board anybody of <paramref name="side"/> still in the fight can see now.</summary>
        public IReadOnlySet<GridPosition> SightOf(Side side)
        {
            var viewers = combatants.Where(c => c.Side == side && Looks(c)).ToList();
            var seen = new HashSet<GridPosition>();
            for (var y = 0; y < Height; y++)
            {
                for (var x = 0; x < Width; x++)
                {
                    var square = new GridPosition(x, y);
                    Visibility? lit = null;
                    if (viewers.Any(v => CanSee(v, square, [square], () => lit ??= VisibilityAtSquare(square))))
                    {
                        seen.Add(square);
                    }
                }
            }

            return seen;
        }

        /// <summary>Awake and on the board — somebody dazed, unconscious, fled or surrendered is looking at nothing.</summary>
        private static bool Looks(Combatant c) => !c.IsOutOfFight && c.OccupiesSquare;

        private bool CanSee(Combatant viewer, GridPosition at, IEnumerable<GridPosition> squares, Func<Visibility> lit)
        {
            if (!Looks(viewer))
            {
                return false;
            }

            var eyes = Footprints.At(viewer.Position, viewer.Across).ToList();
            var stands = squares as IReadOnlyCollection<GridPosition> ?? squares.ToList();

            // Somebody standing in it, or right beside it, is seen whatever lies between.
            var distance = stands.Min(s => eyes.Min(e => e.DistanceTo(s)));
            if (distance > 1 && !eyes.Any(e => stands.Any(s => Setting.LineOpen(e, s))))
            {
                return false;
            }

            if (environment is not { } tables || Setting.Visibility == Visibility.Good || viewer.SeesInDark)
            {
                return true;
            }

            var band = RangeBandCalculator.BandForSquares(bands, distance) ?? DistanceBand.Long;
            return band == DistanceBand.Close || EnvironmentCalculator.AllowsTargeting(tables, lit(), band);
        }
    }
}
