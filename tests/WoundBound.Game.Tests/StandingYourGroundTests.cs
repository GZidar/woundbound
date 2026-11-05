using WoundBound.Game.Combat;
using WoundBound.Game.Combat.Brains;
using WoundBound.Rules.Enums;

namespace WoundBound.Game.Tests;

/// <summary>Core Rules §10, "Standing Your Ground": Dodge, Grounded and Resolute.</summary>
public class StandingYourGroundTests
{
    private static readonly Game.Data.GameData Data = TestData.Load();

    private static (Encounter Encounter, Combatant Fighter, Combatant Biter) Fight(IGmBrain? gm, params int[] dice) => Stage(true, gm, dice);

    /// <summary>The same stage with the ratkin played as Brutes, one to a turn: as Minions they act together as a mob (Core Rules §9).</summary>
    private static (Encounter Encounter, Combatant Fighter, Combatant Biter) Crowd(IGmBrain? gm, params int[] dice) => Stage(false, gm, dice);

    private static (Encounter Encounter, Combatant Fighter, Combatant Biter) Crowd(params int[] dice) => Crowd(null, dice);

    private static (Encounter Encounter, Combatant Fighter, Combatant Biter) Stage(bool asMinions, IGmBrain? gm, int[] dice)
    {
        var encounter = SampleEncounters.FighterVersusRatkin(Data, new ScriptedDice(dice), gm, asMinions);
        return (encounter, encounter.Combatants.Single(c => c.Side == Side.Party), encounter.Combatants.Single(c => c.Name == "Ratkin Biter"));
    }

    private static (Encounter Encounter, Combatant Fighter, Combatant Biter) Fight(params int[] dice) => Fight(null, dice);

    [Fact]
    public void TheThreeThresholds_ComeFromTheRules()
    {
        // Core Rules §10: DT 11 + Tier + Agility, ST 11 + Tier + Strength, RT 11 + Tier + Intellect.
        var (_, fighter, _) = Fight();

        Assert.Equal(14, fighter.DodgeThreshold);
        Assert.Equal(15, fighter.StabilityThreshold);
        Assert.Equal(11, fighter.ResolveThreshold);
    }

    [Fact]
    public void Dodge_TakesTheMove_ForPlusTwoDt_AndNoMoreMovingThisTurn()
    {
        var (encounter, fighter, _) = Fight();

        Assert.True(encounter.TryStandGround(CombatAction.Dodge));

        Assert.Equal(16, fighter.DodgeThreshold);
        Assert.False(fighter.HasMove);
        Assert.Equal(1, fighter.ActionsRemaining);
        Assert.Empty(encounter.ReachableSquares(fighter));
        Assert.False(encounter.TryMove(new GridPosition(2, 4)));
        Assert.Equal(new GridPosition(1, 4), fighter.Position);
    }

    [Fact]
    public void SomebodyWhoHasMoved_CantStandTheirGround()
    {
        var (encounter, _, _) = Fight();
        encounter.TryMove(new GridPosition(2, 4));

        Assert.NotNull(Encounter.StandGroundRefusal(encounter.Active!, CombatAction.Dodge));
        Assert.False(encounter.TryStandGround(CombatAction.Dodge));
    }

    [Fact]
    public void AllThree_TheMoveThenTheActionThenOneBoughtWithStamina_ButNoneTwice()
    {
        var (encounter, fighter, _) = Fight();

        Assert.True(encounter.TryStandGround(CombatAction.Dodge));
        Assert.False(encounter.TryStandGround(CombatAction.Dodge));
        Assert.True(encounter.TryStandGround(CombatAction.Grounded));
        Assert.Equal(0, fighter.ActionsRemaining);
        Assert.False(encounter.TryStandGround(CombatAction.Resolute));

        Assert.True(encounter.TryBuyExtraAction());
        Assert.True(encounter.TryStandGround(CombatAction.Resolute));

        Assert.Equal((16, 17, 13), (fighter.DodgeThreshold, fighter.StabilityThreshold, fighter.ResolveThreshold!.Value));
        Assert.Equal(1, encounter.BankedOpportunities);
    }

    [Fact]
    public void WhileStandingGround_AttacksHaveDisadvantage()
    {
        // 6, 5 and 1: Disadvantage keeps the low pair, 5+1 +4 = 10 against the Biter's 11 — a miss
        // that 6+5 would have made a hit.
        var (encounter, fighter, biter) = Fight(6, 5, 1);
        fighter.Position = new GridPosition(6, 2);
        encounter.TryStandGround(CombatAction.Dodge);

        var outcome = encounter.TryAttack(biter)!;

        Assert.True(outcome.Roll.AppliedDisadvantage);
        Assert.False(outcome.Roll.Success);
        Assert.Contains(encounter.Log, line => line.Contains("Disadvantage for standing their ground"));
    }

    [Fact]
    public void SwappingLoadouts_IsNotMovement_SoABracedCharacterCanStillDoIt_WithTheAction()
    {
        var (encounter, fighter, _) = Fight();
        encounter.TryStandGround(CombatAction.Dodge);

        Assert.True(encounter.TrySwapLoadout());

        Assert.Equal("Short Bow", fighter.InHandWeapon?.Name);
        Assert.Equal(0, fighter.ActionsRemaining);
    }

    [Fact]
    public void TheBonus_HoldsThroughTheGmsTurn_AndStacksWithAPanicDodge()
    {
        // The Biter rolls 9+7 = 16 against the braced 16: a hit, which a panic dodge to 18 would turn.
        var (encounter, fighter, _) = Fight(9, 7);
        fighter.Position = new GridPosition(4, 4);
        encounter.TryStandGround(CombatAction.Dodge);
        encounter.EndTurn();

        encounter.RunAdversaryTurn();

        var pending = encounter.Pending!;
        Assert.Equal(16, pending.Threshold);
        Assert.Equal(18, pending.RaisedThreshold);
    }

    [Fact]
    public void ItEnds_AtTheStartOfTheirNextTurn()
    {
        var (encounter, fighter, _) = Crowd();
        encounter.TryStandGround(CombatAction.Dodge);
        encounter.EndTurn();

        encounter.RunAdversaryTurn();

        Assert.Equal(2, encounter.Round);
        Assert.Equal(14, fighter.DodgeThreshold);
        Assert.False(fighter.IsStandingGround);
        Assert.NotEmpty(encounter.ReachableSquares(fighter));
        Assert.Contains(encounter.Log, line => line.Contains("stops standing their ground (Dodge)"));
    }

    private sealed class BracingBrain : SimpleGmBrain
    {
        public override GmStep NextStep(BattleView view, GmTurn turn) =>
            !turn.HasStoodGround && !turn.HasAttacked ? new GmStep.StandGround(CombatAction.Dodge) : base.NextStep(view, turn);
    }

    [Fact]
    public void AnAdversaryCanStandItsGroundToo_AtTheSamePrice()
    {
        // The Biter braces (DT 11 → 13) and bites at Disadvantage: 2, 3 and 4 keep 2+3 = 5, a miss.
        // It's still braced on the fighter's next turn.
        var (encounter, fighter, biter) = Crowd(new BracingBrain(), 2, 3, 4);
        fighter.Position = new GridPosition(6, 2);
        encounter.EndTurn();

        encounter.RunAdversaryTurn();

        Assert.Equal(EncounterPhase.PartyTurn, encounter.Phase);
        Assert.Equal(13, biter.DodgeThreshold);
        Assert.Contains(encounter.Log, line => line.StartsWith("Ratkin Biter attacks") && line.Contains("Disadvantage"));
        Assert.Equal(13, encounter.PlanAttack(fighter, biter).DodgeThreshold);
    }
}
