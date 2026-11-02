using WoundBound.Game.Combat;
using WoundBound.Game.Combat.Brains.Utility;
using WoundBound.Rules.Enums;

namespace WoundBound.Game.Tests;

/// <summary>
/// Core Rules §11, "Buff / Bane Spells": a Bane rolls nothing and takes hold, a point off a
/// Threshold until the caster's next turn — and the brains reckon it by what that point lets land.
/// </summary>
public class BaneTests
{
    private static readonly Game.Data.GameData Data = TestData.Load();

    /// <summary>Mirella at (2, 4); Salla and Joren either side of a Roadside Bandit at (6, 4), and Drovan with his bow at (2, 6).</summary>
    private static (Encounter Encounter, Combatant Mirella, Combatant[] Others, Combatant Bandit) Setup()
    {
        var mirella = CombatantFactory.FromCharacter(SampleEncounters.MirellaVask(Data), new GridPosition(2, 4));
        Combatant[] others =
        [
            CombatantFactory.FromCharacter(SampleEncounters.SallaRinehart(Data), new GridPosition(5, 4)),
            CombatantFactory.FromCharacter(SampleEncounters.JorenBlacke(Data), new GridPosition(7, 4)),
            CombatantFactory.FromCharacter(SampleEncounters.DrovanThale(Data), new GridPosition(2, 6)),
        ];
        var bandit = CombatantFactory.FromAdversary(SampleEncounters.RoadsideBandit, "Bandit", new GridPosition(6, 4));
        var encounter = new Encounter(SampleEncounters.FieldWidth, SampleEncounters.FieldHeight, [mirella, .. others, bandit], Data.RangeBands,
            new ScriptedDice(), environment: Data.Environment);
        encounter.TryBeginTurn(mirella);
        mirella.HasMove = false;
        return (encounter, mirella, others, bandit);
    }

    private static IEnumerable<Option> Curses(Encounter encounter, Combatant mirella) =>
        new TurnPlanner(new Assessor(encounter.View), BrainSkill.Best).Options(mirella, false, 1, real: true)
            .Where(o => o.Cast?.Spell.Name == "Curse");

    [Fact]
    public void ACurse_TakesHold_WithNoRoll_APointOffTheChosenThreshold()
    {
        var (encounter, mirella, _, bandit) = Setup();
        var before = bandit.DodgeThreshold;

        Assert.True(encounter.TryCast(new SpellCast(mirella.Spells.Single(s => s.Name == "Curse")) { Target = bandit, Threshold = ThresholdType.Dodge }),
            string.Join("\n", encounter.Log));

        Assert.Equal(before - 1, bandit.DodgeThreshold);
    }

    [Fact]
    public void TheBrain_CursesTheDodge_OfAFoe_ThreeOfOursAreStillToStrikeAt()
    {
        var (encounter, mirella, _, bandit) = Setup();

        var curse = Curses(encounter, mirella).OrderByDescending(o => o.Score).FirstOrDefault();

        Assert.NotNull(curse);
        Assert.Equal(bandit, curse!.Cast!.Target);
        Assert.Equal(ThresholdType.Dodge, curse.Cast.Threshold);
    }

    /// <summary>
    /// A fresh round: a Noble Champion — DT 15, hard to hit — ringed by Salla, Joren, Drovan and the Dumb Fighter, and
    /// a Mirella with no Smite or Scourging Light to reach for instead. The fighters lose nothing by
    /// waiting; what she can add is worth most before their blows.
    /// </summary>
    private static (Encounter Encounter, Combatant Mirella) Ringed()
    {
        var build = SampleEncounters.MirellaVask(Data);
        build.Spells.RemoveAll(sp => sp.SpellType is SpellType.Harm or SpellType.Control);
        var mirella = CombatantFactory.FromCharacter(build, new GridPosition(2, 4));
        Combatant[] fighters =
        [
            CombatantFactory.FromCharacter(SampleEncounters.SallaRinehart(Data), new GridPosition(5, 4)),
            CombatantFactory.FromCharacter(SampleEncounters.JorenBlacke(Data), new GridPosition(7, 4)),
            CombatantFactory.FromCharacter(SampleEncounters.DrovanThale(Data), new GridPosition(6, 5)),
            CombatantFactory.FromCharacter(SampleEncounters.DumbFighter(Data), new GridPosition(6, 3)),
        ];
        var champion = CombatantFactory.FromAdversary(Data.Adversary("Noble Champion"), "Champion", new GridPosition(6, 4));
        var encounter = new Encounter(SampleEncounters.FieldWidth, SampleEncounters.FieldHeight, [mirella, .. fighters, champion], Data.RangeBands,
            new ScriptedDice(), environment: Data.Environment);
        return (encounter, mirella);
    }

    private static string FirstPlan(Encounter encounter, Combatant mirella, BrainSkill skill) =>
        new TurnPlanner(new Assessor(encounter.View), skill)
            .Options(mirella, true, 1, real: false, stillToAct: encounter.PartyStillToAct).MaxBy(o => o.Score)!.Why;

    [Fact]
    public void WithAFoeSurrounded_TheCasterGoesFirst_SoTheCurseLandsBeforeTheBlows()
    {
        // With Help and Harry left alone, the Curse is the setup she has. Four blows to come at DT 15
        // make the point off it worth more than her own sword stroke — and she goes first for it.
        var (encounter, mirella) = Ringed();
        var skill = BrainSkill.Best with { Off = new HashSet<Trick> { Trick.Aid } };

        Assert.Equal(mirella, new UtilityPartyBrain(skill).ChooseNextCharacter(encounter.View, encounter.PartyStillToAct));
        Assert.Contains("Curse at Champion", FirstPlan(encounter, mirella, skill));
    }

    [Fact]
    public void ACurse_IsntWorthItsWound_WhenNobodyIsLeftToMakeUseOfIt()
    {
        // A point off one foe's Dodge for one blow buys less than the Soul it costs.
        var (encounter, mirella, others, _) = Setup();
        foreach (var other in others)
        {
            other.HasActedThisRound = true;
        }

        Assert.Empty(Curses(encounter, mirella));
    }
}
