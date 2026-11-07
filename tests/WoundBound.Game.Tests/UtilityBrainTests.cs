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
            Assert.True(encounter.View.DistanceToNearestCharacter(move.Square) > 1, $"it shouldn't step into reach: {move.Square}, fighter {fighter.Position}, ridge {ridge.Position}");
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
        // The lantern-bearer stands between Mirella and Elthric. Whichever it would have gone for,
        // it goes for the other once that one gives themselves away by casting — with the pool still
        // in them to cast again.
        var encounter = SampleEncounters.SignpostWithCasters(Data, new ScriptedDice(), new UtilityGmBrain());
        var bearer = Named(encounter, "Lantern-bearer");
        var mirella = Named(encounter, "Mirella Vask");
        var elthric = Named(encounter, "Elthric Dane");
        foreach (var c in encounter.Combatants.Where(c => c.Side == Side.Party && c != mirella && c != elthric))
        {
            c.Position = new GridPosition(c.Position.X, c.Position.Y + 20);
        }

        bearer.Position = new GridPosition(10, 10);
        mirella.Position = new GridPosition(9, 10);
        elthric.Position = new GridPosition(11, 10);
        bearer.ActionsRemaining = 1;
        var turn = new GmTurn(bearer, isExtraTurn: false, bonusAvailable: false);

        // A GM that knows only what it has seen: the casters are unknown until they cast.
        var seen = new UtilityGmBrain(knowledge: Knowledge.Seen);
        var before = TargetOf(seen.NextStep(encounter.View, turn));
        var other = before == mirella ? elthric : mirella;
        other.HasCastSpell = true;
        var after = TargetOf(seen.NextStep(encounter.View, turn));

        Assert.Same(other, after);
    }

    [Fact]
    public void ACasterWithNothingLeftToCastWith_OrAlreadyDroppedOnce_IsNoLongerTheFirstToPutDown()
    {
        var encounter = SampleEncounters.SignpostWithCasters(Data, new ScriptedDice(), new UtilityGmBrain());
        var mirella = Named(encounter, "Mirella Vask");
        mirella.HasCastSpell = true;
        var assess = new Assessor(encounter.View);
        var fresh = assess.Value(mirella);

        mirella.HasBeenDropped = true;
        var dropped = new Assessor(encounter.View).Value(mirella);

        mirella.HasBeenDropped = false;
        (mirella.Soul, mirella.Body) = (0, 1);
        var spent = new Assessor(encounter.View).Value(mirella);

        // Brought back and not casting again, or with nothing left to cast with: neither is the
        // caster that made them the first to put down.
        Assert.True(dropped < fresh, $"dropped {dropped:0.00}, fresh {fresh:0.00}");
        Assert.True(spent < fresh, $"spent {spent:0.00}, fresh {fresh:0.00}");
        Assert.Equal(0, Assessor.CastsLeft(mirella, mirella.Spells.Single(s => s.Name == "Vital Transfer")));
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

    [Fact]
    public void ThePartyBrain_MeditatesAStunnedFriendBackIntoTheFight()
    {
        var encounter = SampleEncounters.SignpostAmbush(Data, new ScriptedDice());
        foreach (var bandit in encounter.Combatants.Where(c => c.Side == Side.Adversaries))
        {
            bandit.Position = new GridPosition(bandit.Position.X + 14, bandit.Position.Y);
        }

        var salla = Named(encounter, "Salla Rinehart");
        var fighter = Named(encounter, "The Dumb Fighter");
        fighter.Mind = 0;
        fighter.TendEligible[WoundType.Mind] = true;
        encounter.TryBeginTurn(salla);

        var brain = new UtilityPartyBrain();
        var step = brain.NextStep(encounter.View, salla);
        if (step is PartyStep.Move move)
        {
            Assert.True(move.Square.DistanceTo(fighter.Position) <= 1);
            Assert.True(encounter.TryMove(move.Square));
            step = brain.NextStep(encounter.View, salla);
        }

        Assert.Equal(new PartyStep.Tend(CombatAction.Meditate, fighter), step);
    }

    [Fact]
    public void ACharacter_GrowsWarier_AsTheirResourcesAndThePartysRunDown_AndAnAdversaryNever()
    {
        var encounter = SampleEncounters.SignpostAmbush(Data, new ScriptedDice(), new UtilityGmBrain());
        var party = encounter.Combatants.Where(c => c.Side == Side.Party).ToList();
        var (me, friend) = (party[0], party[1]);
        var bandit = encounter.Combatants.First(c => c.Side == Side.Adversaries);
        double Caution(Combatant c) => new Assessor(encounter.View).Caution(c);

        var fresh = Caution(me);
        Assert.Equal(Assessor.FreshCaution, fresh, 3);

        me.Body = 2;
        me.Stamina = 1;
        var spent = Caution(me);
        Assert.True(spent > fresh, $"spent {spent} v fresh {fresh}");

        // A friend down weighs on everybody, not only on them.
        friend.Body = 0;
        Assert.True(Caution(me) > spent);

        bandit.Body = 1;
        Assert.Equal(1, Caution(bandit));
    }

    [Fact]
    public void TheGm_SpendsNoTurnTendingAScratch()
    {
        // A point of Body back on a bandit still standing changes nothing: Heal isn't even weighed.
        var encounter = SampleEncounters.SignpostAmbush(Data, new ScriptedDice(), new UtilityGmBrain());
        var bandits = encounter.Combatants.Where(c => c.Side == Side.Adversaries).ToList();
        foreach (var bandit in bandits)
        {
            bandit.Position = new GridPosition(bandit.Position.X + 14, bandit.Position.Y);
        }

        var (healer, hurt) = (bandits[0], bandits[1]);
        hurt.Position = new GridPosition(healer.Position.X + 1, healer.Position.Y);
        hurt.Body = (hurt.BodyMax + 1) / 2;
        hurt.TendEligible[WoundType.Body] = true;
        healer.HasMove = true;
        healer.ActionsRemaining = 1;

        var planner = new TurnPlanner(new Assessor(encounter.View, Knowledge.Sheets, Side.Adversaries), BrainSkill.Best);
        var options = planner.Options(healer, hasMove: true, actions: 1, real: true);

        Assert.DoesNotContain(options, o => o.Kind == OptionKind.Tend || o.Then == OptionKind.Tend);
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
            dice => SampleEncounters.SignpostAmbush(Data, dice, new UtilityGmBrain(skill, foresight: Foresight.None)),
            dice => SampleEncounters.SignpostAmbush(Data, dice, new UtilityGmBrain(skill, foresight: Foresight.None), fourPlayers: true),
            dice => SampleEncounters.PartyVersusAmbush(Data, dice, new UtilityGmBrain(skill, foresight: Foresight.None)),
            dice => SampleEncounters.FighterVersusRatkin(Data, dice, new UtilityGmBrain(skill, foresight: Foresight.None)),
        ];

        foreach (var stage in fights)
        {
            var report = Simulator.Run(stage, () => new UtilityPartyBrain(skill), fights: 6);
            Assert.Equal(6, report.Fights.Count);
        }
    }

    [Fact]
    public void TheChallenge_SetsHowFarTheGmLooksAhead_NotHowWellItPlays()
    {
        Assert.Equal(Foresight.Default, new UtilityGmBrain(BrainSkill.Best).Foresight);
        Assert.Equal((2, 6), (new UtilityGmBrain(BrainSkill.Balanced).Foresight!.Candidates, new UtilityGmBrain(BrainSkill.Balanced).Foresight!.Games));
        Assert.Equal(Foresight.None, new UtilityGmBrain(BrainSkill.Poor).Foresight);
    }

    [Fact]
    public void TheChallengeSetsHowTheGmMeasuresTheParty()
    {
        Assert.Equal(ThreatMeasure.Default, new UtilityGmBrain(BrainSkill.Best).Measure);
        Assert.Equal(6, new UtilityGmBrain(BrainSkill.Balanced).Measure.Games);
        Assert.False(new UtilityGmBrain(BrainSkill.Poor).Measure.Measures);
    }

    [Fact]
    public void AGmThatMeasures_LearnsWhatEachCharacterActuallyDoes_AndKeepsPlaying()
    {
        // Each round it plays the fight forward and credits every character with the harm of their
        // own turns there (the user's direction, 2026-11-07); the fight itself goes on as ever.
        var gms = new List<UtilityGmBrain>();
        var report = Simulator.Run(dice =>
        {
            var gm = new UtilityGmBrain(BrainSkill.Best, new Random(gms.Count), foresight: Foresight.None, measure: new ThreatMeasure(4, 1));
            gms.Add(gm);
            return SampleEncounters.PartyVersusAmbush(Data, dice, gm);
        }, () => new UtilityPartyBrain(BrainSkill.Best), fights: 2);

        Assert.Equal(2, report.Fights.Count);
        Assert.All(gms, gm =>
        {
            Assert.NotEmpty(gm.MeasuredHarm);
            Assert.All(gm.MeasuredHarm, m => Assert.Equal(Side.Party, m.Key.Side));
            Assert.Contains(gm.MeasuredHarm.Values, harm => harm > 0);
        });
    }

    [Fact]
    public void AGmThatMeasuresItsOwn_CreditsItsCreaturesWithTheHarmTheyDoToTheParty()
    {
        var gms = new List<UtilityGmBrain>();
        Simulator.Run(dice =>
        {
            var gm = new UtilityGmBrain(BrainSkill.Best, new Random(gms.Count), foresight: Foresight.None, measure: new ThreatMeasure(4, 1, Own: true));
            gms.Add(gm);
            return SampleEncounters.PartyVersusAmbush(Data, dice, gm);
        }, () => new UtilityPartyBrain(BrainSkill.Best), fights: 3);

        // What it keeps is the last round's measure, when its last creatures may do nothing at all.
        Assert.All(gms, gm =>
        {
            Assert.Contains(gm.MeasuredHarm, m => m.Key.Side == Side.Party);
            Assert.Contains(gm.MeasuredHarm, m => m.Key.Side == Side.Adversaries);
        });
        Assert.Contains(gms.SelectMany(gm => gm.MeasuredHarm), m => m.Key.Side == Side.Adversaries && m.Value > 0);
    }

    [Fact]
    public void AGmThatLooksAhead_WeighsItsPlansByPlayingThemForward_WithinTheRules()
    {
        var gms = new List<UtilityGmBrain>();
        var report = Simulator.Run(dice =>
        {
            var gm = new UtilityGmBrain(BrainSkill.Best, new Random(gms.Count), foresight: Foresight.Default with { Candidates = 3, Games = 4 });
            gms.Add(gm);
            return SampleEncounters.PartyVersusAmbush(Data, dice, gm);
        }, () => new UtilityPartyBrain(BrainSkill.Best), fights: 2);

        Assert.Equal(2, report.Fights.Count);
        var (weighed, _, failed) = gms.Aggregate((0, 0, 0), (t, g) => (t.Item1 + g.ForesightTally.Weighed, 0, t.Item3 + g.ForesightTally.Failed));
        Assert.True(weighed > 0);
        Assert.True(failed < weighed, $"{failed} games failed over {weighed} decisions");
    }
}
