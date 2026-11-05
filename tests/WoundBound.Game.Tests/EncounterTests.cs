using WoundBound.Game.Combat;
using WoundBound.Rules.Enums;

namespace WoundBound.Game.Tests;

public class EncounterTests
{
    private static readonly Game.Data.GameData Data = TestData.Load();

    private static (Encounter Encounter, Combatant Fighter, List<Combatant> Ratkin) Fight(params int[] dice) => Stage(true, dice);

    /// <summary>The same stage with the ratkin played as Brutes, one to a turn: as Minions they act together as a mob (Core Rules §9).</summary>
    private static (Encounter Encounter, Combatant Fighter, List<Combatant> Ratkin) Crowd(params int[] dice) => Stage(false, dice);

    private static (Encounter Encounter, Combatant Fighter, List<Combatant> Ratkin) Stage(bool asMinions, int[] dice)
    {
        var encounter = SampleEncounters.FighterVersusRatkin(Data, new ScriptedDice(dice), asMinions: asMinions);
        var fighter = encounter.Combatants.Single(c => c.Side == Side.Party);
        var ratkin = encounter.Combatants.Where(c => c.Side == Side.Adversaries).ToList();
        return (encounter, fighter, ratkin);
    }

    /// <summary>Stages the fighter beside a ratkin, with the turn's move and action both still unspent.</summary>
    private static Combatant StandBeside(Combatant fighter, Combatant ratkin)
    {
        fighter.Position = new GridPosition(ratkin.Position.X - 1, ratkin.Position.Y);
        return ratkin;
    }

    [Fact]
    public void DumbFighter_FightsWithTheNumbersTheRulesDerive()
    {
        var (_, fighter, _) = Fight();

        // Core Rules §12: Body 7. DT = 11 + Tier 1 + Agility 2. A Martial character attacks with
        // Strength: Tier 1 + Strength 3. A longsword is Medium (d6) and Power, so +Strength damage.
        Assert.Equal(7, fighter.BodyMax);
        Assert.Equal(14, fighter.DodgeThreshold);
        Assert.Equal(4, fighter.Attack.AttackModifier);
        Assert.Equal(6, fighter.Attack.DamageDieSides);
        Assert.Equal(3, fighter.Attack.DamageModifier);
        Assert.Equal(ArmourWeight.Light, fighter.Armour);
        Assert.Equal(1, fighter.Reach);
        Assert.Equal(1, fighter.TacticalSpendsPerRound);
    }

    [Fact]
    public void Ratkin_ComeFromTheStartingAdversariesStatblock()
    {
        var (_, _, ratkin) = Fight();

        Assert.Equal(3, ratkin.Count);
        Assert.All(ratkin, r =>
        {
            Assert.True(r.IsMinion);
            Assert.Equal(11, r.DodgeThreshold);
            Assert.Equal(4, r.Attack.DamageDieSides);
        });
    }

    [Fact]
    public void AMove_UpToShortCostsOneMove_AndFurtherSpendsTheAction()
    {
        var (encounter, fighter, _) = Fight();

        // At Normal Speed, Short (up to 4 squares) is one move; Middle (5–8) is two (Core Rules §9).
        Assert.Equal(1, encounter.MoveCostTo(fighter, new GridPosition(5, 4)));
        Assert.Equal(2, encounter.MoveCostTo(fighter, new GridPosition(6, 4)));

        Assert.True(encounter.TryMove(new GridPosition(6, 4)));
        Assert.False(fighter.HasMove);
        Assert.Equal(0, fighter.ActionsRemaining);
    }

    [Fact]
    public void AHit_ThatGetsPastArmour_RemovesAMinionOutright()
    {
        // 6+5 +4 = 15 vs DT 11; damage d6 = 1, +3.
        var (encounter, fighter, ratkin) = Fight(6, 5, 1);
        var target = StandBeside(fighter, ratkin.Single(r => r.Name == "Ratkin Sneak"));

        var outcome = encounter.TryAttack(target)!;

        Assert.True(outcome.Roll.Success);
        Assert.Equal(4, outcome.Dealt);
        Assert.True(target.IsOutOfFight);
        Assert.Equal(0, target.Body);
    }

    [Fact]
    public void AMiss_DealsNothing()
    {
        // 2+3 +4 = 9 vs DT 11.
        var (encounter, fighter, ratkin) = Fight(2, 3);
        var target = StandBeside(fighter, ratkin[1]);

        var outcome = encounter.TryAttack(target)!;

        Assert.False(outcome.Roll.Success);
        Assert.Equal(0, outcome.Dealt);
        Assert.False(target.IsOutOfFight);
    }

    [Fact]
    public void ACriticalHit_DealsTheMaximumPlusAnExtraDie()
    {
        // 5+5 is critical and 14 beats DT 11: max d6 (6) + extra d6 (2) + 3 = 11.
        var (encounter, fighter, ratkin) = Fight(5, 5, 2);
        var target = StandBeside(fighter, ratkin[1]);

        var outcome = encounter.TryAttack(target)!;

        Assert.True(outcome.Roll.IsCritical);
        Assert.Equal(11, outcome.RolledDamage);
    }

    [Fact]
    public void ACriticalMiss_WoundsTheAttacker()
    {
        // A double one is an absolute failure, and a critical one (Core Rules §10).
        var (encounter, fighter, ratkin) = Fight(1, 1);
        var target = StandBeside(fighter, ratkin[1]);

        var outcome = encounter.TryAttack(target)!;

        Assert.True(outcome.AttackerWounded);
        Assert.Equal(6, fighter.Body);
    }

    [Fact]
    public void ARatkinBite_IsReducedByTheFightersLightArmour()
    {
        // Fighter ends turn without acting. A ratkin closes and bites: 10+7 +0 = 17 vs DT 14 — too
        // high for a panic dodge to turn — d4 = 3, and light armour absorbs 1 (Core Rules §9, "Armour").
        var (encounter, fighter, _) = Fight(10, 7, 3);
        Assert.True(encounter.TryMove(new GridPosition(4, 4)));
        encounter.EndTurn();

        Assert.Equal(EncounterPhase.AdversaryTurn, encounter.Phase);
        encounter.RunAdversaryTurn();

        Assert.Equal(5, fighter.Body);
    }

    [Fact]
    public void Minions_ActTogetherAsAMob_InOneOfTheCapsTurns()
    {
        // Three ratkin Minions, one fighter: the cap is one turn, and the mob takes it — every
        // ratkin acts in it, one after another (Core Rules §9, "Minions act as a mob"). The
        // fighter stays out of reach, so they only move; nobody is left out to close in.
        var (encounter, _, ratkin) = Fight();
        var before = ratkin.Select(r => r.Position).ToList();
        encounter.EndTurn();

        while (encounter.Phase == EncounterPhase.AdversaryTurn)
        {
            encounter.RunAdversaryTurn();
        }

        Assert.Equal(1, encounter.AdversaryTurnsPerRound);
        Assert.Equal(3, encounter.Log.Count(line => line.StartsWith("Ratkin") && line.Contains(" moves ")));
        Assert.All(ratkin.Select(r => r.Position).Zip(before), p => Assert.NotEqual(p.Second, p.First));
        Assert.DoesNotContain(encounter.Log, line => line.Contains("closes in"));
        Assert.Equal(2, encounter.Round);
    }

    [Fact]
    public void TheMob_KeepsActing_AfterItHasSpentTheCapsOneTurn()
    {
        // The first ratkin spends the round's only turn; the other two still act in it.
        var (encounter, _, ratkin) = Fight();
        encounter.EndTurn();

        encounter.RunAdversaryTurn();

        Assert.Equal(0, encounter.AdversaryTurnsLeft);
        Assert.Equal(EncounterPhase.AdversaryTurn, encounter.Phase);
        Assert.Equal(2, ratkin.Count(encounter.MayTakeOwedTurn));
    }

    [Fact]
    public void TheGm_TakesOneAdversaryTurnPerCharacter_ThenANewRoundBegins()
    {
        // Three ratkin, one fighter: one adversary turn a round (Core Rules §9). The fighter stays
        // out of reach, so the ratkin only move, and no dice are rolled. The two left out of the
        // round's turns close in at its end.
        var (encounter, _, ratkin) = Crowd();
        var before = ratkin.Select(r => r.Position).ToList();
        encounter.EndTurn();

        encounter.RunAdversaryTurn();

        Assert.Equal(1, encounter.AdversaryTurnsPerRound);
        Assert.Single(encounter.Log, line => line.StartsWith("Ratkin") && line.Contains(" moves "));
        Assert.All(ratkin.Select(r => r.Position).Zip(before), p => Assert.NotEqual(p.Second, p.First));
        Assert.Equal(2, encounter.Log.Count(line => line.Contains("closes in")));
        Assert.Equal(2, encounter.Round);
        Assert.Equal(EncounterPhase.PartyTurn, encounter.Phase);
    }

    [Fact]
    public void BuyingAnExtraAction_CostsStamina_FeedsTheGm_AndIsLimitedByTier()
    {
        var (encounter, fighter, _) = Fight();
        var stamina = fighter.Stamina;

        Assert.True(encounter.TryBuyExtraAction());
        Assert.Equal(stamina - 1, fighter.Stamina);
        Assert.Equal(2, fighter.ActionsRemaining);
        Assert.Equal(1, encounter.GmOpportunities);

        // T1 allows one tactical spend a round (Core Rules §6, "Stamina Limit").
        Assert.NotNull(encounter.ExtraActionRefusal(fighter));
        Assert.False(encounter.TryBuyExtraAction());
    }

    [Fact]
    public void DroppingEveryRatkin_WinsTheFight()
    {
        var (encounter, _, ratkin) = Fight();
        foreach (var r in ratkin)
        {
            r.Body = 0;
        }

        encounter.EndTurn();

        Assert.Equal(EncounterPhase.Victory, encounter.Phase);
    }
}
