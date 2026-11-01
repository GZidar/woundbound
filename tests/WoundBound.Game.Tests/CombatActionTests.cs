using WoundBound.Game.Combat;
using WoundBound.Game.Combat.Brains;
using WoundBound.Game.Simulation;
using WoundBound.Rules.Enums;

namespace WoundBound.Game.Tests;

/// <summary>
/// The rest of Core Rules §9's "Actions in Combat": Help, Harry, Heal (and Meditate and Pray),
/// Shield, and Drag — and the Dying rules of §4 that Heal answers.
/// </summary>
public class CombatActionTests
{
    private static readonly Game.Data.GameData Data = TestData.Load();

    private static Combatant Named(Encounter encounter, string name) => encounter.Combatants.Single(c => c.Name == name);

    /// <summary>The signpost with the bandits kept well back, so nobody but whoever a test places can strike.</summary>
    private static Encounter QuietSignpost(bool fourPlayers, params int[] dice)
    {
        var encounter = SampleEncounters.SignpostAmbush(Data, new ScriptedDice(dice), fourPlayers: fourPlayers);
        foreach (var (name, x, y) in new[] { ("Lantern-bearer", 12, 4), ("Ambusher on the ridge", 12, 1), ("Ambusher in the trees", 12, 7), ("Bandit round the corner", 12, 10) })
        {
            if (encounter.Combatants.FirstOrDefault(c => c.Name == name) is { } bandit)
            {
                bandit.Position = new GridPosition(x, y);
            }
        }

        return encounter;
    }

    [Fact]
    public void Joren_ComesOutAsHisSheetSays()
    {
        // Quickstart Adventure: Body 7, Stamina 5 (6, −1 for Medium armour), DT 13 / RT 12 / ST 15,
        // Battle Axe +4 / 1d6+3 — and the buckler adds nothing until it's raised.
        var joren = Named(QuietSignpost(fourPlayers: true), "Joren Blacke");

        Assert.Equal((7, 5, 13, 15, 12), (joren.BodyMax, joren.StaminaMax, joren.DodgeThreshold, joren.StabilityThreshold, joren.ResolveThreshold!.Value));
        Assert.Equal((4, 6, 3), (joren.Attack.AttackModifier, joren.Attack.DamageDieSides, joren.Attack.DamageModifier));
        Assert.Equal(ShieldWeight.Light, joren.InHandShield?.Weight);
        Assert.False(joren.HasFreeHand);
    }

    [Fact]
    public void FourPlayers_TwoOfEachBandit_AndFourAdversaryTurns()
    {
        var encounter = QuietSignpost(fourPlayers: true);

        Assert.Equal(4, encounter.Combatants.Count(c => c.Side == Side.Party));
        Assert.Equal(2, encounter.Combatants.Count(c => c.Name is "Lantern-bearer" or "Bandit round the corner"));
        Assert.Equal(4, encounter.AdversaryTurnsPerRound);
    }

    [Fact]
    public void Shield_RaisedWithTheAction_AddsItsBonusUntilTheBearersNextTurn()
    {
        var encounter = QuietSignpost(fourPlayers: true);
        var joren = Named(encounter, "Joren Blacke");
        encounter.TryBeginTurn(joren);

        Assert.Contains("covers nobody but its bearer", Encounter.ShieldRefusal(joren, Named(encounter, "The Dumb Fighter")));
        Assert.True(encounter.TryRaiseShield());

        Assert.Equal(15, joren.DodgeThreshold);
        Assert.Equal(0, joren.ActionsRemaining);
        Assert.Equal(joren.StaminaMax, joren.Stamina);
        Assert.Equal(0, encounter.GmOpportunities);
    }

    [Fact]
    public void AShieldRaisedAsAReaction_TurnsTheBlow_AndStaysUp()
    {
        // The lantern-bearer steps up and swings at Joren: 7+5 +2 = 14 against DT 13, a hit. The
        // buckler's +2 makes it 15 — a miss — and it's still up for the next blow.
        var encounter = QuietSignpost(true, 7, 5);
        var joren = Named(encounter, "Joren Blacke");
        Named(encounter, "Lantern-bearer").Position = new GridPosition(3, 4);
        encounter.TryBeginTurn(joren);
        encounter.EndTurn();

        encounter.RunAdversaryTurn();

        var pending = Assert.IsType<PendingReaction>(encounter.Pending);
        Assert.Equal(ReactionKind.RaiseShield, pending.Kind);
        Assert.Equal(15, pending.RaisedThreshold);
        Assert.True(encounter.RespondToReaction(spend: true));

        Assert.Equal(joren.BodyMax, joren.Body);
        Assert.Equal(15, joren.DodgeThreshold);
        Assert.Equal(joren.StaminaMax - 1, joren.Stamina);
        Assert.Contains(encounter.Log, line => line.Contains("(+2 shield raised)") && line.EndsWith("miss."));
    }

    [Fact]
    public void Help_GivesAnAdjacentAlly_AdvantageOnTheirNextRoll()
    {
        // Salla Helps the fighter. The ridge ambusher, beside them both, bites at the fighter and
        // misses (1+2 +2). Then the fighter swings with Advantage: 2, 3 and 12 keep 12+3 +4 = 19
        // against DT 15 — 2+3 would have missed — out of the ambusher's dodge, and 1 on the die.
        var encounter = QuietSignpost(false, 1, 2, 2, 3, 12, 1);
        var salla = Named(encounter, "Salla Rinehart");
        var fighter = Named(encounter, "The Dumb Fighter");
        var ridge = Named(encounter, "Ambusher on the ridge");
        ridge.Position = new GridPosition(2, 4);

        encounter.TryBeginTurn(salla);
        Assert.True(encounter.TryAid(CombatAction.Help, fighter));
        Assert.True(fighter.IsHelped);
        Assert.Equal(0, salla.ActionsRemaining);
        encounter.EndTurn();
        encounter.RunAdversaryTurn();

        encounter.TryBeginTurn(fighter);
        var outcome = encounter.TryAttack(ridge);

        Assert.True(outcome!.Roll.AppliedAdvantage);
        Assert.True(outcome.Roll.Success);
        Assert.False(fighter.IsHelped);
        Assert.Contains(encounter.Log, line => line == "The Dumb Fighter rolls with Salla Rinehart's Help: Advantage.");
    }

    [Fact]
    public void Harry_PutsDisadvantage_OnAnAdjacentFoesNextRoll()
    {
        // The fighter Harries the Biter beside him; on its turn it bites at Disadvantage: 6, 5 and 1
        // keep 5+1 = 6, a miss.
        var encounter = SampleEncounters.FighterVersusRatkin(Data, new ScriptedDice(6, 5, 1));
        var fighter = encounter.Active!;
        var biter = Named(encounter, "Ratkin Biter");
        fighter.Position = new GridPosition(6, 2);
        biter.Position = new GridPosition(7, 2);

        Assert.NotNull(Encounter.AidRefusal(fighter, CombatAction.Harry, Named(encounter, "Ratkin Sneak")));
        Assert.True(encounter.TryAid(CombatAction.Harry, biter));
        encounter.EndTurn();
        encounter.RunAdversaryTurn();

        Assert.False(biter.IsHarried);
        Assert.Contains(encounter.Log, line => line == "Ratkin Biter rolls Harried by The Dumb Fighter: Disadvantage.");
        Assert.Contains(encounter.Log, line => line.StartsWith("Ratkin Biter attacks") && line.EndsWith("miss."));
    }

    [Fact]
    public void Heal_BringsADyingAllyBackIntoTheFight_ButOnlyOncePerFreshWound()
    {
        var encounter = QuietSignpost(false);
        var salla = Named(encounter, "Salla Rinehart");
        var fighter = Named(encounter, "The Dumb Fighter");
        fighter.Body = 0;
        Assert.Equal(BodyStatus.Dying, fighter.BodyStatus);
        Assert.True(fighter.IsOutOfFight);
        Assert.Same(fighter, encounter.At(fighter.Position));

        encounter.TryBeginTurn(salla);
        Assert.True(encounter.TryTend(CombatAction.Heal, fighter));

        Assert.Equal(1, fighter.Body);
        Assert.False(fighter.IsOutOfFight);
        Assert.Contains(encounter.Log, line => line.EndsWith("The Dumb Fighter is back in the fight!"));

        Assert.True(encounter.TryBuyExtraAction());
        Assert.Contains("already tended", Encounter.TendRefusal(salla, CombatAction.Heal, fighter));
        Assert.Contains("carries no Body wound", Encounter.TendRefusal(salla, CombatAction.Heal, salla));
        Assert.Contains("carries no Mind wound", Encounter.TendRefusal(salla, CombatAction.Meditate, fighter));
    }

    [Fact]
    public void ADyingCharacter_RollsPresenceAtTheirTurn_TenOrMoreToStabilise()
    {
        // Passed over in the order, the dying fighter rolls 2d12 + Presence 1: 6+5+1 = 12, stable.
        var encounter = QuietSignpost(false, 6, 5);
        var fighter = Named(encounter, "The Dumb Fighter");
        fighter.Body = 0;

        encounter.TryBeginTurn(Named(encounter, "Salla Rinehart"));
        encounter.EndTurn();
        encounter.RunAdversaryTurn();

        Assert.True(fighter.IsStable);
        Assert.Equal(BodyStatus.Stable, fighter.BodyStatus);
        Assert.Contains(encounter.Log, line => line.StartsWith("The Dumb Fighter, dying, rolls to hold on") && line.Contains("stable"));
    }

    [Fact]
    public void ADyingCharacterWhoFailsThePresenceRoll_SlipsFurther()
    {
        var encounter = QuietSignpost(false, 2, 3);
        var fighter = Named(encounter, "The Dumb Fighter");
        fighter.Body = 0;

        encounter.TryBeginTurn(Named(encounter, "Salla Rinehart"));
        encounter.EndTurn();
        encounter.RunAdversaryTurn();

        Assert.Equal(-1, fighter.Body);
        Assert.Equal(BodyStatus.Dying, fighter.BodyStatus);
    }

    [Fact]
    public void Drag_HaulsTheHeldCreatureAlong_ForAMoveMore()
    {
        // Grab (5+4 +4 = 13 v ST 11), buy another action, then drag the Biter a square west: the
        // move costs 2, and 6+5 +4 = 15 against its ST 11 hauls it into the square the fighter left.
        var encounter = SampleEncounters.FighterVersusRatkin(Data, new ScriptedDice(5, 4, 6, 5));
        var fighter = encounter.Active!;
        var biter = Named(encounter, "Ratkin Biter");
        fighter.Position = new GridPosition(6, 2);
        biter.Position = new GridPosition(7, 2);
        encounter.TryControl(ControlAction.Grab, biter);

        Assert.Null(encounter.DragCostTo(fighter, new GridPosition(5, 2)));
        encounter.TryBuyExtraAction();
        Assert.Equal(2, encounter.DragCostTo(fighter, new GridPosition(5, 2)));

        Assert.True(encounter.TryDrag(new GridPosition(5, 2)));

        Assert.Equal(new GridPosition(5, 2), fighter.Position);
        Assert.Equal(new GridPosition(6, 2), biter.Position);
        Assert.Same(fighter, biter.HeldBy);
        Assert.Equal(0, Encounter.MovesAvailable(fighter));
    }

    [Fact]
    public void AFailedDrag_SpendsTheMove_AndNobodyGoesAnywhere()
    {
        var encounter = SampleEncounters.FighterVersusRatkin(Data, new ScriptedDice(5, 4, 2, 3));
        var fighter = encounter.Active!;
        var biter = Named(encounter, "Ratkin Biter");
        fighter.Position = new GridPosition(6, 2);
        biter.Position = new GridPosition(7, 2);
        encounter.TryControl(ControlAction.Grab, biter);
        encounter.TryBuyExtraAction();

        Assert.True(encounter.TryDrag(new GridPosition(5, 2)));

        Assert.Equal(new GridPosition(6, 2), fighter.Position);
        Assert.Equal(new GridPosition(7, 2), biter.Position);
        Assert.Same(fighter, biter.HeldBy);
        Assert.Contains(encounter.Log, line => line.Contains("digs in"));
    }

    [Fact]
    public void BotsPlayTheFourPlayerSignpost_RescuingTheFallen_WithinTheRules()
    {
        var tends = 0;
        foreach (var tactics in PartyTactics.Presets)
        {
            var report = Simulator.Run(dice => SampleEncounters.SignpostAmbush(Data, dice, fourPlayers: true), () => new TacticalPartyBrain(tactics), fights: 15);
            Assert.Equal(15, report.Fights.Count);
            tends += report.Fights.Sum(f => f.Tally.Tends);
        }

        Assert.True(tends > 0);
    }

    [Fact]
    public void ABotWithNobodyToHit_HelpsAnAllyBesideItWhoHas()
    {
        // Salla has spent her move and nobody is beside her; the fighter, beside her and still to
        // act, can step up to the ridge ambusher. She Helps him.
        var encounter = QuietSignpost(false);
        var salla = Named(encounter, "Salla Rinehart");
        Named(encounter, "Ambusher on the ridge").Position = new GridPosition(4, 4);
        encounter.TryBeginTurn(salla);
        salla.HasMove = false;
        var brain = new TacticalPartyBrain(new PartyTactics("Help", HelpWhenIdle: true));

        var step = brain.NextStep(encounter.View, salla);

        Assert.Equal(new PartyStep.Aid(CombatAction.Help, Named(encounter, "The Dumb Fighter")), step);
    }
}
