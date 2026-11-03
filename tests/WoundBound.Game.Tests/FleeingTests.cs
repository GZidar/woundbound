using WoundBound.Game.Combat;
using WoundBound.Game.Combat.Brains;
using WoundBound.Game.Simulation;

namespace WoundBound.Game.Tests;

/// <summary>
/// Foes that break and run (GM Reference §8), the reactions that stop them (Core Rules §9,
/// "Against a creature on the move"), the end of a fight "when no adversary remains willing and
/// able to fight" (§9), and objectives besides dropping everybody.
/// </summary>
public class FleeingTests
{
    private static readonly Game.Data.GameData Data = TestData.Load();

    private static Encounter Signpost(params int[] dice) => SampleEncounters.SignpostAmbush(Data, new ScriptedDice(dice), setting: Setting.Daylight, cowardly: true);

    private static Combatant Named(Encounter encounter, string name) => encounter.Combatants.Single(c => c.Name == name);

    /// <summary>Back by the board's far edge, out of a move's reach of anybody, so he isn't the GM's first choice of turn.</summary>
    private static void KeepTheBearerBack(Encounter encounter) => Named(encounter, "Lantern-bearer").Position = new GridPosition(9, 4);

    private static void FighterTakesATurnAndEnds(Encounter encounter)
    {
        Assert.True(encounter.TryBeginTurn(Named(encounter, "The Dumb Fighter")));
        encounter.EndTurn();
    }

    [Fact]
    public void BreakingPoints_ComeFromTheStatblock()
    {
        Assert.Equal(Morale.NotAFanatic, CombatantFactory.FromAdversary(Data.Adversary("Tollroad Bandit"), "Bandit", default).Morale);
        Assert.Equal(Morale.Cowardice, CombatantFactory.FromAdversary(SampleEncounters.CowardlyBandit(SampleEncounters.RoadsideBandit), "Bandit", default).Morale);
        Assert.Null(CombatantFactory.FromAdversary(Data.Adversary("Bramble Ratkin"), "Ratkin", default).Morale);
    }

    [Fact]
    public void Cowardice_BreaksBelowHalfItsBody()
    {
        var encounter = Signpost();
        var ridge = Named(encounter, "Ambusher on the ridge");
        KeepTheBearerBack(encounter);
        ridge.Body = 3; // half of 6 — not below it

        FighterTakesATurnAndEnds(encounter);
        Assert.False(ridge.IsBroken);

        ridge.Body = 2;
        encounter.RunAdversaryTurn();

        Assert.True(ridge.IsBroken);
        Assert.Contains(encounter.Log, line => line.StartsWith("Ambusher on the ridge has had enough (Cowardice)"));
    }

    [Fact]
    public void ABrokenCreature_Runs_UntilNoCharacterHasItWithinLong()
    {
        // Eleven squares from the party, a move and an action (a Middle move, 8 squares) carry it
        // nineteen from the nearest of them: beyond Long, and off the board. No dice are rolled.
        var encounter = Signpost();
        var ridge = Named(encounter, "Ambusher on the ridge");
        KeepTheBearerBack(encounter);
        ridge.Position = new GridPosition(12, 4);
        ridge.Body = 2;
        FighterTakesATurnAndEnds(encounter);

        encounter.RunAdversaryTurn();

        Assert.True(ridge.HasFled);
        Assert.True(ridge.IsOutOfFight);
        Assert.False(encounter.IsWithinLongOfParty(ridge.Position));
        Assert.Contains(encounter.Log, line => line == "Ambusher on the ridge is beyond Long of everyone, and off the board.");
        Assert.False(encounter.IsOver);
    }

    [Fact]
    public void AtTheEdgeOfTheMap_ARunnerKeepsGoing()
    {
        // The map's edges are open: a runner backed up against one carries on past it.
        var encounter = Signpost();
        var ridge = Named(encounter, "Ambusher on the ridge");
        KeepTheBearerBack(encounter);
        Named(encounter, "Lantern-bearer").Position = new GridPosition(14, 10);
        ridge.Position = new GridPosition(SampleEncounters.FieldWidth - 1, 8);
        Named(encounter, "Salla Rinehart").Position = new GridPosition(SampleEncounters.FieldWidth - 6, 8);
        ridge.Body = 2;
        FighterTakesATurnAndEnds(encounter);

        encounter.RunAdversaryTurn();

        Assert.True(ridge.Position.X >= SampleEncounters.FieldWidth);
        Assert.False(encounter.IsOnBoard(ridge.Position));
    }

    [Fact]
    public void AnyFoeBeyondLongOfEveryCharacter_HasLeftTheBoard_BrokenOrNot()
    {
        var encounter = Signpost();
        var ridge = Named(encounter, "Ambusher on the ridge");
        ridge.Position = new GridPosition(18, 4); // 17 from the party's front rank

        FighterTakesATurnAndEnds(encounter);

        Assert.False(ridge.IsBroken);
        Assert.True(ridge.HasFled);
    }

    [Fact]
    public void TheObjectiveFails_TheMomentTheLanternBearerGetsAway()
    {
        var encounter = Signpost();
        var bearer = Named(encounter, "Lantern-bearer");
        var objective = Assert.Single(encounter.Objectives);
        bearer.Position = new GridPosition(12, 4);
        bearer.Body = 2;
        Assert.Equal(ObjectiveStatus.InProgress, objective.StatusIn(encounter));

        FighterTakesATurnAndEnds(encounter);
        encounter.RunAdversaryTurn();

        Assert.True(bearer.HasFled);
        Assert.Equal(ObjectiveStatus.Failed, objective.StatusIn(encounter));
    }

    [Fact]
    public void ARunnerLeavingACharactersReach_CanBeCaught_AndStopsWhereItWasCaught()
    {
        // The ridge ambusher, broken, starts beside the fighter and makes for the corner. The
        // fighter grabs at it on the way: 9+6 +4 = 19 against DT 15 and ST 11 both — beyond a
        // panic dodge's reach. It had spent its move and its action on the dash, and the rest of
        // the move is lost, so there's nothing left to break free with.
        var encounter = Signpost(9, 6);
        var fighter = Named(encounter, "The Dumb Fighter");
        var ridge = Named(encounter, "Ambusher on the ridge");
        KeepTheBearerBack(encounter);
        ridge.Position = new GridPosition(2, 4);
        ridge.Body = 2;
        FighterTakesATurnAndEnds(encounter);

        encounter.RunAdversaryTurn();

        var pending = Assert.IsType<PendingReaction>(encounter.Pending);
        Assert.Equal(ReactionKind.LeaverReaction, pending.Kind);
        Assert.Same(fighter, pending.Defender);
        Assert.Same(ridge, pending.Attacker);
        Assert.Equal(
            new[] { LeaverResponse.LetThemGo, LeaverResponse.Strike, LeaverResponse.Grab, LeaverResponse.Shove, LeaverResponse.Trip },
            pending.Options);

        Assert.True(encounter.RespondToLeaver(LeaverResponse.Grab));

        Assert.Same(fighter, ridge.HeldBy);
        Assert.Equal(new GridPosition(2, 4), ridge.Position);
        Assert.False(ridge.HasFled);
        Assert.Equal(fighter.StaminaMax - 1, fighter.Stamina);
        Assert.Contains(encounter.Log, line => line.Contains("on the move") && line.Contains("caught at (2,4)"));
        Assert.Contains(encounter.Log, line => line.StartsWith("Ambusher on the ridge is stopped before it gets anywhere"));
        Assert.DoesNotContain(encounter.Log, line => line.StartsWith("Ambusher on the ridge strains against"));
    }

    [Fact]
    public void ACharacterCanLetARunnerGo()
    {
        var encounter = Signpost();
        var ridge = Named(encounter, "Ambusher on the ridge");
        KeepTheBearerBack(encounter);
        ridge.Position = new GridPosition(2, 4);
        ridge.Body = 2;
        FighterTakesATurnAndEnds(encounter);
        encounter.RunAdversaryTurn();

        // Each of the three it passes is asked in turn; nobody stops it.
        while (encounter.Pending is { Kind: ReactionKind.LeaverReaction })
        {
            Assert.True(encounter.RespondToLeaver(LeaverResponse.LetThemGo));
        }

        Assert.NotEqual(new GridPosition(2, 4), ridge.Position);
        Assert.Contains(encounter.Log, line => line.Contains("lets them go"));
    }

    [Fact]
    public void BrokenFoesStillOnTheBoard_KeepTheFightGoing()
    {
        // The lantern-bearer goes down; both ambushers break at the sight of it. Nobody is willing
        // to fight on, but they're still on the board, so the fight stays in effect.
        var encounter = Signpost();
        Named(encounter, "Lantern-bearer").Body = 0;

        FighterTakesATurnAndEnds(encounter);

        Assert.True(Named(encounter, "Ambusher on the ridge").IsBroken);
        Assert.True(Named(encounter, "Ambusher in the trees").IsBroken);
        Assert.False(encounter.IsOver);
    }

    [Fact]
    public void TheLastFoesLeft_BrokenAndHeldFast_GiveUp()
    {
        // The fighter holds the trees ambusher (6+5 +4 = 15 v ST 11); the other two go down. The
        // one left is broken and can't get away, so it gives up, and the lantern-bearer is taken.
        var encounter = Signpost(6, 5);
        var fighter = Named(encounter, "The Dumb Fighter");
        var trees = Named(encounter, "Ambusher in the trees");
        trees.Position = new GridPosition(2, 4);

        encounter.TryBeginTurn(fighter);
        Assert.True(encounter.TryControl(ControlAction.Grab, trees));
        Named(encounter, "Lantern-bearer").Body = 0;
        Named(encounter, "Ambusher on the ridge").Body = 0;
        encounter.EndTurn();

        Assert.Equal(EncounterPhase.Victory, encounter.Phase);
        Assert.True(trees.HasSurrendered);
        Assert.Equal(ObjectiveStatus.Achieved, encounter.Objectives[0].StatusIn(encounter));
        Assert.Contains(encounter.Log, line => line.StartsWith("Ambusher in the trees, broken and held fast by The Dumb Fighter, gives up."));
        Assert.Contains(encounter.Log, line => line == "Objective — Take the lantern-bearer — he can be made to talk: achieved.");
    }

    [Fact]
    public void BotsPlayTheSignpost_ReactingToRunners_WithinTheRules()
    {
        var reactions = 0;
        foreach (var tactics in PartyTactics.Presets)
        {
            var report = Simulator.Run(dice => SampleEncounters.SignpostAmbush(Data, dice, cowardly: true), () => new TacticalPartyBrain(tactics), fights: 15);
            Assert.Equal(15, report.Fights.Count);
            Assert.NotNull(report.ObjectiveRate);
            reactions += report.Fights.Sum(f => f.Tally.LeaverReactions);
        }

        Assert.True(reactions > 0);
    }
}
