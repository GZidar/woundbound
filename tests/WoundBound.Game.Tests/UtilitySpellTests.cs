using WoundBound.Game.Combat;
using WoundBound.Rules.Enums;

namespace WoundBound.Game.Tests;

/// <summary>
/// Core Rules §11, "Utility Spells", and §9, "What the Levers Touch Elsewhere": a lasting Utility
/// spell moves one lever one grade for everything inside its Size, stays where it is put, and ends
/// with its caster.
/// </summary>
public class UtilitySpellTests
{
    private static readonly Game.Data.GameData Data = TestData.Load();

    private static (Encounter Encounter, Combatant Caster, Combatant Bandit) Night(Combatant caster, Setting? setting = null)
    {
        var bandit = CombatantFactory.FromAdversary(TestBandits.Roadside, "Bandit", new GridPosition(8, 4));
        var encounter = new Encounter(SampleEncounters.FieldWidth, SampleEncounters.FieldHeight, [caster, bandit], Data.RangeBands,
            new ScriptedDice(), setting: setting ?? SampleEncounters.SignpostNight, environment: Data.Environment);
        encounter.TryBeginTurn(caster);
        return (encounter, caster, bandit);
    }

    private static Rules.Models.SpellDefinition Spell(Combatant caster, string name) => caster.Spells.Single(s => s.Name == name);

    [Fact]
    public void ShrineLight_LightsWhereItIsPut_AsOneCarriedLight_WithNoRoll()
    {
        var mirella = CombatantFactory.FromCharacter(SampleEncounters.MirellaVask(Data), new GridPosition(6, 4));
        var (encounter, _, bandit) = Night(mirella);
        Assert.Equal(Visibility.None, encounter.VisibilityOf(bandit));

        var cast = new SpellCast(Spell(mirella, "Shrine Light")) { Corner = new GridPosition(7, 4) };
        var plan = encounter.PlanCast(mirella, cast);
        Assert.True(plan.CanCast, plan.Refusal);
        Assert.Equal(1, plan.Cost);
        Assert.Empty(plan.Targets);

        Assert.True(encounter.TryCast(cast), string.Join("\n", encounter.Log));

        // One carried light, a square from the bandit: Good where a lone torch is Good.
        Assert.True(encounter.VisibilityOf(bandit) < Visibility.None, string.Join("\n", encounter.Log));
        Assert.Single(encounter.Workings);
        Assert.Equal(mirella.SoulMax - 1, mirella.Soul);
    }

    [Fact]
    public void TheLight_GoesOut_WhenItsCasterIsKnockedUnconscious()
    {
        var mirella = CombatantFactory.FromCharacter(SampleEncounters.MirellaVask(Data), new GridPosition(6, 4));
        var (encounter, _, bandit) = Night(mirella);
        Assert.True(encounter.TryCast(new SpellCast(Spell(mirella, "Shrine Light")) { Corner = new GridPosition(7, 4) }));
        var lit = encounter.VisibilityOf(bandit);

        mirella.IsUnconscious = true;

        Assert.Empty(encounter.Workings);
        Assert.Equal(Visibility.None, encounter.VisibilityOf(bandit));
        Assert.True(lit < Visibility.None);
    }

    [Fact]
    public void SettleTheGround_FirmsBrokenGround_OrFoulsFirmGround_AGradeEitherWay()
    {
        var elthric = CombatantFactory.FromCharacter(SampleEncounters.ElthricDane(Data), new GridPosition(5, 2));
        var (encounter, _, _) = Night(elthric, SampleEncounters.SignpostNight with { Visibility = Visibility.Good, Cause = null });
        var settle = Spell(elthric, "Settle the Ground");

        // SignpostNight's ground is Broken over (6, 0)–(9, 1).
        Assert.Equal(Terrain.Broken, encounter.GroundAt(new GridPosition(6, 0)));
        Assert.True(encounter.TryCast(new SpellCast(settle) { Corner = new GridPosition(6, 0) }), string.Join("\n", encounter.Log));
        Assert.Equal(Terrain.Firm, encounter.GroundAt(new GridPosition(6, 0)));
        Assert.Equal(Terrain.Firm, encounter.GroundAt(new GridPosition(7, 1)));
        Assert.Equal(Terrain.Broken, encounter.GroundAt(new GridPosition(8, 0)));

        // A second casting of the caster's own takes the place of the first: they don't stack.
        Assert.True(encounter.TryBuyExtraAction());
        Assert.True(encounter.TryCast(new SpellCast(settle) { Corner = new GridPosition(4, 4), GroundShift = -1 }));
        Assert.Equal(Terrain.Broken, encounter.GroundAt(new GridPosition(4, 4)));
        Assert.Equal(Terrain.Broken, encounter.GroundAt(new GridPosition(6, 0)));
        Assert.Single(encounter.Workings);
    }

    [Fact]
    public void AUtilitySpell_ThatMovesNoLever_HasNothingToDoInAFight()
    {
        Assert.Null(SpellArea.LeverOf(Data.Spell("Purify Water")));
        Assert.Null(SpellArea.LeverOf(Data.Spell("Farseeing")));
        Assert.Equal(Lever.Light, SpellArea.LeverOf(SampleEncounters.ShrineLight));
        Assert.Equal(Lever.Ground, SpellArea.LeverOf(SampleEncounters.SettleTheGround));
    }

    [Fact]
    public void HeldWarmth_EasesTheWeather_ForWhoeverStandsInIt()
    {
        var warmth = SampleEncounters.ShrineLight;
        warmth.Name = "Hearth-Warmth";
        warmth.Notes = "A held warmth against the cold.";
        Assert.Equal(Lever.Exposure, SpellArea.LeverOf(warmth));

        var build = SampleEncounters.MirellaVask(Data);
        build.Spells.Add(warmth);
        var mirella = CombatantFactory.FromCharacter(build, new GridPosition(6, 4));
        var (encounter, _, bandit) = Night(mirella, Setting.Daylight with { Exposure = Exposure.Harsh });
        var before = mirella.ResolveThreshold;

        Assert.True(encounter.TryCast(new SpellCast(warmth) { Corner = new GridPosition(6, 4) }), string.Join("\n", encounter.Log));

        Assert.Equal(Exposure.Normal, encounter.ExposureFor(mirella));
        Assert.Equal(Exposure.Harsh, encounter.ExposureFor(bandit));
        Assert.True(mirella.ResolveThreshold > before);
    }
}
