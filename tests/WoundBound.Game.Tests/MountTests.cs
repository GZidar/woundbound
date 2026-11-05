using WoundBound.Game.Combat;
using WoundBound.Rules.Enums;

namespace WoundBound.Game.Tests;

/// <summary>Core Rules §9, "Mounts": movement and a target, and nothing more.</summary>
public class MountTests
{
    private static readonly Game.Data.GameData Data = TestData.Load();

    private static Combatant Fighter(int x, int y) => CombatantFactory.FromCharacter(SampleEncounters.DumbFighter(Data), new GridPosition(x, y));

    private static Combatant Bandit(string name, int x, int y)
    {
        var bandit = CombatantFactory.FromAdversary(TestBandits.Roadside, name, new GridPosition(x, y));
        bandit.Stamina = 0;
        return bandit;
    }

    private static Encounter Fight(int[] dice, params Combatant[] combatants)
    {
        var encounter = new Encounter(SampleEncounters.FieldWidth, SampleEncounters.FieldHeight, combatants, Data.RangeBands, new ScriptedDice(dice));
        encounter.DeclineFreeChoices();
        return encounter;
    }

    [Fact]
    public void ARider_MovesAtTheHorsesSpeed_AndMayUseItsHooves()
    {
        var rider = CombatantFactory.Mounted(Fighter(2, 4));

        Assert.Equal(Speed.Fast, rider.Speed);
        Assert.Contains(rider.Attacks, a => a.Name.StartsWith("Hooves"));
        var encounter = Fight([], rider, Bandit("Bandit", 20, 15));
        // A Middle move is one move at Fast.
        Assert.Equal(1, encounter.MoveCostTo(rider, new GridPosition(8, 4)));
    }

    [Fact]
    public void ANimbleRider_LeavesTheirFootworkOnTheGround()
    {
        var salla = CombatantFactory.Mounted(CombatantFactory.FromCharacter(SampleEncounters.SallaRinehart(Data), new GridPosition(2, 4)));

        Assert.True(salla.IsNimble);
        Assert.False(salla.HasFootwork);
    }

    [Fact]
    public void NoHeavyRangedWeaponFromTheSaddle()
    {
        var build = SampleEncounters.DrovanThale(Data);
        build.Weapons.Clear();
        build.Weapons.Add(CombatantFactory.InHand(Data.Weapon("Longbow")));
        var drovan = CombatantFactory.Mounted(CombatantFactory.FromCharacter(build, new GridPosition(2, 4)));
        var bandit = Bandit("Bandit", 8, 4);
        var encounter = Fight([], drovan, bandit);

        Assert.Contains("saddle", encounter.PlanAttackWith(drovan, drovan.Attack, drovan.Position, bandit).Refusal);
    }

    [Fact]
    public void MountingAndDismounting_EachCostAMove_AndAreNotMovement()
    {
        var rider = CombatantFactory.Mounted(Fighter(2, 4));
        var encounter = Fight([], rider, Bandit("Bandit", 20, 15));
        encounter.TryBeginTurn(rider);

        Assert.True(encounter.TryDismount());
        Assert.False(rider.IsRiding);
        Assert.False(rider.HasMove);
        Assert.Equal(Speed.Normal, rider.Speed);

        Assert.True(encounter.TryMount());
        Assert.True(rider.IsRiding);
        Assert.Equal(0, rider.ActionsRemaining);
    }

    [Fact]
    public void AMountBroughtDown_TakesItsRiderWithIt()
    {
        // 11+10 against the horse's DT 12; a Longsword's d6 on 6, +3: 9, and the horse has 8.
        var fighter = Fighter(2, 4);
        var bandit = CombatantFactory.Mounted(Bandit("Bandit", 3, 4));
        var encounter = Fight([11, 10, 6], fighter, bandit, Bandit("Other", 20, 15));
        encounter.TryBeginTurn(fighter);

        Assert.NotNull(encounter.TryAttack(bandit.Steed!));

        Assert.False(bandit.IsRiding);
        Assert.True(bandit.IsProne);
        Assert.NotEqual(new GridPosition(3, 4), bandit.Position);
        Assert.Contains(encounter.Log, l => l.Contains("goes down"));
    }

    [Fact]
    public void ATripLandedOnARider_UnhorsesThem_AndTheHorseStays()
    {
        var fighter = Fighter(2, 4);
        var bandit = CombatantFactory.Mounted(Bandit("Bandit", 3, 4));
        var encounter = Fight([12, 11], fighter, bandit, Bandit("Other", 20, 15));
        encounter.TryBeginTurn(fighter);

        Assert.True(encounter.TryControl(ControlAction.Trip, bandit));

        Assert.False(bandit.IsRiding);
        Assert.True(bandit.IsProne);
        Assert.Equal(new GridPosition(3, 4), bandit.Steed!.Position);
        Assert.Contains(encounter.Log, l => l.Contains("thrown from the saddle"));
    }
}
