using WoundBound.Game.Combat;
using WoundBound.Rules.Enums;

namespace WoundBound.Game.Tests;

public class LoadoutAndRangeTests
{
    private static readonly Game.Data.GameData Data = TestData.Load();

    private static (Encounter Encounter, Combatant Fighter, Combatant Biter, Combatant Sneak) Fight(params int[] dice) => Stage(true, dice);

    /// <summary>The same stage with the ratkin played as Brutes, one to a turn: as Minions they act together as a mob (Core Rules §9).</summary>
    private static (Encounter Encounter, Combatant Fighter, Combatant Biter, Combatant Sneak) Crowd(params int[] dice) => Stage(false, dice);

    private static (Encounter Encounter, Combatant Fighter, Combatant Biter, Combatant Sneak) Stage(bool asMinions, int[] dice)
    {
        var encounter = SampleEncounters.FighterVersusRatkin(Data, new ScriptedDice(dice), asMinions: asMinions);
        return (encounter,
            encounter.Combatants.Single(c => c.Side == Side.Party),
            encounter.Combatants.Single(c => c.Name == "Ratkin Biter"),
            encounter.Combatants.Single(c => c.Name == "Ratkin Sneak"));
    }

    [Fact]
    public void TheFighter_StartsWithTheLongswordInHand_AndTheBowAtTheReady()
    {
        var (_, fighter, _, _) = Fight();

        Assert.Equal("Longsword", fighter.InHandWeapon?.Name);
        Assert.Equal("Short Bow", fighter.ReadyWeapon?.Name);
        Assert.False(fighter.Attack.IsRanged);
    }

    [Fact]
    public void Swapping_CostsTheMove_KeepsTheAction_AndGivesTheGmNothing()
    {
        var (encounter, fighter, _, _) = Fight();

        Assert.True(encounter.TrySwapLoadout());

        Assert.False(fighter.HasMove);
        Assert.Equal(1, fighter.ActionsRemaining);
        Assert.Equal(0, encounter.GmOpportunities);
        Assert.Equal(0, fighter.TacticalSpendsThisRound);

        // A Martial character aims anything with Strength (Tier 1 + 3); the bow's Finesse grip
        // decides its damage, Agility +2 (Core Rules §9, "Attack Roll", "Weapons").
        Assert.Equal("Short Bow", fighter.InHandWeapon?.Name);
        Assert.Equal("Longsword", fighter.ReadyWeapon?.Name);
        Assert.True(fighter.Attack.IsRanged);
        Assert.Equal(4, fighter.Attack.AttackModifier);
        Assert.Equal(2, fighter.Attack.DamageModifier);
        Assert.Equal(6, fighter.Attack.DamageDieSides);
    }

    [Fact]
    public void Swapping_AfterMoving_IsPaidForWithTheAction()
    {
        var (encounter, fighter, _, _) = Fight();
        Assert.True(encounter.TryMove(new GridPosition(3, 4)));

        Assert.True(encounter.TrySwapLoadout());

        Assert.Equal(0, fighter.ActionsRemaining);
    }

    [Fact]
    public void Swapping_WithNothingLeftToPayWith_IsRefused()
    {
        var (encounter, fighter, _, _) = Fight();
        Assert.True(encounter.TryMove(new GridPosition(6, 4))); // Middle: the move and the action

        Assert.NotNull(Encounter.SwapRefusal(fighter));
        Assert.False(encounter.TrySwapLoadout());
        Assert.Equal("Longsword", fighter.InHandWeapon?.Name);
    }

    [Fact]
    public void AShotAtMiddleRange_RaisesTheDodgeThreshold_AndShrinksTheDie()
    {
        // Six squares is Middle: DT +2, and a Medium weapon's d6 falls to a d4 (Core Rules §9).
        // 8+5 +4 = 17 vs 13 hits; d4 = 3, +2 Agility.
        var (encounter, fighter, biter, _) = Fight(8, 5, 3);
        encounter.TrySwapLoadout();

        var plan = encounter.PlanAttack(fighter, biter);
        Assert.Equal(13, plan.DodgeThreshold);
        Assert.Equal(4, plan.DamageDieSides);

        var outcome = encounter.TryAttack(biter)!;
        Assert.Equal(5, outcome.RolledDamage);
        Assert.True(biter.IsOutOfFight);
    }

    [Fact]
    public void AShotAtShortRange_IsAtTheTargetsOwnDodgeThreshold_WithTheFullDie()
    {
        var (encounter, fighter, biter, _) = Fight();
        encounter.TrySwapLoadout();
        fighter.Position = new GridPosition(4, 2); // three squares: Short

        var plan = encounter.PlanAttack(fighter, biter);

        Assert.Equal(11, plan.DodgeThreshold);
        Assert.Equal(6, plan.DamageDieSides);
    }

    [Fact]
    public void ABow_CantBeLoosedAtCloseRange()
    {
        var (encounter, fighter, biter, _) = Fight();
        encounter.TrySwapLoadout();
        fighter.Position = new GridPosition(6, 2);

        Assert.False(encounter.PlanAttack(fighter, biter).CanAttack);
        Assert.Null(encounter.TryAttack(biter));
    }

    [Fact]
    public void ACriticalAtRange_MaxesTheShrunkenDie_ButAddsTheFullBaseDie()
    {
        // 6+6 critical at Middle: the d4 maxed (4), plus a bonus d6 (5), plus Agility 2 (Core Rules §9, "Critical hits at range").
        var (encounter, fighter, biter, _) = Fight(6, 6, 5);
        encounter.TrySwapLoadout();

        var outcome = encounter.TryAttack(biter)!;

        Assert.True(outcome.Roll.IsCritical);
        Assert.Equal(11, outcome.RolledDamage);
    }

    [Fact]
    public void ALongsword_ReachesOneSquare()
    {
        var (encounter, fighter, biter, _) = Fight();
        fighter.Position = new GridPosition(5, 2);

        Assert.False(encounter.PlanAttack(fighter, biter).CanAttack);

        fighter.Position = new GridPosition(6, 3);
        Assert.True(encounter.PlanAttack(fighter, biter).CanAttack);
    }

    [Fact]
    public void AnUnwieldyHalberd_ReachesTwo_ButCantStrikeAnAdjacentFoe()
    {
        var build = SampleEncounters.DumbFighter(Data);
        build.Weapons.Clear();
        build.Weapons.Add(CombatantFactory.InHand(Data.Weapon("Halberd")));
        var halberdier = CombatantFactory.FromCharacter(build, new GridPosition(0, 0));
        var ratkin = CombatantFactory.FromAdversary(Data.Adversary("Bramble Ratkin"), "Ratkin", new GridPosition(1, 0));
        var encounter = new Encounter(6, 6, [halberdier, ratkin], Data.RangeBands, new ScriptedDice());

        Assert.Equal(2, halberdier.Reach);
        Assert.False(encounter.PlanAttack(halberdier, ratkin).CanAttack);

        ratkin.Position = new GridPosition(2, 0);
        Assert.True(encounter.PlanAttack(halberdier, ratkin).CanAttack);
    }

    [Fact]
    public void TheOpeningExchange_ShootFromRange_ThenSwapToTheSwordWhenTheyClose()
    {
        // Round 1: no ratkin can be reached on foot with an action to spare, so the fighter swaps
        // to the bow (his move) and shoots the Sneak (8+5 +4 = 17 vs DT 13; d4 = 3, +2).
        var (encounter, fighter, _, sneak) = Crowd(8, 5, 3, 7, 6, 2);
        Assert.True(encounter.TrySwapLoadout());
        encounter.TryAttack(sneak);
        Assert.True(sneak.IsOutOfFight);
        encounter.EndTurn();

        // The GM's one turn: the nearest ratkin is six squares out — a Middle dash that spends its
        // move and its action — so it closes to beside him but has nothing left to bite with.
        encounter.RunAdversaryTurn();
        var closer = encounter.Fighting(Side.Adversaries).Single(r => r.Position.DistanceTo(fighter.Position) == 1);
        Assert.Equal(fighter.BodyMax, fighter.Body);

        // Round 2: it's at Close, where the bow can't be loosed, so he swaps back for the sword
        // and cuts it down (7+6 +4 = 17 vs DT 11; d6 = 2, +3).
        Assert.Equal(2, encounter.Round);
        Assert.False(encounter.CanAttack(fighter, closer));
        Assert.True(encounter.TrySwapLoadout());
        Assert.NotNull(encounter.TryAttack(closer));
        Assert.True(closer.IsOutOfFight);
    }
}
