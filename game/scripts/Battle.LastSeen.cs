using System.Collections.Generic;
using System.Linq;
using Godot;
using WoundBound.Game.Combat;
using Side = WoundBound.Game.Combat.Side;

namespace WoundBoundGame
{
    /// <summary>
    /// Where the party last saw each foe, in the Players' view. A foe that slips out of sight
    /// leaves a ghost on the last square the party saw it on, faded and dashed with a "?", and
    /// stays on the roster as a hollow icon. Picking it there goes to the ghost, never to where the
    /// foe really is. The ghost goes when the foe is seen again, or once it has left the fight.
    /// </summary>
    public partial class Battle
    {
        /// <summary>Where each foe was last seen, and whether it was down when it was.</summary>
        private readonly Dictionary<Combatant, (GridPosition At, bool Down)> lastSeen = new();

        /// <summary>Notes where every foe the party sees now stands; the rest keep where they were last seen.</summary>
        private void UpdateLastSeen()
        {
            if (fog is null)
            {
                lastSeen.Clear();
                return;
            }

            foreach (var foe in encounter.Combatants.Where(c => c.Side == Side.Adversaries))
            {
                if (foe.HasFled)
                {
                    lastSeen.Remove(foe);
                }
                else if (Shown(foe))
                {
                    lastSeen[foe] = (foe.Position, foe.IsOutOfFight);
                }
            }
        }

        /// <summary>
        /// A foe's walk, as the party saw it: the last square of its route they could see, if it
        /// went on out of sight — so the ghost stands where it vanished, not where it began.
        /// </summary>
        private void NoteWalk(Moved move)
        {
            if (fog is null || move.Mover.Side != Side.Adversaries || move.Path.Count < 2)
            {
                return;
            }

            var sight = encounter.SightOf(Side.Party);
            var last = move.Path.ToList().FindLastIndex(sight.Contains);
            if (last >= 0 && last < move.Path.Count - 1)
            {
                lastSeen[move.Mover] = (move.Path[last], false);
            }
        }

        /// <summary>A foe the party has seen but doesn't see now, and where.</summary>
        private (GridPosition At, bool Down)? GhostOf(Combatant foe) =>
            fog is not null && foe.Side == Side.Adversaries && !foe.HasFled && !Shown(foe) && lastSeen.TryGetValue(foe, out var seen) ? seen : null;

        /// <summary>The middle of the place <paramref name="who"/> is shown at, in squares: where it is, or where its ghost stands.</summary>
        private Vector2 PlaceOf(Combatant who) =>
            GhostOf(who) is not null ? GhostSpot(who).Middle : Middle(who, who.Position);

        /// <summary><paramref name="who"/>'s middle standing at <paramref name="at"/>, pinned to the map's edge if that's past it.</summary>
        private Vector2 Middle(Combatant who, GridPosition at) =>
            new(Mathf.Clamp(at.X, 0, encounter.Width - 1) + who.Across / 2f, Mathf.Clamp(at.Y, 0, encounter.Height - 1) + who.Across / 2f);

        /// <summary>The corners a ghost moves to, in turn, when its square is taken: up right, down right, down left, up left.</summary>
        private static readonly Vector2[] GhostCorners = { new(0.3f, -0.3f), new(0.3f, 0.3f), new(-0.3f, 0.3f), new(-0.3f, -0.3f) };

        /// <summary>
        /// Where <paramref name="ghost"/> is drawn, in squares, and how big. Alone on its square it
        /// stands in the middle, as a token would — smaller past the edge, as tokens are there. If a
        /// token is drawn on that square, or another ghost came first, it shrinks into a corner, so
        /// the two never cover each other.
        /// </summary>
        private (Vector2 Middle, float Radius) GhostSpot(Combatant ghost)
        {
            var at = lastSeen[ghost].At;
            var square = new GridPosition(Mathf.Clamp(at.X, 0, encounter.Width - 1), Mathf.Clamp(at.Y, 0, encounter.Height - 1));
            var middle = Middle(ghost, at);
            var offMap = square != at;

            bool Covers(Combatant c, GridPosition from)
            {
                var x = Mathf.Clamp(from.X, 0, encounter.Width - 1);
                var y = Mathf.Clamp(from.Y, 0, encounter.Height - 1);
                return square.X < x + c.Across && x < square.X + ghost.Across && square.Y < y + c.Across && y < square.Y + ghost.Across;
            }

            var taken = encounter.Combatants.Any(c => c != ghost && !c.HasFled && Shown(c) && Covers(c, c.Position));
            var before = 0;
            foreach (var other in encounter.Combatants)
            {
                if (other == ghost)
                {
                    break;
                }

                if (GhostOf(other) is { } seen && Covers(other, seen.At))
                {
                    before++;
                }
            }

            if (!taken && before == 0)
            {
                return (middle, ghost.Across * (offMap ? 0.22f : 0.36f));
            }

            var slot = before % GhostCorners.Length;
            return (middle + GhostCorners[slot] * ghost.Across, ghost.Across * 0.16f);
        }

        /// <summary>Where a point on the board, in squares, is on the screen, in either view.</summary>
        private Vector2 ScreenOfSquares(Vector2 squares) =>
            In3D ? Project(squares) : BoardOrigin + flatPan + squares * CellSize;

        /// <summary>The ghosts of foes out of sight: a faded, dashed token with a "?", crossed if it was down.</summary>
        private void DrawGhosts()
        {
            var font = ThemeDB.FallbackFont;
            foreach (var foe in encounter.Combatants)
            {
                if (GhostOf(foe) is not { } ghost)
                {
                    continue;
                }

                var (middle, across) = GhostSpot(foe);
                var centre = ScreenOfSquares(middle);
                var radius = CellSize * across;
                var small = across < foe.Across * 0.3f;
                var faded = AdversaryToken with { A = 0.55f };
                DrawCircle(centre, radius, AdversaryToken with { A = 0.12f });
                for (var i = 0; i < 12; i++)
                {
                    var a = Mathf.Tau * i / 12;
                    DrawArc(centre, radius, a, a + Mathf.Tau / 24, 4, faded, 2);
                }

                var size = Mathf.Max(9, small ? CellSize / 6 : CellSize / 4);
                if (ghost.Down)
                {
                    DrawString(font, centre + new Vector2(-CellSize / 2f, size / 2f), "✕", HorizontalAlignment.Center, CellSize, size, Colors.White with { A = 0.5f });
                    continue;
                }

                DrawString(font, centre + new Vector2(-CellSize / 2f, size / 2f - 1), small ? "?" : Initials(foe.Name) + "?", HorizontalAlignment.Center, CellSize, size, Colors.White with { A = 0.6f });
            }
        }
    }
}
