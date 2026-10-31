using WoundBound.Game.Combat;
using WoundBound.Game.Combat.Brains;

namespace WoundBound.Game.Tests;

/// <summary>The GM brain chooses; the encounter checks and carries out. These swap in other brains.</summary>
public class GmBrainTests
{
    private static readonly Game.Data.GameData Data = TestData.Load();

    /// <summary>The simple brain, with whichever of its choices a test wants to override.</summary>
    private sealed class TestBrain : SimpleGmBrain
    {
        public Func<BattleView, GmTurn, GmStep?>? Step { get; init; }

        public bool Dodges { get; init; } = true;

        public bool Reacts { get; init; } = true;

        public override GmStep NextStep(BattleView view, GmTurn turn) => Step?.Invoke(view, turn) ?? base.NextStep(view, turn);

        public override bool PanicDodges(BattleView view, PendingReaction reaction) => Dodges;

        public override bool StrikesAtLeaver(BattleView view, Combatant adversary, Combatant leaver) => Reacts;
    }

    private static (Encounter Encounter, Combatant Fighter, Combatant Biter) Fight(IGmBrain gm, params int[] dice)
    {
        var encounter = SampleEncounters.FighterVersusRatkin(Data, new ScriptedDice(dice), gm);
        return (encounter, encounter.Combatants.Single(c => c.Side == Side.Party), encounter.Combatants.Single(c => c.Name == "Ratkin Biter"));
    }

    [Fact]
    public void ABrainCanStrikeAndThenFallBack_WithTheMoveItKept()
    {
        // Hit and run: bite from beside the fighter (2+3, a miss), then spend the unused move on
        // stepping away, which the simple brain would never do.
        var gm = new TestBrain
        {
            Step = (view, turn) => turn.HasAttacked && !turn.HasMoved
                ? new GmStep.Move(new GridPosition(turn.Actor.Position.X + 2, turn.Actor.Position.Y))
                : null,
        };
        var (encounter, fighter, biter) = Fight(gm, 2, 3);
        fighter.Position = new GridPosition(4, 2);
        biter.Position = new GridPosition(5, 2);
        encounter.EndTurn();

        encounter.RunAdversaryTurn();

        Assert.Contains(encounter.Log, line => line.StartsWith("Ratkin Biter attacks"));
        Assert.Equal(new GridPosition(7, 2), biter.Position);
    }

    [Fact]
    public void ABrainCanDeclineADodge_AndTheHitLands()
    {
        // 4+3 +4 = 11 against DT 11: a dodge would turn it, but this GM keeps the Stamina.
        var (encounter, fighter, biter) = Fight(new TestBrain { Dodges = false }, 4, 3, 1);
        fighter.Position = new GridPosition(6, 2);

        encounter.TryAttack(biter);

        Assert.True(biter.IsOutOfFight);
        Assert.Equal(1, biter.Stamina);
    }

    [Fact]
    public void ABrainCanLetALeaverGo()
    {
        // No dice scripted: a reaction attack would throw.
        var (encounter, fighter, biter) = Fight(new TestBrain { Reacts = false });
        biter.Position = new GridPosition(2, 4);

        Assert.True(encounter.TryMove(new GridPosition(1, 1)));

        Assert.Equal(new GridPosition(1, 1), fighter.Position);
        Assert.Equal(1, biter.Stamina);
        Assert.Contains(encounter.Log, line => line.Contains("lets them go"));
    }

    [Fact]
    public void AnIllegalStep_IsRefused_NotCarriedOut()
    {
        // Attacking a fighter clean across the board is no move a rule allows.
        var gm = new TestBrain { Step = (view, turn) => new GmStep.Attack(view.Characters.Single()) };
        var (encounter, fighter, _) = Fight(gm);
        encounter.EndTurn();

        var refused = Assert.Throws<InvalidOperationException>(encounter.RunAdversaryTurn);

        Assert.Contains("can't do now", refused.Message);
        Assert.Equal(fighter.BodyMax, fighter.Body);
    }

    [Fact]
    public void AdvantageWithoutAnOpportunity_IsRefused()
    {
        var gm = new TestBrain
        {
            Step = (view, turn) => view.CanAttack(turn.Actor, view.Characters.Single()) && !turn.HasAttacked
                ? new GmStep.Attack(view.Characters.Single(), WithAdvantage: true)
                : null,
        };
        var (encounter, fighter, _) = Fight(gm);
        fighter.Position = new GridPosition(6, 2);
        encounter.EndTurn();

        Assert.Throws<InvalidOperationException>(encounter.RunAdversaryTurn);
    }

    [Fact]
    public void ABrainCanSpendTheActionOnASecondMove_ButNoMore()
    {
        // Back and forth along the edge: the move, then the action spent as a move. With both gone
        // the next Move is refused, so the brain has to end its turn.
        var gm = new TestBrain
        {
            Step = (view, turn) => view.MovesAvailable(turn.Actor) > 0
                ? new GmStep.Move(new GridPosition(turn.Actor.Position.X, turn.Actor.Position.Y == 0 ? 1 : 0))
                : GmStep.End,
        };
        var (encounter, _, biter) = Fight(gm);
        biter.Position = new GridPosition(5, 0);  // near enough to strike, so it's the one the GM picks
        encounter.EndTurn();

        encounter.RunAdversaryTurn();

        Assert.Equal(2, encounter.Log.Count(line => line.StartsWith("Ratkin Biter moves")));
        Assert.Equal(new GridPosition(5, 0), biter.Position);
        Assert.Equal(2, encounter.Round);
    }
}
