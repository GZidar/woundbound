using WoundBound.Game.Combat;
using WoundBound.Game.Combat.Brains;
using WoundBound.Game.Simulation;

namespace WoundBound.Game.Tests;

/// <summary>A party of three: the Quickstart's characters, and the players choosing their own order (Core Rules §9).</summary>
public class PartyTests
{
    private static readonly Game.Data.GameData Data = TestData.Load();

    private static Encounter Ambush(int seed = 1) => SampleEncounters.PartyVersusAmbush(Data, new RandomDiceRoller(new Random(seed)));

    private static Combatant Named(Encounter encounter, string name) => encounter.Combatants.Single(c => c.Name == name);

    [Fact]
    public void TheQuickstartCharacters_ComeOutAsTheirSheetsSay()
    {
        // Quickstart Adventure, "The Player Characters" — every number derived by the Rules library.
        var encounter = Ambush();
        var salla = Named(encounter, "Salla Rinehart");
        var drovan = Named(encounter, "Drovan Thale");

        Assert.Equal((8, 7, 15, 13, 13), (salla.BodyMax, salla.StaminaMax, salla.DodgeThreshold, salla.StabilityThreshold, salla.ResolveThreshold!.Value));
        Assert.Equal(("Rapier", 4, 6, 3), (salla.Attack.Name, salla.Attack.AttackModifier, salla.Attack.DamageDieSides, salla.Attack.DamageModifier));

        Assert.Equal((6, 5, 15, 11, 13), (drovan.BodyMax, drovan.StaminaMax, drovan.DodgeThreshold, drovan.StabilityThreshold, drovan.ResolveThreshold!.Value));
        Assert.Equal(("Short Bow", 4, 6, 3), (drovan.Attack.Name, drovan.Attack.AttackModifier, drovan.Attack.DamageDieSides, drovan.Attack.DamageModifier));
        Assert.True(drovan.Attack.IsRanged);
        Assert.Equal("Dagger", drovan.ReadyWeapon?.Name);
    }

    [Fact]
    public void ThreeCharacters_GiveTheGmThreeTurnsARound()
    {
        Assert.Equal(3, Ambush().AdversaryTurnsPerRound);
    }

    [Fact]
    public void TheRoundOpens_WithThePlayersChoosingWhoActs()
    {
        var encounter = Ambush();

        Assert.Equal(EncounterPhase.ChoosingCharacter, encounter.Phase);
        Assert.Null(encounter.Active);
        Assert.Equal(3, encounter.PartyStillToAct.Count);
        Assert.False(encounter.TryMove(new GridPosition(2, 4)));
    }

    [Fact]
    public void AnyoneLeftToAct_MayGoNext_ButNobodyTwice()
    {
        var encounter = Ambush();
        var drovan = Named(encounter, "Drovan Thale");
        var salla = Named(encounter, "Salla Rinehart");

        Assert.False(encounter.TryBeginTurn(encounter.Combatants.First(c => c.Side == Side.Adversaries)));
        Assert.True(encounter.TryBeginTurn(drovan));
        Assert.Equal(EncounterPhase.PartyTurn, encounter.Phase);
        Assert.Same(drovan, encounter.Active);
        Assert.False(encounter.TryBeginTurn(salla));

        encounter.EndTurn();
        RunGmTurns(encounter);

        Assert.Equal(EncounterPhase.ChoosingCharacter, encounter.Phase);
        Assert.DoesNotContain(drovan, encounter.PartyStillToAct);
        Assert.False(encounter.TryBeginTurn(drovan));
        Assert.True(encounter.TryBeginTurn(salla));
    }

    [Fact]
    public void TheLastCharacterLeft_TakesTheirTurnWithoutBeingChosen()
    {
        var encounter = Ambush();

        encounter.TryBeginTurn(Named(encounter, "Drovan Thale"));
        encounter.EndTurn();
        RunGmTurns(encounter);
        encounter.TryBeginTurn(Named(encounter, "Salla Rinehart"));
        encounter.EndTurn();
        RunGmTurns(encounter);

        Assert.Equal(EncounterPhase.PartyTurn, encounter.Phase);
        Assert.Equal("The Dumb Fighter", encounter.Active?.Name);
    }

    [Fact]
    public void ACharacterWhoIsDown_IsPassedOver_AndTheGmStillGetsTheTurnAfterThem()
    {
        var encounter = Ambush();
        var salla = Named(encounter, "Salla Rinehart");

        encounter.TryBeginTurn(Named(encounter, "Drovan Thale"));
        salla.Body = WoundBound.Rules.Calculation.PlayStateCalculator.MinBodyWounds; // dead: no Presence roll to make
        encounter.EndTurn();
        RunGmTurns(encounter);

        // Salla's gap still gave the GM an owed turn: two taken so far, and the Fighter is the only one left to act.
        Assert.Contains(encounter.Log, line => line == "Salla Rinehart has no turn to take.");
        Assert.Equal(EncounterPhase.PartyTurn, encounter.Phase);
        Assert.Equal("The Dumb Fighter", encounter.Active?.Name);
        Assert.Empty(encounter.PartyStillToAct);
    }

    [Fact]
    public void ABot_ChoosesWhoActs_AndPlaysTheWholeParty()
    {
        // Every tactic, many seeds: a brain that broke a rule would throw.
        foreach (var tactics in PartyTactics.Presets)
        {
            var report = Simulator.Run(dice => SampleEncounters.PartyVersusAmbush(Data, dice), () => new TacticalPartyBrain(tactics), fights: 15);
            Assert.Equal(15, report.Fights.Count);
            Assert.True(report.Wins + report.Losses > 0);
        }
    }

    [Fact]
    public void TheBotSendsInWhoeverCanAlreadyStrike()
    {
        var encounter = Ambush();
        var drovan = Named(encounter, "Drovan Thale");
        var brain = new TacticalPartyBrain(PartyTactics.Presets[0]);

        // Only the archer can reach anybody from where the party starts.
        Assert.Same(drovan, brain.ChooseNextCharacter(encounter.View, encounter.PartyStillToAct));
    }

    private static void RunGmTurns(Encounter encounter)
    {
        while (encounter.Phase == EncounterPhase.AdversaryTurn)
        {
            encounter.RunAdversaryTurn();
        }

        // A dice roll aimed at a character might be waiting on a panic dodge; take the hit.
        while (encounter.Phase == EncounterPhase.AwaitingReaction)
        {
            encounter.RespondToReaction(spend: false);
            while (encounter.Phase == EncounterPhase.AdversaryTurn)
            {
                encounter.RunAdversaryTurn();
            }
        }
    }
}
