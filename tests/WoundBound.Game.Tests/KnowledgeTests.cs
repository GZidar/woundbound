using WoundBound.Game.Combat;
using WoundBound.Game.Combat.Brains.Utility;
using WoundBound.Rules.Enums;

namespace WoundBound.Game.Tests;

/// <summary>
/// The GM knowledge dial (the user's direction, 2026-11-02 and 2026-11-03): a brain reads the
/// other side's sheets, or knows only what it has seen — Thresholds narrowed by the rolls made
/// against them, casters once they cast, and the rest guessed from the look of them.
/// </summary>
public class KnowledgeTests
{
    private static readonly Game.Data.GameData Data = TestData.Load();

    /// <summary>The Dumb Fighter, bow in hand, eight squares from the Biter.</summary>
    private static (Encounter Encounter, Combatant Fighter, Combatant Biter) Board(params int[] dice)
    {
        var fighter = CombatantFactory.FromCharacter(SampleEncounters.DumbFighter(Data), new GridPosition(2, 4));
        var biter = CombatantFactory.FromAdversary(Data.Adversary("Bramble Ratkin"), "Ratkin Biter", new GridPosition(6, 4));
        var encounter = new Encounter(SampleEncounters.BoardWidth, SampleEncounters.BoardHeight, new[] { fighter, biter },
            Data.RangeBands, new ScriptedDice(dice), environment: Data.Environment);
        encounter.TrySwapLoadout();
        return (encounter, fighter, biter);
    }

    [Fact]
    public void NothingRolled_NothingSeen()
    {
        var (encounter, _, biter) = Board();

        Assert.Null(encounter.SeenBounds(biter, ThresholdType.Dodge));
    }

    [Fact]
    public void AMiss_SaysTheSheetIsHigher_AndAHit_SaysItIsNoHigher()
    {
        var (encounter, fighter, biter) = Board(2, 3);
        var plan = encounter.PlanAttack(fighter, biter);
        var declared = plan.DodgeThreshold - biter.BaseDodgeThreshold;

        var outcome = encounter.TryAttack(biter)!;

        Assert.False(outcome.Roll.Success);
        var (min, max) = encounter.SeenBounds(biter, ThresholdType.Dodge)!.Value;
        Assert.Equal(outcome.Roll.Total + 1 - declared, min);
        Assert.Equal(int.MaxValue, max);
        Assert.InRange(biter.BaseDodgeThreshold, min, max);
    }

    [Fact]
    public void ABrainThatReadsTheSheets_SeesTheTruth_OneThatHasnt_Guesses()
    {
        var (encounter, _, biter) = Board();
        var sheets = new Assessor(encounter.View, Knowledge.Sheets, Side.Party);
        var seen = new Assessor(encounter.View, Knowledge.Seen, Side.Party);

        Assert.Equal(biter.DodgeThreshold, sheets.Dodge(biter));
        Assert.Equal(seen.Guess(biter, ThresholdType.Dodge) + biter.DodgeThreshold - biter.BaseDodgeThreshold, seen.Dodge(biter));
    }

    [Fact]
    public void TheGuess_IsHeldInsideWhatTheRollsHaveShown()
    {
        // A hit low enough to pin the guess down under the middling guess.
        var (encounter, fighter, biter) = Board(1, 2);
        var seen = new Assessor(encounter.View, Knowledge.Seen, Side.Party);
        var before = seen.Guess(biter, ThresholdType.Dodge);

        var outcome = encounter.TryAttack(biter)!;

        if (outcome.Roll.Success == true && !outcome.Roll.IsCritical)
        {
            var (_, max) = encounter.SeenBounds(biter, ThresholdType.Dodge)!.Value;
            Assert.True(seen.Guess(biter, ThresholdType.Dodge) <= max);
        }
        else
        {
            var (min, _) = encounter.SeenBounds(biter, ThresholdType.Dodge)!.Value;
            Assert.Equal(Math.Max(before, min), seen.Guess(biter, ThresholdType.Dodge));
        }
    }

    [Fact]
    public void ReadingTheSheets_KnowsACasterBeforeTheyCast()
    {
        var encounter = SampleEncounters.SignpostWithCasters(Data, new ScriptedDice());
        var mirella = encounter.Combatants.Single(c => c.Name == "Mirella Vask");
        Assert.False(mirella.ShowsAsCaster);

        Assert.True(new Assessor(encounter.View, Knowledge.Sheets, Side.Adversaries).KnownCaster(mirella));
        Assert.False(new Assessor(encounter.View, Knowledge.Seen, Side.Adversaries).KnownCaster(mirella));
    }

    [Fact]
    public void HeavyArmour_LooksLikeAFighter_LittleOrNone_LikeACaster()
    {
        var encounter = SampleEncounters.SignpostWithCasters(Data, new ScriptedDice());
        var mirella = encounter.Combatants.Single(c => c.Name == "Mirella Vask");
        var drovan = encounter.Combatants.Single(c => c.Name == "Drovan Thale");
        var seen = new Assessor(encounter.View, Knowledge.Seen, Side.Adversaries);
        mirella.Armour = ArmourWeight.None;
        drovan.Armour = ArmourWeight.Heavy;

        Assert.True(seen.SuspectedCaster(mirella) > seen.SuspectedCaster(drovan));
        Assert.True(seen.SuspectedCaster(drovan) < 0.1);
        Assert.Equal(0, new Assessor(encounter.View, Knowledge.Sheets, Side.Adversaries).SuspectedCaster(mirella));

        // Once she casts there's nothing left to suspect: she's known.
        mirella.HasCastSpell = true;
        Assert.Equal(0, seen.SuspectedCaster(mirella));
        Assert.True(seen.KnownCaster(mirella));
    }

    [Fact]
    public void TheDial_GoesWithTheSkill_UnlessNamed()
    {
        Assert.Equal(Knowledge.Sheets, new Combat.Brains.Utility.UtilityGmBrain(BrainSkill.Best).Knowledge);
        Assert.Equal(Knowledge.Seen, new Combat.Brains.Utility.UtilityGmBrain(BrainSkill.Balanced).Knowledge);
        Assert.Equal(Knowledge.Sheets, new Combat.Brains.Utility.UtilityGmBrain(BrainSkill.Poor, knowledge: Knowledge.Sheets).Knowledge);
    }
}

/// <summary>Morale's breaking points by whose fall counts: Not a Fanatic's "second bandit" means a second ally (the user's ruling, 2026-11-03).</summary>
public class MoraleCountTests
{
    private static readonly Game.Data.GameData Data = TestData.Load();

    [Fact]
    public void NotAFanatic_CountsAnyAlly_AndAKinLine_OnlyItsKind()
    {
        var fighter = CombatantFactory.FromCharacter(SampleEncounters.DumbFighter(Data), new GridPosition(1, 1));
        var tollroad = CombatantFactory.FromAdversary(Data.Adversary("Tollroad Bandit"), "Tollroad Bandit", new GridPosition(8, 1));
        var ratkin = Enumerable.Range(0, 2).Select(i => CombatantFactory.FromAdversary(Data.Adversary("Bramble Ratkin"), $"Ratkin {i}", new GridPosition(8, 3 + i))).ToArray();
        var encounter = new Encounter(SampleEncounters.FieldWidth, SampleEncounters.FieldHeight, [fighter, tollroad, .. ratkin], Data.RangeBands, new ScriptedDice());

        foreach (var r in ratkin)
        {
            r.Body = 0;
        }

        Assert.Equal(2, encounter.View.AlliesDown(tollroad));
        Assert.True(tollroad.Morale!.IsBroken(tollroad, encounter.View.AlliesDown(tollroad)));

        var kinOnly = Morale.NotAFanatic with { Kin = "Bandit" };
        Assert.False(kinOnly.Counts(ratkin[0]));
        Assert.True(kinOnly.Counts(tollroad));
    }
}
