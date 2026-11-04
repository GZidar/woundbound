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

        /// <summary>The middle of the place <paramref name="who"/> is shown at, in squares: where it is, or where it was last seen.</summary>
        private Vector2 PlaceOf(Combatant who)
        {
            var at = GhostOf(who)?.At ?? who.Position;
            return new Vector2(Mathf.Clamp(at.X, 0, encounter.Width - 1) + who.Across / 2f, Mathf.Clamp(at.Y, 0, encounter.Height - 1) + who.Across / 2f);
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

                var middle = PlaceOf(foe);
                var centre = ScreenOfSquares(middle);
                var radius = CellSize * foe.Across * 0.36f;
                var faded = AdversaryToken with { A = 0.55f };
                DrawCircle(centre, radius, AdversaryToken with { A = 0.12f });
                for (var i = 0; i < 12; i++)
                {
                    var a = Mathf.Tau * i / 12;
                    DrawArc(centre, radius, a, a + Mathf.Tau / 24, 4, faded, 2);
                }

                var size = Mathf.Max(9, CellSize / 4);
                if (ghost.Down)
                {
                    DrawString(font, centre + new Vector2(-CellSize / 2f, size / 2f), "✕", HorizontalAlignment.Center, CellSize, size, Colors.White with { A = 0.5f });
                    continue;
                }

                DrawString(font, centre + new Vector2(-CellSize / 2f, size / 2f - 1), Initials(foe.Name) + "?", HorizontalAlignment.Center, CellSize, size, Colors.White with { A = 0.6f });
            }
        }
    }
}
