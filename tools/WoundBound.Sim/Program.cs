// Bot v bot: plays a sample fight many times over with each way of playing the party, the GM's
// brain held fixed, and prints how each one fared.
//
//   dotnet run --project tools/WoundBound.Sim -- [--fight signpost|signpost4|casters|ford|fordcasters|camp|witches|rivals|rivals2..4|party|solo|...] [--gm simple|tripping|best|balanced|poor] [--party all|presets|utility] [--fights 1000] [--seed 1] [--show N]
//
//   signpost (the default): the Dumb Fighter, Salla Rinehart and Drovan Thale v the Quickstart's
//     signpost bandits, who break and run; the lantern-bearer mustn't get away.
//   signpost4: the book's table of four — Joren Blacke joins, and so does a second Roadside Bandit.
//   casters: the Quickstart's own five — Salla, Drovan, Joren, Mirella and Elthric — v five bandits.
//   ford: the same three, ambushed mid-ford at the Ferry Shrine at Longreach Crossing (Inner Sea Loom).
//   fordcasters: the Quickstart's five, casters among them, ambushed mid-ford by three Skulkers and two Ambushers.
//   witches: the Quickstart's five on a hedge road v a Tollroad Bandit, two wolves, a Bog Hex-Caller, a Hollow Bloodwright and a Wyrding Chanter.
//   camp: the Quickstart's five round their campfire at night, set on by two Tollroad Bandits, three wolves and three Ambushers.
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
var data = GameData.Load(file => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", file)));
RuleTrials.IgnoreMorale = args.Contains("--no-morale");
RuleTrials.NoOpportunityTurns = args.Contains("--no-opp-turns");

// --setup smart|neutral|poor and --unprepared: how a band met at random is placed (the clearing).
var setup = Enum.TryParse<SetupSkill>(Text("--setup", "smart"), ignoreCase: true, out var s) ? s : SetupSkill.Smart;
var prepared = !args.Contains("--unprepared");
// --band S,W,R: the clearing's band — Skulkers, Wolves, Ratkin — for sizing it.
(int, int, int)? ClearingBand() => Text("--band", null) is { } b && b.Split(',') is [var bs, var bw, var br] ? (int.Parse(bs), int.Parse(bw), int.Parse(br)) : null;
var placementSeed = 7;
// The GM knowledge dial: sheets or seen; left out, it goes with the skill (Best reads the sheets).
Knowledge? knowledge = Text("--knowledge", null) switch
{
    "sheets" => Knowledge.Sheets,
    "seen" => Knowledge.Seen,
    _ => null,
};
// The fog dial: peer (knows where every character stands) or fog (only what its creatures have seen); left out, it goes with the skill (Best peers).
bool? peers = Text("--sight", null) switch
{
    "peer" => true,
    "fog" => false,
    _ => null,
};
// --gm-caution: what share of its skill's Care the GM spends on its own creatures — for tuning.
var caution = double.TryParse(Text("--gm-caution", null), System.Globalization.CultureInfo.InvariantCulture, out var c) ? c : UtilityGmBrain.GmCaution;
// --foresight K,N,R[,own[,quick|best]]: the GM looks ahead — K plans, N games each, R rounds past this one, its own
// creatures worth `own` of the party's, its other creatures in those games played by the quick GM or its own best. Left out, it goes with the GM's challenge (Foresight.For); "off", it doesn't look ahead.
Foresight? foresight = Text("--foresight", null) switch
{
    null => null,
    "off" => Foresight.None,
    var sight when sight.Split(',') is [var k, var n, var r, .. var rest] => new Foresight(int.Parse(k), int.Parse(n), int.Parse(r),
        rest is [var own, ..] && own.Length > 0 ? double.Parse(own, System.Globalization.CultureInfo.InvariantCulture) : UtilityGmBrain.GmCaution,
        rest is [_, var others] ? Enum.Parse<GamesPlayedBy>(others, ignoreCase: true) : GamesPlayedBy.Quick),
    var sight => throw new ArgumentException($"--foresight wants K,N,R[,own[,quick|best]] or off, not {sight}"),
};
// --stream-dice: fights rolled on copyable dice, as --cheat does — its fair baseline.
// --cheat: the GM's look-ahead plays every plan on the fight's real coming dice (Foresight.Prescient) — a ceiling, never play.
var cheat = args.Contains("--cheat");
if (cheat)
{
    foresight = (foresight ?? Foresight.Default) with { Prescient = true };
}

// --measure G,R[,weight] or off: left out, it goes with the GM's challenge (ThreatMeasure.For). The GM measures the party's threat each round over G games to R rounds past it (ThreatMeasure).
var measure = Text("--measure", null) switch
{
    null => null,
    "off" => ThreatMeasure.None,
    var m when m.Split(',') is [var g, var r, .. var w] => new ThreatMeasure(int.Parse(g), int.Parse(r),
        w is [var wt] ? double.Parse(wt, System.Globalization.CultureInfo.InvariantCulture) : ThreatMeasure.Default.Weight),
    var m => throw new ArgumentException($"--measure wants G,R[,weight] or off, not {m}"),
};
// --ruthless R|merciless: a fixed GM intent, 0 to 1 (merciless also finishes the dying). --challenge easy|average|push: a director steers it.
GmIntent? intent = (Text("--challenge", null), Text("--ruthless", null)) switch
{
    ({ } ch, _) => GmIntent.Directed(Enum.Parse<Challenge>(ch, ignoreCase: true)),
    (_, "merciless") => GmIntent.Merciless,
    (_, { } r) => GmIntent.Fixed(double.Parse(r, System.Globalization.CultureInfo.InvariantCulture)),
    _ => null,
};
// Every utility GM built, so the look-ahead tally can be summed at the end.
var utilityGms = new System.Collections.Concurrent.ConcurrentBag<UtilityGmBrain>();
UtilityGmBrain Kept(UtilityGmBrain gm)
{
    utilityGms.Add(gm);
    return gm;
}

IGmBrain Gm() => gmName switch
{
    "tripping" => new TrippingGmBrain(),
    "best" => Kept(new UtilityGmBrain(BrainSkill.Best, knowledge: knowledge, caution: caution, peers: peers, foresight: foresight, measure: measure, intent: intent)),
    "balanced" => Kept(new UtilityGmBrain(BrainSkill.Balanced, knowledge: knowledge, caution: caution, peers: peers, foresight: foresight, measure: measure, intent: intent)),
    "poor" => Kept(new UtilityGmBrain(BrainSkill.Poor, knowledge: knowledge, caution: caution, peers: peers, foresight: foresight, measure: measure, intent: intent)),
    _ => new SimpleGmBrain(),
};

// --fight bench --foe NAME --count N: the Quickstart five v N of one statblock on open ground — for pricing it.
var benchFoe = Text("--foe", "Timber Wolf")!;
var benchCount = Option("--count", 5);
WoundBound.Rules.Catalog.StartingAdversaryInfo BenchFoe() => Altered(benchFoe switch
{
    "Roadside Bandit" => SampleEncounters.RoadsideBandit,
    "Bandit Ambusher" => SampleEncounters.BanditAmbusher,
    "Fen Glamourer" => SampleEncounters.FenGlamourer(data),
    _ => data.Adversary(benchFoe),
}, Text("--set", null));

// --set Field=Value,...: the bench's statblock with numbers changed — for pricing a statblock as it's
// tuned. Any property of the statblock: BodyMax=4, DamageDie=d6, Role=Minion, Traits= (none), Features= (none).
WoundBound.Rules.Catalog.StartingAdversaryInfo Altered(WoundBound.Rules.Catalog.StartingAdversaryInfo info, string? changes)
{
    if (changes is null)
    {
        return info;
    }

    var copy = info with { };
    foreach (var change in changes.Split(',', StringSplitOptions.RemoveEmptyEntries))
    {
        var (name, value) = change.Split('=', 2) is [var k, var v] ? (k.Trim(), v.Trim()) : throw new ArgumentException($"--set wants Field=Value, not {change}");
        var property = typeof(WoundBound.Rules.Catalog.StartingAdversaryInfo).GetProperty(name)
                       ?? throw new ArgumentException($"A statblock has no {name}.");
        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        object? parsed = value.Length == 0 && !type.IsValueType ? (type.IsArray ? Array.CreateInstance(type.GetElementType()!, 0)
                : type.IsGenericType ? Activator.CreateInstance(typeof(List<>).MakeGenericType(type.GetGenericArguments()[0])) : null)
            : type.IsEnum ? Enum.Parse(type, value, ignoreCase: true)
            // A list of enum values, '+' between them: Traits=HitAndFade+PackTactics.
            : type.IsGenericType && type.GetGenericArguments()[0] is { IsEnum: true } item ? EnumList(item, value)
            // Spell lines by name, '+' between them: Spells=Blood Mist+Blood Lance — read from the catalogue.
            : type.IsGenericType && type.GetGenericArguments()[0] == typeof(WoundBound.Rules.Catalog.StartingSpellLineInfo)
                ? value.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(n => new WoundBound.Rules.Catalog.StartingSpellLineInfo(n, "", "")).ToList()
            : type == typeof(string) ? value
            : Convert.ChangeType(value, type, System.Globalization.CultureInfo.InvariantCulture);
        property.SetValue(copy, parsed);
    }

    return copy;

    static object EnumList(Type item, string value)
    {
        var list = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(item))!;
        foreach (var one in value.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            list.Add(Enum.Parse(item, one, ignoreCase: true));
        }

        return list;
    }
}

Encounter Stage(IDiceRoller dice) => which switch
{
    "bench" => SampleEncounters.Bench(data, dice, Gm(), BenchFoe(), benchCount,
        Text("--foe2", null) is { } foe2 ? Altered(data.Adversary(foe2), Text("--set2", null)) : null, Option("--count2", 0),
        cover: args.Contains("--cover"), partyTier: Enum.Parse<WoundBound.Rules.Enums.Tier>(Text("--tier", "T1")!, ignoreCase: true),
        third: Text("--foe3", null) is { } foe3 ? Altered(data.Adversary(foe3), Text("--set3", null)) : null, thirdCount: Option("--count3", 0)),
    "solo" => SampleEncounters.FighterVersusRatkin(data, dice, Gm()),
    "party" => SampleEncounters.PartyVersusAmbush(data, dice, Gm(), ambush: true),
    "signpost4" => SampleEncounters.SignpostAmbush(data, dice, Gm(), fourPlayers: true, ambush: true),
    "casters" => SampleEncounters.SignpostWithCasters(data, dice, Gm(), ambush: true, cowardly: args.Contains("--cowardly")),
    "ford" => SampleEncounters.FerryShrineAmbush(data, dice, Gm(), ambush: true),
    "fordcasters" => SampleEncounters.FerryShrineWithCasters(data, dice, Gm(), ambush: true),
    "camp" => SampleEncounters.CampfireAmbush(data, dice, Gm(), ambush: true),
    "witches" => SampleEncounters.HedgeWitchesToll(data, dice, Gm()),
    "rivals" => SampleEncounters.RivalBand(data, dice, Gm()),
    "rivals2" or "rivals3" or "rivals4" => SampleEncounters.RivalBand(data, dice, Gm(), count: which[^1] - '0'),
    "swarm" => SampleEncounters.RatswarmOnTheCauseway(data, dice, Gm()),
    "swarmopen" => SampleEncounters.RatswarmOnTheCauseway(data, dice, Gm(), openGround: true),
    "champion" => SampleEncounters.ChampionOnTheGreen(data, dice, Gm()),
    "outriders" => SampleEncounters.Outriders(data, dice, Gm()),
    "glamour" => SampleEncounters.GlamouredMill(data, dice, Gm()),
    "roadblock" => SampleEncounters.Roadblock(data, dice, Gm(), ambush: true),
    "gnoll" => SampleEncounters.GnollRaid(data, dice, Gm(), ambush: true),
    "clearing" => SampleEncounters.WoodlandClearing(data, dice, Gm(), setup, prepared, placementSeed + dice.Roll(1000), ClearingBand()),
    "guardians" => SampleEncounters.ShrineGuardians(data, dice, Gm()),
    "stag" => SampleEncounters.HollowStagInTheBirchwood(data, dice, Gm()),
    _ => SampleEncounters.SignpostAmbush(data, dice, Gm(), ambush: true),
};

var tactics = PartyTactics.Presets;
var fight = which switch
{
    "bench" => $"Salla, Drovan, Joren, Mirella and Elthric v {benchCount} × {benchFoe}{(Text("--set", null) is { } set ? $" ({set})" : "")} on open ground",
    "solo" => "The Dumb Fighter v three Bramble Ratkin",
    "party" => "The Dumb Fighter, Salla and Drovan v a Tollroad Bandit, a Timber Wolf and two Bramble Ratkin",
    "signpost4" => "The Dumb Fighter, Salla, Drovan and Joren v four signpost bandits (objective: take the lantern-bearer)",
    "casters" => "Salla, Drovan, Joren, Mirella and Elthric v five signpost bandits (objective: take the lantern-bearer)",
    "ford" => "The Dumb Fighter, Salla and Drovan, ambushed mid-ford at Longreach Crossing by two Skulkers and an Ambusher",
    "witches" => "Salla, Drovan, Joren, Mirella and Elthric v a Tollroad Bandit, his two wolves, and three casters: Hex-Caller, Bloodwright, Chanter",
    "rivals" => "Salla, Drovan, Joren, Mirella and Elthric v the Grey Lanterns, a rival company of five built from the same rules (Hard: 7.5 Threat)",
    "rivals2" or "rivals3" or "rivals4" => $"Salla, Drovan, Joren, Mirella and Elthric v {which[^1]} of the Grey Lanterns, rivals built from the same rules",
    "swarm" => "Salla, Drovan, Joren, Mirella and Elthric v two Fenmoor Ratswarms and eight Bramble Ratkin on the fen causeway",
    "swarmopen" => "Salla, Drovan, Joren, Mirella and Elthric v two Fenmoor Ratswarms and eight Bramble Ratkin on open ground",
    "champion" => "Salla, Drovan, Joren, Mirella and Elthric v a Noble Champion and two retainers on the village green",
    "outriders" => "Salla, Drovan, Joren, Mirella and Elthric v four Tollroad Bandits on horseback and two ambushers on foot",
    "glamour" => "Salla, Drovan, Joren, Mirella and Elthric v two Tollroad Bandits, two wolves, four Vampire Bats and a Fen Glamourer at the old mill",
    "clearing" => $"Salla, Drovan, Joren, Mirella and Elthric met at dusk in a woodland clearing by skulkers, wolves and ratkin, placed by a {setup} setup" + (prepared ? ", lying in wait" : string.Empty),
    "gnoll" => "Salla, Drovan, Joren, Mirella and Elthric v a Duskfang Gnoll (Rampage) with a Tollroad Bandit and two Roadside Bandits",
    "roadblock" => "Salla, Drovan, Joren, Mirella and Elthric v a roadblock: four crossbows behind crates, four bandits behind carts, and the Tollroad Bandit",
    "guardians" => "Salla, Drovan, Joren, Mirella and Elthric v a Stoneheart Golem and two Animated Suits of Mail in a cloister yard",
    "stag" => "Salla, Drovan, Joren, Mirella and Elthric v the Hollow Stag, a T1 Solo, in the birchwood",
    "camp" => "Salla, Drovan, Joren, Mirella and Elthric round their campfire at night v two Tollroad Bandits, three Timber Wolves and three Ambushers",
    "fordcasters" => "Salla, Drovan, Joren, Mirella and Elthric, ambushed mid-ford at Longreach Crossing by three Skulkers and two Ambushers",
    _ => "The Dumb Fighter, Salla and Drovan v the signpost bandits (objective: take the lantern-bearer)",
};

Console.WriteLine($"{fight} — {fights} fights per tactic, seeds {seed}–{seed + fights - 1}, GM: {gmName}{(Gm() is UtilityGmBrain u ? $", knowing {u.Knowledge.ToString().ToLowerInvariant()}, {(u.PeersThroughFog ? "peering through the fog" : "in the fog")}{(u.Foresight is { LooksAhead: true } f ? $", looking ahead ({f.Candidates} plans, {f.Games} games, {f.Rounds} rounds on, {f.Others.ToString().ToLowerInvariant()} GM in them{(f.Prescient ? ", CHEATING: knowing the dice" : string.Empty)})" : string.Empty)}{(u.Measure is { Measures: true } tm ? $", measuring the party ({tm.Games} games, {tm.Rounds} rounds on, weight {tm.Weight:0.##})" : string.Empty)}" : string.Empty)}.");
Console.WriteLine();

// --show N: play the one fight on seed N with the best utility party (or --show-as balanced|poor), and print its log.
// The sample Skill Challenges, by name.
static IReadOnlyList<WoundBound.Game.Skills.SkillNode> SkillSequence(string name) => name switch
{
    "stair" => WoundBound.Game.Skills.SampleSkillChallenges.TheStair(),
    "snare" => WoundBound.Game.Skills.SampleSkillChallenges.TheSnare(),
    "vent" => WoundBound.Game.Skills.SampleSkillChallenges.TheVent(),
    _ => WoundBound.Game.Skills.SampleSkillChallenges.TheWagon(),
};

// --skill wagon|stair|snare|vent: a Skill Challenge, played by the Quickstart five, many times over.
if (Text("--skill", null) is { } skillName)
{
    IReadOnlyList<WoundBound.Game.Skills.SkillNode> Sequence() => SkillSequence(skillName);
    List<Combatant> Five() =>
    [
        CombatantFactory.FromCharacter(SampleEncounters.SallaRinehart(data), new GridPosition(0, 0)),
        CombatantFactory.FromCharacter(SampleEncounters.DrovanThale(data), new GridPosition(1, 0)),
        CombatantFactory.FromCharacter(SampleEncounters.JorenBlacke(data), new GridPosition(2, 0)),
        CombatantFactory.FromCharacter(SampleEncounters.MirellaVask(data), new GridPosition(3, 0)),
        CombatantFactory.FromCharacter(SampleEncounters.ElthricDane(data), new GridPosition(4, 0)),
    ];

    if (Text("--show", null) is { } one && int.TryParse(one, out var which1))
    {
        var crew = Five();
        var run = new WoundBound.Game.Skills.SkillChallenge(crew, Sequence(), new RandomDiceRoller(new Random(which1))).Run();
        Console.WriteLine(string.Join(Environment.NewLine, run.Log));
        Console.WriteLine($"Ending: {run.Ending}. Rounds: {run.Rounds}.");
        foreach (var member in crew)
        {
            Console.WriteLine($"  {member.Name}: Body {member.Body}/{member.BodyMax}, Mind {member.Mind}/{member.MindMax}, Soul {member.Soul}/{member.SoulMax}, Stamina {member.Stamina}/{member.StaminaMax}");
        }

        return;
    }

    var runs = Enumerable.Range(0, fights).Select(i =>
    {
        var crew = Five();
        var start = crew.Sum(c => c.Stamina);
        var result = new WoundBound.Game.Skills.SkillChallenge(crew, Sequence(), new RandomDiceRoller(new Random(seed + i))).Run();
        return (result, Body: crew.Sum(c => c.BodyMax - Math.Max(0, c.Body)) / (double)crew.Sum(c => c.BodyMax),
            MindSoul: crew.Sum(c => c.MindMax + c.SoulMax - c.Mind - c.Soul) / (double)crew.Sum(c => c.MindMax + c.SoulMax),
            Stamina: (start - crew.Sum(c => c.Stamina)) / (double)start, Down: crew.Count(c => c.Body <= 0));
    }).ToList();
    Console.WriteLine($"{skillName} — {fights} runs by the Quickstart five.");
    Console.WriteLine($"Cleared {runs.Count(r => r.result.Cleared) / (double)runs.Count:P0}  Rounds {runs.Average(r => r.result.Rounds):0.0}  " +
                      $"Body lost {runs.Average(r => r.Body):P0}  Mind+Soul lost {runs.Average(r => r.MindSoul):P0}  Stamina spent {runs.Average(r => r.Stamina):P0}  " +
                      $"Down {runs.Average(r => r.Down):0.00}  Story bands {runs.Average(r => r.result.StoryBands.Sum()):0.0}");
    foreach (var ending in runs.GroupBy(r => r.result.Ending).OrderByDescending(g => g.Count()))
    {
        Console.WriteLine($"  {ending.Count() / (double)runs.Count,4:P0}  {ending.Key}");
    }

    return;
}

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
    Console.WriteLine($"- End: {one.Phase}, round {one.Round} -");
    foreach (var who in one.Combatants.Where(w => !w.IsPhantom))
    {
        Console.WriteLine($"  [{who.Side}] {who.Name}: Body {who.Body}/{who.BodyMax}, Stamina {who.Stamina}/{who.StaminaMax}{(who.IsOutOfFight ? ", out" : "")}{(who.HasFled ? ", fled" : "")}{(who.IsBroken ? ", broken" : "")}");
    }

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
        static string Named(double multiplier) => new[] { (0.75, "Easy"), (1.0, "Standard"), (1.5, "Hard"), (2.0, "Deadly") }
            .FirstOrDefault(d => multiplier <= d.Item1 + 1e-9, (99, "over Deadly")).Item2;
        var spend = foes.Sum(f => f.Role is { } role ? WoundBound.Rules.Calculation.ChallengeCalculator.ThreatValue(role, f.Tier, partyTier) : 1);
        Console.WriteLine($"Cost: Threat {spend:0.##} for {party.Count} PCs = {spend / party.Count:0.##}× ({Named(spend / party.Count)}).");
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

// --day N: N fights of this kind back to back, wounds carried, a short rest between — one row per party.
if (Option("--day", 0) is var perDay and > 0)
{
    Console.WriteLine($"{fights} days of {perDay} fights each, a short rest between (Stamina back, each fresh wound tended once)"
                      + (Text("--with-skill", null) is { } opener ? $", opening with the {opener} Skill Challenge (no rest before the first fight)." : "."));
    Console.WriteLine($"{"Party",-24}  Survived  Fights won  Dead at day's end  Body left at day's end");
    foreach (var p in parties)
    {
        var withSkill = Text("--with-skill", null);
        var days = Simulator.RunDays(Stage, p, fights, perDay, seed, opening: withSkill is null ? null
            : (crew, dice) => new WoundBound.Game.Skills.SkillChallenge(crew, SkillSequence(withSkill), dice).Run());
        var name = p().Name;
        var lasted = days.Where(d => d.Survived).ToList();
        var bodyLeft = lasted.Count == 0 ? 0 : lasted.Average(d => 1 - d.Fights.Sum(f => f.Cost?.BodyLost ?? 0));
        Console.WriteLine($"{name,-24}  {(double)lasted.Count / days.Count,8:P0}  {days.Average(d => d.Fights.Count(f => f.Won)),10:0.00}  {days.Average(d => d.Dead),17:0.00}  {bodyLeft,22:P0}");
    }

    return;
}

var clock = System.Diagnostics.Stopwatch.StartNew();
var reports = parties.Select(p => Simulator.Run(Stage, p, fights, seed, streamDice: cheat || args.Contains("--stream-dice"))).ToList();
// --stats: what the run cost the machine — for speed work, where allocation is the cost that counts.
if (args.Contains("--stats"))
{
    var played = Math.Max(1, reports.Sum(r => r.Fights.Count));
    Console.WriteLine($"Stats: {clock.Elapsed.TotalSeconds:0.0} s wall, {System.Diagnostics.Process.GetCurrentProcess().TotalProcessorTime.TotalSeconds:0} s CPU, "
                      + $"{GC.GetTotalAllocatedBytes() / 1e9 / played:0.00} GB allocated per fight, GCs {GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)}, "
                      + $"{GC.GetTotalPauseDuration().TotalSeconds:0.0} s paused.");
}

Console.WriteLine(Simulator.Table(reports));
Console.WriteLine();
Console.WriteLine(Simulator.AttritionTable(reports));
Console.WriteLine();
Console.WriteLine("Obj% is fights with every objective met. Body left and Up (characters still standing) are averaged over wins only.");
Console.WriteLine("Foes = dropped, Fled = got away. Attacks to Leavers (reactions to a foe leaving reach) are per fight, across the party.");
Console.WriteLine("W/L/S/X = wins, losses, stalemates (50 rounds), withdrawals (the party got clear of foes still willing to fight).");
var (weighedAll, changedAll, failedAll) = utilityGms.Aggregate((0, 0, 0), (t, g) =>
    (t.Item1 + g.ForesightTally.Weighed, t.Item2 + g.ForesightTally.Changed, t.Item3 + g.ForesightTally.Failed));
if (weighedAll > 0)
{
    Console.WriteLine($"Look-ahead: {weighedAll} decisions weighed, {changedAll} ({(double)changedAll / weighedAll:P0}) taken other than the top-scored plan, {failedAll} games lost to a refusal.");
}

// The GM's ruthlessness round by round, averaged over every fight still going at that round.
if (utilityGms.Any(g => g.Intent.Steer is not null))
{
    var rounds = utilityGms.Select(g => g.RuthlessnessByRound).Where(r => r.Count > 0).ToList();
    var by = Enumerable.Range(0, Math.Min(8, rounds.Max(r => r.Count)))
        .Select(i => rounds.Where(r => r.Count > i).Average(r => r[i]).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
    Console.WriteLine($"Ruthlessness by round: {string.Join(" ", by)} (fight mean {rounds.Average(r => r.Average()):0.00}).");
}

int Option(string name, int fallback) => int.TryParse(Text(name, null), out var value) ? value : fallback;

string? Text(string name, string? fallback)
{
    var at = Array.IndexOf(args, name);
    return at >= 0 && at + 1 < args.Length ? args[at + 1] : fallback;
}
