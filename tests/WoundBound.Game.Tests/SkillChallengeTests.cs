using WoundBound.Game.Combat;
using WoundBound.Game.Skills;
using WoundBound.Rules.Calculation;
using WoundBound.Rules.Enums;
using WoundBound.Rules.Models;

namespace WoundBound.Game.Tests;

/// <summary>Skill Challenges (Core Rules §8), played from the books' own statblocks.</summary>
public class SkillChallengeTests
{
    private static readonly Game.Data.GameData Data = TestData.Load();

    /// <summary>A party that just has its best attempter roll: no Probe, no Assist, no Stamina spent.</summary>
    private sealed class PlainBrain : SkillPartyBrain
    {
        public override Combatant? Prober(SkillChallenge challenge, AdversaryTemplate obstacle, SkillHurdle? hurdle, IReadOnlyList<SkillApproach> open, IReadOnlySet<Combatant> acted) => null;

        public override SkillPlan? Plan(SkillChallenge challenge, IReadOnlyList<SkillApproach> approaches, int steps, IReadOnlySet<Combatant> acted) =>
            base.Plan(challenge, approaches, steps, acted) is { } plan ? plan with { Assist = null } : null;

        public override bool Pushes(SkillChallenge challenge, SkillPlan plan, AdversaryTemplate obstacle, int steps, int failures, int allowed) => false;

        public override bool Steadies(SkillChallenge challenge, Combatant who, AdversaryTemplate obstacle, int band, int failures, int allowed) => false;
    }

    private static List<Combatant> Five() =>
    [
        CombatantFactory.FromCharacter(SampleEncounters.SallaRinehart(Data), new GridPosition(0, 0)),
        CombatantFactory.FromCharacter(SampleEncounters.DrovanThale(Data), new GridPosition(1, 0)),
        CombatantFactory.FromCharacter(SampleEncounters.JorenBlacke(Data), new GridPosition(2, 0)),
        CombatantFactory.FromCharacter(SampleEncounters.MirellaVask(Data), new GridPosition(3, 0)),
        CombatantFactory.FromCharacter(SampleEncounters.ElthricDane(Data), new GridPosition(4, 0)),
    ];

    [Fact]
    public void TheBooksStatblocks_ReadAsWritten()
    {
        var wagon = SampleSkillChallenges.TheWagon();
        var river = wagon[0].Obstacle;
        var repair = wagon[1].Obstacle;
        var stair = SampleSkillChallenges.TheStair()[0].Obstacle;
        var snare = SampleSkillChallenges.TheSnare()[0].Obstacle;

        Assert.Equal((SkillAdversaryShape.Moment, SkillAdversaryBehavior.Passive, SkillStakesKind.Story), (river.Shape, river.Behavior, river.StakesKind));
        Assert.Equal((SkillAdversaryShape.Track, 3, 3, SkillStakesKind.Stamina), (repair.Shape, repair.SuccessesRequired, repair.FailuresAllowed, repair.StakesKind));
        Assert.Equal(3, repair.Hurdles.Count);
        Assert.Equal((SkillAdversaryBehavior.Active, ThresholdType.Stability, true), (stair.Behavior, stair.OppositionVsThreshold, stair.RampsOnFailure));
        Assert.Equal(OppositionTiming.EachRound, SkillChallenge.TimingOf(stair));
        Assert.Equal(OppositionTiming.OnFailure, SkillChallenge.TimingOf(snare));
    }

    [Fact]
    public void AFailedCrossing_BreaksTheWagonInTheRiver_WhereAFailedCheckCostsStamina_ByHowFarItMissed()
    {
        // Mirella (Persuasion +5) rolls 1+2: 8 against 18, the crossing lost. In the river, 2+3: 10
        // against 18, eight short — two Stamina. The rest roll double twelves.
        var party = Five();
        var mirella = party.Single(c => c.Name == "Mirella Vask");
        var stamina = mirella.Stamina;
        var challenge = new SkillChallenge(party, SampleSkillChallenges.TheWagon(), new ScriptedDice([1, 2, 2, 3, .. Enumerable.Repeat(12, 40)]), new PlainBrain());

        var result = challenge.Run();

        Assert.Equal(("Cross the River", SkillThresholdOutcome.Failed), result.Visited[0]);
        Assert.Equal("Repair the Wagon in the River", result.Visited[1].Node);
        Assert.Contains(result.Log, l => l.StartsWith("Mirella Vask is worn down: 2 Stamina"));
        Assert.Equal(stamina - 2, mirella.Stamina);
        Assert.Contains(2, result.StoryBands);
    }

    [Fact]
    public void Steady_WipesAFailure_AndBanksTheGmAnOpportunity()
    {
        var party = Five();
        var challenge = new SkillChallenge(party, SampleSkillChallenges.TheWagon(), new ScriptedDice([1, 2, .. Enumerable.Repeat(12, 40)]), new SteadyBrain());

        var result = challenge.Run();

        Assert.Contains(result.Log, l => l.Contains("Steadies"));
        Assert.Contains(result.Log, l => l.Contains("wiped away"));
        Assert.Equal(("Cross the River", SkillThresholdOutcome.ClearedClean), result.Visited[0]);
    }

    /// <summary>The plain party, but it Steadies every failure it can.</summary>
    private sealed class SteadyBrain : SkillPartyBrain
    {
        public override Combatant? Prober(SkillChallenge challenge, AdversaryTemplate obstacle, SkillHurdle? hurdle, IReadOnlyList<SkillApproach> open, IReadOnlySet<Combatant> acted) => null;

        public override SkillPlan? Plan(SkillChallenge challenge, IReadOnlyList<SkillApproach> approaches, int steps, IReadOnlySet<Combatant> acted) =>
            base.Plan(challenge, approaches, steps, acted) is { } plan ? plan with { Assist = null } : null;

        public override bool Pushes(SkillChallenge challenge, SkillPlan plan, AdversaryTemplate obstacle, int steps, int failures, int allowed) => false;

        public override bool Steadies(SkillChallenge challenge, Combatant who, AdversaryTemplate obstacle, int band, int failures, int allowed) =>
            SkillChallenge.CanSpend(who);
    }

    [Fact]
    public void FailingBadly_CostsOneTwoOrThree_ByTheLadderStep()
    {
        Assert.Equal(1, SkillChallengeCalculator.WoundsForFailedCheck(12, 18));
        Assert.Equal(2, SkillChallengeCalculator.WoundsForFailedCheck(11, 18));
        Assert.Equal(3, SkillChallengeCalculator.WoundsForFailedCheck(5, 18));
    }
}
