using WoundBound.Game.Combat;
using WoundBound.Rules.Enums;

namespace WoundBound.Game.Tests;

/// <summary>The Noble Champion's traits (GM Reference §9): Unshakable, and Fitness.</summary>
public class ChampionTests
{
    private static readonly Game.Data.GameData Data = TestData.Load();

    /// <summary>A GM whose creatures stand where they are and do nothing.</summary>
    private sealed class HoldingGm : Combat.Brains.SimpleGmBrain
    {
        public override Combat.Brains.GmStep NextStep(Combat.Brains.BattleView view, Combat.Brains.GmTurn turn) => Combat.Brains.GmStep.End;

        public override Combat.Brains.GmCloseIn? NextCloseIn(Combat.Brains.BattleView view, IReadOnlyList<Combatant> stillIdle) => null;
    }

    private static (Encounter Encounter, Combatant Joren, Combatant Champion) Facing(int[] dice, bool holding = false)
    {
        var joren = CombatantFactory.FromCharacter(SampleEncounters.JorenBlacke(Data), new GridPosition(4, 4));
        var champion = CombatantFactory.FromAdversary(Data.Adversary("Noble Champion"), "Champion", new GridPosition(5, 4));
        var encounter = new Encounter(SampleEncounters.FieldWidth, SampleEncounters.FieldHeight, [joren, champion], Data.RangeBands,
            new ScriptedDice(dice), holding ? new HoldingGm() : null, environment: Data.Environment);
        return (encounter, joren, champion);
    }

    [Fact]
    public void TheChampion_IsUnshakable_AndFit()
    {
        var (_, _, champion) = Facing([]);

        Assert.True(champion.IsUnshakable);
        Assert.Equal(1, champion.StaminaEachRound);
    }

    [Fact]
    public void TrippingAnUnshakableFoe_IsRolledAtDisadvantage()
    {
        // Three dice for Disadvantage: 12, 11 and 2 — the lowest two, 11+2, are what counts.
        var (encounter, joren, champion) = Facing([12, 11, 2]);
        champion.Stamina = 0;
        encounter.TryBeginTurn(joren);

        Assert.True(encounter.TryControl(ControlAction.Trip, champion), string.Join("\n", encounter.Log));

        Assert.Contains(encounter.Log, l => l.Contains("Unshakable: Disadvantage on the trip"));
        Assert.False(champion.IsProne, string.Join("\n", encounter.Log));
    }

    [Fact]
    public void Fitness_GivesBackAStaminaAtTheStartOfEachRound_UpToItsPool()
    {
        var (encounter, joren, champion) = Facing([], holding: true);
        champion.Stamina = 1;

        encounter.TryBeginTurn(joren);
        encounter.EndTurn();
        encounter.RunAdversaryTurn();

        Assert.Equal(2, encounter.Round);
        Assert.True(champion.Stamina >= 2, string.Join("\n", encounter.Log));
        Assert.Contains(encounter.Log, l => l.Contains("Fitness: +1 Stamina"));
    }
}
