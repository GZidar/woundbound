// Bot v bot: plays the Dumb Fighter against the Bramble Ratkin many times over with each way of
// playing the party, the GM's brain held fixed, and prints how each one fared.
//
//   dotnet run --project tools/WoundBound.Sim -- [--fights 1000] [--seed 1]

using WoundBound.Game.Combat;
using WoundBound.Game.Combat.Brains;
using WoundBound.Game.Data;
using WoundBound.Game.Simulation;

var fights = Option("--fights", 1000);
var seed = Option("--seed", 1);

var data = GameData.Load(file => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", file)));
Encounter Stage(IDiceRoller dice) => SampleEncounters.FighterVersusRatkin(data, dice, new SimpleGmBrain());

var tactics = PartyTactics.Presets;

Console.WriteLine($"The Dumb Fighter v three Bramble Ratkin — {fights} fights per tactic, seeds {seed}–{seed + fights - 1}, GM: simple.");
Console.WriteLine();

var reports = tactics.Select(t => Simulator.Run(Stage, () => new TacticalPartyBrain(t), fights, seed)).ToList();
Console.WriteLine(Simulator.Table(reports));
Console.WriteLine();
Console.WriteLine("Body left is the average over wins only. Attacks to Dodges are per fight. W/L/S = wins, losses, stalemates.");

int Option(string name, int fallback)
{
    var at = Array.IndexOf(args, name);
    return at >= 0 && at + 1 < args.Length && int.TryParse(args[at + 1], out var value) ? value : fallback;
}
