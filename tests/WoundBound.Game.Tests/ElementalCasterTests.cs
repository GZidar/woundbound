using WoundBound.Game.Combat;
using WoundBound.Rules.Catalog;
using WoundBound.Rules.Enums;
using WoundBound.Rules.Models;

namespace WoundBound.Game.Tests;

/// <summary>GM Reference §9, "On Casters": Caster - Elemental, with Fuel N, and of its element.</summary>
public class ElementalCasterTests
{
    private static readonly Game.Data.GameData Data = TestData.Load();

    /// <summary>The Hex-Caller's block, casting fire instead: Flame Burst, and Fuel N — or made of flame.</summary>
    private static Combatant FireCaster(int fuel, bool ofItsElement = false, int x = 10, int y = 5) =>
        CombatantFactory.FromAdversary(Data.Adversary("Bog Hex-Caller") with
        {
            AttackName = "None",
            Casting = new AdversaryCasting { Source = MagicSource.ElementalMagic, Element = Element.Fire, Fuel = fuel, OfItsElement = ofItsElement },
            Spells = [new StartingSpellLineInfo("Flame Burst", "", "1 Fuel")],
        }, "Ember-witch", new GridPosition(x, y), Data.Spells);

    private static (Encounter Encounter, Combatant Caster, Combatant Target) Scene(Combatant caster)
    {
        var fighter = CombatantFactory.FromCharacter(SampleEncounters.DumbFighter(Data), new GridPosition(caster.Position.X - 3, caster.Position.Y));
        caster.ActionsRemaining = 1;
        return (new Encounter(SampleEncounters.FieldWidth, SampleEncounters.FieldHeight, [fighter, caster], Data.RangeBands, new ScriptedDice()), caster, fighter);
    }

    private static SpellCast FlameBurstAt(Combatant caster, Combatant target) =>
        new(caster.Spells.Single(s => s.Name == "Flame Burst")) { Target = target };

    [Fact]
    public void AFuelledCaster_PaysFromTheFuelItCarries()
    {
        var (encounter, caster, target) = Scene(FireCaster(fuel: 3));

        var plan = encounter.PlanCast(caster, FlameBurstAt(caster, target));

        Assert.True(plan.CanCast, plan.Refusal);
        Assert.Equal(3, caster.StatblockFuel);
        Assert.Equal(plan.Cost, plan.Fuel!.FromCarried);
        Assert.Equal(0, plan.WoundsPaid);
    }

    [Fact]
    public void WithTooLittleFuel_TheSpellCantBeCast()
    {
        var (encounter, caster, target) = Scene(FireCaster(fuel: 0));

        Assert.Contains("short", encounter.PlanCast(caster, FlameBurstAt(caster, target)).Refusal);
    }

    [Fact]
    public void ACasterOfItsElement_PaysInBody_LikeBloodMagic()
    {
        var (encounter, caster, target) = Scene(FireCaster(fuel: 0, ofItsElement: true));

        var plan = encounter.PlanCast(caster, FlameBurstAt(caster, target));

        Assert.True(plan.CanCast, plan.Refusal);
        Assert.Equal(WoundType.Body, plan.Track);
        Assert.Null(plan.Fuel);
        Assert.Equal(plan.Cost, plan.WoundsPaid);
    }
}
