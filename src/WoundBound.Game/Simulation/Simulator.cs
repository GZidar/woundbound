using WoundBound.Game.Combat;
using WoundBound.Game.Combat.Brains;

namespace WoundBound.Game.Simulation
{
    /// <summary>How one bot-played fight came out.</summary>
    public sealed record FightResult(
        EncounterPhase Outcome,
        int Rounds,
        int PartyBodyLeft,
        int PartyStaminaLeft,
        int FoesDropped,
        int CharactersStanding,
        int FoesFled,
        int ObjectivesMet,
        int ObjectivesSet,
        BotTally Tally)
    {
        public bool Won => Outcome == EncounterPhase.Victory;

        public bool Lost => Outcome == EncounterPhase.Defeat;

        /// <summary>The party got clear of foes still willing to fight.</summary>
        public bool Withdrew => Outcome == EncounterPhase.Withdrew;

        public bool Stalemate => !Won && !Lost && !Withdrew;
    }

    /// <summary>A tactic's record over many fights: how often it wins, how fast, at what cost, and what it actually did.</summary>
    public sealed record TacticReport(string Tactic, IReadOnlyList<FightResult> Fights)
    {
        public int Wins => Fights.Count(f => f.Won);

        public int Losses => Fights.Count(f => f.Lost);

        public int Stalemates => Fights.Count(f => f.Stalemate);

        public int Withdrawals => Fights.Count(f => f.Withdrew);

        public double WinRate => Fights.Count == 0 ? 0 : (double)Wins / Fights.Count;

        public double AverageRounds => Fights.Average(f => f.Rounds);

        /// <summary>Body the party had left, in the fights it won — how close a win was.</summary>
        public double AverageBodyLeftOnWin => Wins == 0 ? 0 : Fights.Where(f => f.Won).Average(f => f.PartyBodyLeft);

        public double AverageFoesDropped => Fights.Average(f => f.FoesDropped);

        /// <summary>The share of fights in which every objective was met; null when the fight sets none.</summary>
        public double? ObjectiveRate => Fights.Count == 0 || Fights[0].ObjectivesSet == 0
            ? null
            : (double)Fights.Count(f => f.ObjectivesMet == f.ObjectivesSet) / Fights.Count;

        public double AverageFled => Fights.Average(f => f.FoesFled);

        /// <summary>Characters still fighting at the end of the fights the party won.</summary>
        public double AverageStandingOnWin => Wins == 0 ? 0 : Fights.Where(f => f.Won).Average(f => f.CharactersStanding);

        public double Per(Func<BotTally, int> count) => Fights.Average(f => count(f.Tally));
    }

    /// <summary>
    /// Plays a fight over and over with bots at both seats, and reports how each way of playing
    /// the party fared — the GM's brain held fixed, the dice random but seeded.
    ///
    /// <para>Every tactic faces the same seeds, fight for fight, so where two tactics differ it's
    /// their choices that differ rather than their luck at the start.</para>
    /// </summary>
    public static class Simulator
    {
        /// <param name="stage">Sets up a fresh fight on the given dice: the board, the combatants and the GM brain.</param>
        /// <param name="party">Plays the party. Brains may hold state, so one is made per fight.</param>
        public static TacticReport Run(Func<IDiceRoller, Encounter> stage, Func<IPartyBrain> party, int fights, int seed = 1, int maxRounds = 50)
        {
            var results = new List<FightResult>(fights);
            string? name = null;

            for (var i = 0; i < fights; i++)
            {
                var brain = party();
                name ??= brain.Name;
                results.Add(PlayOne(stage(new RandomDiceRoller(new Random(seed + i))), brain, maxRounds));
            }

            return new TacticReport(name ?? "(none)", results);
        }

        public static FightResult PlayOne(Encounter encounter, IPartyBrain party, int maxRounds = 50)
        {
            var driver = new BotDriver(encounter, party);
            driver.PlayToEnd(maxRounds);

            var members = encounter.Combatants.Where(c => c.Side == Side.Party).ToList();
            return new FightResult(
                encounter.Phase,
                encounter.Round,
                members.Sum(c => c.Body),
                members.Sum(c => c.Stamina),
                encounter.Combatants.Count(c => c.Side == Side.Adversaries && c.IsOutOfFight && !c.HasFled),
                members.Count(c => !c.IsOutOfFight),
                encounter.Combatants.Count(c => c.Side == Side.Adversaries && c.HasFled),
                encounter.Objectives.Count(o => o.StatusIn(encounter) == ObjectiveStatus.Achieved),
                encounter.Objectives.Count,
                driver.Tally);
        }

        /// <summary>The reports side by side, best win rate first, as a plain-text table.</summary>
        public static string Table(IEnumerable<TacticReport> reports)
        {
            var rows = reports.OrderByDescending(r => r.ObjectiveRate ?? r.WinRate).ThenByDescending(r => r.WinRate).ThenBy(r => r.AverageRounds).ToList();
            var width = Math.Max("Tactic".Length, rows.Count == 0 ? 0 : rows.Max(r => r.Tactic.Length));

            var lines = new List<string>
            {
                $"{"Tactic".PadRight(width)}  Obj%  Win%   W/L/S/X       Rounds  Body left  Up   Foes  Fled  Attacks  Trips  Swaps  Extra  Braces  Dodges  Leavers  Heals  Helps  Casts  Intr  Shelt  Expl  Man",
            };

            lines.AddRange(rows.Select(r =>
                $"{r.Tactic.PadRight(width)}  {(r.ObjectiveRate is { } obj ? obj.ToString("P0") : "—"),4}  {r.WinRate,5:P0}  {$"{r.Wins}/{r.Losses}/{r.Stalemates}/{r.Withdrawals}",-12}  " +
                $"{r.AverageRounds,6:0.0}  {r.AverageBodyLeftOnWin,9:0.0}  {r.AverageStandingOnWin,3:0.0}  {r.AverageFoesDropped,4:0.0}  {r.AverageFled,4:0.0}  " +
                $"{r.Per(t => t.Attacks),7:0.0}  {r.Per(t => t.Manoeuvres),5:0.0}  {r.Per(t => t.Swaps),5:0.0}  {r.Per(t => t.ExtraActions),5:0.0}  " +
                $"{r.Per(t => t.StoodGround),6:0.0}  {r.Per(t => t.DodgesTaken),6:0.0}  {r.Per(t => t.LeaverReactions),7:0.0}  {r.Per(t => t.Tends),5:0.0}  {r.Per(t => t.Aids),5:0.0}  {r.Per(t => t.Casts),5:0.0}  {r.Per(t => t.Interrupts),4:0.0}  {r.Per(t => t.Shelters),5:0.0}  {r.Per(t => t.Exploits),4:0.0}  {r.Per(t => t.Manoeuvres),4:0.0}"));

            return string.Join(Environment.NewLine, lines);
        }
    }
}
