using WoundBound.Game.Combat;
using WoundBound.Rules.Enums;

namespace WoundBound.Game.Tests;

/// <summary>The scene editor's Threat readout: the chosen foes priced against the chosen party (GM Reference §5).</summary>
public class SceneThreatTests
{
    private static readonly Game.Data.GameData Data = TestData.Load();

    [Fact]
    public void No_foes_named_prices_the_clearings_band_against_the_quickstart_five()
    {
        var threat = SceneThreat.Of(Data, SceneMap.Blank(12, 8, "Empty"));

        // Three Skulkers and two Wolves (Skirmishers, 0.5) and two Ratkin (Minions, 0.25).
        Assert.Equal(3.0, threat.Spend, 3);
        Assert.Equal(5, threat.PartySize);
        Assert.Equal(ChallengeDifficulty.Easy, threat.Difficulty);
    }

    [Fact]
    public void Foes_are_summed_and_read_against_the_chosen_partys_budget()
    {
        var map = SceneMap.Blank(12, 8, "Road");
        map.Party = ["Salla Rinehart", "Joren Blacke", "Drovan Thale", "Mirella Vask"];
        map.Foes.Add(new SceneMap.FoeGroup("Tollroad Bandit", 4));
        map.Foes.Add(new SceneMap.FoeGroup("Cave Bear", 1));

        var threat = SceneThreat.Of(Data, map);

        Assert.Equal(5.5, threat.Spend, 3);  // 4 × 1 + 1.5
        Assert.Equal(ChallengeDifficulty.Hard, threat.Difficulty);  // over 4, inside 6
        Assert.Contains(threat.Notes, n => n.Contains("GM turns"));  // five foes, four characters
    }

    [Fact]
    public void Creatures_above_the_partys_tier_pay_full_price_and_a_solo_is_the_whole_budget()
    {
        var map = SceneMap.Blank(12, 8, "Barrow");
        map.Foes.Add(new SceneMap.FoeGroup("The Ashen Warden", 1));
        map.Foes.Add(new SceneMap.FoeGroup("Stoneheart Golem", 1));

        var threat = SceneThreat.Of(Data, map);

        Assert.True(threat.HasSolo);
        Assert.Equal(1.5, threat.Spend, 3);
        Assert.Contains("Solo", threat.Summary());
    }

    [Fact]
    public void Too_much_reads_as_beyond_deadly()
    {
        var map = SceneMap.Blank(12, 8, "Den");
        map.Foes.Add(new SceneMap.FoeGroup("Cave Bear", 8));

        Assert.Null(SceneThreat.Of(Data, map).Difficulty);
        Assert.Contains("beyond Deadly", SceneThreat.Of(Data, map).Summary());
    }
}
