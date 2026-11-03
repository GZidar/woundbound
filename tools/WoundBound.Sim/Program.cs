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
RuleTrials.MinionStamina = Option("--minion-stamina", 1);
var data = GameData.Load(file => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", file)));
RuleTrials.IgnoreMorale = args.Contains("--no-morale");

// --extra N: N more of the fight's own adversaries, cycling through them, each on the nearest open
// square to the one it copies — for calibrating Threat Value against what the brains can do.
var extra = Option("--extra", 0);
if (extra > 0)
{
    RuleTrials.Reinforce = (present, setting) =>
    {
        var copyable = present.Where(c => c.Side == Side.Adversaries && c.Statblock is not null && !c.IsSteed && !c.IsPhantom
                                          && c.Role != WoundBound.Rules.Enums.AdversaryRole.Solo).ToList();
        var added = new List<Combatant>();
        if (copyable.Count == 0)
        {
            return added;
        }

        var taken = present.SelectMany(c => c.Footprint).ToHashSet();
        for (var i = 0; i < extra; i++)
        {
            var original = copyable[i % copyable.Count];
            var spot = Enumerable.Range(1, 12)
                .SelectMany(r => Enumerable.Range(-r, 2 * r + 1).SelectMany(dx => Enumerable.Range(-r, 2 * r + 1)
                    .Select(dy => new GridPosition(original.Position.X + dx, original.Position.Y + dy))))
                .First(sq => sq.X >= 0 && sq.Y >= 0 && sq.X < 28 && sq.Y < 18 && !setting.IsSolid(sq)
                             && Footprints.At(sq, original.Across).All(f => !taken.Contains(f) && !setting.IsSolid(f)));
            var copy = CombatantFactory.FromAdversary(original.Statblock!, $"{original.Name} (more {i + 1})", spot, data.Spells);
            taken.UnionWith(copy.Footprint);
            added.Add(copy);
        }

        return added;
    };
}
// The GM knowledge dial: sheets or seen; left out, it goes with the skill (Best reads the sheets).
Knowledge? knowledge = Text("--knowledge", null) switch
{
    "sheets" => Knowledge.Sheets,
    "seen" => Knowledge.Seen,
    _ => null,
};
// --gm-caution: what share of its skill's Care the GM spends on its own creatures — for tuning.
var caution = double.TryParse(Text("--gm-caution", null), System.Globalization.CultureInfo.InvariantCulture, out var c) ? c : UtilityGmBrain.GmCaution;
IGmBrain Gm() => gmName switch
{
    "tripping" => new TrippingGmBrain(),
    "best" => new UtilityGmBrain(BrainSkill.Best, knowledge: knowledge, caution: caution),
    "balanced" => new UtilityGmBrain(BrainSkill.Balanced, knowledge: knowledge, caution: caution),
    "poor" => new UtilityGmBrain(BrainSkill.Poor, knowledge: knowledge, caution: caution),
    _ => new SimpleGmBrain(),
};

Encounter Stage(IDiceRoller dice) => which switch
{
    "solo" => SampleEncounters.FighterVersusRatkin(data, dice, Gm()),
    "party" => SampleEncounters.PartyVersusAmbush(data, dice, Gm(), ambush: true),
    "signpost4" => SampleEncounters.SignpostAmbush(data, dice, Gm(), fourPlayers: true, ambush: true),
    "casters" => SampleEncounters.SignpostWithCasters(data, dice, Gm(), ambush: true),
    "ford" => SampleEncounters.FerryShrineAmbush(data, dice, Gm(), ambush: true),
    "fordcasters" => SampleEncounters.FerryShrineWithCasters(data, dice, Gm(), ambush: true),
    "camp" => SampleEncounters.CampfireAmbush(data, dice, Gm(), ambush: true),
    "witches" => SampleEncounters.HedgeWitchesToll(data, dice, Gm()),
    "rivals" => SampleEncounters.RivalBand(data, dice, Gm()),
    "swarm" => SampleEncounters.RatswarmOnTheCauseway(data, dice, Gm()),
    "swarmopen" => SampleEncounters.RatswarmOnTheCauseway(data, dice, Gm(), openGround: true),
    "champion" => SampleEncounters.ChampionOnTheGreen(data, dice, Gm()),
    "outriders" => SampleEncounters.Outriders(data, dice, Gm()),
    "glamour" => SampleEncounters.GlamouredMill(data, dice, Gm()),
    "roadblock" => SampleEncounters.Roadblock(data, dice, Gm(), ambush: true),
    "gnoll" => SampleEncounters.GnollRaid(data, dice, Gm(), ambush: true),
    "guardians" => SampleEncounters.ShrineGuardians(data, dice, Gm()),
    "stag" => SampleEncounters.HollowStagInTheBirchwood(data, dice, Gm()),
    _ => SampleEncounters.SignpostAmbush(data, dice, Gm(), ambush: true),
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
    "outriders" => "Salla, Drovan, Joren, Mirella and Elthric v three Tollroad Bandits on horseback and an ambusher on foot",
    "glamour" => "Salla, Drovan, Joren, Mirella and Elthric v a Tollroad Bandit, his wolf, two Vampire Bats and a Fen Glamourer at the old mill",
    "gnoll" => "Salla, Drovan, Joren, Mirella and Elthric v a Duskfang Gnoll (Rampage) with a Tollroad Bandit and two Roadside Bandits",
    "roadblock" => "Salla, Drovan, Joren, Mirella and Elthric v a roadblock: two crossbows behind crates, two bandits behind carts, and the Tollroad Bandit",
    "guardians" => "Salla, Drovan, Joren, Mirella and Elthric v a Stoneheart Golem and two Animated Suits of Mail in a cloister yard",
    "stag" => "Salla, Drovan, Joren, Mirella and Elthric v the Hollow Stag, a T1 Solo, in the birchwood",
    "camp" => "Salla, Drovan, Joren, Mirella and Elthric round their campfire at night v a Tollroad Bandit, two Timber Wolves and two Ambushers",
    "fordcasters" => "Salla, Drovan, Joren, Mirella and Elthric, ambushed mid-ford at Longreach Crossing by three Skulkers and two Ambushers",
    _ => "The Dumb Fighter, Salla and Drovan v the signpost bandits (objective: take the lantern-bearer)",
};

Console.WriteLine($"{fight} — {fights} fights per tactic, seeds {seed}–{seed + fights - 1}, GM: {gmName}{(Gm() is UtilityGmBrain u ? $", knowing {u.Knowledge.ToString().ToLowerInvariant()}" : string.Empty)}.");
Console.WriteLine();

// --show N: play the one fight on seed N with the best utility party (or --show-as balanced|poor), and print its log.
if (Text("--show", null) is { } show && int.TryParse(show, out var shown))
{
    var one = Stage(new RandomDiceRoller(new Random(shown)));
    var showAs = BrainSkill.Levels.FirstOrDefault(l => l.Name.Equals(Text("--show-as", "Best"), StringComparison.OrdinalIgnoreCase)) ?? BrainSkill.Best;
    if (args.Contains("--trace"))
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        // Live, so the GM's weighing of each choice lands beside what came of it.
        Console.WriteLine(string.Join(Environment.NewLine, one.Log));
        one.Logged += Console.WriteLine;
        BrainTrace.Sink = Console.WriteLine;
        Simulator.PlayOne(one, new UtilityPartyBrain(showAs));
        return;
    }

    Simulator.PlayOne(one, new UtilityPartyBrain(showAs));
    Console.WriteLine(string.Join(Environment.NewLine, one.Log));
    return;
}

// What the fight costs (GM Reference §5): Threat Value against the party's budget.
{
    var staged = Stage(new RandomDiceRoller(new Random(1)));
    var party = staged.Combatants.Where(c => c.Side == Side.Party && !c.IsSteed).ToList();
    var foes = staged.Combatants.Where(c => c.Side == Side.Adversaries && !c.IsSteed && !c.IsPhantom).ToList();
    var partyTier = party.Max(c => c.Tier);
    if (foes.Any(f => f.Role == WoundBound.Rules.Enums.AdversaryRole.Solo))
    {
        Console.WriteLine($"Cost: a Solo — the whole budget, by its sizing (GM Reference §3), {party.Count} PCs.");
    }
    else
    {
        var spend = foes.Sum(f => f.Role is { } role
            ? WoundBound.Rules.Calculation.ChallengeCalculator.ThreatValue(role, f.Tier, partyTier)
            : 1.0);
        var multiplier = spend / party.Count;
        var named = new[] { (0.75, "Easy"), (1.0, "Standard"), (1.5, "Hard"), (2.0, "Deadly") }
            .FirstOrDefault(d => multiplier <= d.Item1 + 1e-9, (99, "over Deadly")).Item2;
        Console.WriteLine($"Cost: Threat {spend:0.##} for {party.Count} PCs = {multiplier:0.##}× ({named}).");
    }
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

// --party best: the Best party alone — the measure for "every fight can be lost".
if (seats is "best")
{
    parties.Add(() => new UtilityPartyBrain(BrainSkill.Best));
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
