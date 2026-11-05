using WoundBound.Game.Combat;
using WoundBound.Rules.Calculation;
using WoundBound.Rules.Catalog;
using WoundBound.Rules.Enums;
using WoundBound.Rules.Models;

namespace WoundBound.Game.Tests;

/// <summary>Core Rules §10, "Fortitude Threshold": poisoned blows, lines rolled against FT, and what has no living body.</summary>
public class FortitudeTests
{
    private static readonly Game.Data.GameData Data = TestData.Load();

    private const string Venom = "poison vs FT, 1d4 Body (Direct)";

    private static Encounter Fight(int[] dice, params Combatant[] combatants)
    {
        var encounter = new Encounter(SampleEncounters.FieldWidth, SampleEncounters.FieldHeight, combatants, Data.RangeBands, new ScriptedDice(dice));
        encounter.DeclineFreeChoices();
        return encounter;
    }

    private static Combatant Bandit(StartingAdversaryInfo info, int x, int y) =>
        CombatantFactory.FromAdversary(info with { MoreAttacks = null }, "Bandit", new GridPosition(x, y));

    private static Combatant Salla(int x, int y)
    {
        var salla = CombatantFactory.FromCharacter(SampleEncounters.SallaRinehart(Data), new GridPosition(x, y));
        salla.Stamina = 1; // awake, and nothing a panic dodge could turn at 22 and up
        return salla;
    }

    /// <summary>Salla takes her turn doing nothing; the bandit beside her then swings, rolling 11 and 11.</summary>
    private static Encounter BanditSwingsAt(Combatant salla, Combatant bandit, Action? first = null)
    {
        var encounter = Fight([11, 11, 3, 2, 1, 1, 1, 1, 1, 1], salla, bandit);
        encounter.TryBeginTurn(salla);
        encounter.EndTurn();
        first?.Invoke();
        encounter.RunAdversaryTurn();
        return encounter;
    }

    [Fact]
    public void ACharacter_HasFortitude11PlusTierPlusVitality()
    {
        var build = SampleEncounters.SallaRinehart(Data);
        var salla = CombatantFactory.FromCharacter(build, new GridPosition(1, 1));

        Assert.Equal(11 + 1 + build.GetModifier(Ability.Vitality), salla.FortitudeThreshold);
    }

    [Theory]
    [InlineData("Grave-Bound Husk")]
    [InlineData("Stoneheart Golem")]
    public void NoLivingBody_OrBoundNotAlive_IsFortitudeImmune(string name)
    {
        var dead = CombatantFactory.FromAdversary(Data.Adversary(name), name, new GridPosition(1, 1));

        Assert.Null(dead.FortitudeThreshold);
    }

    [Fact]
    public void ThePoisonIsReadFromTheAttackLine()
    {
        var poison = CombatantFactory.PoisonIn("1d6 Body; " + Venom);

        Assert.Equal(new Poison(1, 4, 0, WoundType.Body), poison);
        Assert.Null(CombatantFactory.PoisonIn("may attempt Topple (ST) as a manoeuvre"));
    }

    [Fact]
    public void APoisonedHit_ThatMeetsFortitude_TakesHold_DirectOnTopOfTheWeapon()
    {
        var salla = Salla(2, 4);
        var bandit = Bandit(TestBandits.Roadside with { AttackEffect = Venom }, 3, 4);
        var before = salla.Body;

        var encounter = BanditSwingsAt(salla, bandit);

        Assert.Contains(encounter.Log, l => l.Contains("it takes hold, 2 Body (Direct)"));
        Assert.True(before - salla.Body >= 2);
    }

    [Fact]
    public void APoisonedHit_ShortOfFortitude_IsThrownOff()
    {
        var salla = Salla(2, 4);
        // A Blessing on her blood: only a Buff or Bane moves Fortitude (Core Rules §10).
        var bandit = Bandit(TestBandits.Roadside with { AttackEffect = Venom }, 3, 4);

        var encounter = BanditSwingsAt(salla, bandit,
            () => salla.ThresholdModifiers.Add(new ThresholdModifier { Threshold = ThresholdType.Fortitude, Amount = 20, Source = "Blessing" }));

        Assert.Contains(encounter.Log, l => l.Contains("their body throws it off"));
        Assert.DoesNotContain(encounter.Log, l => l.Contains("takes hold"));
    }

    [Fact]
    public void APoisonedHit_ArmourTookWhole_DeliversNothing()
    {
        var salla = Salla(2, 4);
        var bandit = Bandit(TestBandits.Roadside with { AttackEffect = Venom, DamageDie = "d4", DamageModifier = -10 }, 3, 4);

        var encounter = BanditSwingsAt(salla, bandit);

        Assert.DoesNotContain(encounter.Log, l => l.Contains("takes hold"));
    }

    [Fact]
    public void ALineAgainstFortitude_IsRolledAgainstFT_AndRefusedOnTheBodiless()
    {
        var spores = TestBandits.Roadside with
        {
            AttackName = "Spore Cloud",
            AttackVsThreshold = ThresholdType.Fortitude,
            DamageIsDirect = true,
        };
        var spitter = Bandit(spores, 3, 4);
        var salla = Salla(2, 4);
        var husk = CombatantFactory.FromAdversary(Data.Adversary("Grave-Bound Husk"), "Husk", new GridPosition(4, 4));
        var encounter = Fight([], salla, spitter, husk);

        var plan = encounter.PlanAttack(spitter, salla);
        Assert.True(plan.CanAttack);
        Assert.Equal(salla.FortitudeThreshold, plan.DodgeThreshold);
        Assert.False(encounter.PlanAttack(spitter, husk).CanAttack);
    }
}
