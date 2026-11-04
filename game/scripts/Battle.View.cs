using System.Collections.Generic;
using System.Linq;
using Godot;
using WoundBound.Game.Combat;
using Side = WoundBound.Game.Combat.Side;

namespace WoundBoundGame
{
    /// <summary>
    /// Whose eyes the screen looks through, and finding somebody on the board. The Players' view
    /// keeps the fog of war, and its roster lists only the foes the party sees now; the GM's view
    /// lifts the fog and lists every foe. Clicking anyone on the roster brings the camera to them
    /// and pulses a ring round them, in either board.
    /// </summary>
    public partial class Battle
    {
        private const float FoeRowY = 104;
        private const float FoeStep = 28;
        private const float FoeRowX = 112;
        private const double SpotlightLife = 1.4;

        /// <summary>Who was last picked from the roster, and how long the ring round them has pulsed.</summary>
        private (Combatant Who, double Age)? spotlight;

        /// <summary>The foes on the roster: every one in the GM's view; in the Players' view, only those the party sees now.</summary>
        private List<Combatant> RosterFoes() =>
            encounter.Combatants.Where(c => c.Side == Side.Adversaries && !c.HasFled && (Shown(c) || GhostOf(c) is not null)).ToList();

        /// <summary>The roster's second row: the foes in rust, the one acting ringed, those down faded.</summary>
        private void DrawFoeStrip()
        {
            var font = ThemeDB.FallbackFont;
            var dim = new Color(0.75f, 0.75f, 0.8f);
            DrawString(font, new Vector2(BoardOrigin.X + 10, FoeRowY + 5), fogOn ? "Seen" : "All foes", HorizontalAlignment.Left, -1, 12, dim);
            var foes = RosterFoes();
            if (foes.Count == 0)
            {
                DrawString(font, new Vector2(FoeRowX - 10, FoeRowY + 5), "none", HorizontalAlignment.Left, -1, 12, dim with { A = 0.6f });
            }

            for (var i = 0; i < foes.Count; i++)
            {
                var foe = foes[i];
                var at = new Vector2(FoeRowX + i * FoeStep, FoeRowY);
                if (at.X > BoardOrigin.X + BoardArea.X - 14)
                {
                    DrawString(font, at + new Vector2(-10, 5), $"+{foes.Count - i}", HorizontalAlignment.Left, -1, 11, dim);
                    break;
                }

                if (GhostOf(foe) is { } ghost)
                {
                    // Out of sight: hollow, with where it was last seen behind the click.
                    DrawArc(at, 10, 0, Mathf.Tau, 24, AdversaryToken with { A = ghost.Down ? 0.3f : 0.8f }, 2);
                    DrawString(font, at + new Vector2(-12, 4), "?", HorizontalAlignment.Center, 24, 11, Colors.White with { A = 0.6f });
                    continue;
                }

                var down = foe.IsOutOfFight;
                DrawCircle(at, 11, down ? AdversaryToken with { A = 0.3f } : AdversaryToken);
                if (foe == encounter.Active)
                {
                    DrawArc(at, 14, 0, Mathf.Tau, 32, ActiveRing, 2);
                }

                DrawString(font, at + new Vector2(-12, 4), Initials(foe.Name), HorizontalAlignment.Center, 24, 10, down ? Colors.White with { A = 0.5f } : Colors.White);
            }
        }

        /// <summary>The creature whose roster icon is under <paramref name="point"/>, if any.</summary>
        private Combatant? RosterAt(Vector2 point)
        {
            foreach (var (who, _) in RosterTargets())
            {
                if (rosterAt.TryGetValue(who, out var at) && at.DistanceTo(point) <= 22)
                {
                    return who;
                }
            }

            var foes = RosterFoes();
            for (var i = 0; i < foes.Count; i++)
            {
                if (new Vector2(FoeRowX + i * FoeStep, FoeRowY).DistanceTo(point) <= 13)
                {
                    return foes[i];
                }
            }

            return null;
        }

        /// <summary>Brings the camera to <paramref name="who"/> and rings them for a moment.</summary>
        private void CentreOn(Combatant who)
        {
            // A foe out of sight is looked for where it was last seen, never where it is.
            var middle = PlaceOf(who);
            if (In3D)
            {
                board3D!.FocusOn(middle);
            }
            else
            {
                MoveFlatCamera(() =>
                {
                    // With the whole field in view there's nowhere to go: come in a little first.
                    if (flatZoom < 1.5f)
                    {
                        flatZoom = Mathf.Min(2, Mathf.Max(1, (float)LargestFlatCell / FittedCell));
                    }

                    flatPan = BoardArea / 2 - middle * CellSize;
                });
            }

            spotlight = (who, 0);
            QueueRedraw();
        }

        private bool StepSpotlight(double delta)
        {
            if (spotlight is not { } lit)
            {
                return false;
            }

            spotlight = lit.Age + delta >= SpotlightLife ? null : (lit.Who, lit.Age + delta);
            return true;
        }

        /// <summary>Two rings closing in on whoever was picked from the roster.</summary>
        private void DrawSpotlight()
        {
            if (spotlight is not { } lit || (!Shown(lit.Who) && GhostOf(lit.Who) is null))
            {
                return;
            }

            var k = (float)(lit.Age / SpotlightLife);
            var ghost = !Shown(lit.Who);
            var centre = ghost ? ScreenOfSquares(PlaceOf(lit.Who)) : TokenCentre(lit.Who);
            var radius = CellSize * lit.Who.Across * 0.36f;
            for (var i = 0; i < 2; i++)
            {
                var u = (k * 2 + i * 0.5f) % 1;
                var colour = ActiveRing with { A = (1 - k) * u };
                if (ghost)
                {
                    // Round the ghost, laid on the ground in 3D, so the ring never gives the foe's real square away.
                    var r = lit.Who.Across * 0.42f + (1 - u) * 0.8f;
                    var middle = PlaceOf(lit.Who);
                    DrawPolyline(Enumerable.Range(0, 33).Select(n => ScreenOfSquares(middle + Vector2.FromAngle(Mathf.Tau * n / 32) * r)).ToArray(), colour, 3);
                }
                else
                {
                    DrawRing(lit.Who, centre, radius + 4 + (1 - u) * CellSize * 0.8f, colour, 3);
                }
            }
        }
    }
}
