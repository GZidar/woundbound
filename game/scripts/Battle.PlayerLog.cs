using System.Collections.Generic;
using System.Linq;
using WoundBound.Game.Combat;
using Side = WoundBound.Game.Combat.Side;

namespace WoundBoundGame
{
    /// <summary>
    /// The log as the players may read it. The GM's log keeps every line. The players' log is
    /// judged line by line as each is written, against what the party sees at that moment. A line
    /// about foes they can't see and nobody else is left out, and so is whatever follows from it.
    /// Where an unseen foe does something to a character, or to a foe they can see, the line
    /// stays, with the unseen foe named only as "something unseen": the character knows they were
    /// bitten, not by what. A foe a character acts on is known to them for the rest of that turn.
    /// </summary>
    public partial class Battle
    {
        private const string Unseen = "something unseen";

        private BattleLog playerBattleLog = null!;

        /// <summary>The players' log, formatted as it was written.</summary>
        private readonly List<string> playerLog = new();

        /// <summary>Whether the last line that named anybody was left out of the players' log; what follows from it goes with it.</summary>
        private bool lastLeftOut;

        /// <summary>
        /// Foes a character has acted on this turn: whoever picks a target knows what they struck,
        /// seen or not, until the turn is over.
        /// </summary>
        private readonly HashSet<Combatant> targeted = new();

        private bool lastGmActing;

        /// <summary>The log on show: the players' while the fog is on, the GM's otherwise.</summary>
        private List<string> ShownLog => fogOn ? playerLog : formattedLog;

        private void StartLogs()
        {
            battleLog = new BattleLog(ColourOf);
            playerBattleLog = new BattleLog(ColourOf);
            formattedLog.Clear();
            playerLog.Clear();
            lastLeftOut = false;
            targeted.Clear();
            lastGmActing = false;

            // Lines written while the fight was set up came before anyone was listening.
            foreach (var line in encounter.Log)
            {
                AddToLogs(line, gmActing: false);
            }
        }

        private void AddToLogs(string line, bool gmActing)
        {
            formattedLog.Add(battleLog.Format(line, encounter.Combatants, gmActing));
            if (gmActing != lastGmActing || line.EndsWith("'s turn."))
            {
                targeted.Clear();
                lastGmActing = gmActing;
            }

            if (ForPlayers(line) is { } told)
            {
                playerLog.Add(playerBattleLog.Format(told, encounter.Combatants, gmActing));
            }
        }

        /// <summary>Shows the other log from the start, after the view changes.</summary>
        private void ReshowLog()
        {
            logLabel.Clear();
            linesShown = 0;
        }

        /// <summary><paramref name="line"/> as the players may read it, or null if it tells them nothing they could know.</summary>
        private string? ForPlayers(string line)
        {
            if (BattleLog.IsRound(line))
            {
                lastLeftOut = false;
                targeted.Clear();
                return line;
            }

            // Longest names first, so "Wolf from the north" is found whole, never as a "Wolf".
            var named = encounter.Combatants.OrderByDescending(c => c.Name.Length)
                .Where(c => line.Contains(c.Name)).ToList();
            if (named.Count == 0)
            {
                // Follows from the line before: told or left out with it.
                return lastLeftOut ? null : line;
            }

            if (named.FirstOrDefault(c => line.StartsWith(c.Name + " ")) is { Side: Side.Party })
            {
                targeted.UnionWith(named.Where(c => c.Side != Side.Party));
            }

            var unseen = named.Where(c => !PartySees(c) && !targeted.Contains(c)).ToList();
            if (unseen.Count == 0)
            {
                lastLeftOut = false;
                return line;
            }

            if (unseen.Count == named.Count)
            {
                lastLeftOut = true;
                return null;
            }

            lastLeftOut = false;
            foreach (var foe in unseen)
            {
                line = line.Replace(foe.Name, Unseen);
            }

            return char.ToUpperInvariant(line[0]) + line[1..];
        }

        /// <summary>Whether the party sees <paramref name="c"/> now, standing or lying where it fell.</summary>
        private bool PartySees(Combatant c) =>
            c.Side == Side.Party
            || (c.OccupiesSquare ? encounter.SideSees(Side.Party, c) : !c.HasFled && encounter.SightOf(Side.Party).Contains(c.Position));
    }
}
