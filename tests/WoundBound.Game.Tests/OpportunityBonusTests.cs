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
        var info = TestBandits.Roadside with { OpportunityText = opportunity };
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
        var info = TestBandits.Roadside with { OpportunityText = "Cleave" };
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
            CombatantFactory.FromAdversary(TestBandits.Roadside, "Bandit", new GridPosition(6, 4)),
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

/// <summary>Core Rules §9, "Mounts": an area attack catches the horse as well as its rider.</summary>
public class AreaAttackMountTests
{
    private static readonly Game.Data.GameData Data = TestData.Load();

    private sealed class Stamper : SimpleGmBrain
    {
        public override GmStep NextStep(BattleView view, GmTurn turn) => turn.HasAttacked
            ? GmStep.End
            : new GmStep.Attack(view.Characters.Single(), With: turn.Actor.Attacks.Single(a => a.AreaSquares is not null));

        public override Combatant? ChooseExtraTurn(BattleView view) => null;
    }

    [Fact]
    public void AStatblockAreaAttack_CatchesTheSteed()
    {
        var rider = CombatantFactory.Mounted(CombatantFactory.FromCharacter(SampleEncounters.SallaRinehart(Data), new GridPosition(4, 4)));
        var encounter = new Encounter(12, 10, new[]
        {
            rider,
            CombatantFactory.FromAdversary(SampleEncounters.HollowStag(Data), "Stag", new GridPosition(5, 4)),
        }, Data.RangeBands, new RandomDiceRoller(new Random(3)), new Stamper());

        encounter.EndTurn();
        encounter.RunAdversaryTurn();
        while (encounter.Pending is not null)
        {
            encounter.RespondToReaction(false);
        }

        Assert.Contains(encounter.Log, l => l.StartsWith("Stag attacks") && l.Contains(rider.Steed!.Name));
    }
}

/// <summary>The brains price a poison carried on a blow (Core Rules §10, "Fortitude Threshold").</summary>
public class PoisonPricingTests
{
    private static readonly Game.Data.GameData Data = TestData.Load();

    [Fact]
    public void APoisonedBlow_IsWorthMore_ExceptAgainstNoLivingBody()
    {
        var fighter = CombatantFactory.FromCharacter(SampleEncounters.DumbFighter(Data), new GridPosition(4, 4));
        var bandit = CombatantFactory.FromAdversary(TestBandits.Roadside, "Bandit", new GridPosition(5, 4));
        var encounter = new Encounter(12, 10, new[] { fighter, bandit }, Data.RangeBands, new ScriptedDice());
        var assess = new Game.Combat.Brains.Utility.Assessor(new BattleView(encounter));
        var plain = bandit.Attack;
        var poisoned = plain with { Poison = new Poison(1, 4, 0, WoundType.Body) };

        var without = assess.Blow(bandit, plain, fighter, plain.DamageDieSides, fighter.DodgeThreshold).ExpectedDealt;
        var with = assess.Blow(bandit, poisoned, fighter, plain.DamageDieSides, fighter.DodgeThreshold).ExpectedDealt;
        Assert.True(with > without, $"{with} vs {without}");

        var husk = CombatantFactory.FromAdversary(Data.Adversary("Grave-Bound Husk"), "Husk", new GridPosition(6, 4));
        Assert.Equal(assess.Blow(bandit, plain, husk, plain.DamageDieSides, husk.DodgeThreshold).ExpectedDealt,
            assess.Blow(bandit, poisoned, husk, plain.DamageDieSides, husk.DodgeThreshold).ExpectedDealt);
    }
}

/// <summary>The off-hand strike: a statblock's is its Opportunity bonus (the user's ruling, 2026-11-03); a two-weapon rival pays Stamina, as a character does (2026-11-05).</summary>
public class OffHandBonusTests
{
    private static readonly Game.Data.GameData Data = TestData.Load();

    private sealed class ScriptedGm(Func<BattleView, GmTurn, GmStep?> step) : SimpleGmBrain
    {
        public override GmStep NextStep(BattleView view, GmTurn turn) => step(view, turn) ?? GmStep.End;

        public override Combatant? ChooseExtraTurn(BattleView view) => null;

        public override bool PanicDodges(BattleView view, PendingReaction reaction) => false;
    }

    private static (Encounter Encounter, Combatant Fighter, Combatant Bandit) Fight(IGmBrain gm, params int[] dice)
    {
        var info = TestBandits.Roadside with { OpportunityText = "Off-hand attack (d4)" };
        var encounter = new Encounter(12, 10, new[]
        {
            CombatantFactory.FromCharacter(SampleEncounters.DumbFighter(Data), new GridPosition(4, 4)),
            CombatantFactory.FromAdversary(info, "Bandit", new GridPosition(5, 4)),
        }, Data.RangeBands, new ScriptedDice(dice), gm);
        encounter.BankedOpportunities = 1;
        return (encounter, encounter.Combatants.Single(c => c.Side == Side.Party), encounter.Combatants.Single(c => c.Name == "Bandit"));
    }

    [Fact]
    public void AStatblock_NamingAnOffHandAttack_HasAnOffHandBlow()
    {
        var bandit = CombatantFactory.FromAdversary(TestBandits.Roadside with { OpportunityText = "Off-hand attack (d4)" }, "Bandit", new GridPosition(0, 0));

        Assert.Equal(OpportunityBonusKind.OffHand, bandit.OpportunityBonus.Kind);
        Assert.Equal(4, bandit.OffHandAttack!.DamageDieSides);
        Assert.Equal(bandit.Attack.AttackModifier, bandit.OffHandAttack.AttackModifier);
    }

    [Fact]
    public void ARivalWithTwoWeapons_PaysItsOffHandInStamina_AsACharacterDoes()
    {
        var rival = CombatantFactory.FromCharacter(SampleEncounters.SallaRinehart(Data), new GridPosition(0, 0), Side.Adversaries);

        Assert.NotEqual(OpportunityBonusKind.OffHand, rival.OpportunityBonus.Kind);
        Assert.NotNull(rival.OffHandWeapon);
    }

    [Fact]
    public void ARivalsAttack_OpensItsOffHandStrike_For1Stamina_AndNoOpportunity()
    {
        var gm = new ScriptedGm((view, turn) => turn.Steps switch
        {
            0 => new GmStep.Attack(view.Characters.Single()),
            1 => new GmStep.OffHand(view.Characters.Single()),
            _ => null,
        });
        var encounter = new Encounter(12, 10, new[]
        {
            CombatantFactory.FromCharacter(SampleEncounters.DumbFighter(Data), new GridPosition(4, 4)),
            CombatantFactory.FromCharacter(SampleEncounters.SallaRinehart(Data), new GridPosition(5, 4), Side.Adversaries),
        }, Data.RangeBands, new ScriptedDice(2, 3, 2, 3, 1, 1, 1, 1), gm);
        encounter.DeclineFreeChoices();
        var rival = encounter.Combatants.Single(c => c.Side == Side.Adversaries);
        var stamina = rival.Stamina;

        encounter.EndTurn();
        encounter.RunAdversaryTurn();

        Assert.Contains(encounter.Log, l => l.Contains("in the off hand too (1 Stamina from the equipment"));
        Assert.Equal(stamina - 1, rival.Stamina);
        Assert.Equal(0, encounter.BankedOpportunities);
    }

    [Fact]
    public void AfterItsAttack_TheOpportunityBuysTheOffHandStrike_NoStaminaNoSecondAttack()
    {
        var (encounter, _, bandit) = Fight(new ScriptedGm((view, turn) => turn.Steps switch
        {
            0 => new GmStep.Attack(view.Characters.Single()),
            1 => new GmStep.OffHand(view.Characters.Single()),
            _ => null,
        }), 2, 3, 2, 3);
        var stamina = bandit.Stamina;

        encounter.EndTurn();
        encounter.RunAdversaryTurn();

        Assert.Contains(encounter.Log, l => l.Contains("bonus: an off-hand strike"));
        Assert.Contains(encounter.Log, l => l.StartsWith("Bandit attacks") && l.Contains("Off-hand blow"));
        Assert.Equal(stamina, bandit.Stamina);
        Assert.Equal(2, encounter.Log.Count(l => l.StartsWith("Bandit attacks")));
        Assert.Equal(0, encounter.BankedOpportunities);
    }

    [Fact]
    public void TheOffHandStrike_BeforeAnyAttack_IsRefused()
    {
        var (encounter, _, _) = Fight(new ScriptedGm((view, turn) => turn.Steps == 0 ? new GmStep.OffHand(view.Characters.Single()) : null));
        encounter.EndTurn();

        Assert.Throws<InvalidOperationException>(encounter.RunAdversaryTurn);
    }
}
