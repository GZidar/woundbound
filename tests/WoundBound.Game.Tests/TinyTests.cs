using WoundBound.Game.Combat;

namespace WoundBound.Game.Tests;

/// <summary>Tiny creatures (Core Rules §9, "Movement"; GM Reference §9, "On Reach"): Reach 0, and no ground held.</summary>
public class TinyTests
{
    private static readonly Game.Data.GameData Data = TestData.Load();

    private static Combatant Bat(int x, int y) =>
        CombatantFactory.FromAdversary(Data.Adversary("Vampire Bat"), "Vampire Bat", new GridPosition(x, y));

    private static Combatant Fighter(int x, int y) =>
        CombatantFactory.FromCharacter(SampleEncounters.DumbFighter(Data), new GridPosition(x, y));

    private static Encounter Fight(params Combatant[] combatants) =>
        new(SampleEncounters.FieldWidth, SampleEncounters.FieldHeight, combatants, Data.RangeBands, new ScriptedDice());

    [Fact]
    public void AVampireBat_IsTiny_ReachesZero_AndSeesInTheDark()
    {
        var bat = Bat(5, 5);

        Assert.True(bat.IsTiny);
        Assert.False(bat.HoldsGround);
        Assert.Equal(0, bat.Reach);
        Assert.True(bat.SeesInDark);
    }

    [Fact]
    public void ABat_ThreatensOnlyItsOwnSquare()
    {
        var bat = Bat(5, 5);
        var fighter = Fighter(6, 5);
        var encounter = Fight(fighter, bat, Bat(15, 15));

        Assert.False(encounter.PlanAttack(bat, fighter).CanAttack);
        Assert.False(bat.CanReach(fighter.Position));
        Assert.True(bat.CanReach(bat.Position));
    }

    [Fact]
    public void ACharacter_WalksThroughABat_AndMayStopOnIt()
    {
        var fighter = Fighter(4, 5);
        var bat = Bat(5, 5);
        var encounter = Fight(fighter, bat, Bat(15, 15));
        encounter.TryBeginTurn(fighter);

        Assert.True(encounter.TryMove(new GridPosition(5, 5)));
        Assert.Equal(new GridPosition(5, 5), fighter.Position);
        Assert.Same(fighter, encounter.At(new GridPosition(5, 5)));
    }

    [Fact]
    public void ABat_MayComeDownOnSomebody_TheOnlyWayItBites()
    {
        var fighter = Fighter(4, 5);
        var bat = Bat(7, 5);
        var encounter = Fight(fighter, bat);

        Assert.True(encounter.View.SquaresWithin(bat, 1).ContainsKey(fighter.Position));
    }
}
