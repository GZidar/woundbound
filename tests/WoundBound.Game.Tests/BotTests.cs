using WoundBound.Game.Combat;
using WoundBound.Game.Combat.Brains;
using WoundBound.Game.Simulation;
using WoundBound.Rules.Enums;

namespace WoundBound.Game.Tests;

/// <summary>Bot v bot: party brains, the driver that plays them, and the simulator.</summary>
public class BotTests
{
    private static readonly Game.Data.GameData Data = TestData.Load();

    private static Encounter Stage(IDiceRoller dice) => SampleEncounters.FighterVersusRatkin(Data, dice);

    public static TheoryData<PartyTactics> EveryTactic => new()
    {
        new PartyTactics("Charge"),
        new PartyTactics("Never dodge", PanicDodges: PanicDodgePolicy.Never),
        new PartyTactics("Ripostes", PanicDodges: PanicDodgePolicy.OnlyRipostes),
        new PartyTactics("Extra attacks", BuyExtraAttacks: true, StaminaReserve: 0),
        new PartyTactics("Brace", BraceBelowBody: 0.9),
        new PartyTactics("Bow", ShootWhenOutOfReach: true),
        new PartyTactics("Everything", ShootWhenOutOfReach: true, BuyExtraAttacks: true, BraceBelowBody: 0.6),
    };

    [Theory]
    [MemberData(nameof(EveryTactic))]
    public void EveryTactic_PlaysFightsToTheEnd_WithoutTheRulesRefusingIt(PartyTactics tactics)
    {
        // Two hundred random fights: a brain choosing something illegal would throw.
        var report = Simulator.Run(Stage, () => new TacticalPartyBrain(tactics), fights: 200, seed: 7);

        Assert.Equal(200, report.Fights.Count);
        Assert.Equal(0, report.Stalemates);
        Assert.All(report.Fights, f => Assert.True(f.Won ? f.FoesDropped == 3 : f.PartyBodyLeft <= 0 || f.PartyStaminaLeft == 0));
    }

    [Fact]
    public void TheSameSeed_PlaysTheSameFight()
    {
        var tactics = new PartyTactics("Bow", ShootWhenOutOfReach: true);

        var first = Simulator.Run(Stage, () => new TacticalPartyBrain(tactics), fights: 20, seed: 3);
        var again = Simulator.Run(Stage, () => new TacticalPartyBrain(tactics), fights: 20, seed: 3);

        Assert.Equal(first.Fights.Select(f => (f.Outcome, f.Rounds, f.PartyBodyLeft)), again.Fights.Select(f => (f.Outcome, f.Rounds, f.PartyBodyLeft)));
    }

    [Fact]
    public void BowFirst_SwapsWithTheMove_AndShootsWithTheAction()
    {
        // No ratkin can be reached on the first turn. 6+6 is a critical: max d6 + a d6 of 1, +2 = 9.
        var encounter = SampleEncounters.FighterVersusRatkin(Data, new ScriptedDice(6, 6, 1));
        var driver = new BotDriver(encounter, new TacticalPartyBrain(new PartyTactics("Bow", ShootWhenOutOfReach: true)));

        driver.Step();
        driver.Step();

        Assert.Equal("Short Bow", encounter.Active!.InHandWeapon?.Name);
        Assert.Equal((1, 1), (driver.Tally.Swaps, driver.Tally.Attacks));
        Assert.Single(encounter.Combatants, c => c.Side == Side.Adversaries && c.IsOutOfFight);
    }

    [Fact]
    public void TheDriver_AnswersAReactionPrompt_ForTheParty()
    {
        // The Biter rolls 8+6 = 14 against DT 14; the bot dodges it, as a player could.
        var encounter = SampleEncounters.FighterVersusRatkin(Data, new ScriptedDice(8, 6, 3, 2));
        var fighter = encounter.Active!;
        fighter.Position = new GridPosition(4, 4);
        encounter.EndTurn();
        var driver = new BotDriver(encounter, new TacticalPartyBrain(new PartyTactics("Charge")));

        driver.Step();
        Assert.Equal(EncounterPhase.AwaitingReaction, encounter.Phase);
        driver.Step();

        Assert.Equal((1, 1), (driver.Tally.DodgesOffered, driver.Tally.DodgesTaken));
        Assert.Equal(fighter.BodyMax, fighter.Body);
    }

    private sealed class CheatingBrain : IPartyBrain
    {
        public string Name => "Cheat";

        public Combatant ChooseNextCharacter(BattleView view, IReadOnlyList<Combatant> stillToAct) => stillToAct[0];

        public PartyStep NextStep(BattleView view, Combatant character) => new PartyStep.Attack(view.Adversaries.First());

        public bool PanicDodges(BattleView view, PendingReaction reaction) => true;

        public bool StandsFirm(BattleView view, PendingReaction reaction) => true;

        public bool ArrestsShove(BattleView view, PendingReaction reaction) => true;

        public LeaverResponse ReactsToLeaver(BattleView view, PendingReaction reaction) => LeaverResponse.LetThemGo;
    }

    [Fact]
    public void AStepTheRulesRefuse_Throws_SayingWhy()
    {
        var encounter = SampleEncounters.FighterVersusRatkin(Data, new ScriptedDice());
        var driver = new BotDriver(encounter, new CheatingBrain());

        var refused = Assert.Throws<InvalidOperationException>(() => driver.Step());

        Assert.Contains("\"Cheat\"", refused.Message);
        Assert.Contains("Out of reach", refused.Message);
    }

    [Fact]
    public void BracingWhenHurt_TakesDodgeBeforeAnythingElse()
    {
        var encounter = SampleEncounters.FighterVersusRatkin(Data, new ScriptedDice());
        var fighter = encounter.Active!;
        fighter.Body = 3;
        fighter.Position = new GridPosition(5, 4);
        var brain = new TacticalPartyBrain(new PartyTactics("Brace", BraceBelowBody: 0.5));

        var step = brain.NextStep(encounter.View, fighter);

        Assert.Equal(new PartyStep.StandGround(CombatAction.Dodge), step);
    }
}
