using WoundBound.Game.Combat;

namespace WoundBound.Game.Tests;

/// <summary>A foe's attack lines: shots that follow the weapon range rules, and the knife for close work (GM Reference §9).</summary>
public class StatblockRangedTests
{
    private static readonly Game.Data.GameData Data = TestData.Load();

    private static (Encounter Encounter, Combatant Fighter, Combatant Ambusher) Facing(int distance)
    {
        var encounter = SampleEncounters.SignpostAmbush(Data, new ScriptedDice());
        var fighter = encounter.Combatants.Single(c => c.Name == "The Dumb Fighter");
        var ambusher = encounter.Combatants.Single(c => c.Name == "Ambusher on the ridge");
        ambusher.Position = new GridPosition(fighter.Position.X + distance, fighter.Position.Y);
        return (encounter, fighter, ambusher);
    }

    [Fact]
    public void AStatblockWithTwoLines_HasBothAttacks_TheBowOneAShot()
    {
        var skulker = CombatantFactory.FromAdversary(Data.Adversary("Reedmarsh Skulker"), "Skulker", default);

        Assert.Equal(new[] { "Short Bow", "Thrown Dagger" }, skulker.Attacks.Select(a => a.Name));
        Assert.True(skulker.Attacks[0].IsRanged);
        Assert.False(skulker.Attacks[1].IsRanged);
        Assert.True(skulker.HasRangedAttack);
        Assert.Equal(1, skulker.Reach); // what it threatens is the dagger's, not the bow's
    }

    [Fact]
    public void AtShort_TheAmbusherShoots_WithItsOwnNumbers()
    {
        var (encounter, fighter, ambusher) = Facing(3);

        var plan = encounter.PlanAttack(ambusher, fighter);

        Assert.True(plan.CanAttack);
        Assert.Equal("Worn Crossbow", plan.Profile!.Name);
        Assert.Equal((14, 6), (plan.DodgeThreshold, plan.DamageDieSides!.Value));
    }

    [Fact]
    public void AtMiddle_TheRangeRulesTaxTheShot()
    {
        // GM Reference §9: at Middle a Medium weapon rolls d4 rather than d6, and DT is 2 higher.
        var (encounter, fighter, ambusher) = Facing(6);

        var plan = encounter.PlanAttack(ambusher, fighter);

        Assert.Equal("Worn Crossbow", plan.Profile!.Name);
        Assert.Equal((16, 4), (plan.DodgeThreshold, plan.DamageDieSides!.Value));
    }

    [Fact]
    public void AtClose_ThereIsNoShot_SoTheKnifeComesOut()
    {
        var (encounter, fighter, ambusher) = Facing(1);

        var plan = encounter.PlanAttack(ambusher, fighter);

        Assert.True(plan.CanAttack);
        Assert.Equal("Rusty Knife", plan.Profile!.Name);
        Assert.Equal((4, 2), (plan.DamageDieSides!.Value, plan.Profile.DamageModifier));
    }

    [Fact]
    public void BeyondLong_NothingReaches()
    {
        var (encounter, fighter, ambusher) = Facing(17);

        Assert.False(encounter.PlanAttack(ambusher, fighter).CanAttack);
    }
}
