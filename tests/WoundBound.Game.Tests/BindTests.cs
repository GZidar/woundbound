using WoundBound.Game.Combat;
using WoundBound.Game.Combat.Brains;
using WoundBound.Rules.Enums;

namespace WoundBound.Game.Tests;

/// <summary>Core Rules §11, "Motion Spells": Bind tightens by Tier — Slowed, Stuck, Frozen — and the innate kind.</summary>
public class BindTests
{
    private static readonly Game.Data.GameData Data = TestData.Load();

    private sealed class StepsGm(params GmStep[] steps) : SimpleGmBrain
    {
        private int next;

        public override GmStep NextStep(BattleView view, GmTurn turn) => next < steps.Length ? steps[next++] : GmStep.End;
    }

    [Theory]
    [InlineData(Tier.T1, CharacterCondition.Slowed)]
    [InlineData(Tier.T2, CharacterCondition.Stuck)]
    [InlineData(Tier.T3, CharacterCondition.Frozen)]
    public void BindTightensByTier(Tier tier, CharacterCondition effect) => Assert.Equal(effect, Encounter.BindEffect(tier));

    [Fact]
    public void Stuck_CantChangeSquare_ButMayAct()
    {
        var fighter = CombatantFactory.FromCharacter(SampleEncounters.DumbFighter(Data), new GridPosition(2, 4));
        var encounter = new Encounter(SampleEncounters.FieldWidth, SampleEncounters.FieldHeight,
            [fighter, CombatantFactory.FromAdversary(TestBandits.Roadside, "Bandit", new GridPosition(15, 15))], Data.RangeBands, new ScriptedDice());
        fighter.SetCondition(CharacterCondition.Stuck, true);
        encounter.TryBeginTurn(fighter);

        Assert.False(encounter.TryMove(new GridPosition(3, 4)));
        Assert.Equal(1, fighter.ActionsRemaining);
    }

    [Fact]
    public void Frozen_MayNeitherMoveNorAct_SoTheTurnPasses()
    {
        var fighter = CombatantFactory.FromCharacter(SampleEncounters.DumbFighter(Data), new GridPosition(2, 4));
        // Two in the party, so nobody's turn begins by itself.
        var salla = CombatantFactory.FromCharacter(SampleEncounters.SallaRinehart(Data), new GridPosition(2, 8));
        var encounter = new Encounter(SampleEncounters.FieldWidth, SampleEncounters.FieldHeight,
            [fighter, salla, CombatantFactory.FromAdversary(TestBandits.Roadside, "Bandit", new GridPosition(15, 15))], Data.RangeBands, new ScriptedDice());
        fighter.SetCondition(CharacterCondition.Frozen, true);

        encounter.TryBeginTurn(fighter);

        Assert.NotEqual(fighter, encounter.Active);
        Assert.Contains(encounter.Log, l => l.Contains("is Frozen, and can do nothing"));
        Assert.NotNull(Encounter.TacticalSpendRefusal(fighter));
    }

    [Fact]
    public void TheWeepingSentinelsGaze_LeavesItsTargetStuck_UntilItsNextTurn_AndDealsNoDamage()
    {
        // +3 and 11+10 = 24 against the fighter's ST: it takes.
        var fighter = CombatantFactory.FromCharacter(SampleEncounters.DumbFighter(Data), new GridPosition(2, 4));
        fighter.Stamina = 1;
        var sentinel = CombatantFactory.FromAdversary(Data.Adversary("Weeping Sentinel"), "Weeping Sentinel", new GridPosition(3, 4));
        var gaze = sentinel.Attack;
        Assert.Equal(ThresholdType.Stability, gaze.Against);
        Assert.Equal(CharacterCondition.Stuck, gaze.Imposes);

        var encounter = new Encounter(SampleEncounters.FieldWidth, SampleEncounters.FieldHeight, [fighter, sentinel], Data.RangeBands,
            new ScriptedDice(11, 10, 1, 1, 1, 1), new StepsGm(new GmStep.Attack(fighter)));
        encounter.DeclineFreeChoices();
        encounter.TryBeginTurn(fighter);
        encounter.EndTurn();
        var body = fighter.Body;
        encounter.RunAdversaryTurn();

        Assert.Contains(CharacterCondition.Stuck, fighter.Conditions);
        Assert.Equal(body, fighter.Body);
    }
}
