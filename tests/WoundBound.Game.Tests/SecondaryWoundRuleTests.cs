using WoundBound.Game.Combat;
using WoundBound.Rules.Enums;

namespace WoundBound.Game.Tests;

/// <summary>Core Rules §3, "Secondary Wound Rules": Interrupt (Mind) and Shelter (Soul).</summary>
public class SecondaryWoundRuleTests
{
    private static readonly Game.Data.GameData Data = TestData.Load();

    private static Encounter Fight(int[] dice, params Combatant[] combatants) =>
        new(SampleEncounters.FieldWidth, SampleEncounters.FieldHeight, combatants, Data.RangeBands, new ScriptedDice(dice));

    [Fact]
    public void Interrupt_TakesTheTurnBeforeTheDeclaredBlow_AndALandedShotPutsItAtDisadvantage()
    {
        // Salla acts and ends. The bandit beside her declares its Rusty Sword at her; Drovan Interrupts
        // for 1 Mind and shoots it: 10+9 +4 = 23 against DT 15, 3 on the d6, less 1 for leather. Back
        // in the GM's turn, its swing is caught mid-motion: 4, 6, 8 at Disadvantage keeps 4+6, +2 = 12,
        // a miss.
        var salla = CombatantFactory.FromCharacter(SampleEncounters.SallaRinehart(Data), new GridPosition(3, 4));
        var drovan = CombatantFactory.FromCharacter(SampleEncounters.DrovanThale(Data), new GridPosition(0, 4));
        var bandit = CombatantFactory.FromAdversary(TestBandits.Roadside, "Bandit", new GridPosition(4, 4));
        var encounter = Fight([10, 9, 3, 4, 6, 8], salla, drovan, bandit);

        encounter.TryBeginTurn(salla);
        encounter.EndTurn();
        encounter.RunAdversaryTurn();

        Assert.Contains(encounter.Log, l => l == "Bandit declares: Rusty Sword at Salla Rinehart.");
        Assert.Equal(ReactionKind.Interrupt, encounter.Pending!.Kind);
        Assert.Equal(salla, encounter.Pending.Against);
        Assert.True(encounter.RespondToReaction(true));

        Assert.Equal((EncounterPhase.PartyTurn, drovan), (encounter.Phase, encounter.Active));
        Assert.Equal(drovan.MindMax - 1, drovan.Mind);
        Assert.NotNull(encounter.TryAttack(bandit));
        encounter.EndTurn();

        Assert.Contains(encounter.Log, l => l.Contains("caught mid-motion"));
        Assert.Contains(encounter.Log, l => l.StartsWith("Bandit attacks Salla Rinehart") && l.Contains("Disadvantage") && l.EndsWith("miss."));
        Assert.Equal(salla.BodyMax, salla.Body);

        // The Interrupt was Drovan's turn for the round: with it taken, the round is over.
        Assert.Equal(2, encounter.Round);
    }

    [Fact]
    public void Interrupt_ThatTakesTheTargetOutOfReach_LeavesTheDeclaredActionNothingToDo()
    {
        // Drovan is the one declared at; he Interrupts and simply walks out of the bandit's reach.
        var drovan = CombatantFactory.FromCharacter(SampleEncounters.DrovanThale(Data), new GridPosition(3, 4));
        var salla = CombatantFactory.FromCharacter(SampleEncounters.SallaRinehart(Data), new GridPosition(0, 8));
        var bandit = CombatantFactory.FromAdversary(TestBandits.Roadside, "Bandit", new GridPosition(4, 4));
        var encounter = Fight([], salla, drovan, bandit);

        encounter.TryBeginTurn(salla);
        encounter.EndTurn();
        encounter.RunAdversaryTurn();
        bandit.Stamina = 0; // its turn begun: nothing left to strike at him with as he goes
        Assert.Equal(drovan, encounter.Pending!.Against);
        encounter.RespondToReaction(true);

        Assert.True(encounter.TryMove(new GridPosition(0, 4)));
        encounter.EndTurn();

        Assert.Contains(encounter.Log, l => l.Contains("has nothing left to do — the action is spent"));
        Assert.Equal(drovan.BodyMax, drovan.Body);
    }

    [Fact]
    public void Shelter_TakesTheBlowMeantForAnAlly_AsSoul_TheirOwnArmourNoHelp()
    {
        // The bandit hits Mirella: 10+9 +2 = 21 against DT 12; 5 on the d6, her leather takes 1. Joren,
        // within Short, takes the 4 as Soul instead — his chainmail doesn't come into it.
        var mirella = CombatantFactory.FromCharacter(SampleEncounters.MirellaVask(Data), new GridPosition(2, 4));
        var joren = CombatantFactory.FromCharacter(SampleEncounters.JorenBlacke(Data), new GridPosition(2, 6));
        var bandit = CombatantFactory.FromAdversary(TestBandits.Roadside, "Bandit", new GridPosition(3, 4));
        var encounter = Fight([10, 9, 5], mirella, joren, bandit);

        encounter.TryBeginTurn(joren);
        encounter.EndTurn();
        encounter.RunAdversaryTurn();

        Assert.Equal(ReactionKind.Shelter, encounter.Pending!.Kind);
        Assert.Equal((joren, mirella, 4), (encounter.Pending.Defender, encounter.Pending.Attacker, encounter.Pending.ImpactWounds));
        Assert.True(encounter.RespondToReaction(true));

        Assert.Equal(mirella.BodyMax, mirella.Body);
        Assert.Equal(joren.SoulMax - 4, joren.Soul);
        Assert.Equal(joren.StaminaMax, joren.Stamina);
        Assert.True(joren.HasShelteredThisRound);
        Assert.Equal(0, encounter.GmOpportunities);
    }

    [Fact]
    public void Shelter_MayTakeAnyPartOfIt_AndTheAllyTakesTheRest()
    {
        // The same blow, 4 past Mirella's leather: Joren takes 3 of it, as in the Core Rules example, and she takes 1.
        var mirella = CombatantFactory.FromCharacter(SampleEncounters.MirellaVask(Data), new GridPosition(2, 4));
        var joren = CombatantFactory.FromCharacter(SampleEncounters.JorenBlacke(Data), new GridPosition(2, 6));
        var bandit = CombatantFactory.FromAdversary(TestBandits.Roadside, "Bandit", new GridPosition(3, 4));
        var encounter = Fight([10, 9, 5], mirella, joren, bandit);

        encounter.TryBeginTurn(joren);
        encounter.EndTurn();
        encounter.RunAdversaryTurn();

        Assert.False(encounter.RespondToShelter(5));
        Assert.True(encounter.RespondToShelter(3));

        Assert.Equal(mirella.BodyMax - 1, mirella.Body);
        Assert.Equal(joren.SoulMax - 3, joren.Soul);
        Assert.Contains(encounter.Log, l => l.Contains("taking 3 of the 4"));
    }

    [Fact]
    public void TheBrain_SheltersJustEnoughToKeepAnAllyStanding()
    {
        // Mirella on 2 Body with 4 coming: taking 3 leaves her on 1, and keeps two of Joren's Soul spare.
        var mirella = CombatantFactory.FromCharacter(SampleEncounters.MirellaVask(Data), new GridPosition(2, 4));
        var joren = CombatantFactory.FromCharacter(SampleEncounters.JorenBlacke(Data), new GridPosition(2, 6));
        var bandit = CombatantFactory.FromAdversary(TestBandits.Roadside, "Bandit", new GridPosition(3, 4));
        var encounter = Fight([], mirella, joren, bandit);
        mirella.Body = 2;
        var offer = new PendingReaction(ReactionKind.Shelter, joren, mirella, null, 0, "", ImpactWounds: 4);

        var taking = new Game.Combat.Brains.Utility.UtilityPartyBrain().ShelterAmount(encounter.View, offer);

        Assert.InRange(taking, 3, Math.Min(4, joren.Soul - 1));
    }
}
