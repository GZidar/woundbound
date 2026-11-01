using WoundBound.Game.Combat;
using WoundBound.Game.Combat.Brains;
using WoundBound.Game.Combat.Brains.Utility;
using WoundBound.Game.Simulation;
using WoundBound.Rules.Enums;

namespace WoundBound.Game.Tests;

/// <summary>Brains that weigh every option and take the most effective, as their skill allows.</summary>
public class UtilityBrainTests
{
    private static readonly Game.Data.GameData Data = TestData.Load();

    private static Combatant Named(Encounter encounter, string name) => encounter.Combatants.Single(c => c.Name == name);

    [Fact]
    public void TheOdds_AreTheRulesOwn_AbsolutesIncluded()
    {
        // Nothing reaches a Threshold of 30 but a double twelve; nothing misses one of 0 but a double one.
        Assert.Equal(1 / 144.0, Odds.Of(0, 30).Success, 9);
        Assert.Equal(143 / 144.0, Odds.Of(30, 0).Success, 9);
        Assert.Equal(11 / 144.0, Odds.Of(30, 0).CriticalSuccess, 9);
        Assert.True(Odds.Of(4, 15, advantage: true).Success > Odds.Of(4, 15).Success);
        Assert.True(Odds.Of(4, 15, disadvantage: true).Success < Odds.Of(4, 15).Success);
    }

    [Fact]
    public void AtBest_TheTopScoreIsAlwaysTaken()
    {
        var options = new[] { 1.0, 3.0, 2.0 };

        for (var i = 0; i < 20; i++)
        {
            Assert.Equal(3.0, Choose.Pick(options, o => o, BrainSkill.Best, new Random(i)));
        }
    }

    [Fact]
    public void FurtherDownTheDial_TheBestIsTakenLessOften()
    {
        var options = new[] { 1.0, 3.0, 2.0 };
        int Bests(BrainSkill skill) => Enumerable.Range(0, 400).Count(i => Choose.Pick(options, o => o, skill, new Random(i)) == 3.0);

        Assert.True(Bests(BrainSkill.Balanced) > Bests(BrainSkill.Poor));
        Assert.True(Bests(BrainSkill.Balanced) < 400);
    }

    [Fact]
    public void ASkirmisherWithABow_ShootsFromWhereItIs_RatherThanWalkingIn()
    {
        // The ridge ambusher has the fighter at Short: a crossbow shot, no closing to knife range.
        var encounter = SampleEncounters.SignpostAmbush(Data, new ScriptedDice(), new UtilityGmBrain());
        var fighter = Named(encounter, "The Dumb Fighter");
        var ridge = Named(encounter, "Ambusher on the ridge");
        ridge.Position = new GridPosition(fighter.Position.X + 4, fighter.Position.Y - 3);
        var turn = new GmTurn(ridge, isExtraTurn: false, bonusAvailable: false);
        ridge.HasMove = true;
        ridge.ActionsRemaining = 1;

        var step = new UtilityGmBrain().NextStep(encounter.View, turn);

        if (step is GmStep.Move move)
        {
            Assert.True(encounter.View.DistanceToNearestCharacter(move.Square) > 1, "it shouldn't step into reach");
        }
        else
        {
            var attack = Assert.IsType<GmStep.Attack>(step);
            Assert.True((attack.With ?? ridge.Attack).IsRanged);
        }
    }

    [Fact]
    public void ABrute_ClosesAndStrikes()
    {
        // The Tollroad Bandit, a move from the fighter: it steps in to swing.
        var encounter = SampleEncounters.PartyVersusAmbush(Data, new ScriptedDice(), new UtilityGmBrain());
        var fighter = Named(encounter, "The Dumb Fighter");
        var bandit = Named(encounter, "Tollroad Bandit");
        bandit.Position = new GridPosition(fighter.Position.X + 4, fighter.Position.Y);
        bandit.HasMove = true;
        bandit.ActionsRemaining = 1;

        var step = new UtilityGmBrain().NextStep(encounter.View, new GmTurn(bandit, isExtraTurn: false, bonusAvailable: false));

        var move = Assert.IsType<GmStep.Move>(step);
        Assert.True(encounter.View.DistanceToNearestCharacter(move.Square) <= 1);
    }

    [Fact]
    public void OnceSomebodyCasts_TheyAreTheOneToPutDown()
    {
        // The lantern-bearer stands beside all three characters. Whichever it would have gone for,
        // it goes for another once that one has given themselves away by casting.
        var encounter = SampleEncounters.SignpostAmbush(Data, new ScriptedDice(), new UtilityGmBrain());
        var bearer = Named(encounter, "Lantern-bearer");
        bearer.Position = new GridPosition(2, 4);
        bearer.ActionsRemaining = 1;
        var turn = new GmTurn(bearer, isExtraTurn: false, bonusAvailable: false);

        var before = TargetOf(new UtilityGmBrain().NextStep(encounter.View, turn));
        var other = encounter.Combatants.First(c => c.Side == Side.Party && c != before && c.Position.DistanceTo(bearer.Position) <= 1);
        other.HasCastSpell = true;

        var after = TargetOf(new UtilityGmBrain().NextStep(encounter.View, turn));

        Assert.Same(other, after);
    }

    /// <summary>Who a step is aimed at — a blow, or hands put on them.</summary>
    private static Combatant TargetOf(GmStep step) => step switch
    {
        GmStep.Attack attack => attack.Target,
        GmStep.Control control => control.Target,
        _ => throw new Xunit.Sdk.XunitException($"Expected a blow or a Grab/Shove/Trip, got {step}."),
    };

    [Fact]
    public void ThePartyBrain_HealsADyingFriend()
    {
        var encounter = SampleEncounters.SignpostAmbush(Data, new ScriptedDice());
        foreach (var bandit in encounter.Combatants.Where(c => c.Side == Side.Adversaries))
        {
            bandit.Position = new GridPosition(bandit.Position.X + 14, bandit.Position.Y);
        }

        var salla = Named(encounter, "Salla Rinehart");
        var fighter = Named(encounter, "The Dumb Fighter");
        fighter.Body = 0;
        encounter.TryBeginTurn(salla);

        // She may step up first — the fighter is still beside her — but the action is the Heal.
        var brain = new UtilityPartyBrain();
        var step = brain.NextStep(encounter.View, salla);
        if (step is PartyStep.Move move)
        {
            Assert.True(move.Square.DistanceTo(fighter.Position) <= 1);
            Assert.True(encounter.TryMove(move.Square));
            step = brain.NextStep(encounter.View, salla);
        }

        Assert.Equal(new PartyStep.Tend(CombatAction.Heal, fighter), step);
    }

    [Theory]
    [InlineData("Best")]
    [InlineData("Balanced")]
    [InlineData("Poor")]
    public void AtEverySkill_BothSeatsPlayEveryFight_WithinTheRules(string level)
    {
        var skill = BrainSkill.Levels.Single(l => l.Name == level);
        Func<IDiceRoller, Encounter>[] fights =
        [
            dice => SampleEncounters.SignpostAmbush(Data, dice, new UtilityGmBrain(skill)),
            dice => SampleEncounters.SignpostAmbush(Data, dice, new UtilityGmBrain(skill), fourPlayers: true),
            dice => SampleEncounters.PartyVersusAmbush(Data, dice, new UtilityGmBrain(skill)),
            dice => SampleEncounters.FighterVersusRatkin(Data, dice, new UtilityGmBrain(skill)),
        ];

        foreach (var stage in fights)
        {
            var report = Simulator.Run(stage, () => new UtilityPartyBrain(skill), fights: 6);
            Assert.Equal(6, report.Fights.Count);
        }
    }

    [Fact]
    public void TheBestGm_IsHarderToBeatThanThePoorOne()
    {
        // Same seeds, same middling party: the GM's skill should show in the results.
        double PartyBodyLeft(BrainSkill gm) => Simulator.Run(
            dice => SampleEncounters.PartyVersusAmbush(Data, dice, new UtilityGmBrain(gm)),
            () => new TacticalPartyBrain(PartyTactics.Presets[0]), fights: 40).Fights.Average(f => f.PartyBodyLeft);

        Assert.True(PartyBodyLeft(BrainSkill.Best) < PartyBodyLeft(BrainSkill.Poor));
    }
}
