using WoundBound.Game.Combat;

namespace WoundBound.Game.Tests;

public class ReactionAndOpportunityTests
{
    private static readonly Game.Data.GameData Data = TestData.Load();

    private sealed record Board(Encounter Encounter, Combatant Fighter, Combatant Biter, Combatant Sneak, Combatant Shiv);

    // The fighter's DT is 14 throughout: a panic dodge raises it to 16 for the one attack.
    private static Board Fight(params int[] dice)
    {
        var encounter = SampleEncounters.FighterVersusRatkin(Data, new ScriptedDice(dice));
        Combatant Named(string name) => encounter.Combatants.Single(c => c.Name == name);
        return new Board(encounter, encounter.Combatants.Single(c => c.Side == Side.Party),
            Named("Ratkin Biter"), Named("Ratkin Sneak"), Named("Ratkin Shiv"));
    }

    /// <summary>Walks the fighter to (4,4), where every ratkin can reach him with one move, and hands the GM its turn.</summary>
    private static void AdvanceAndEndTurn(Board board)
    {
        Assert.True(board.Encounter.TryMove(new GridPosition(4, 4)));
        board.Encounter.EndTurn();
    }

    [Fact]
    public void AHitAPanicDodgeWouldTurn_IsOfferedToThePlayer_AndDodgingMakesItMiss()
    {
        // Biter closes and rolls 8+6 = 14 against DT 14: a hit, but not against 16.
        var board = Fight(8, 6, 3, 2);
        var stamina = board.Fighter.Stamina;
        AdvanceAndEndTurn(board);

        board.Encounter.RunAdversaryTurn();

        Assert.Equal(EncounterPhase.AwaitingReaction, board.Encounter.Phase);
        var pending = board.Encounter.Pending!;
        Assert.Same(board.Fighter, pending.Defender);
        Assert.Equal(16, pending.RaisedThreshold);
        Assert.False(pending.WouldRiposte);

        Assert.True(board.Encounter.RespondToReaction(spend: true));

        Assert.Equal(board.Fighter.BodyMax, board.Fighter.Body);
        Assert.Equal(stamina - 1, board.Fighter.Stamina);
        Assert.Equal(1, board.Fighter.TacticalSpendsThisRound);
        Assert.Null(board.Encounter.Pending);
    }

    [Fact]
    public void Dodging_HandsTheGmAnImmediateOpportunity_WhichItSpendsOnAnExtraTurn()
    {
        // 8+6 = 14 dodged; the GM answers with a second Biter turn on the spot: 3+2, a miss.
        var board = Fight(8, 6, 3, 2);
        AdvanceAndEndTurn(board);
        board.Encounter.RunAdversaryTurn();

        board.Encounter.RespondToReaction(spend: true);

        Assert.Equal(1, board.Encounter.ImmediateOpportunities);
        Assert.Equal(EncounterPhase.AdversaryTurn, board.Encounter.Phase);

        board.Encounter.RunAdversaryTurn();

        Assert.Equal(0, board.Encounter.GmOpportunities);
        Assert.Contains(board.Encounter.Log, line => line.Contains("Immediate Opportunity on an extra turn for Ratkin Biter"));
        Assert.Equal(2, board.Encounter.Round);
        Assert.Equal(EncounterPhase.PartyTurn, board.Encounter.Phase);
    }

    [Fact]
    public void TakingTheHit_LandsItAsRolled_AndGivesTheGmNothing()
    {
        // 8+6 = 14 hits; d4 = 3, less 1 for light armour.
        var board = Fight(8, 6, 3);
        AdvanceAndEndTurn(board);
        board.Encounter.RunAdversaryTurn();

        board.Encounter.RespondToReaction(spend: false);

        Assert.Equal(board.Fighter.BodyMax - 2, board.Fighter.Body);
        Assert.Equal(0, board.Encounter.GmOpportunities);
        Assert.Equal(EncounterPhase.PartyTurn, board.Encounter.Phase);
    }

    [Fact]
    public void AHitTheDodgeCouldNotTurn_IsNotOffered()
    {
        // 10+6 = 16 meets even the raised 16, so there's nothing to decide.
        var board = Fight(10, 6, 3);
        AdvanceAndEndTurn(board);

        board.Encounter.RunAdversaryTurn();

        Assert.Null(board.Encounter.Pending);
        Assert.Equal(board.Fighter.BodyMax - 2, board.Fighter.Body);
    }

    [Fact]
    public void ADoubleTwelve_IsBeyondAnyDodge()
    {
        var board = Fight(12, 12, 4);
        AdvanceAndEndTurn(board);

        board.Encounter.RunAdversaryTurn();

        Assert.Null(board.Encounter.Pending);
    }

    [Fact]
    public void ACharacterWhoHasSpentThisRound_CantDodge_AndTheirExtraActionBuysTheGmATurn()
    {
        // The extra action is the fighter's one tactical spend at T1, so the 14 can't be dodged:
        // d4 = 2, 1 after armour. Then the banked Opportunity buys Biter a second turn: 2+3, a miss.
        var board = Fight(8, 6, 2, 2, 3);
        Assert.True(board.Encounter.TryBuyExtraAction());
        AdvanceAndEndTurn(board);

        board.Encounter.RunAdversaryTurn();

        Assert.Null(board.Encounter.Pending);
        Assert.Equal(board.Fighter.BodyMax - 1, board.Fighter.Body);
        Assert.Equal(EncounterPhase.AdversaryTurn, board.Encounter.Phase);

        board.Encounter.RunAdversaryTurn();

        Assert.Equal(0, board.Encounter.BankedOpportunities);
        Assert.Equal(2, board.Encounter.Round);
    }

    [Fact]
    public void DodgingACriticalHit_RipostesIt_WoundingTheAttacker()
    {
        // 7+7 = 14 is a critical hit. Raised to 16 it's out of reach — a critical failure — and the
        // Biter, a Minion, is dropped by its own wound (Core Rules §10, "Riposte").
        var board = Fight(7, 7, 2, 3);
        AdvanceAndEndTurn(board);
        board.Encounter.RunAdversaryTurn();
        Assert.True(board.Encounter.Pending!.WouldRiposte);

        board.Encounter.RespondToReaction(spend: true);

        Assert.True(board.Biter.IsOutOfFight);
        Assert.Equal(board.Fighter.BodyMax, board.Fighter.Body);
        Assert.Contains(board.Encounter.Log, line => line.Contains("Riposte!"));
    }

    [Fact]
    public void AnImmediateOpportunity_LapsesAtTheSpendersNextTurn_IfNobodyCanUseIt()
    {
        // Only the Biter is near enough to strike. It's Riposted, nobody else can reach the
        // fighter this turn, and the GM's Opportunity is gone by the time he acts again.
        var board = Fight(7, 7);
        board.Biter.Position = new GridPosition(3, 4);
        board.Sneak.Position = new GridPosition(9, 0);
        board.Shiv.Position = new GridPosition(9, 7);
        board.Encounter.EndTurn();

        board.Encounter.RunAdversaryTurn();
        board.Encounter.RespondToReaction(spend: true);

        Assert.Equal(2, board.Encounter.Round);
        Assert.Equal(0, board.Encounter.GmOpportunities);
        Assert.Contains(board.Encounter.Log, line => line.Contains("lapses unused"));
    }

    [Fact]
    public void TheBank_HoldsNoMoreThanThree()
    {
        var board = Fight();
        board.Encounter.BankedOpportunities = Encounter.MaxBankedOpportunities;

        Assert.True(board.Encounter.TryBuyExtraAction());

        Assert.Equal(Encounter.MaxBankedOpportunities, board.Encounter.BankedOpportunities);
        Assert.Contains(board.Encounter.Log, line => line.Contains("is lost"));
    }

    [Fact]
    public void WithAFullBank_TheGmSpendsOneOnAnAdversarysBonus()
    {
        // A full bank is the one time the bonus beats an extra turn. The Biter rolls three, keeps
        // 8+7 and drops the 2: 15 against 14. Taken, the d4 of 1 is absorbed by armour.
        var board = Fight(2, 8, 7, 1);
        board.Encounter.BankedOpportunities = Encounter.MaxBankedOpportunities;
        AdvanceAndEndTurn(board);

        board.Encounter.RunAdversaryTurn();
        Assert.True(board.Encounter.Pending!.Roll.AppliedAdvantage);
        board.Encounter.RespondToReaction(spend: false);

        Assert.Equal(Encounter.MaxBankedOpportunities - 1, board.Encounter.BankedOpportunities);
        Assert.Equal(board.Fighter.BodyMax, board.Fighter.Body);
    }

    [Fact]
    public void LeavingAnAdversarysReach_LetsItStrikeAsYouGo()
    {
        // 10+8 = 18: too high to dodge. d4 = 4, 3 after armour. It costs the ratkin its only
        // Stamina, and the GM gains nothing from a creature's spend.
        var board = Fight(10, 8, 4);
        board.Biter.Position = new GridPosition(2, 4);

        Assert.True(board.Encounter.TryMove(new GridPosition(1, 1)));

        Assert.Equal(new GridPosition(1, 1), board.Fighter.Position);
        Assert.Equal(board.Fighter.BodyMax - 3, board.Fighter.Body);
        Assert.Equal(0, board.Biter.Stamina);
        Assert.Equal(0, board.Encounter.GmOpportunities);
        Assert.Equal(EncounterPhase.PartyTurn, board.Encounter.Phase);
    }

    [Fact]
    public void MovingWithinAnAdversarysReach_TriggersNothing()
    {
        // No dice scripted: any reaction attack would throw.
        var board = Fight();
        board.Biter.Position = new GridPosition(2, 4);

        Assert.True(board.Encounter.TryMove(new GridPosition(1, 3)));

        Assert.Equal(1, board.Biter.Stamina);
    }

    [Fact]
    public void AReactionAttack_CanItselfBeDodged_BeforeTheMoveFinishes()
    {
        var board = Fight(8, 6);
        board.Biter.Position = new GridPosition(2, 4);

        board.Encounter.TryMove(new GridPosition(1, 1));

        Assert.Equal(EncounterPhase.AwaitingReaction, board.Encounter.Phase);
        Assert.Equal(new GridPosition(1, 4), board.Fighter.Position);

        board.Encounter.RespondToReaction(spend: true);

        Assert.Equal(new GridPosition(1, 1), board.Fighter.Position);
        Assert.Equal(board.Fighter.BodyMax, board.Fighter.Body);
        Assert.Equal(EncounterPhase.PartyTurn, board.Encounter.Phase);
        Assert.Equal(1, board.Encounter.ImmediateOpportunities);
    }

    [Fact]
    public void ARatkin_PanicDodgesAHitItCanTurn_ForNoOpportunity()
    {
        // 4+3 +4 = 11 against the Biter's 11: a hit, but not against 13. A Minion dropped by any
        // blow has every reason to spend its Stamina.
        var board = Fight(4, 3);
        board.Fighter.Position = new GridPosition(6, 2);

        var outcome = board.Encounter.TryAttack(board.Biter)!;

        Assert.True(outcome.Dodged);
        Assert.False(outcome.Roll.Success);
        Assert.False(board.Biter.IsOutOfFight);
        Assert.Equal(0, board.Biter.Stamina);
        Assert.Equal(0, board.Encounter.GmOpportunities);
    }
}
