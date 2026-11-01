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
    private static Encounter Casters(int[] dice, (int X, int Y)? bandit = null, (int X, int Y)? ambusher = null, bool passiveGm = false)
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
            passiveGm ? new HoldingGm() : null);
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

        Assert.Equal(["Smite", "Scourging Light", "Vital Transfer", "Blessing"], mirella.Spells.Select(s => s.Name));
        Assert.Equal(["Mind Spike", "Foresight", "Telekinetic Shove", "Blood Mist"], elthric.Spells.Select(s => s.Name));
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
