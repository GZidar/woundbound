using WoundBound.Game.Combat;
using WoundBound.Rules.Enums;

namespace WoundBound.Game.Tests;

/// <summary>Core Rules §9, "Two Weapons": a Light off-hand weapon strikes too, for 1 Stamina the equipment levies.</summary>
public class TwoWeaponsTests
{
    private static readonly Game.Data.GameData Data = TestData.Load();

    private static Combatant Bandit(string name, int x, int y)
    {
        var bandit = CombatantFactory.FromAdversary(TestBandits.Roadside, name, new GridPosition(x, y));
        bandit.Stamina = 0;
        return bandit;
    }

    private static Encounter Fight(int[] dice, params Combatant[] combatants) =>
        new(SampleEncounters.FieldWidth, SampleEncounters.FieldHeight, combatants, Data.RangeBands, new ScriptedDice(dice), null);

    /// <summary>Salla Rinehart: a rapier, and a dagger in the off hand.</summary>
    private static Combatant Salla(int x, int y) => CombatantFactory.FromCharacter(SampleEncounters.SallaRinehart(Data), new GridPosition(x, y));

    [Fact]
    public void TheRapierIsTheMainWeapon_AndTheDaggerTheOffHand()
    {
        var salla = Salla(2, 4);

        Assert.Equal("Rapier", salla.InHandWeapon?.Name);
        Assert.Equal("Dagger", salla.OffHandWeapon?.Name);
        Assert.Equal("Dagger", salla.OffHandAttack?.Name);
        Assert.Equal("Rapier", salla.Attack.Name);
    }

    [Fact]
    public void AfterTheAttackAction_TheOffHandStrikes_For1Stamina_NoActionNoTacticalSpendNoOpportunity()
    {
        var salla = Salla(2, 4);
        var bandit = Bandit("Bandit", 3, 4);
        var other = Bandit("Other", 9, 9);
        var encounter = Fight([10, 9, 1, 10, 9, 1], salla, bandit, other);
        encounter.TryBeginTurn(salla);

        Assert.NotNull(encounter.OffHandRefusal(salla, bandit));
        Assert.NotNull(encounter.TryAttack(bandit));
        Assert.Equal(0, salla.ActionsRemaining);
        Assert.Null(encounter.OffHandRefusal(salla, bandit));

        var stamina = salla.Stamina;
        Assert.NotNull(encounter.TryOffHandAttack(bandit));

        Assert.Equal(stamina - 1, salla.Stamina);
        Assert.Equal(0, salla.TacticalSpendsThisRound);
        Assert.Equal(0, encounter.BankedOpportunities);
        Assert.Contains(encounter.Log, l => l.Contains("Dagger"));
        // Once per Attack action.
        Assert.NotNull(encounter.OffHandRefusal(salla, bandit));
        Assert.Null(encounter.TryOffHandAttack(bandit));
    }

    [Fact]
    public void TheOffHandStrike_IsLost_OnceTheNextMoveIsMade()
    {
        var salla = Salla(2, 4);
        var bandit = Bandit("Bandit", 3, 4);
        var encounter = Fight([10, 9, 1], salla, bandit, Bandit("Other", 9, 9));
        encounter.TryBeginTurn(salla);
        Assert.NotNull(encounter.TryAttack(bandit));

        // Her Nimble free step spends no move, so it costs her nothing here (Core Rules §9, "Footwork").
        Assert.True(encounter.TryMove(new GridPosition(2, 3)));
        Assert.True(salla.HasUsedFreeStep);
        Assert.Null(encounter.OffHandRefusal(salla, bandit));

        // A paid move does.
        Assert.True(encounter.TryMove(new GridPosition(2, 4)));
        Assert.NotNull(encounter.OffHandRefusal(salla, bandit));
    }

    [Fact]
    public void OneWeaponInHand_HasNoOffHandStrike()
    {
        var fighter = CombatantFactory.FromCharacter(SampleEncounters.DumbFighter(Data), new GridPosition(2, 4));
        var bandit = Bandit("Bandit", 3, 4);
        var encounter = Fight([10, 9, 1], fighter, bandit, Bandit("Other", 9, 9));
        encounter.TryBeginTurn(fighter);
        Assert.NotNull(encounter.TryAttack(bandit));

        Assert.Null(fighter.OffHandWeapon);
        Assert.Contains("Two Weapons", encounter.OffHandRefusal(fighter, bandit));
    }

    [Fact]
    public void AMartialCharacter_HasTwoSetsOfManoeuvres_OnePerWeapon()
    {
        var build = SampleEncounters.JorenBlacke(Data);
        build.Weapons.Clear();
        build.Shields.Clear();
        build.Weapons.Add(CombatantFactory.InHand(Data.Weapon("Longsword")));
        build.Weapons.Add(CombatantFactory.InHand(Data.Weapon("Dagger")));
        var joren = CombatantFactory.FromCharacter(build, new GridPosition(2, 4));
        var bandit = Bandit("Bandit", 3, 4);

        Assert.Contains(WeaponManoeuvre.Cleave, Encounter.ManoeuvresOf(joren));
        Assert.DoesNotContain(WeaponManoeuvre.Disarm, Encounter.ManoeuvresOf(joren));
        Assert.Contains(WeaponManoeuvre.Disarm, Encounter.ManoeuvresOf(joren, offHand: true));
        Assert.NotNull(Encounter.ManoeuvreRefusal(joren, bandit, new Manoeuvre(WeaponManoeuvre.Disarm)));
        Assert.Null(Encounter.ManoeuvreRefusal(joren, bandit, new Manoeuvre(WeaponManoeuvre.Disarm), offHand: true));
    }
}
