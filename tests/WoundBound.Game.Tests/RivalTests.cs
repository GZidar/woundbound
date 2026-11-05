using WoundBound.Game.Combat;
using WoundBound.Game.Combat.Brains;
using WoundBound.Rules.Enums;

namespace WoundBound.Game.Tests;

/// <summary>
/// A rival — an adversary built from a character sheet — is treated exactly as a character is
/// (the user's ruling, 2026-11-05), save that its Stamina hands nobody an Opportunity and its
/// turns are the GM's.
/// </summary>
public class RivalTests
{
    private static readonly Game.Data.GameData Data = TestData.Load();

    private static (Encounter Encounter, Combatant Fighter, Combatant Rival) Scene(params int[] dice)
    {
        var fighter = CombatantFactory.FromCharacter(SampleEncounters.DumbFighter(Data), new GridPosition(4, 5));
        var rival = CombatantFactory.FromCharacter(SampleEncounters.SallaRinehart(Data), new GridPosition(6, 5), Side.Adversaries);
        // A bandit across the field keeps the fight going once the rival is down.
        var bandit = CombatantFactory.FromAdversary(TestBandits.Roadside, "Bandit", new GridPosition(16, 5));
        var encounter = new Encounter(SampleEncounters.FieldWidth, SampleEncounters.FieldHeight, [fighter, rival, bandit], Data.RangeBands,
            new ScriptedDice(dice), new SimpleGmBrain());
        encounter.DeclineFreeChoices();
        return (encounter, fighter, rival);
    }

    [Fact]
    public void ARivalOnNoBody_LiesDying_InItsSquare_Defenceless()
    {
        var (_, _, rival) = Scene();

        rival.Body = 0;

        Assert.True(rival.DiesLikeACharacter);
        Assert.Equal(BodyStatus.Dying, rival.BodyStatus);
        Assert.True(rival.IsOutOfFight);
        Assert.True(rival.IsDefenceless);
        Assert.True(rival.OccupiesSquare);
    }

    [Fact]
    public void AStatblockFoe_OnNoBody_IsSimplyOut()
    {
        var bandit = CombatantFactory.FromAdversary(TestBandits.Roadside, "Bandit", new GridPosition(1, 1));

        bandit.Body = 0;

        Assert.False(bandit.DiesLikeACharacter);
        Assert.False(bandit.IsDefenceless);
        Assert.False(bandit.OccupiesSquare);
    }

    [Fact]
    public void ADyingRival_RollsToHoldOn_AtTheStartOfEachRound()
    {
        // 2d12 + Presence against 10: a 1 and a 1 fails, and the rival slips a wound further.
        var (encounter, fighter, rival) = Scene(Enumerable.Repeat(1, 40).ToArray());
        rival.Body = 0;

        encounter.TryBeginTurn(fighter);
        encounter.EndTurn();
        encounter.RunAdversaryTurn();

        Assert.Contains(encounter.Log, l => l.StartsWith("Salla Rinehart, dying, rolls to hold on"));
        Assert.Equal(-1, rival.Body);
    }

    /// <summary>A GM that has its rival Interrupt whenever offered, and otherwise plays simply.</summary>
    private sealed class InterruptingGm : SimpleGmBrain, IGmBrain
    {
        public bool Interrupts(BattleView view, PendingReaction reaction) => true;
    }

    [Fact]
    public void ACharactersDeclaredAttack_MayBeInterruptedByAMindRival_WhoseTurnItSpends()
    {
        var fighter = CombatantFactory.FromCharacter(SampleEncounters.DumbFighter(Data), new GridPosition(4, 5));
        var rival = CombatantFactory.FromCharacter(SampleEncounters.DrovanThale(Data), new GridPosition(5, 5), Side.Adversaries);
        var encounter = new Encounter(SampleEncounters.FieldWidth, SampleEncounters.FieldHeight, [fighter, rival], Data.RangeBands,
            new ScriptedDice(Enumerable.Repeat(6, 60).ToArray()), new InterruptingGm());
        encounter.DeclineFreeChoices();
        var mind = rival.Mind;

        encounter.TryBeginTurn(fighter);
        encounter.TryAttack(rival);

        Assert.Contains(encounter.Log, l => l.StartsWith("The Dumb Fighter declares:"));
        Assert.Contains(encounter.Log, l => l.StartsWith("Drovan Thale Interrupts The Dumb Fighter"));
        Assert.Equal(mind - 1, rival.Mind);
        Assert.Equal(1, rival.TurnsTakenThisRound);
        Assert.Equal(0, encounter.AdversaryTurnsLeft);
    }

    [Fact]
    public void ACharactersAttack_GoesUndeclared_WithNoRivalWhoCouldInterrupt()
    {
        var (encounter, fighter, rival) = Scene(Enumerable.Repeat(6, 20).ToArray());

        encounter.TryBeginTurn(fighter);
        encounter.TryAttack(rival);

        Assert.DoesNotContain(encounter.Log, l => l.Contains("declares"));
    }

    /// <summary>The GM, made to take the steps given, in order, then end.</summary>
    private sealed class StepsGm(params GmStep[] steps) : SimpleGmBrain
    {
        private int next;

        public override GmStep NextStep(BattleView view, GmTurn turn) => next < steps.Length ? steps[next++] : GmStep.End;

        public override bool PanicDodges(BattleView view, PendingReaction reaction) => false;
    }

    [Fact]
    public void ARival_MaySacrificeABody_ForAdvantage_AsACharacterMay()
    {
        var fighter = CombatantFactory.FromCharacter(SampleEncounters.DumbFighter(Data), new GridPosition(4, 5));
        var rival = CombatantFactory.FromCharacter(SampleEncounters.SallaRinehart(Data), new GridPosition(5, 5), Side.Adversaries);
        var encounter = new Encounter(SampleEncounters.FieldWidth, SampleEncounters.FieldHeight, [fighter, rival], Data.RangeBands,
            new ScriptedDice(Enumerable.Repeat(6, 40).ToArray()), new StepsGm(new GmStep.Attack(fighter, Sacrifice: true)));
        encounter.DeclineFreeChoices();
        var body = rival.Body;

        encounter.TryBeginTurn(fighter);
        encounter.EndTurn();
        encounter.RunAdversaryTurn();

        Assert.Equal(body - 1, rival.Body);
        Assert.Contains(encounter.Log, l => l.Contains("Sacrifice", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ARival_MaySwapLoadouts_ForAMove()
    {
        var fighter = CombatantFactory.FromCharacter(SampleEncounters.DumbFighter(Data), new GridPosition(4, 5));
        var rival = CombatantFactory.FromCharacter(SampleEncounters.DrovanThale(Data), new GridPosition(9, 5), Side.Adversaries);
        var encounter = new Encounter(SampleEncounters.FieldWidth, SampleEncounters.FieldHeight, [fighter, rival], Data.RangeBands,
            new ScriptedDice(Enumerable.Repeat(6, 40).ToArray()), new StepsGm(new GmStep.SwapLoadout()));
        encounter.DeclineFreeChoices();

        encounter.TryBeginTurn(fighter);
        encounter.EndTurn();
        encounter.RunAdversaryTurn();

        Assert.Contains(encounter.Log, l => l.StartsWith("Drovan Thale swaps loadouts"));
    }
}
