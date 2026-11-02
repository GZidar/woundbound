using WoundBound.Game.Combat;
using WoundBound.Game.Combat.Brains;
using WoundBound.Rules.Calculation;
using WoundBound.Rules.Enums;

namespace WoundBound.Game.Tests;

/// <summary>Core Rules §9, "The Manoeuvres": Disarm and Sunder — and foes whose Attack line names a manoeuvre (GM Reference §2).</summary>
public class DisarmSunderTests
{
    private static readonly Game.Data.GameData Data = TestData.Load();

    /// <summary>The simple GM, made to do one thing on its turn.</summary>
    private sealed class OneStepGm(Func<BattleView, GmTurn, GmStep?> step) : SimpleGmBrain
    {
        public override GmStep NextStep(BattleView view, GmTurn turn) => step(view, turn) ?? base.NextStep(view, turn);

        public override bool PanicDodges(BattleView view, PendingReaction reaction) => false;

        public override bool StandsFirm(BattleView view, PendingReaction reaction) => false;
    }

    /// <summary>Joren, with a Duelling Blade (Cutting, Finesse, Heavy: Disarm and Sunder) in place of his axe and buckler.</summary>
    private static Combatant JorenWithDuellingBlade(int x, int y)
    {
        var build = SampleEncounters.JorenBlacke(Data);
        build.Weapons.Clear();
        build.Shields.Clear();
        build.Weapons.Add(CombatantFactory.InHand(Data.Weapon("Duelling Blade")));
        return CombatantFactory.FromCharacter(build, new GridPosition(x, y));
    }

    private static Combatant Bandit(string name, int x, int y, string? effect = null) =>
        CombatantFactory.FromAdversary(effect is null ? SampleEncounters.RoadsideBandit : SampleEncounters.RoadsideBandit with { AttackEffect = effect },
            name, new GridPosition(x, y));

    private static Encounter Fight(int[] dice, IGmBrain? gm, params Combatant[] combatants) =>
        new(SampleEncounters.FieldWidth, SampleEncounters.FieldHeight, combatants, Data.RangeBands, new ScriptedDice(dice), gm);

    [Fact]
    public void AStatblockThatNamesAManoeuvre_MayDeclareIt_WithoutBeingMartial()
    {
        var ogrekin = CombatantFactory.FromAdversary(Data.Adversary("Quarrymarch Ogrekin"), "Ogrekin", new GridPosition(3, 4));
        var joren = CombatantFactory.FromCharacter(SampleEncounters.JorenBlacke(Data), new GridPosition(2, 4));

        Assert.Equal([WeaponManoeuvre.Topple], Encounter.ManoeuvresOf(ogrekin));
        Assert.Null(Encounter.ManoeuvreRefusal(ogrekin, joren, new Manoeuvre(WeaponManoeuvre.Topple)));
        Assert.NotNull(Encounter.ManoeuvreRefusal(ogrekin, joren, new Manoeuvre(WeaponManoeuvre.Sunder)));
        Assert.Empty(Encounter.ManoeuvresOf(Bandit("Bandit", 5, 5)));
    }

    [Fact]
    public void Disarm_KnocksTheWeaponToTheirFeet_OffGuard_AndPunchingUntilTheyTakeItUp()
    {
        // 11+10 + Joren's bonus against the bandit's DT 15 and ST 11: a hit, and the sword goes.
        var joren = JorenWithDuellingBlade(2, 4);
        var bandit = Bandit("Bandit", 3, 4);
        bandit.Stamina = 0;
        var encounter = Fight([11, 10, 3], null, joren, bandit);
        encounter.TryBeginTurn(joren);

        Assert.NotNull(encounter.TryAttack(bandit, manoeuvre: new Manoeuvre(WeaponManoeuvre.Disarm)));

        Assert.True(bandit.IsDisarmed);
        Assert.True(bandit.IsOffGuard);
        Assert.Equal("Unarmed", bandit.Attack.Name);
        Assert.Equal(4, bandit.Attack.DamageDieSides);
        Assert.Equal(bandit.Position, bandit.DroppedAt);
        Assert.False(bandit.WieldsWeapon);
        Assert.Contains("no weapon in hand", Encounter.ManoeuvreRefusal(joren, bandit, new Manoeuvre(WeaponManoeuvre.Disarm)));
    }

    [Fact]
    public void ADisarmedFoe_SpendsAnActionTakingItUp_AndIsArmedAgain()
    {
        var joren = JorenWithDuellingBlade(2, 4);
        var bandit = Bandit("Bandit", 3, 4);
        bandit.Stamina = 0;
        var gm = new OneStepGm((view, turn) => turn.Actor.DroppedWeapon is not null && turn.Actor.ActionsRemaining > 0 ? new GmStep.TakeUpWeapon() : GmStep.End);
        var encounter = Fight([11, 10, 3], gm, joren, bandit);
        encounter.TryBeginTurn(joren);
        encounter.TryAttack(bandit, manoeuvre: new Manoeuvre(WeaponManoeuvre.Disarm));
        encounter.EndTurn();
        bandit.Stamina = 1; // awake for its turn: a creature that starts one on 0 falls unconscious

        encounter.RunAdversaryTurn();

        Assert.False(bandit.IsDisarmed);
        Assert.False(bandit.IsOffGuard);
        Assert.Equal("Rusty Sword", bandit.Attack.Name);
        Assert.Null(bandit.DroppedWeapon);
    }

    [Fact]
    public void ADisarmedCharacter_DropsTheWeaponFromTheirSheet_AndCanTakeItUpForAnAction()
    {
        // The bandit's Attack line is written to allow it: 11+10 +2 = 23 against Joren's DT and ST.
        var joren = JorenWithDuellingBlade(2, 4);
        var bandit = Bandit("Bandit", 3, 4, effect: "; may attempt Disarm (ST) as a manoeuvre");
        var gm = new OneStepGm((view, turn) => turn.HasAttacked ? GmStep.End : new GmStep.Attack(joren, Manoeuvre: new Manoeuvre(WeaponManoeuvre.Disarm)));
        var encounter = Fight([11, 10, 2], gm, joren, bandit);
        encounter.EndTurn();
        encounter.RunAdversaryTurn();
        while (encounter.Pending is not null)
        {
            encounter.RespondToReaction(false);
        }

        Assert.True(joren.IsDisarmed, string.Join("\n", encounter.Log));
        Assert.Null(joren.InHandWeapon);
        Assert.Equal("Duelling Blade", joren.DroppedWeapon?.Name);
        Assert.Equal(bandit.StaminaMax - 1, bandit.Stamina);
        Assert.Equal(0, encounter.BankedOpportunities);

        encounter.TryBeginTurn(joren);
        Assert.True(encounter.TryTakeUpWeapon());
        Assert.Equal("Duelling Blade", joren.InHandWeapon?.Name);
        Assert.False(joren.IsOffGuard);
        Assert.Equal(0, joren.ActionsRemaining);
    }

    [Fact]
    public void Sunder_OnAHit_BattersTheArmourDownAType_OncePerCombat()
    {
        var joren = JorenWithDuellingBlade(2, 4);
        var bandit = Bandit("Bandit", 3, 4);
        bandit.Stamina = 0;
        var encounter = Fight([11, 10, 3], null, joren, bandit);
        encounter.TryBeginTurn(joren);

        Assert.Equal(ArmourWeight.Light, bandit.Armour);
        Assert.NotNull(encounter.TryAttack(bandit, manoeuvre: new Manoeuvre(WeaponManoeuvre.Sunder)));

        Assert.Equal(ArmourWeight.None, bandit.Armour);
        Assert.True(bandit.HasBeenSundered);
        Assert.Contains("already been Sundered", Encounter.ManoeuvreRefusal(joren, bandit, new Manoeuvre(WeaponManoeuvre.Sunder)));
    }

    [Fact]
    public void Sunder_ThatOnlyTheRaisedShieldTurned_SplitsTheShieldInstead()
    {
        // Joren raises a Heavy shield; the bandit's Sunder is rolled to land between his bare DT and
        // his DT behind the shield — the shield alone stopped it, so it cracks down to Medium.
        var build = SampleEncounters.JorenBlacke(Data);
        build.Shields.Clear();
        build.Shields.Add(CombatantFactory.ShieldInHand("Tower shield", ShieldWeight.Heavy));
        var joren = CombatantFactory.FromCharacter(build, new GridPosition(2, 4));
        var bandit = Bandit("Bandit", 3, 4, effect: "; may attempt Sunder (ST) as a manoeuvre");
        var shield = InventoryCalculator.ShieldDodgeThresholdBonus(ShieldWeight.Heavy);
        var bare = joren.DodgeThreshold;
        var total = Math.Max(bare, joren.StabilityThreshold);
        Assert.True(total < bare + shield, $"the test needs a total between the two DTs, at or over ST {joren.StabilityThreshold}, DT {bare}+{shield}");
        var die = total - SampleEncounters.RoadsideBandit.AttackModifier;

        var gm = new OneStepGm((view, turn) => turn.HasAttacked ? GmStep.End : new GmStep.Attack(joren, Manoeuvre: new Manoeuvre(WeaponManoeuvre.Sunder)));
        var encounter = Fight([die / 2, die - die / 2], gm, joren, bandit);
        encounter.TryBeginTurn(joren);
        Assert.True(encounter.TryRaiseShield());
        encounter.EndTurn();
        encounter.RunAdversaryTurn();
        while (encounter.Pending is not null)
        {
            encounter.RespondToReaction(false);
        }

        Assert.Equal(ShieldWeight.Medium, joren.InHandShield?.Weight);
        Assert.Contains(encounter.Log, l => l.Contains($"is DT {bare + InventoryCalculator.ShieldDodgeThresholdBonus(ShieldWeight.Medium)} now"));
        Assert.True(joren.HasBeenSundered);
        Assert.Equal(joren.BodyMax, joren.Body);
        Assert.Equal(ArmourWeight.Medium, joren.Armour);
    }
}

/// <summary>GM Reference §2: a rival company built from the character rules, fighting for the GM.</summary>
public class RivalBandTests
{
    private static readonly Game.Data.GameData Data = TestData.Load();

    [Fact]
    public void TheGreyLanterns_AreCharactersOnTheGmsSide_AndFightItOut()
    {
        var encounter = SampleEncounters.RivalBand(Data, new ScriptedDice());
        var rivals = encounter.Combatants.Where(c => c.Side == Side.Adversaries).ToList();
        Assert.Equal(5, rivals.Count);
        Assert.All(rivals, r => Assert.NotNull(r.Build));
        Assert.Contains(rivals, r => Encounter.ManoeuvresOf(r).Contains(WeaponManoeuvre.Sunder));

        var report = Game.Simulation.Simulator.Run(
            dice => SampleEncounters.RivalBand(Data, dice, new Game.Combat.Brains.Utility.UtilityGmBrain()),
            () => new Game.Combat.Brains.Utility.UtilityPartyBrain(), fights: 4);
        Assert.Equal(4, report.Fights.Count);
    }
}

/// <summary>Core Rules §9, "Rounds & Turn Order": an adversary's turns in a round.</summary>
public class AdversaryTurnTests
{
    private static readonly Game.Data.GameData Data = TestData.Load();

    [Fact]
    public void EachAdversaryTakesOneOwedTurn_AndAnOpportunityBuysItOnlyOneMore()
    {
        // Two fighters, one bandit, three Opportunities banked: the bandit takes its one owed turn,
        // the second owed turn has nobody to take it, and only one of the Opportunities buys it another.
        var encounter = new Encounter(SampleEncounters.FieldWidth, SampleEncounters.FieldHeight, new[]
        {
            CombatantFactory.FromCharacter(SampleEncounters.DumbFighter(Data), new GridPosition(2, 4)),
            CombatantFactory.FromCharacter(SampleEncounters.JorenBlacke(Data), new GridPosition(2, 6)),
            CombatantFactory.FromAdversary(SampleEncounters.RoadsideBandit, "Bandit", new GridPosition(12, 5)),
        }, Data.RangeBands, new RandomDiceRoller(new Random(4)), new Game.Combat.Brains.Utility.UtilityGmBrain());
        encounter.BankedOpportunities = 3;

        foreach (var name in new[] { "The Dumb Fighter", "Joren Blacke" })
        {
            encounter.TryBeginTurn(encounter.Combatants.Single(c => c.Name == name));
            encounter.EndTurn();
            while (encounter.Phase == EncounterPhase.AdversaryTurn)
            {
                encounter.RunAdversaryTurn();
                encounter.DeclineFreeChoices();
            }
        }

        var round1 = encounter.Log.TakeWhile(l => !l.StartsWith("— Round 2")).ToList();
        Assert.True(round1.Count(l => l.Contains("extra turn for Bandit")) <= 1, string.Join("\n", round1));
        Assert.Contains(round1, l => l.Contains("has had its turn this round"));
    }
}
