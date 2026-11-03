using WoundBound.Game.Combat;
using WoundBound.Game.Data;

namespace WoundBound.Game.Tests;

/// <summary>The records the screen animates from: each one raised as its line is written, saying what the line says.</summary>
public class HappeningTests
{
    private static readonly GameData Data = TestData.Load();

    private static (Encounter Fight, Combatant Fighter, List<Happening> Seen) Fight(params int[] dice)
    {
        var encounter = SampleEncounters.FighterVersusRatkin(Data, new ScriptedDice(dice));
        var seen = new List<Happening>();
        encounter.Happened += seen.Add;
        var fighter = encounter.Combatants.Single(c => c.Side == Side.Party);
        Assert.True(encounter.TryMove(new GridPosition(4, 4)));
        encounter.EndTurn();
        return (encounter, fighter, seen);
    }

    [Fact]
    public void AHit_IsRaisedAsAStruckThatHit()
    {
        // 8+6 = 14 against DT 14, not dodged: a hit for d4 = 3.
        var (fight, fighter, seen) = Fight(8, 6, 3, 2);
        fight.RunAdversaryTurn();
        fight.RespondToReaction(spend: false);

        var struck = Assert.IsType<Struck>(Assert.Single(seen));
        Assert.Same(fighter, struck.Target);
        Assert.True(struck.Hit);
        Assert.False(struck.Dodged);
        Assert.False(struck.With.IsRanged);
    }

    [Fact]
    public void ADodgedCritical_IsRaisedAsARiposte()
    {
        var (fight, fighter, seen) = Fight(7, 7, 2, 3);
        fight.RunAdversaryTurn();
        fight.RespondToReaction(spend: true);

        var struck = Assert.IsType<Struck>(seen.First());
        Assert.Same(fighter, struck.Target);
        Assert.False(struck.Hit);
        Assert.True(struck.Dodged);
        Assert.True(struck.Riposte);
    }
}
