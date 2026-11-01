using WoundBound.Game.Combat;
using WoundBound.Rules.Enums;

namespace WoundBound.Game.Tests;

/// <summary>Core Rules §11, Magic — the Quickstart's two casters, and the spells on their sheets.</summary>
public class SpellTests
{
    private static readonly Game.Data.GameData Data = TestData.Load();

    private static Combatant Named(Encounter encounter, string name) => encounter.Combatants.Single(c => c.Name == name);

    /// <summary>
    /// Mirella and Elthric, with Salla beside them, against a Roadside Bandit and a Bandit Ambusher
    /// set where a test wants them — in daylight, on open ground.
    /// </summary>
    private static Encounter Casters(int[] dice, (int X, int Y)? bandit = null, (int X, int Y)? ambusher = null, bool passiveGm = false, Setting? setting = null)
    {
        var combatants = new List<Combatant>
        {
            CombatantFactory.FromCharacter(SampleEncounters.MirellaVask(Data), new GridPosition(2, 4)),
            CombatantFactory.FromCharacter(SampleEncounters.ElthricDane(Data), new GridPosition(2, 3)),
            CombatantFactory.FromCharacter(SampleEncounters.SallaRinehart(Data), new GridPosition(3, 5)),
            CombatantFactory.FromAdversary(SampleEncounters.RoadsideBandit, "Bandit", ToSquare(bandit ?? (5, 3))),
            CombatantFactory.FromAdversary(SampleEncounters.BanditAmbusher, "Ambusher", ToSquare(ambusher ?? (14, 14))),
        };

        return new Encounter(SampleEncounters.FieldWidth, SampleEncounters.FieldHeight, combatants, Data.RangeBands, new ScriptedDice(dice),
            passiveGm ? new HoldingGm() : null, setting: setting, environment: Data.Environment);
    }

    /// <summary>A GM whose creatures stand where they are and do nothing.</summary>
    private sealed class HoldingGm : Combat.Brains.SimpleGmBrain
    {
        public override Combat.Brains.GmStep NextStep(Combat.Brains.BattleView view, Combat.Brains.GmTurn turn) => Combat.Brains.GmStep.End;

        public override Combat.Brains.GmCloseIn? NextCloseIn(Combat.Brains.BattleView view, IReadOnlyList<Combatant> stillIdle) => null;
    }

    private static GridPosition ToSquare((int X, int Y) at) => new(at.X, at.Y);

    private static Rules.Models.SpellDefinition Spell(Combatant caster, string name) => caster.Spells.Single(s => s.Name == name);

    [Fact]
    public void TheCasters_ComeOutAsTheirSheetsSay()
    {
        var encounter = Casters([]);
        var mirella = Named(encounter, "Mirella Vask");
        var elthric = Named(encounter, "Elthric Dane");

        // Mirella: Body 7 / Mind 5 / Soul 8, Stamina 2, DT 12 / RT 13 / ST 11.
        Assert.Equal((7, 5, 8, 2, 12, 13, 11), (mirella.BodyMax, mirella.MindMax, mirella.SoulMax, mirella.StaminaMax,
            mirella.DodgeThreshold, mirella.ResolveThreshold!.Value, mirella.StabilityThreshold));

        // Elthric: Body 7 / Mind 8 / Soul 4, Stamina 4, DT 13 / RT 13 / ST 11.
        Assert.Equal((7, 8, 4, 4, 13, 13, 11), (elthric.BodyMax, elthric.MindMax, elthric.SoulMax, elthric.StaminaMax,
            elthric.DodgeThreshold, elthric.ResolveThreshold!.Value, elthric.StabilityThreshold));

        Assert.Equal(["Smite", "Scourging Light", "Vital Transfer", "Blessing", "Eye of Eolis"], mirella.Spells.Select(s => s.Name));
        Assert.Equal(["Mind Spike", "Foresight", "Telekinetic Shove", "Blood Mist", "Flame Burst", "Conflagration"], elthric.Spells.Select(s => s.Name));
        Assert.True(elthric.HasOverclock);
        Assert.True(mirella.HasSacrifice && elthric.HasSacrifice);
    }

    [Fact]
    public void MindSpike_AtShort_CostsOneMind_AndDealsMind_ThatLeatherDoesNothingAgainst()
    {
        // Core Rules §3's Overclock example, paid in Mind: 9+5 +4 = 18 against DT 15, a hit; d6 = 3 Mind.
        var encounter = Casters([9, 5, 3]);
        var elthric = Named(encounter, "Elthric Dane");
        var bandit = Named(encounter, "Bandit");
        encounter.TryBeginTurn(elthric);

        var cast = new SpellCast(Spell(elthric, "Mind Spike")) { Target = bandit };
        var plan = encounter.PlanCast(elthric, cast);
        Assert.Null(plan.Refusal);
        Assert.Equal((1, WoundType.Mind, Ability.Focus, 4), (plan.Cost, plan.Track, plan.CastAbility, plan.CastBonus));

        Assert.True(encounter.TryCast(cast));

        Assert.Equal(7, elthric.Mind);
        Assert.Equal(1, bandit.Mind);
        Assert.Equal(bandit.BodyMax, bandit.Body);
        Assert.True(elthric.HasCastSpell);
        Assert.Equal(0, elthric.ActionsRemaining);
    }

    [Fact]
    public void Overclock_PaysTheWoundInStamina_OnceARound_AndHandsTheGmNothing()
    {
        var encounter = Casters([9, 5, 3, 6, 4]);
        var elthric = Named(encounter, "Elthric Dane");
        var bandit = Named(encounter, "Bandit");
        encounter.TryBeginTurn(elthric);

        Assert.True(encounter.TryCast(new SpellCast(Spell(elthric, "Mind Spike")) { Target = bandit, Overclock = true }));
        Assert.Equal((8, 3, 0), (elthric.Mind, elthric.Stamina, encounter.GmOpportunities));

        // An extra action is a tactical spend; the second Spike can't be Overclocked again.
        Assert.True(encounter.TryBuyExtraAction());
        var again = new SpellCast(Spell(elthric, "Mind Spike")) { Target = bandit, Overclock = true };
        Assert.Contains("Already Overclocked", encounter.PlanCast(elthric, again).Refusal);
        Assert.True(encounter.TryCast(again with { Overclock = false }));
        Assert.Equal(7, elthric.Mind);
    }

    [Fact]
    public void Range_PricesThePointOfOrigin_MiddleAddsOne()
    {
        var encounter = Casters([], bandit: (8, 3));
        var elthric = Named(encounter, "Elthric Dane");
        encounter.TryBeginTurn(elthric);

        var plan = encounter.PlanCast(elthric, new SpellCast(Spell(elthric, "Mind Spike")) { Target = Named(encounter, "Bandit") });

        Assert.Equal((SpellRange.Middle, 2), (plan.Range, plan.Cost));
    }

    [Fact]
    public void Smite_ToZeroSoul_LeavesAFoeNumbed_OutOfTheFight_ButStillLyingThere()
    {
        // 10+10 +4 = 24, a hit; d6 = 6, +1 Grace = 7 Soul against the bandit's 4. The GM lets it go down.
        var encounter = Casters([10, 10, 6], bandit: (4, 4));
        var mirella = Named(encounter, "Mirella Vask");
        var bandit = Named(encounter, "Bandit");
        encounter.TryBeginTurn(mirella);

        Assert.True(encounter.TryCast(new SpellCast(Spell(mirella, "Smite")) { Target = bandit }));

        Assert.Equal(7, mirella.Soul);
        Assert.True(bandit.IsNumbed);
        Assert.True(bandit.IsOutOfFight);
        Assert.True(bandit.OccupiesSquare);
        Assert.True(bandit.IsDefenceless);
    }

    [Fact]
    public void VitalTransfer_IsPaidInBody_HealsAnotherWithGrace_AndNeverTheCaster()
    {
        var encounter = Casters([4]);
        var mirella = Named(encounter, "Mirella Vask");
        var salla = Named(encounter, "Salla Rinehart");
        salla.Body = 2;
        encounter.TryBeginTurn(mirella);
        var transfer = Spell(mirella, "Vital Transfer");

        Assert.Contains("never target its own caster", encounter.PlanCast(mirella, new SpellCast(transfer) { Target = mirella }).Refusal);
        Assert.True(encounter.TryCast(new SpellCast(transfer) { Target = salla }));

        Assert.Equal(6, mirella.Body);
        Assert.Equal(7, salla.Body); // 2 + (4 + 1 Grace)
        Assert.Equal(mirella.SoulMax, mirella.Soul);
    }

    [Fact]
    public void Blessing_LastsUntilTheStartOfTheCastersNextTurn()
    {
        // The bandits are kept too far off to reach anybody this round.
        var encounter = Casters([], bandit: (17, 3), ambusher: (17, 12), passiveGm: true);
        var mirella = Named(encounter, "Mirella Vask");
        var elthric = Named(encounter, "Elthric Dane");
        encounter.TryBeginTurn(mirella);

        Assert.True(encounter.TryCast(new SpellCast(Spell(mirella, "Blessing")) { Target = elthric, Threshold = ThresholdType.Dodge }));
        Assert.Equal(15, elthric.DodgeThreshold);

        encounter.EndTurn();
        PlayOutRoundExcept(encounter, mirella);

        Assert.Equal(15, elthric.DodgeThreshold);
        Assert.True(encounter.TryBeginTurn(mirella));
        Assert.Equal(13, elthric.DodgeThreshold);
    }

    [Fact]
    public void ASustainedBlessing_IsPaidForAtTheStartOfTheCastersTurn_AndHandsTheGmABankedOpportunity()
    {
        var encounter = Casters([], bandit: (17, 3), ambusher: (17, 12), passiveGm: true);
        var mirella = Named(encounter, "Mirella Vask");
        var elthric = Named(encounter, "Elthric Dane");
        mirella.Stamina = 2;
        encounter.TryBeginTurn(mirella);

        Assert.True(encounter.TryCast(new SpellCast(Spell(mirella, "Blessing")) { Target = elthric, Sustain = true }));
        encounter.EndTurn();
        PlayOutRoundExcept(encounter, mirella);
        Assert.True(encounter.TryBeginTurn(mirella));

        Assert.Equal(EncounterPhase.AwaitingReaction, encounter.Phase);
        Assert.Equal(ReactionKind.Sustain, encounter.Pending!.Kind);
        Assert.True(encounter.RespondToReaction(true));

        Assert.Equal(15, elthric.DodgeThreshold);
        Assert.Equal(1, mirella.Stamina);
        Assert.Equal(1, encounter.BankedOpportunities);
        Assert.Equal(EncounterPhase.PartyTurn, encounter.Phase);
    }

    [Fact]
    public void BloodMist_CatchesEverybodyInTheBlock_RollingForEach_AlliesIncluded()
    {
        // A Small block over the bandit's square and Salla's beside it: one roll each.
        // Salla first: 1+2 +4 = 7, a miss. Bandit: 8+8 +4 = 20 — a critical — 4 on the d4 and 2
        // more, less 1 for leather.
        var encounter = Casters([1, 2, 8, 8, 2], bandit: (4, 5));
        var elthric = Named(encounter, "Elthric Dane");
        var bandit = Named(encounter, "Bandit");
        var salla = Named(encounter, "Salla Rinehart");
        encounter.TryBeginTurn(elthric);

        var cast = new SpellCast(Spell(elthric, "Blood Mist")) { Corner = new GridPosition(3, 5) };
        var plan = encounter.PlanCast(elthric, cast);
        Assert.Equal([salla, bandit], plan.Targets.OrderBy(t => t.Position.X));
        Assert.Equal(WoundType.Body, plan.Track);

        Assert.True(encounter.TryCast(cast));

        Assert.Equal(6, elthric.Body);
        Assert.Equal(bandit.BodyMax - 5, bandit.Body);
        Assert.Equal(salla.BodyMax, salla.Body);
    }

    [Fact]
    public void TelekineticShove_DrivesTheTargetAShortDistance_InAStraightLine()
    {
        // 9+9 +4 = 22 against ST 11 — beyond standing firm — and the bandit doesn't arrest it.
        var encounter = Casters([9, 9], bandit: (4, 3));
        var elthric = Named(encounter, "Elthric Dane");
        var bandit = Named(encounter, "Bandit");
        bandit.Stamina = 0;
        encounter.TryBeginTurn(elthric);

        Assert.True(encounter.TryCast(new SpellCast(Spell(elthric, "Telekinetic Shove")) { Target = bandit }));

        Assert.Equal(new GridPosition(8, 3), bandit.Position);
    }

    [Fact]
    public void BacklashOnASpend_HoldsTheTrackAtOne_AndTakesTheRestFromBody()
    {
        var encounter = Casters([1, 2], bandit: (4, 3));
        var elthric = Named(encounter, "Elthric Dane");
        elthric.Mind = 1;
        encounter.TryBeginTurn(elthric);

        Assert.True(encounter.TryCast(new SpellCast(Spell(elthric, "Mind Spike")) { Target = Named(encounter, "Bandit"), Backlash = true }));

        Assert.Equal((1, 6), (elthric.Mind, elthric.Body));
        Assert.False(elthric.IsStunned);
    }

    [Fact]
    public void Sacrifice_PaysABody_ForAdvantage_OnceARound()
    {
        // Three dice for Advantage: 2, 9, 8 keeps 9+8, +4 = 21. Then 3 on the d6.
        var encounter = Casters([2, 9, 8, 3], bandit: (4, 3));
        var elthric = Named(encounter, "Elthric Dane");
        encounter.TryBeginTurn(elthric);

        Assert.True(encounter.TryCast(new SpellCast(Spell(elthric, "Mind Spike")) { Target = Named(encounter, "Bandit"), Sacrifice = true }));

        Assert.Equal((6, 7), (elthric.Body, elthric.Mind));
        Assert.Equal(1, Named(encounter, "Bandit").Mind);
        Assert.Contains("Already Sacrificed", Encounter.SacrificeRefusal(elthric));
    }

    [Fact]
    public void AStunnedCharacter_TakesNoTurn_UntilMeditationBringsThemBack()
    {
        var encounter = Casters([], bandit: (17, 3), ambusher: (17, 12));
        var elthric = Named(encounter, "Elthric Dane");
        var mirella = Named(encounter, "Mirella Vask");
        elthric.Mind = 0;
        elthric.TendEligible[WoundType.Mind] = true;

        Assert.True(elthric.IsStunned && elthric.IsOutOfFight && elthric.OccupiesSquare && elthric.IsDefenceless);
        Assert.DoesNotContain(elthric, encounter.Fighting(Side.Party));

        encounter.TryBeginTurn(mirella);
        Assert.True(encounter.TryTend(CombatAction.Meditate, elthric));
        Assert.Equal(1, elthric.Mind);
        Assert.False(elthric.IsOutOfFight);
    }

    [Fact]
    public void ADoubleOne_WoundsTheCaster_OnTheSpellsOwnTrack_AndTheyMayBacklashIt()
    {
        // Mind 2: the Spike costs 1, then a double one costs another Mind wound — which would Stun
        // him. He Backlashes it: held at 1, and the 1 excess comes off Body.
        var encounter = Casters([1, 1], bandit: (4, 3));
        var elthric = Named(encounter, "Elthric Dane");
        elthric.Mind = 2;
        encounter.TryBeginTurn(elthric);

        Assert.True(encounter.TryCast(new SpellCast(Spell(elthric, "Mind Spike")) { Target = Named(encounter, "Bandit") }));
        Assert.Equal(ReactionKind.Backlash, encounter.Pending!.Kind);
        Assert.True(encounter.RespondToReaction(true));

        Assert.Equal((1, 6, elthric.StaminaMax), (elthric.Mind, elthric.Body, elthric.Stamina));
        Assert.False(elthric.IsStunned);
        Assert.Equal(0, encounter.GmOpportunities);
    }

    [Theory]
    [InlineData("utility")]
    [InlineData("preset")]
    public void Bots_PlayTheCastersFight_ToTheEnd_AndCast(string party)
    {
        var casts = 0;
        for (var seed = 1; seed <= 15; seed++)
        {
            var encounter = SampleEncounters.SignpostWithCasters(Data, new RandomDiceRoller(new Random(seed)),
                new Combat.Brains.Utility.UtilityGmBrain(Combat.Brains.Utility.BrainSkill.Best));
            Combat.Brains.IPartyBrain brain = party == "utility"
                ? new Combat.Brains.Utility.UtilityPartyBrain(Combat.Brains.Utility.BrainSkill.Best)
                : new Combat.Brains.TacticalPartyBrain(Combat.Brains.PartyTactics.Presets[0]);
            var result = Simulation.Simulator.PlayOne(encounter, brain);
            Assert.True(encounter.IsOver);
            casts += result.Tally.Casts;
        }

        Assert.True(casts > 0);
    }

    private static Rules.Models.InventoryItemDefinition Torches(Combatant c) => c.Build!.InventoryItems.Single(i => i.Name.StartsWith("Torches"));

    [Fact]
    public void FlameBurst_TapsTheTorchInHand_CastWithIntellect_AndTheLightGoesWithIt()
    {
        // Core Rules §11's Example of Play: one target, a Ray, Short — 1 Fuel, from the torch.
        // 11+9 +2 (Intellect +1, Tier 1) = 22, a hit; d6 = 4, the bandit's leather takes 1.
        var encounter = Casters([11, 9, 4], bandit: (4, 3));
        var elthric = Named(encounter, "Elthric Dane");
        var bandit = Named(encounter, "Bandit");
        Torches(elthric).IsLit = true;
        encounter.TryBeginTurn(elthric);

        var cast = new SpellCast(Spell(elthric, "Flame Burst")) { Target = bandit, Tap = FuelSource.Torch };
        var plan = encounter.PlanCast(elthric, cast);
        Assert.Null(plan.Refusal);
        Assert.Equal((Ability.Intellect, 2, 1, 1, 0), (plan.CastAbility, plan.CastBonus, plan.Cost, plan.Fuel!.FromSource, plan.WoundsPaid));

        Assert.True(encounter.TryCast(cast));

        Assert.Equal(bandit.BodyMax - 3, bandit.Body);
        Assert.False(Torches(elthric).IsLit);
        Assert.Equal(5, Torches(elthric).Quantity);
        Assert.Equal(0, elthric.CarriedLights);
        Assert.Equal((elthric.MindMax, elthric.BodyMax), (elthric.Mind, elthric.Body));
    }

    [Fact]
    public void WithNoFireToHand_AnElementalSpellCantBePaidFor_AndOverclockCantHelp()
    {
        var encounter = Casters([], bandit: (4, 3));
        var elthric = Named(encounter, "Elthric Dane");
        encounter.TryBeginTurn(elthric);
        var burst = new SpellCast(Spell(elthric, "Flame Burst")) { Target = Named(encounter, "Bandit") };

        Assert.Contains("1 short", encounter.PlanCast(elthric, burst).Refusal);
        Assert.Contains("isn't holding a lit torch", encounter.PlanCast(elthric, burst with { Tap = FuelSource.Torch }).Refusal);
        Assert.Contains("Fuel, not wounds", encounter.PlanCast(elthric, burst with { Overclock = true }).Refusal);
    }

    [Fact]
    public void AbundantFire_PaysTheWholeCost_AndTheTorchStaysLit()
    {
        var forge = Setting.Daylight with { Name = "The smithy, forge in use", Elements = new Dictionary<Element, AmbientPresence> { [Element.Fire] = AmbientPresence.Abundant } };
        var encounter = Casters([], bandit: (8, 3), setting: forge);
        var elthric = Named(encounter, "Elthric Dane");
        Torches(elthric).IsLit = true;
        encounter.TryBeginTurn(elthric);

        // Middle range: 2 Fuel, both off the forge — and with nothing left to pay, the torch isn't drawn on.
        var plan = encounter.PlanCast(elthric, new SpellCast(Spell(elthric, "Flame Burst")) { Target = Named(encounter, "Bandit"), Tap = FuelSource.Torch });
        Assert.Equal((2, 2, 0), (plan.Cost, plan.Fuel!.FromAmbient, plan.Fuel.SourceSpent));
    }

    [Fact]
    public void ABuiltFire_InReach_PaysTwo_AndBurnsDown()
    {
        // A Conflagration over the bandit at Middle range: 2 Fuel, all of it from the campfire beside him.
        var camp = Setting.Daylight with { Name = "The camp", Fires = new HashSet<GridPosition> { new(3, 2) } };
        var encounter = Casters([1, 2], bandit: (9, 3), setting: camp);
        var elthric = Named(encounter, "Elthric Dane");
        encounter.TryBeginTurn(elthric);

        var cast = new SpellCast(Spell(elthric, "Conflagration")) { Corner = new GridPosition(9, 3), Tap = FuelSource.Campfire, TapAt = new GridPosition(3, 2) };
        var plan = encounter.PlanCast(elthric, cast);
        Assert.Null(plan.Refusal);
        Assert.Equal((2, 2), (plan.Cost, plan.Fuel!.FromSource));

        Assert.True(encounter.TryCast(cast));
        Assert.Empty(encounter.Fires);
    }

    [Fact]
    public void ATappedLantern_GoesCold_AndNeedsAFlaskOfOilAndAnActionToRelight()
    {
        var encounter = Casters([1, 2], bandit: (4, 3));
        var elthric = Named(encounter, "Elthric Dane");
        Torches(elthric).IsStored = true;
        var lantern = new Rules.Models.InventoryItemDefinition { Name = "Lantern", Quantity = 1, IsLit = true };
        var oil = new Rules.Models.InventoryItemDefinition { Name = "Oil (flask)", Quantity = 1 };
        elthric.Build!.InventoryItems.Add(lantern);
        encounter.TryBeginTurn(elthric);

        Assert.True(encounter.TryCast(new SpellCast(Spell(elthric, "Flame Burst")) { Target = Named(encounter, "Bandit"), Tap = FuelSource.Lantern }));
        Assert.False(lantern.IsLit);
        Assert.Equal(1, lantern.Quantity);

        Assert.True(encounter.TryBuyExtraAction());
        Assert.Contains("no oil", Encounter.LightRefusal(elthric));
        elthric.Build.InventoryItems.Add(oil);
        Assert.True(encounter.TryLight());
        Assert.True(lantern.IsLit);
        Assert.Equal(0, oil.Quantity);
    }

    [Fact]
    public void TheUtilityBrain_ReachesForFire_WhenThoughtAndBloodAreRunningOut()
    {
        // One Mind left — a Spike would Stun him — too little Body to bleed, and nothing left to Heal: the torch it is.
        var encounter = Casters([], bandit: (5, 3));
        var elthric = Named(encounter, "Elthric Dane");
        Torches(elthric).IsLit = true;
        (elthric.Mind, elthric.Body, elthric.Stamina) = (1, 2, 2);
        elthric.TendEligible[WoundType.Body] = false; // Healed already: the Heal action has nothing more to give.
        encounter.TryBeginTurn(elthric);
        elthric.HasMove = false; // It would step for a better angle first; with the move gone, it casts from here.

        var step = new Combat.Brains.Utility.UtilityPartyBrain(Combat.Brains.Utility.BrainSkill.Best)
            .NextStep(encounter.View, elthric);

        var planner = new Combat.Brains.Utility.TurnPlanner(new Combat.Brains.Utility.Assessor(encounter.View), Combat.Brains.Utility.BrainSkill.Best);
        var best = planner.Options(elthric, false, 1, real: true).OrderByDescending(o => o.Score).Take(6).Select(o => $"{o.Score:0.00} {o.Why}");
        Assert.True(step is Combat.Brains.PartyStep.Cast, "Chose instead: " + string.Join(" | ", best));
        var cast = (Combat.Brains.PartyStep.Cast)step;
        Assert.Equal("Flame Burst", cast.Spell.Spell.Name);
        Assert.Equal(FuelSource.Torch, cast.Spell.Tap);
    }

    [Fact]
    public void AtTheCamp_TappingTheFire_LeavesOnlyMirellasLantern()
    {
        var encounter = SampleEncounters.CampfireAmbush(Data, new ScriptedDice(1, 2, 1, 2));
        var elthric = Named(encounter, "Elthric Dane");
        var salla = Named(encounter, "Salla Rinehart");
        var wolf = Named(encounter, "Wolf from the north");
        Assert.NotEqual(Visibility.None, encounter.VisibilityOf(salla));
        encounter.TryBeginTurn(elthric);

        // A Conflagration over the wolf at Middle range: 2 Fuel, the whole of it from the fire.
        var cast = new SpellCast(Spell(elthric, "Conflagration"))
        {
            Corner = wolf.Position,
            Tap = FuelSource.Campfire,
            TapAt = SampleEncounters.CampfireSquare,
        };
        var plan = encounter.PlanCast(elthric, cast);
        Assert.Null(plan.Refusal);
        Assert.Equal((SpellRange.Middle, 2, 2), (plan.Range, plan.Cost, plan.Fuel!.FromSource));

        var before = encounter.VisibilityOf(wolf);
        Assert.True(encounter.TryCast(cast));

        // The fire is gone; Mirella's lantern still lights the camp, but not out to the wolves.
        Assert.Empty(encounter.Fires);
        Assert.NotEqual(Visibility.None, encounter.VisibilityOf(salla));
        Assert.True(encounter.VisibilityOf(wolf) > before);
    }

    /// <summary>Ends Mirella's turn and runs adversary turns until it's a character's turn to choose again.</summary>
    private static void EndTurnAndLetTheGmAct(Encounter encounter)
    {
        encounter.EndTurn();
        while (encounter.Phase == EncounterPhase.AdversaryTurn)
        {
            encounter.RunAdversaryTurn();
        }
    }

    [Fact]
    public void Fear_MakesTheTargetGiveGround_ABandATurn_WithDisadvantage()
    {
        // 10+8 +4 = 22 against RT 12: Frightened. At the end of Mirella's turn it's rechecked: 9+8 +4 = 21, it holds.
        var encounter = Casters([10, 8, 9, 8], bandit: (4, 3), passiveGm: true);
        var mirella = Named(encounter, "Mirella Vask");
        var bandit = Named(encounter, "Bandit");
        encounter.TryBeginTurn(mirella);

        Assert.True(encounter.TryCast(new SpellCast(Spell(mirella, "Eye of Eolis")) { Target = bandit, Effect = ControlEffect.Fear }));
        Assert.Contains(CharacterCondition.Frightened, bandit.Conditions);
        Assert.Contains("only one at a time", encounter.PlanCastFrom(mirella, mirella.Position, new SpellCast(Spell(mirella, "Eye of Eolis")) { Target = Named(encounter, "Ambusher") }, checkTurn: false).Refusal);

        // From Short it has to finish in Middle — 5 squares or more from her.
        EndTurnAndLetTheGmAct(encounter);
        Assert.InRange(bandit.Position.DistanceTo(mirella.Position), 5, 8);
        Assert.True(bandit.HasWithdrawnThisTurn);
    }

    [Fact]
    public void Fear_AtLong_DrivesTheTargetFromTheScene()
    {
        var encounter = Casters([10, 8, 9, 8], bandit: (12, 4), passiveGm: true);
        var mirella = Named(encounter, "Mirella Vask");
        var bandit = Named(encounter, "Bandit");
        encounter.TryBeginTurn(mirella);
        mirella.Soul = 8;

        Assert.True(encounter.TryCast(new SpellCast(Spell(mirella, "Eye of Eolis")) { Target = bandit }));
        EndTurnAndLetTheGmAct(encounter);

        Assert.True(bandit.HasFled);
        Assert.Empty(encounter.ControlsInPlay);
    }

    [Fact]
    public void Compulsion_ToKneel_TakesTheTargetsWholeTurn()
    {
        var encounter = Casters([10, 8, 9, 8], bandit: (4, 5));
        var mirella = Named(encounter, "Mirella Vask");
        var bandit = Named(encounter, "Bandit");
        encounter.TryBeginTurn(mirella);

        Assert.True(encounter.TryCast(new SpellCast(Spell(mirella, "Eye of Eolis")) { Target = bandit, Effect = ControlEffect.Compulsion }));
        var at = bandit.Position;
        EndTurnAndLetTheGmAct(encounter);

        Assert.Contains(encounter.Log, l => l.Contains("Bandit is Compelled, and kneels"));
        Assert.Equal(at, bandit.Position);
        Assert.All(encounter.Combatants.Where(c => c.Side == Side.Party), c => Assert.Equal(c.BodyMax, c.Body));
    }

    [Fact]
    public void Confusion_TurnsTheTargetOnWhoeverIsInReach_AlliesIncluded()
    {
        // The bandit's only neighbour is the ambusher. Its Rusty Sword: 11+10 +2 = 23, a hit; d6 = 4, less 1 for leather.
        var encounter = Casters([10, 8, 9, 8, 1, 11, 10, 4], bandit: (8, 8), ambusher: (9, 8), passiveGm: true);
        var mirella = Named(encounter, "Mirella Vask");
        var bandit = Named(encounter, "Bandit");
        var ambusher = Named(encounter, "Ambusher");
        ambusher.Stamina = 0;
        encounter.TryBeginTurn(mirella);

        Assert.True(encounter.TryCast(new SpellCast(Spell(mirella, "Eye of Eolis")) { Target = bandit, Effect = ControlEffect.Confusion }));
        EndTurnAndLetTheGmAct(encounter);

        Assert.Equal(ambusher.BodyMax - 3, ambusher.Body);
    }

    /// <summary>Everybody but <paramref name="last"/> ends their turn, and the adversaries take theirs, until it's <paramref name="last"/>'s pick next round.</summary>
    private static void PlayOutRoundExcept(Encounter encounter, Combatant last)
    {
        var round = encounter.Round;
        for (var guard = 0; guard < 50; guard++)
        {
            switch (encounter.Phase)
            {
                case EncounterPhase.AdversaryTurn:
                    encounter.RunAdversaryTurn();
                    break;
                case EncounterPhase.ChoosingCharacter when encounter.Round > round:
                    return;
                case EncounterPhase.ChoosingCharacter:
                    encounter.TryBeginTurn(encounter.PartyStillToAct.First(c => c != last));
                    break;
                case EncounterPhase.PartyTurn:
                    encounter.EndTurn();
                    break;
                default:
                    throw new InvalidOperationException($"Unexpected phase {encounter.Phase}.");
            }
        }
    }
}
