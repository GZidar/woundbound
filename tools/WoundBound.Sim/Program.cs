// Bot v bot: plays a sample fight many times over with each way of playing the party, the GM's
// brain held fixed, and prints how each one fared.
//
//   dotnet run --project tools/WoundBound.Sim -- [--fight party|solo] [--gm simple|tripping] [--fights 1000] [--seed 1]
//
//   party (the default): the Dumb Fighter, Salla Rinehart and Drovan Thale v a roadside ambush.
//   solo: the Dumb Fighter v three Bramble Ratkin.
//
//   simple (the default GM): everything goes for the nearest character.
//   tripping: as simple, but a creature that isn't a Minion trips a character its pack can also reach.

using WoundBound.Game.Combat;
using WoundBound.Game.Combat.Brains;
using WoundBound.Game.Data;
using WoundBound.Game.Simulation;

var fights = Option("--fights", 1000);
var seed = Option("--seed", 1);
var solo = Text("--fight", "party") == "solo";
var tripping = Text("--gm", "simple") == "tripping";
IGmBrain Gm() => tripping ? new TrippingGmBrain() : new SimpleGmBrain();

var data = GameData.Load(file => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", file)));
Encounter Stage(IDiceRoller dice) => solo
    ? SampleEncounters.FighterVersusRatkin(data, dice, Gm())
    : SampleEncounters.PartyVersusAmbush(data, dice, Gm());

var tactics = PartyTactics.Presets;
var fight = solo
    ? "The Dumb Fighter v three Bramble Ratkin"
    : "The Dumb Fighter, Salla and Drovan v a Tollroad Bandit, a Timber Wolf and two Bramble Ratkin";

Console.WriteLine($"{fight} — {fights} fights per tactic, seeds {seed}–{seed + fights - 1}, GM: {(tripping ? "tripping" : "simple")}.");
Console.WriteLine();

var reports = tactics.Select(t => Simulator.Run(Stage, () => new TacticalPartyBrain(t), fights, seed)).ToList();
Console.WriteLine(Simulator.Table(reports));
Console.WriteLine();
Console.WriteLine("Body left and Up (characters still standing) are averaged over wins only. Attacks to Dodges are per fight, across the party.");
Console.WriteLine("W/L/S = wins, losses, stalemates.");

int Option(string name, int fallback) => int.TryParse(Text(name, null), out var value) ? value : fallback;

string? Text(string name, string? fallback)
{
    var at = Array.IndexOf(args, name);
    return at >= 0 && at + 1 < args.Length ? args[at + 1] : fallback;
}
