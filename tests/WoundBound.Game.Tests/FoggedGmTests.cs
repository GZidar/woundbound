using WoundBound.Game.Combat;
using WoundBound.Game.Combat.Brains;
using WoundBound.Game.Combat.Brains.Utility;

namespace WoundBound.Game.Tests;

/// <summary>
/// The GM in the fog (the user's direction, 2026-11-04): a GM that doesn't peer through it plays its
/// creatures on what they've seen. That means the characters in sight now, and where the rest were
/// last seen. Peering is a skill, at Best.
/// </summary>
public class FoggedGmTests
{
    private static readonly Game.Data.GameData Data = TestData.Load();

    /// <summary>A wall down the middle of the board with a gap at the bottom; the fighter on one side, the bandit on the other.</summary>
    private static (Encounter Encounter, Combatant Fighter, Combatant Biter, BattleView Fogged) Board()
    {
        var fighter = CombatantFactory.FromCharacter(SampleEncounters.DumbFighter(Data), new GridPosition(1, 4));
        var biter = CombatantFactory.FromAdversary(Data.Adversary("Tollroad Bandit"), "Tollroad Bandit", new GridPosition(8, 4));
        var walled = new Setting { Name = "Wall", Walls = Setting.Squares((5, 0, 5, 6)) };
        var encounter = new Encounter(SampleEncounters.BoardWidth, SampleEncounters.BoardHeight, new[] { fighter, biter },
            Data.RangeBands, new ScriptedDice(), setting: walled, environment: Data.Environment);
        encounter.TrackSightings(Side.Adversaries);
        return (encounter, fighter, biter, new BattleView(encounter, Side.Adversaries));
    }

    [Fact]
    public void AFoggedView_LeavesOutTheCharactersOutOfSight()
    {
        var (encounter, fighter, biter, fogged) = Board();

        Assert.Empty(fogged.Characters);
        Assert.Empty(fogged.EnemiesOf(biter));
        Assert.DoesNotContain(fighter, fogged.Combatants);
        Assert.Contains(biter, fogged.Combatants);
        Assert.Contains(fighter, encounter.View.Characters); // the unfogged view still has everybody

        // Round the end of the wall, and the Biter sees the fighter.
        fighter.Position = new GridPosition(5, 7);
        encounter.LookAround();
        Assert.Contains(fighter, fogged.EnemiesOf(biter));
    }

    [Fact]
    public void AFoeThatWalksOutOfSight_IsRememberedWhereItVanished()
    {
        var (encounter, fighter, _, fogged) = Board();
        fighter.Position = new GridPosition(6, 7);
        encounter.LookAround();

        fighter.Position = new GridPosition(1, 4);
        encounter.LookAround();

        Assert.False(fogged.Sees(fighter));
        var (foe, at) = Assert.Single(fogged.Remembered);
        Assert.Equal(fighter, foe);
        Assert.NotEqual(new GridPosition(1, 4), at);
        Assert.True(at.X >= 4, $"it vanished at the wall's end, not at {at}");

        // It stays remembered while nobody goes to look.
        encounter.LookAround();
        Assert.Single(fogged.Remembered);
    }

    [Fact]
    public void ARememberedSquare_FoundEmpty_IsForgotten()
    {
        var (encounter, fighter, biter, fogged) = Board();
        fighter.Position = new GridPosition(6, 7);
        encounter.LookAround();
        fighter.Position = new GridPosition(1, 4);
        encounter.LookAround();
        var at = Assert.Single(fogged.Remembered).At;

        biter.Position = at;
        encounter.LookAround();

        Assert.True(fogged.Sees(fighter) || !fogged.Remembered.Any());
    }

    [Fact]
    public void AGmPeeringThroughTheFog_WalksRoundTheWall_OneInItDoesNot()
    {
        var (encounter, fighter, biter, fogged) = Board();
        biter.HasMove = true;
        biter.ActionsRemaining = 1;
        var turn = new GmTurn(biter, isExtraTurn: false, bonusAvailable: false);
        bool Closes(GmStep step) => step is GmStep.Move move && fighter.DistanceTo(move.Square) < fighter.DistanceTo(biter);

        Assert.True(Closes(new UtilityGmBrain(peers: true).NextStep(encounter.View, turn)));
        Assert.False(Closes(new UtilityGmBrain(peers: false).NextStep(fogged, turn)));
    }

    [Fact]
    public void AFoggedGm_HuntsWhereItLastSawSomebody()
    {
        var (encounter, fighter, biter, fogged) = Board();
        fighter.Position = new GridPosition(6, 7);
        encounter.LookAround();
        fighter.Position = new GridPosition(1, 4);
        encounter.LookAround();
        var last = Assert.Single(fogged.Remembered).At;
        biter.HasMove = true;
        biter.ActionsRemaining = 1;

        var step = new UtilityGmBrain(peers: false).NextStep(fogged, new GmTurn(biter, isExtraTurn: false, bonusAvailable: false));

        var move = Assert.IsType<GmStep.Move>(step);
        Assert.True(move.Square.DistanceTo(last) < biter.Position.DistanceTo(last));
    }

    [Fact]
    public void PeeringGoesWithBest_UnlessSaidOtherwise()
    {
        Assert.True(new UtilityGmBrain(BrainSkill.Best).PeersThroughFog);
        Assert.False(new UtilityGmBrain(BrainSkill.Balanced).PeersThroughFog);
        Assert.True(new UtilityGmBrain(BrainSkill.Poor, peers: true).PeersThroughFog);
        Assert.True(((IGmBrain)new SimpleGmBrain()).PeersThroughFog);
    }

    [Fact]
    public void FoggedFights_PlayOutWithoutAnIllegalStep()
    {
        // Every decision a fogged GM makes is still checked by the rules; none may throw.
        for (var seed = 0; seed < 6; seed++)
        {
            var encounter = SampleEncounters.SignpostAmbush(Data, new RandomDiceRoller(new Random(seed)),
                new UtilityGmBrain(BrainSkill.Balanced, new Random(seed), peers: false, foresight: Foresight.None));
            new BotDriver(encounter, new UtilityPartyBrain(BrainSkill.Best, new Random(seed))).PlayToEnd();
            Assert.True(encounter.IsOver || encounter.Round > 0);
        }
    }
}
