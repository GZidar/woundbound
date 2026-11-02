using WoundBound.Game.Combat;
using WoundBound.Game.Combat.Brains;
using WoundBound.Rules.Catalog;
using WoundBound.Rules.Enums;

namespace WoundBound.Game.Tests;

/// <summary>
/// GM Reference §9, "On Opportunity": "The bonus is Advantage unless the statblock names something
/// else - Cleave, Precise, Direct damage, a Buff for an ally, extra movement, a Threshold boost."
/// </summary>
public class OpportunityBonusTests
{
    private static readonly Game.Data.GameData Data = TestData.Load();

    /// <summary>A GM that takes whatever steps a test scripts, then ends the turn, and never buys extra turns.</summary>
    private sealed class ScriptedGm : SimpleGmBrain
    {
        public Func<BattleView, GmTurn, GmStep?>? Step { get; init; }

        public override GmStep NextStep(BattleView view, GmTurn turn) => Step?.Invoke(view, turn) ?? GmStep.End;

        public override Combatant? ChooseExtraTurn(BattleView view) => null;

        /// <summary>The adversary to give the owed turn to, where a test wants one in particular.</summary>
        public string? Prefer { get; init; }

        public override Combatant ChooseOwedTurn(BattleView view) =>
            view.YetToAct.FirstOrDefault(a => a.Name == Prefer) ?? base.ChooseOwedTurn(view);

        public override bool PanicDodges(BattleView view, PendingReaction reaction) => false;
    }

    /// <summary>The Dumb Fighter (Light armour) beside a bandit whose Opportunity row reads <paramref name="opportunity"/>, with one Opportunity banked.</summary>
    private static (Encounter Encounter, Combatant Fighter, Combatant Bandit) Fight(string opportunity, IGmBrain gm, params int[] dice)
    {
        var info = SampleEncounters.RoadsideBandit with { OpportunityText = opportunity };
        var encounter = new Encounter(12, 10, new[]
        {
            CombatantFactory.FromCharacter(SampleEncounters.DumbFighter(Data), new GridPosition(4, 4)),
            CombatantFactory.FromAdversary(info, "Bandit", new GridPosition(5, 4)),
        }, Data.RangeBands, new ScriptedDice(dice), gm);
        encounter.BankedOpportunities = 1;
        return (encounter, encounter.Combatants.Single(c => c.Side == Side.Party), encounter.Combatants.Single(c => c.Name == "Bandit"));
    }

    /// <summary>Ends the fighter's turn and runs the GM's, letting every party reaction pass.</summary>
    private static void RunGm(Encounter encounter)
    {
        encounter.EndTurn();
        encounter.RunAdversaryTurn();
        while (encounter.Pending is not null)
        {
            encounter.RespondToReaction(false);
        }
    }

    [Theory]
    [InlineData("Advantage", OpportunityBonusKind.Advantage)]
    [InlineData("", OpportunityBonusKind.Advantage)]
    [InlineData("Direct damage", OpportunityBonusKind.Direct)]
    [InlineData("Precise", OpportunityBonusKind.Precise)]
    [InlineData("Cleave", OpportunityBonusKind.Cleave)]
    [InlineData("Buff an ally (+2 to a chosen Threshold) for no wound cost", OpportunityBonusKind.FreeBuff)]
    [InlineData("Extra movement", OpportunityBonusKind.ExtraMove)]
    [InlineData("A Threshold boost", OpportunityBonusKind.ThresholdBoost)]
    [InlineData("Extra attack once per round, otherwise Advantage", OpportunityBonusKind.ExtraAttack)]
    public void TheStatblockRow_NamesTheBonus(string text, OpportunityBonusKind kind) =>
        Assert.Equal(kind, OpportunityBonus.Parse(text).Kind);

    [Fact]
    public void TheCatalogue_GivesEachCreatureItsBonus()
    {
        Assert.Equal(OpportunityBonusKind.Direct, CombatantFactory.FromAdversary(Data.Adversary("Vampire Bat"), "Bat", new GridPosition(0, 0)).OpportunityBonus.Kind);
        Assert.Equal(OpportunityBonusKind.FreeBuff, CombatantFactory.FromAdversary(Data.Adversary("Wyrding Chanter"), "Chanter", new GridPosition(0, 0), Data.Spells).OpportunityBonus.Kind);
        Assert.Equal(OpportunityBonusKind.ExtraAttack, CombatantFactory.FromAdversary(Data.Adversary("The Ashen Warden"), "Warden", new GridPosition(0, 0)).OpportunityBonus.Kind);
        Assert.Equal(OpportunityBonusKind.Advantage, CombatantFactory.FromAdversary(Data.Adversary("Bramble Ratkin"), "Ratkin", new GridPosition(0, 0)).OpportunityBonus.Kind);
    }

    [Fact]
    public void DirectDamage_GoesThroughArmour_WithoutAdvantage()
    {
        // 11+10 hits (not a double, so no critical); a 4 on the sword's d6. Light armour would take some of it.
        var gm = new ScriptedGm { Step = (view, turn) => turn.HasAttacked ? null : new GmStep.Attack(view.Characters.Single(), WithBonus: true) };
        var (encounter, fighter, bandit) = Fight("Direct damage", gm, 11, 10, 4);

        RunGm(encounter);

        Assert.Contains(encounter.Log, l => l.Contains("bonus: Direct damage"));
        Assert.Equal(fighter.BodyMax - (4 + bandit.Attack.DamageModifier), fighter.Body);
        Assert.Equal(0, encounter.BankedOpportunities);
        Assert.DoesNotContain(encounter.Log, l => l.Contains("(Advantage, dropped"));
    }

    [Fact]
    public void TheDefaultBonus_IsStillAdvantage()
    {
        var gm = new ScriptedGm { Step = (view, turn) => turn.HasAttacked ? null : new GmStep.Attack(view.Characters.Single(), WithBonus: true) };
        var (encounter, _, _) = Fight("Advantage", gm, 11, 10, 3, 4);

        RunGm(encounter);

        Assert.Contains(encounter.Log, l => l.Contains("bonus: Advantage"));
        Assert.Contains(encounter.Log, l => l.Contains("(Advantage, dropped"));
    }

    [Fact]
    public void ADirectBonus_CannotRideOnAGrab()
    {
        var gm = new ScriptedGm { Step = (view, turn) => turn.HasAttacked ? null : new GmStep.Control(ControlAction.Grab, view.Characters.Single(), WithBonus: true) };
        var (encounter, _, _) = Fight("Direct damage", gm);
        encounter.EndTurn();

        Assert.Throws<InvalidOperationException>(encounter.RunAdversaryTurn);
    }

    [Fact]
    public void ACleaveBonus_PaysForACleave_TheCreatureHasNoneOfItsOwn()
    {
        var gm = new ScriptedGm
        {
            Step = (view, turn) => turn.HasAttacked ? null : new GmStep.Attack(view.Characters.First(c => c.Name == "The Dumb Fighter"), WithBonus: true,
                Manoeuvre: new Manoeuvre(WeaponManoeuvre.Cleave, view.Characters.First(c => c.Name == "Joren Blacke"))),
        };
        var info = SampleEncounters.RoadsideBandit with { OpportunityText = "Cleave" };
        var encounter = new Encounter(12, 10, new[]
        {
            CombatantFactory.FromCharacter(SampleEncounters.DumbFighter(Data), new GridPosition(4, 4)),
            CombatantFactory.FromCharacter(SampleEncounters.JorenBlacke(Data), new GridPosition(5, 5)),
            CombatantFactory.FromAdversary(info, "Bandit", new GridPosition(5, 4)),
        }, Data.RangeBands, new ScriptedDice(11, 10, 3, 3), gm);
        encounter.BankedOpportunities = 1;
        var bandit = encounter.Combatants.Single(c => c.Name == "Bandit");
        var stamina = bandit.Stamina;

        Assert.NotNull(Encounter.ManoeuvreRefusal(bandit, encounter.Combatants[0], new Manoeuvre(WeaponManoeuvre.Cleave, encounter.Combatants[1])));
        Assert.Null(Encounter.ManoeuvreRefusal(bandit, encounter.Combatants[0], new Manoeuvre(WeaponManoeuvre.Cleave, encounter.Combatants[1]), byOpportunity: true));

        encounter.TryBeginTurn(encounter.Combatants[0]);
        encounter.EndTurn();
        encounter.TryBeginTurn(encounter.Combatants[1]);
        encounter.EndTurn();
        encounter.RunAdversaryTurn();
        while (encounter.Pending is not null)
        {
            encounter.RespondToReaction(false);
        }

        Assert.Contains(encounter.Log, l => l.Contains("paid by the Opportunity"));
        Assert.Equal(stamina, bandit.Stamina);
        Assert.Equal(0, encounter.BankedOpportunities);
    }

    [Fact]
    public void AnExtraMovementBonus_GivesAnotherMove()
    {
        var gm = new ScriptedGm
        {
            Step = (view, turn) => turn.Steps switch
            {
                0 => new GmStep.Move(new GridPosition(7, 4)),
                1 => new GmStep.BonusMove(),
                2 => new GmStep.Move(new GridPosition(9, 4)),
                _ => null,
            },
        };
        var (encounter, _, bandit) = Fight("Extra movement", gm);
        bandit.Position = new GridPosition(6, 4);

        RunGm(encounter);

        Assert.Equal(new GridPosition(9, 4), bandit.Position);
        Assert.Contains(encounter.Log, l => l.Contains("another move"));
        Assert.Equal(0, encounter.BankedOpportunities);
    }

    [Fact]
    public void AThresholdBoostBonus_RaisesItUntilItsNextTurn()
    {
        var gm = new ScriptedGm { Step = (view, turn) => turn.Steps == 0 ? new GmStep.BoostThreshold(ThresholdType.Dodge) : null };
        var (encounter, _, bandit) = Fight("Threshold boost", gm);
        var before = bandit.DodgeThreshold;

        RunGm(encounter);

        Assert.Equal(before + Encounter.OpportunityThresholdBoost, bandit.DodgeThreshold);
        Assert.Equal(0, encounter.BankedOpportunities);
    }

    [Fact]
    public void ABonusTheStatblockDoesNotName_IsRefused()
    {
        var gm = new ScriptedGm { Step = (view, turn) => turn.Steps == 0 ? new GmStep.BoostThreshold(ThresholdType.Dodge) : null };
        var (encounter, _, _) = Fight("Advantage", gm);
        encounter.EndTurn();

        Assert.Throws<InvalidOperationException>(encounter.RunAdversaryTurn);
    }

    [Fact]
    public void AFreeBuff_CostsTheChanterNoSoul()
    {
        var gm = new ScriptedGm
        {
            Prefer = "Chanter",
            Step = (view, turn) => turn.HasAttacked || turn.Actor.Name != "Chanter" ? null : new GmStep.Cast(new SpellCast(turn.Actor.Spells.Single(s => s.SpellType == SpellType.Buff))
            {
                Target = view.Adversaries.Single(a => a.Name == "Bandit"),
                Threshold = ThresholdType.Dodge,
            }, WithBonus: true),
        };
        var encounter = new Encounter(12, 10, new[]
        {
            CombatantFactory.FromCharacter(SampleEncounters.DumbFighter(Data), new GridPosition(1, 4)),
            CombatantFactory.FromAdversary(SampleEncounters.RoadsideBandit, "Bandit", new GridPosition(6, 4)),
            CombatantFactory.FromAdversary(Data.Adversary("Wyrding Chanter"), "Chanter", new GridPosition(7, 4), Data.Spells),
        }, Data.RangeBands, new ScriptedDice(), gm);
        encounter.BankedOpportunities = 1;
        var chanter = encounter.Combatants.Single(c => c.Name == "Chanter");
        var bandit = encounter.Combatants.Single(c => c.Name == "Bandit");
        var soul = chanter.Soul;
        var dodge = bandit.DodgeThreshold;

        encounter.EndTurn();
        while (encounter.Phase == EncounterPhase.AdversaryTurn && !chanter.HasActedThisRound)
        {
            encounter.RunAdversaryTurn();
            encounter.DeclineFreeChoices();
        }

        Assert.True(bandit.DodgeThreshold == dodge + 2, string.Join("\n", encounter.Log));
        Assert.Equal(soul, chanter.Soul);
        Assert.Equal(dodge + 2, bandit.DodgeThreshold);
        Assert.Contains(encounter.Log, l => l.Contains("the Opportunity pays"));
    }

    [Fact]
    public void TheWardensBonus_IsASecondAttack_BoughtWithTheOpportunityNotStamina()
    {
        var gm = new ScriptedGm
        {
            Step = (view, turn) => turn.HasAttacked ? null : turn.Actor.AttacksThisRound switch
            {
                0 => new GmStep.Attack(view.Characters.Single()),
                1 => new GmStep.Attack(view.Characters.Single(), WithBonus: true),
                _ => null,
            },
        };
        var info = SampleEncounters.HollowStag(Data) with { OpportunityText = "Extra attack once per round, otherwise Advantage" };
        var encounter = new Encounter(12, 10, new[]
        {
            CombatantFactory.FromCharacter(SampleEncounters.DumbFighter(Data), new GridPosition(4, 4)),
            CombatantFactory.FromAdversary(info, "Stag", new GridPosition(5, 4)),
        }, Data.RangeBands, new ScriptedDice(1, 2, 1, 3), gm);
        encounter.BankedOpportunities = 1;
        var stag = encounter.Combatants.Single(c => c.Name == "Stag");
        var stamina = stag.Stamina;

        // A Solo's attack is once a round, so its second comes on a later turn of the same round.
        encounter.EndTurn();
        while (encounter.Phase == EncounterPhase.AdversaryTurn && encounter.Round == 1)
        {
            try { encounter.RunAdversaryTurn(); } catch (Exception e) { Assert.Fail(e.Message + "\n" + string.Join("\n", encounter.Log)); }
            while (encounter.Pending is not null)
            {
                encounter.RespondToReaction(false);
            }
        }

        var round1 = encounter.Log.TakeWhile(l => !l.StartsWith("— Round 2")).ToList();
        Assert.True(round1.Count(l => l.StartsWith("Stag attacks")) == 2, string.Join(System.Environment.NewLine, round1));
        Assert.Equal(stamina, stag.Stamina);
        Assert.Contains(encounter.Log, l => l.Contains("bonus: an extra attack this round"));
        Assert.Equal(0, encounter.BankedOpportunities);
    }
}
