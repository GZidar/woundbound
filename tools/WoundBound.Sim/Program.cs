// Bot v bot: plays a sample fight many times over with each way of playing the party, the GM's
// brain held fixed, and prints how each one fared.
//
//   dotnet run --project tools/WoundBound.Sim -- [--fight signpost|signpost4|casters|ford|fordcasters|camp|witches|rivals|party|solo] [--gm simple|tripping|best|balanced|poor] [--party all|presets|utility] [--fights 1000] [--seed 1] [--show N]
//
//   signpost (the default): the Dumb Fighter, Salla Rinehart and Drovan Thale v the Quickstart's
//     signpost bandits, who break and run; the lantern-bearer mustn't get away.
//   signpost4: the book's table of four — Joren Blacke joins, and so does a second Roadside Bandit.
//   casters: the Quickstart's own five — Salla, Drovan, Joren, Mirella and Elthric — v five bandits.
//   ford: the same three, ambushed mid-ford at the Ferry Shrine at Longreach Crossing (Inner Sea Loom).
//   fordcasters: the Quickstart's five, casters among them, ambushed mid-ford by three Skulkers and two Ambushers.
//   witches: the Quickstart's five on a hedge road v a Tollroad Bandit, his wolf, a Bog Hex-Caller, a Hollow Bloodwright and a Wyrding Chanter.
//   camp: the Quickstart's five round their campfire at night, set on by a Tollroad Bandit, two wolves and two Ambushers.
//   party: the same three v a Tollroad Bandit, a Timber Wolf and two Bramble Ratkin.
//   solo: the Dumb Fighter v three Bramble Ratkin.
//
//   simple (the default GM): everything goes for the nearest character.
//   tripping: as simple, but a creature that isn't a Minion trips a character its pack can also reach.
//   best / balanced / poor: the utility GM, weighing every option, at that skill.
//
//   The party: every fixed-tactic preset and the utility party at each skill (all, the default),
//   only the presets, or only the utility party.

using WoundBound.Game.Combat;
using WoundBound.Game.Combat.Brains;
using WoundBound.Game.Combat.Brains.Utility;
using WoundBound.Game.Data;
using WoundBound.Game.Simulation;

var fights = Option("--fights", 1000);
var seed = Option("--seed", 1);
var which = Text("--fight", "signpost");
var gmName = Text("--gm", "simple")!;
var seats = Text("--party", "all");
IGmBrain Gm() => gmName switch
{
    "tripping" => new TrippingGmBrain(),
    "best" => new UtilityGmBrain(BrainSkill.Best),
    "balanced" => new UtilityGmBrain(BrainSkill.Balanced),
    "poor" => new UtilityGmBrain(BrainSkill.Poor),
    _ => new SimpleGmBrain(),
};

var data = GameData.Load(file => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", file)));
Encounter Stage(IDiceRoller dice) => which switch
{
    "solo" => SampleEncounters.FighterVersusRatkin(data, dice, Gm()),
    "party" => SampleEncounters.PartyVersusAmbush(data, dice, Gm()),
    "signpost4" => SampleEncounters.SignpostAmbush(data, dice, Gm(), fourPlayers: true),
    "casters" => SampleEncounters.SignpostWithCasters(data, dice, Gm()),
    "ford" => SampleEncounters.FerryShrineAmbush(data, dice, Gm()),
    "fordcasters" => SampleEncounters.FerryShrineWithCasters(data, dice, Gm()),
    "camp" => SampleEncounters.CampfireAmbush(data, dice, Gm()),
    "witches" => SampleEncounters.HedgeWitchesToll(data, dice, Gm()),
    "rivals" => SampleEncounters.RivalBand(data, dice, Gm()),
    "swarm" => SampleEncounters.RatswarmOnTheCauseway(data, dice, Gm()),
    "swarmopen" => SampleEncounters.RatswarmOnTheCauseway(data, dice, Gm(), openGround: true),
    "champion" => SampleEncounters.ChampionOnTheGreen(data, dice, Gm()),
    "guardians" => SampleEncounters.ShrineGuardians(data, dice, Gm()),
    "stag" => SampleEncounters.HollowStagInTheBirchwood(data, dice, Gm()),
    _ => SampleEncounters.SignpostAmbush(data, dice, Gm()),
};

var tactics = PartyTactics.Presets;
var fight = which switch
{
    "solo" => "The Dumb Fighter v three Bramble Ratkin",
    "party" => "The Dumb Fighter, Salla and Drovan v a Tollroad Bandit, a Timber Wolf and two Bramble Ratkin",
    "signpost4" => "The Dumb Fighter, Salla, Drovan and Joren v four signpost bandits (objective: take the lantern-bearer)",
    "casters" => "Salla, Drovan, Joren, Mirella and Elthric v five signpost bandits (objective: take the lantern-bearer)",
    "ford" => "The Dumb Fighter, Salla and Drovan, ambushed mid-ford at Longreach Crossing by two Skulkers and an Ambusher",
    "witches" => "Salla, Drovan, Joren, Mirella and Elthric v a Tollroad Bandit, his wolf, and three casters: Hex-Caller, Bloodwright, Chanter",
    "rivals" => "Salla, Drovan, Joren, Mirella and Elthric v the Grey Lanterns, a rival company of five built from the same rules",
    "swarm" => "Salla, Drovan, Joren, Mirella and Elthric v a Fenmoor Ratswarm and four Bramble Ratkin on the fen causeway",
    "swarmopen" => "Salla, Drovan, Joren, Mirella and Elthric v a Fenmoor Ratswarm and four Bramble Ratkin on open ground",
    "champion" => "Salla, Drovan, Joren, Mirella and Elthric v a Noble Champion and two retainers on the village green",
    "guardians" => "Salla, Drovan, Joren, Mirella and Elthric v a Stoneheart Golem and two Animated Suits of Mail in a cloister yard",
    "stag" => "Salla, Drovan, Joren, Mirella and Elthric v the Hollow Stag, a T1 Solo, in the birchwood",
    "camp" => "Salla, Drovan, Joren, Mirella and Elthric round their campfire at night v a Tollroad Bandit, two Timber Wolves and two Ambushers",
    "fordcasters" => "Salla, Drovan, Joren, Mirella and Elthric, ambushed mid-ford at Longreach Crossing by three Skulkers and two Ambushers",
    _ => "The Dumb Fighter, Salla and Drovan v the signpost bandits (objective: take the lantern-bearer)",
};

Console.WriteLine($"{fight} — {fights} fights per tactic, seeds {seed}–{seed + fights - 1}, GM: {gmName}.");
Console.WriteLine();

// --show N: play the one fight on seed N with the best utility party (or --show-as balanced|poor), and print its log.
if (Text("--show", null) is { } show && int.TryParse(show, out var shown))
{
    var one = Stage(new RandomDiceRoller(new Random(shown)));
    var showAs = BrainSkill.Levels.FirstOrDefault(l => l.Name.Equals(Text("--show-as", "Best"), StringComparison.OrdinalIgnoreCase)) ?? BrainSkill.Best;
    Simulator.PlayOne(one, new UtilityPartyBrain(showAs));
    Console.WriteLine(string.Join(Environment.NewLine, one.Log));
    return;
}

var parties = new List<Func<IPartyBrain>>();
if (seats is "all" or "presets")
{
    parties.AddRange(tactics.Select(t => (Func<IPartyBrain>)(() => new TacticalPartyBrain(t))));
}

// --party variants: Best with one setting changed at a time, against Balanced — for tuning.
if (seats is "variants")
{
    BrainSkill[] variants =
    [
        BrainSkill.Best,
        BrainSkill.Best with { Name = "Best, no tricks", UsesTricks = false },
        .. Enum.GetValues<Trick>().Select(t => BrainSkill.Best with { Name = $"Best, no {t}", Off = new HashSet<Trick> { t } }),
    ];
    parties.AddRange(variants.Select(v => (Func<IPartyBrain>)(() => new UtilityPartyBrain(v))));
}

if (seats is "all" or "utility")
{
    parties.AddRange(BrainSkill.Levels.Select(level => (Func<IPartyBrain>)(() => new UtilityPartyBrain(level))));
}

var reports = parties.Select(p => Simulator.Run(Stage, p, fights, seed)).ToList();
Console.WriteLine(Simulator.Table(reports));
Console.WriteLine();
Console.WriteLine("Obj% is fights with every objective met. Body left and Up (characters still standing) are averaged over wins only.");
Console.WriteLine("Foes = dropped, Fled = got away. Attacks to Leavers (reactions to a foe leaving reach) are per fight, across the party.");
Console.WriteLine("W/L/S/X = wins, losses, stalemates (50 rounds), withdrawals (the party got clear of foes still willing to fight).");

int Option(string name, int fallback) => int.TryParse(Text(name, null), out var value) ? value : fallback;

string? Text(string name, string? fallback)
{
    var at = Array.IndexOf(args, name);
    return at >= 0 && at + 1 < args.Length ? args[at + 1] : fallback;
}
