using WoundBound.Game.Combat;
using WoundBound.Game.Combat.Brains;
using WoundBound.Game.Combat.Brains.Utility;
using WoundBound.Game.Simulation;

namespace WoundBound.Game.Tests;

/// <summary>
/// The GM's intent: how hard it plays for the kill, and the director that steers it (the user's
/// ruling, 2026-11-08). Downed characters are almost never finished. The threat of it is the point.
/// </summary>
public class RuthlessnessTests
{
    private static readonly Game.Data.GameData Data = TestData.Load();

    private static Combatant Named(Encounter encounter, string name) => encounter.Combatants.Single(c => c.Name == name);

    /// <summary>The fighter lying at <paramref name="body"/> with one bandit beside him, everybody else far off.</summary>
    private static (Encounter Encounter, Combatant Fighter, GmTurn Turn) DownedFighter(int body)
    {
        var encounter = SampleEncounters.SignpostAmbush(Data, new ScriptedDice());
        var fighter = Named(encounter, "The Dumb Fighter");
        var bandit = encounter.Combatants.First(c => c.Side == Side.Adversaries && !c.HasRangedAttack);
        foreach (var other in encounter.Combatants.Where(c => c != fighter && c != bandit))
        {
            other.Position = new GridPosition(other.Position.X + (other.Side == Side.Party ? -14 : 14), other.Position.Y);
        }

        bandit.Position = new GridPosition(fighter.Position.X + 1, fighter.Position.Y);
        fighter.Body = body;
        bandit.HasMove = true;
        bandit.ActionsRemaining = 1;
        Assert.True(fighter.IsDefenceless);
        return (encounter, fighter, new GmTurn(bandit, isExtraTurn: false, bonusAvailable: false));
    }

    /// <summary>Whether the bandit's options, as a GM of this intent weighs them, include a blow at the fighter.</summary>
    private static bool WouldStrike(int body, double ruthlessness, bool finishes = false, bool alreadyMenaced = false)
    {
        var (encounter, fighter, turn) = DownedFighter(body);
        var assess = new Assessor(encounter.View, side: Side.Adversaries)
        {
            Ruthlessness = ruthlessness,
            FinishesTheDowned = finishes,
            Menaced = alreadyMenaced ? new HashSet<Combatant> { fighter } : null,
        };
        return new TurnPlanner(assess, BrainSkill.Best).Options(turn.Actor, hasMove: false, actions: 1, real: true)
            .Any(o => o.Kind == OptionKind.Attack && o.Target == fighter);
    }

    [Fact]
    public void AMercifulGm_LeavesTheDyingAlone() => Assert.False(WouldStrike(body: 0, ruthlessness: 0.3));

    [Fact]
    public void ARuthlessGm_MayLayOneBlowOnTheDying_ButOnlyOnce()
    {
        Assert.True(WouldStrike(body: 0, ruthlessness: 1));
        Assert.True(WouldStrike(body: 0, ruthlessness: 0.6));
        Assert.False(WouldStrike(body: 0, ruthlessness: 1, alreadyMenaced: true));
    }

    [Fact]
    public void ARuthlessGm_WontStrikeABlowThatBringsDeathNear() =>
        // At −1, a flat wound leaves him one failed Presence roll from death: too close.
        Assert.False(WouldStrike(body: -1, ruthlessness: 1));

    [Fact]
    public void OnlyAMercilessGm_FinishesTheDying() => Assert.True(WouldStrike(body: -2, ruthlessness: 1, finishes: true));

    [Fact]
    public void TheGmBrain_RemembersTheDownedItHasStruck()
    {
        var (encounter, fighter, turn) = DownedFighter(body: 0);
        foreach (var other in encounter.Combatants.Where(c => c.Side == Side.Party && c != fighter))
        {
            other.Body = 0; // everybody down, so the blow at the fighter is the best thing it can do
        }

        var gm = new UtilityGmBrain(intent: GmIntent.Ruthless, foresight: Foresight.None, measure: ThreatMeasure.None);
        var first = gm.NextStep(encounter.View, turn);
        var struck = Assert.IsType<GmStep.Attack>(first).Target;
        Assert.True(struck.IsDefenceless);

        var second = gm.NextStep(encounter.View, turn);
        Assert.False(second is GmStep.Attack again && again.Target == struck, $"{second}");
    }

    [Fact]
    public void AMercifulGm_HoldsAWoundToAHurtCharacterAsWorthLess_SoItSpreadsItsHarm()
    {
        var encounter = SampleEncounters.SignpostAmbush(Data, new ScriptedDice());
        var fighter = Named(encounter, "The Dumb Fighter");
        fighter.Body = 1;
        var ruthless = new Assessor(encounter.View, side: Side.Adversaries);
        var merciful = new Assessor(encounter.View, side: Side.Adversaries) { Ruthlessness = 0 };

        Assert.True(merciful.PerWound(fighter) < 0.5 * ruthless.PerWound(fighter));
        Assert.Equal(0, merciful.KillWorth(fighter, 1));
    }

    [Fact]
    public void TheDirector_EasesOffWhenThePartyFallsBehind_AndPressesWhenItRunsAway()
    {
        Assert.True(GmIntent.Steered(0.7, Challenge.Average, lead: -0.2) < 0.7);
        Assert.True(GmIntent.Steered(0.7, Challenge.Average, lead: 0.5) > 0.7);
        Assert.True(GmIntent.Steered(0.3, Challenge.Easy, lead: 0.1) < 0.3);
        Assert.Equal(1, GmIntent.Steered(1, Challenge.Push, lead: -0.5));
    }

    [Fact]
    public void TheDirector_ReadsTheLead_AsFoesWornLessPartyWorn()
    {
        var encounter = SampleEncounters.SignpostAmbush(Data, new ScriptedDice());
        Assert.Equal(0, GmIntent.Lead(encounter.View), 6);

        foreach (var foe in encounter.Combatants.Where(c => c.Side == Side.Adversaries))
        {
            foe.Body = 0;
        }

        Assert.True(GmIntent.Lead(encounter.View) > 0.9);
    }

    [Fact]
    public void AMercifulGm_LetsAHurtCreatureBreak_ARuthlessOneDoesNot()
    {
        // GM Reference §8, "Let non-Solo adversaries break": below half its Body, or half its side down.
        var encounter = SampleEncounters.SignpostAmbush(Data, new ScriptedDice());
        var bandit = encounter.Combatants.First(c => c.Side == Side.Adversaries && c.BodyMax >= 2);
        IGmBrain merciful = new UtilityGmBrain(intent: GmIntent.Fixed(0.2));
        IGmBrain ruthless = new UtilityGmBrain(intent: GmIntent.Ruthless);

        var half = (encounter.Combatants.Count(c => c.Side == Side.Adversaries) + 1) / 2;
        Assert.False(merciful.LetsBreak(encounter.View, bandit, alliesDown: 0));
        Assert.False(merciful.LetsBreak(encounter.View, bandit, alliesDown: 1));
        Assert.True(merciful.LetsBreak(encounter.View, bandit, alliesDown: half));
        bandit.Body = bandit.BodyMax / 2 - (bandit.BodyMax % 2 == 0 ? 1 : 0);
        Assert.True(merciful.LetsBreak(encounter.View, bandit, alliesDown: 0));
        Assert.False(ruthless.LetsBreak(encounter.View, bandit, alliesDown: half));
    }
}
