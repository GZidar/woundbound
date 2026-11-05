using WoundBound.Game.Combat;
using WoundBound.Rules.Enums;

namespace WoundBound.Game.Tests;

/// <summary>Core Rules §9: a Nimble character's footwork and Exploits, and a Martial character's manoeuvres.</summary>
public class ProwessTests
{
    private static readonly Game.Data.GameData Data = TestData.Load();

    private static Encounter Fight(int[] dice, params Combatant[] combatants) =>
        new(SampleEncounters.FieldWidth, SampleEncounters.FieldHeight, combatants, Data.RangeBands, new ScriptedDice(dice));

    private static Combatant Bandit(string name, int x, int y) =>
        CombatantFactory.FromAdversary(TestBandits.Roadside, name, new GridPosition(x, y));

    [Fact]
    public void Footwork_AStepIsFree_OnceARound_AndALongMoveCostsOneLess()
    {
        var salla = CombatantFactory.FromCharacter(SampleEncounters.SallaRinehart(Data), new GridPosition(2, 4));
        var joren = CombatantFactory.FromCharacter(SampleEncounters.JorenBlacke(Data), new GridPosition(2, 8));
        var encounter = Fight([], salla, joren, Bandit("Bandit", 16, 12));

        encounter.TryBeginTurn(salla);
        Assert.Equal(0, encounter.MoveCostTo(salla, new GridPosition(3, 4)));
        Assert.True(encounter.TryMove(new GridPosition(3, 4)));
        Assert.True(salla.HasMove);
        Assert.Equal(1, salla.ActionsRemaining);

        // The second step this round is paid for like anybody's.
        Assert.Equal(1, encounter.MoveCostTo(salla, new GridPosition(4, 4)));

        // Twelve squares on is Long: three moves for her — the move, the action and one bought — where
        // anyone else at Normal pays four.
        Assert.True(encounter.TryBuyExtraAction());
        Assert.Equal(3, encounter.MoveCostTo(salla, new GridPosition(15, 4)));
        Assert.Equal(4, Rules.Calculation.RangeBandCalculator.MoveCost(Data.RangeBands, 12));
    }

    [Fact]
    public void Slip_LetsThemLeaveReach_WithNoBladeOnTheWayOut_AndNoOpportunityForTheGm()
    {
        var salla = CombatantFactory.FromCharacter(SampleEncounters.SallaRinehart(Data), new GridPosition(2, 4));
        var bandit = Bandit("Bandit", 3, 4);
        var encounter = Fight([], salla, bandit);
        encounter.TryBeginTurn(salla);

        Assert.True(encounter.TryExploit(NimbleExploit.Slip));
        Assert.True(encounter.TryMove(new GridPosition(0, 4)));

        Assert.Null(encounter.Pending);
        Assert.Equal(bandit.StaminaMax, bandit.Stamina);
        Assert.Equal((salla.StaminaMax - 1, 0), (salla.Stamina, encounter.GmOpportunities));
        Assert.DoesNotContain(encounter.Log, l => l.Contains("strikes at"));
    }

    [Fact]
    public void Precise_AfterAMove_GoesPastArmour_AndAddsVitality()
    {
        // Salla steps, buys Precise, and runs the bandit through: 10+9 +4 = 23 against DT 15; her
        // rapier's d6 (4) + 1 Agility... the Rapier's own modifier, plus Vitality +2, and the leather
        // takes nothing.
        var salla = CombatantFactory.FromCharacter(SampleEncounters.SallaRinehart(Data), new GridPosition(1, 4));
        var bandit = Bandit("Bandit", 3, 4);
        bandit.Stamina = 0;
        var encounter = Fight([10, 9, 4], salla, bandit);
        encounter.TryBeginTurn(salla);

        Assert.Contains("needs a move", Encounter.ExploitRefusal(salla, NimbleExploit.Precise));
        Assert.True(encounter.TryMove(new GridPosition(2, 4)));
        Assert.True(encounter.TryExploit(NimbleExploit.Precise));
        var outcome = encounter.TryAttack(bandit)!;

        Assert.Equal(0, outcome.Absorbed);
        Assert.Equal(4 + salla.Attack.DamageModifier + 2, outcome.Dealt);
        Assert.False(salla.HoldsPrecise);
    }

    [Fact]
    public void Patient_LeftAlone_TheNextAttackHasAdvantage()
    {
        // 1, 9, 8 keeps 9+8 +4 = 21; 3 on the d6.
        var salla = CombatantFactory.FromCharacter(SampleEncounters.SallaRinehart(Data), new GridPosition(2, 4));
        var bandit = Bandit("Bandit", 3, 4);
        bandit.Stamina = 0;
        var encounter = Fight([1, 9, 8, 3], salla, bandit);
        encounter.TryBeginTurn(salla);

        Assert.True(encounter.TryExploit(NimbleExploit.Patient));
        var outcome = encounter.TryAttack(bandit)!;

        Assert.True(outcome.Roll.AppliedAdvantage);
        Assert.True(outcome.Roll.Success);
    }

    [Fact]
    public void Cleave_OneRoll_TwoDodgeThresholds_TheSameDamageOnBoth()
    {
        // Joren's battle axe (Cutting, Power) between two bandits: 10+9 +4 = 23 against both DT 15s;
        // d6 = 4, +3, so 7 — each one's leather takes 1.
        var joren = CombatantFactory.FromCharacter(SampleEncounters.JorenBlacke(Data), new GridPosition(3, 4));
        var left = Bandit("Left bandit", 2, 4);
        var right = Bandit("Right bandit", 4, 4);
        left.Stamina = right.Stamina = 0;
        var encounter = Fight([10, 9, 4], joren, left, right);
        encounter.TryBeginTurn(joren);

        Assert.Contains("doesn't permit Topple", Encounter.ManoeuvreRefusal(joren, left, new Manoeuvre(WeaponManoeuvre.Topple)));
        Assert.NotNull(encounter.TryAttack(left, manoeuvre: new Manoeuvre(WeaponManoeuvre.Cleave, right)));

        Assert.Equal((left.BodyMax - 6, right.BodyMax - 6), (left.Body, right.Body));
        Assert.Equal((joren.StaminaMax - 1, 1), (joren.Stamina, encounter.BankedOpportunities));
    }
}
