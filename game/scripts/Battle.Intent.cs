using System.Collections.Generic;
using System.Linq;
using WoundBound.Game.Combat;
using Side = WoundBound.Game.Combat.Side;

namespace WoundBoundGame
{
    /// <summary>
    /// The adversary's declared intent on the board (the user's direction, 2026-11-03): when a GM
    /// creature declares an action, a ring of its own opens around it with the path of that choice
    /// lit — the action, what it is done with, who it is aimed at — so a player sees what is coming
    /// before choosing to Interrupt, dodge, raise a shield or let it pass. The battle log keeps the words.
    /// </summary>
    public partial class Battle
    {
        private void UpdateIntent()
        {
            if (encounter.Declared is not { } declared || declared.Actor.Side != Side.Adversaries || declared.Actor.IsOutOfFight || !Shown(declared.Actor))
            {
                radial.ShowIntent(null);
                return;
            }

            var canvas = GetGlobalTransformWithCanvas();
            radial.ShowIntent(new RadialMenu.Intent(
                canvas * TokenCentre(declared.Actor),
                declared.Target is { } target ? canvas * TokenCentre(target) : null,
                declared.Actor.Name.Split(' ').First(w => w != "The"),
                IntentPath(declared)));
        }

        /// <summary>The steps a declaration takes, as the player's own radial would have offered them.</summary>
        private static IReadOnlyList<RadialItem> IntentPath(Declaration declared)
        {
            var (verb, icon) = declared.Kind switch
            {
                IntentKind.Strike => ("Strikes", "strike"),
                IntentKind.Shot => ("Shoots", "shoot"),
                IntentKind.Cast => ("Casts", "magic"),
                IntentKind.Grab => ("Grabs", "grab"),
                IntentKind.Shove => ("Shoves", "shove"),
                IntentKind.Trip => ("Trips", "trip"),
                _ => ("Acts", "attack"),
            };

            var path = new List<RadialItem> { new(verb) { Icon = icon } };
            if (declared.Kind is IntentKind.Strike or IntentKind.Shot or IntentKind.Cast)
            {
                path.Add(new RadialItem(declared.What));
            }

            if (declared.Manoeuvre is { } manoeuvre)
            {
                path.Add(new RadialItem(manoeuvre.ToString()) { Icon = manoeuvre.ToString().ToLowerInvariant() });
            }

            path.Add(new RadialItem(declared.Target is { } target ? target.Name : "the ground"));

            // Why, in the few words the table may hear: only what the creatures could have seen.
            if (declared.Why is { } why)
            {
                path.Add(new RadialItem(why));
            }

            return path;
        }
    }
}
