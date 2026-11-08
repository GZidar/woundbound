using WoundBound.Game.Combat;
using WoundBound.Game.Combat.Brains;
using WoundBound.Game.Combat.Brains.Utility;
using WoundBound.Game.Simulation;

namespace WoundBound.Game.Tests;

/// <summary>
/// A fight's brief: why it is happening and what the GM's side wants from it, which informs the
/// GM's defaults (the user, 2026-11-08). It never tries to steer what the players do.
/// </summary>
public class SceneBriefTests
{
    private static readonly Game.Data.GameData Data = TestData.Load();

    [Theory]
    [InlineData(Stakes.Incidental, Challenge.Easy)]
    [InlineData(Stakes.Relevant, Challenge.Average)]
    [InlineData(Stakes.Climactic, Challenge.Push)]
    public void TheStakes_SuggestTheChallenge(Stakes stakes, Challenge challenge) =>
        Assert.Equal(challenge, new SceneBrief(stakes, new AdversaryGoal.Kill()).DefaultChallenge);

    [Fact]
    public void AGmGivenNoIntent_SteersTowardsTheBriefsChallenge_AndAChosenOneOverridesIt()
    {
        // The clearing is incidental: Easy, unless somebody chose otherwise.
        var unchosen = new UtilityGmBrain(foresight: Foresight.None, measure: ThreatMeasure.None);
        var chosen = new UtilityGmBrain(foresight: Foresight.None, measure: ThreatMeasure.None, intent: GmIntent.Ruthless);
        foreach (var gm in new[] { unchosen, chosen })
        {
            var encounter = SampleEncounters.WoodlandClearing(Data, new RandomDiceRoller(new Random(2)), gm, prepared: false);
            gm.ChooseExtraTurn(encounter.View);
        }

        Assert.Equal(Challenge.Easy, unchosen.Intent.Steer);
        Assert.Equal(GmIntent.Ruthless, chosen.Intent);
    }

    [Fact]
    public void Hirelings_BreakOnceHurt_WhateverTheIntent_TheCommittedAsTheIntentSays()
    {
        var hired = SampleEncounters.Roadblock(Data, new ScriptedDice());
        var bandit = hired.Combatants.First(c => c.Side == Side.Adversaries && c.BodyMax >= 2);
        bandit.Body = 0 + bandit.BodyMax / 2 - (bandit.BodyMax % 2 == 0 ? 1 : 0);
        IGmBrain ruthless = new UtilityGmBrain(intent: GmIntent.Ruthless);

        Assert.Equal(Commitment.Hireling, hired.Brief!.Commitment);
        Assert.True(ruthless.LetsBreak(hired.View, bandit, alliesDown: 0));

        IGmBrain merciful = new UtilityGmBrain(intent: GmIntent.Fixed(0));
        var fanatics = SampleEncounters.CampfireAmbush(Data, new ScriptedDice());
        var wolf = fanatics.Combatants.First(c => c.Side == Side.Adversaries && c.Morale is null && c.BodyMax >= 2);
        wolf.Body = 0;
        Assert.True(merciful.LetsBreak(fanatics.View, wolf, alliesDown: 0)); // committed: the intent decides
    }

    [Fact]
    public void AGmThatWantsThemAlive_NeverStrikesTheDowned()
    {
        var encounter = SampleEncounters.SignpostAmbush(Data, new ScriptedDice());
        var fighter = encounter.Combatants.Single(c => c.Name == "The Dumb Fighter");
        var bandit = encounter.Combatants.First(c => c.Side == Side.Adversaries && !c.HasRangedAttack);
        bandit.Position = new GridPosition(fighter.Position.X + 1, fighter.Position.Y);
        fighter.Body = 0;
        var assess = new Assessor(encounter.View, side: Side.Adversaries) { SparesTheDowned = true, FinishesTheDowned = false };

        var options = new TurnPlanner(assess, BrainSkill.Best).Options(bandit, hasMove: false, actions: 1, real: true);

        Assert.DoesNotContain(options, o => o.Kind == OptionKind.Attack && o.Target == fighter);
    }

    [Fact]
    public void AHeldZone_IsLostTheMomentACharacterStandsInIt_InsideItsRounds()
    {
        var encounter = SampleEncounters.Roadblock(Data, new ScriptedDice());
        var hold = Assert.IsType<AdversaryGoal.Hold>(encounter.Brief!.Goal);
        Assert.Equal(ObjectiveStatus.InProgress, encounter.GoalStatus);

        var salla = encounter.Combatants.Single(c => c.Name == "Salla Rinehart");
        salla.Position = hold.Zone.Squares.First(sq => encounter.Combatants.All(c => c.Position != sq) && !encounter.Setting.IsSolid(sq));
        encounter.LookAround();

        Assert.Equal(1, encounter.HoldBrokenInRound);
        Assert.Equal(ObjectiveStatus.Failed, encounter.GoalStatus);
    }

    [Fact]
    public void AHolder_WontBeDrawnOutOfItsZone()
    {
        var encounter = SampleEncounters.Roadblock(Data, new ScriptedDice());
        var hold = Assert.IsType<AdversaryGoal.Hold>(encounter.Brief!.Goal);
        var bandit = encounter.Combatants.Single(c => c.Name == "Tollroad Bandit");
        var gm = new UtilityGmBrain(foresight: Foresight.None, measure: ThreatMeasure.None);
        bandit.HasMove = true;
        bandit.ActionsRemaining = 1;

        var step = gm.NextStep(encounter.View, new GmTurn(bandit, isExtraTurn: false, bonusAvailable: false));

        if (step is GmStep.Move move)
        {
            Assert.Contains(move.Square, hold.Zone.Squares);
        }
    }
}
