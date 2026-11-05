using WoundBound.Game.Combat;
using WoundBound.Game.Combat.Brains;
using WoundBound.Rules.Enums;

namespace WoundBound.Game.Tests;

/// <summary>Core Rules §9, "Grab, Shove, Trip and Drag", and the conditions they leave behind.</summary>
public class ManoeuvreTests
{
    private static readonly Game.Data.GameData Data = TestData.Load();

    /// <summary>
    /// The fighter beside the Biter, facing east: the fighter's unarmed roll is 2d12 + 4 (Tier 1 +
    /// Strength 3), against the Biter's ST 11. It has 1 Stamina, which it would spend standing firm.
    /// </summary>
    private static (Encounter Encounter, Combatant Fighter, Combatant Biter) Beside(IGmBrain? gm, params int[] dice) => Stage(true, gm, dice);

    /// <summary>The same stage with the ratkin played as Brutes, one to a turn: as Minions they act together as a mob (Core Rules §9).</summary>
    private static (Encounter Encounter, Combatant Fighter, Combatant Biter) BesideCrowd(IGmBrain? gm, params int[] dice) => Stage(false, gm, dice);

    private static (Encounter Encounter, Combatant Fighter, Combatant Biter) Stage(bool asMinions, IGmBrain? gm, int[] dice)
    {
        var encounter = SampleEncounters.FighterVersusRatkin(Data, new ScriptedDice(dice), gm, asMinions);
        var fighter = encounter.Combatants.Single(c => c.Side == Side.Party);
        var biter = encounter.Combatants.Single(c => c.Name == "Ratkin Biter");
        fighter.Position = new GridPosition(6, 2);
        biter.Position = new GridPosition(7, 2);
        return (encounter, fighter, biter);
    }

    private static (Encounter Encounter, Combatant Fighter, Combatant Biter) Beside(params int[] dice) => Beside(null, dice);

    [Fact]
    public void Trip_RollsUnarmedAgainstStability_AndLeavesTheTargetProne()
    {
        // 4+5 +4 = 13 against ST 11. Standing firm would make it 13 — still met — so it isn't offered.
        var (encounter, fighter, biter) = Beside(4, 5);

        Assert.True(encounter.TryControl(ControlAction.Trip, biter));

        Assert.True(biter.IsProne);
        Assert.True(biter.IsOffGuard);
        Assert.Equal(9, biter.DodgeThreshold);
        Assert.Equal(Speed.Crawling, biter.Speed);
        Assert.Equal(0, fighter.ActionsRemaining);
        Assert.Equal(1, biter.Stamina);
        Assert.Equal(0, encounter.GmOpportunities);
    }

    [Fact]
    public void AFailedTrip_DoesNothing()
    {
        var (encounter, _, biter) = Beside(2, 3);

        encounter.TryControl(ControlAction.Trip, biter);

        Assert.False(biter.IsProne);
        Assert.Contains(encounter.Log, line => line.StartsWith("The Dumb Fighter tries to trip Ratkin Biter") && line.EndsWith("fails."));
    }

    [Fact]
    public void ACriticalTrip_ShovesToo()
    {
        var (encounter, _, biter) = Beside(6, 6);

        encounter.TryControl(ControlAction.Trip, biter);
        // Shoved out of the fighter's own reach: the fighter may react to that too, and doesn't.
        Assert.True(encounter.RespondToLeaver(LeaverResponse.LetThemGo));

        Assert.True(biter.IsProne);
        Assert.Equal(new GridPosition(8, 2), biter.Position);
    }

    [Fact]
    public void ACriticalFailure_PutsTheOneWhoLungedOnTheGround_InsteadOfAWound()
    {
        // 3+3 +4 = 10: doubles, and a miss.
        var (encounter, fighter, biter) = Beside(3, 3);

        encounter.TryControl(ControlAction.Trip, biter);

        Assert.False(biter.IsProne);
        Assert.True(fighter.IsProne);
        Assert.Equal(fighter.BodyMax, fighter.Body);
    }

    [Fact]
    public void Shove_PushesOwnReachStraightAway_DiagonalsIncluded()
    {
        var (encounter, fighter, biter) = Beside(5, 4);
        fighter.Position = new GridPosition(6, 1);

        encounter.TryControl(ControlAction.Shove, biter);
        Assert.True(encounter.RespondToLeaver(LeaverResponse.LetThemGo));

        Assert.Equal(new GridPosition(8, 3), biter.Position);
        Assert.False(biter.IsProne);
    }

    [Fact]
    public void ShovedIntoSomebody_TheTargetMayArrestIt()
    {
        // Driven into the Sneak behind it, the Biter would take a wound — and a Minion dies of one —
        // so the GM spends its last Stamina keeping its footing.
        var (encounter, _, biter) = Beside(5, 4);
        encounter.Combatants.Single(c => c.Name == "Ratkin Sneak").Position = new GridPosition(8, 2);

        encounter.TryControl(ControlAction.Shove, biter);

        Assert.Equal(new GridPosition(7, 2), biter.Position);
        Assert.Equal(0, biter.Stamina);
        Assert.False(biter.IsOutOfFight);
        Assert.Contains(encounter.Log, line => line == "Ratkin Biter arrests the shove (1 Stamina).");
    }

    [Fact]
    public void ShovedPastTheEdgeOfTheMap_TheyGoOn_OntoOpenGround()
    {
        // The map's edges are open: nothing solid there, so no wound, and nothing to arrest against.
        var (encounter, fighter, biter) = Beside(5, 4);
        fighter.Position = new GridPosition(8, 2);
        biter.Position = new GridPosition(9, 2);

        encounter.TryControl(ControlAction.Shove, biter);
        encounter.RespondToLeaver(LeaverResponse.LetThemGo);

        Assert.Equal(new GridPosition(10, 2), biter.Position);
        Assert.False(encounter.IsOnBoard(biter.Position));
        Assert.False(biter.IsOutOfFight);
        Assert.Equal(1, biter.Stamina);
    }

    [Fact]
    public void ShovedIntoSomebody_TheDistanceLeftIsBodyWounds()
    {
        // Nothing left to arrest with: the Biter is driven into the Sneak behind it, 1 Body, no
        // armour — and any wound drops a Minion.
        var (encounter, _, biter) = Beside(5, 4);
        biter.Stamina = 0;
        encounter.Combatants.Single(c => c.Name == "Ratkin Sneak").Position = new GridPosition(8, 2);

        encounter.TryControl(ControlAction.Shove, biter);

        Assert.Equal(new GridPosition(7, 2), biter.Position);
        Assert.True(biter.IsOutOfFight);
        Assert.Contains(encounter.Log, line => line.StartsWith("Ratkin Biter is driven into Ratkin Sneak: 1 Body"));
    }

    [Fact]
    public void TheTargetMayStandFirm_AfterSeeingTheRoll()
    {
        // 4+3 +4 = 11 meets ST 11, but not 13: the Biter spends its Stamina and stays on its feet.
        var (encounter, _, biter) = Beside(4, 3);
        biter.Stamina = 2; // never its last: on 0 it would fall unconscious at its next turn

        encounter.TryControl(ControlAction.Trip, biter);

        Assert.False(biter.IsProne);
        Assert.Equal(1, biter.Stamina);
        Assert.Contains(encounter.Log, line => line == "Ratkin Biter stands firm (1 Stamina, +2 ST).");
    }

    [Fact]
    public void Grab_HoldsTheTargetInPlace_UntilTheHolderMovesOutOfReach()
    {
        var (encounter, fighter, biter) = Beside(5, 4);
        biter.Stamina = 0; // no reaction attack as the fighter steps away

        Assert.True(encounter.TryControl(ControlAction.Grab, biter));

        Assert.Same(fighter, biter.HeldBy);
        Assert.Same(biter, fighter.Holding);
        Assert.Contains(CharacterCondition.Held, biter.Conditions);
        Assert.False(biter.IsOffGuard);
        Assert.False(Encounter.CanMoveItself(biter));

        Assert.True(encounter.TryMove(new GridPosition(4, 2)));

        Assert.Null(biter.HeldBy);
        Assert.Null(fighter.Holding);
        Assert.DoesNotContain(CharacterCondition.Held, biter.Conditions);
    }

    [Fact]
    public void ACriticalGrab_LeavesTheTargetOffGuard_ForAsLongAsTheHoldLasts()
    {
        var (encounter, _, biter) = Beside(6, 6);

        encounter.TryControl(ControlAction.Grab, biter);

        Assert.True(biter.IsOffGuard);
        Assert.Equal(9, biter.DodgeThreshold);

        Assert.True(encounter.TryLetGo());

        Assert.False(biter.IsOffGuard);
        Assert.Equal(11, biter.DodgeThreshold);
    }

    [Fact]
    public void TakingUpATwoHandedBow_EndsTheHold()
    {
        var (encounter, fighter, biter) = Beside(5, 4);
        encounter.TryControl(ControlAction.Grab, biter);

        Assert.True(encounter.TrySwapLoadout());

        Assert.Null(fighter.Holding);
        Assert.Contains(encounter.Log, line => line.StartsWith("The Dumb Fighter has no hand free to hold with"));
    }

    [Fact]
    public void Grab_NeedsAFreeHand_AndEverythingNeedsTheBodysOwnReach()
    {
        var encounter = SampleEncounters.PartyVersusAmbush(Data, new ScriptedDice());
        var salla = encounter.Combatants.Single(c => c.Name == "Salla Rinehart");
        var drovan = encounter.Combatants.Single(c => c.Name == "Drovan Thale");
        var fighter = encounter.Combatants.Single(c => c.Name == "The Dumb Fighter");
        var bandit = encounter.Combatants.Single(c => c.Name == "Tollroad Bandit");
        foreach (var character in new[] { salla, drovan, fighter })
        {
            character.ActionsRemaining = 1;
        }

        // Beside all three of them.
        bandit.Position = new GridPosition(1, 3);
        drovan.Position = new GridPosition(0, 3);

        // Rapier and dagger fill both of Salla's hands; Drovan's bow takes both of his. The
        // fighter's longsword leaves him one.
        Assert.Contains("No free hand", Encounter.ControlRefusal(salla, ControlAction.Grab, bandit));
        Assert.Null(Encounter.ControlRefusal(salla, ControlAction.Trip, bandit));
        Assert.Contains("No free hand", Encounter.ControlRefusal(drovan, ControlAction.Grab, bandit));
        Assert.Null(Encounter.ControlRefusal(fighter, ControlAction.Grab, bandit));

        bandit.Position = new GridPosition(3, 4);
        Assert.Contains("Out of reach", Encounter.ControlRefusal(fighter, ControlAction.Shove, bandit));
    }

    [Fact]
    public void ASwarm_CantBeGrabbedShovedOrTripped()
    {
        var (_, fighter, _) = Beside();
        var swarm = CombatantFactory.FromAdversary(Data.Adversary("Fenmoor Ratswarm"), "Ratswarm", new GridPosition(7, 1));
        fighter.ActionsRemaining = 1;

        foreach (var action in Enum.GetValues<ControlAction>())
        {
            Assert.Contains("Swarm", Encounter.ControlRefusal(fighter, action, swarm));
        }
    }

    [Fact]
    public void StandingUp_CostsAMove_ButIsntMovement()
    {
        var (encounter, fighter, _) = Beside();
        fighter.SetCondition(CharacterCondition.Prone, true);

        Assert.True(encounter.TryStandUp());

        Assert.False(fighter.IsProne);
        Assert.False(fighter.HasMove);
        Assert.Equal(1, fighter.ActionsRemaining);
        // Not movement, so they can still stand their ground — with the action, the move being spent.
        Assert.True(encounter.TryStandGround(CombatAction.Grounded));
    }

    [Fact]
    public void AProneCreature_CrawlsIfItMovesWithoutStanding()
    {
        // Crawling: a single square costs two moves.
        var (encounter, fighter, _) = Beside();
        fighter.SetCondition(CharacterCondition.Prone, true);

        Assert.Equal(2, encounter.MoveCostTo(fighter, new GridPosition(5, 2)));
    }

    [Fact]
    public void OffGuard_PutsDisadvantageOnAttacks()
    {
        // 6, 5 and 1 at Disadvantage keep 5+1: +4 = 10 against DT 11, a miss.
        var (encounter, fighter, biter) = Beside(6, 5, 1);
        fighter.SetCondition(CharacterCondition.Prone, true);

        var outcome = encounter.TryAttack(biter);

        Assert.False(outcome!.Roll.Success);
        Assert.Contains(encounter.Log, line => line.Contains("Disadvantage for being Off-guard"));
    }

    [Fact]
    public void BotsOnBothSides_TripAndStandFirm_WithinTheRules()
    {
        // The tripping GM against every party tactic, the tripping ones included: a brain that
        // broke a rule would throw, and somebody does get tripped.
        var tripped = 0;
        foreach (var tactics in PartyTactics.Presets)
        {
            var report = Game.Simulation.Simulator.Run(
                dice => SampleEncounters.PartyVersusAmbush(Data, dice, new TrippingGmBrain()), () => new TacticalPartyBrain(tactics), fights: 12);
            Assert.Equal(12, report.Fights.Count);
            tripped += report.Fights.Sum(f => f.Tally.StandUps);
        }

        Assert.True(tripped > 0);
    }

    /// <summary>A GM whose creatures grab the nearest character, then do whatever the simple GM would.</summary>
    private sealed class GrabbingBrain : SimpleGmBrain
    {
        public override GmStep NextStep(BattleView view, GmTurn turn) =>
            !turn.HasAttacked && view.NearestEnemy(turn.Actor) is { } target
                && BattleView.ControlRefusal(turn.Actor, ControlAction.Grab, target) is null
                ? new GmStep.Control(ControlAction.Grab, target)
                : base.NextStep(view, turn);
    }

    [Fact]
    public void ACharacterGrabbed_IsAskedWhetherToStandFirm_AndPaysForIt()
    {
        // The Biter grabs at +0: 9+7 = 16 against the fighter's ST 15. Standing firm makes it 17.
        var (encounter, fighter, _) = Beside(new GrabbingBrain(), 9, 7);
        encounter.EndTurn();

        encounter.RunAdversaryTurn();

        Assert.Equal(EncounterPhase.AwaitingReaction, encounter.Phase);
        Assert.Equal(ReactionKind.StandFirm, encounter.Pending!.Kind);
        Assert.Equal(17, encounter.Pending.RaisedThreshold);

        Assert.True(encounter.RespondToReaction(spend: true));

        Assert.Null(fighter.HeldBy);
        Assert.Equal(fighter.StaminaMax - 1, fighter.Stamina);
        Assert.Contains(encounter.Log, line => line.Contains("+2 standing firm") && line.EndsWith("fails."));
    }

    [Fact]
    public void ACharacterHeld_CantMove_ButCanBreakFreeWithTheAction()
    {
        // Grabbed (9+7 = 16 v ST 15, taken), then on the fighter's turn: 6+5 +4 = 15 against the
        // Biter's ST 11 — out of reach of its standing firm, so it isn't asked.
        var (encounter, fighter, biter) = BesideCrowd(new GrabbingBrain(), 9, 7, 6, 5);
        encounter.EndTurn();
        encounter.RunAdversaryTurn();
        encounter.RespondToReaction(spend: false);

        Assert.Same(biter, fighter.HeldBy);
        Assert.Same(fighter, encounter.Active);
        Assert.Empty(encounter.ReachableSquares(fighter));

        Assert.True(encounter.TryBreakFree());

        Assert.Null(fighter.HeldBy);
        Assert.Equal(0, fighter.ActionsRemaining);
        Assert.NotEmpty(encounter.ReachableSquares(fighter));
    }
}
