using WoundBound.Game.Combat;
using WoundBound.Game.Combat.Brains;
using WoundBound.Rules.Enums;

namespace WoundBound.Game.Tests;

/// <summary>
/// Core Rules §11, "Illusion Spells", with the user's rulings for a fight: a figure, a wall,
/// reinforcements or a wound; creatures act on what they believe; Morale reads belief; and anything
/// that calls an illusion into question gives every witness a fresh look at it.
/// </summary>
public class IllusionTests
{
    private static readonly Game.Data.GameData Data = TestData.Load();

    private sealed class OneStepGm(Func<BattleView, GmTurn, GmStep?> step) : SimpleGmBrain
    {
        public override GmStep NextStep(BattleView view, GmTurn turn) => step(view, turn) ?? GmStep.End;

        public override GmCloseIn? NextCloseIn(BattleView view, IReadOnlyList<Combatant> stillIdle) => null;

        public override bool PanicDodges(BattleView view, PendingReaction reaction) => false;
    }

    /// <summary>Elthric at (2, 4); two Roadside Bandits (Cowardice) at (9, 4) and (9, 6), in daylight.</summary>
    private static (Encounter Encounter, Combatant Elthric, Combatant First, Combatant Second) Road(int[] dice, IGmBrain? gm = null, Setting? setting = null)
    {
        var elthric = CombatantFactory.FromCharacter(SampleEncounters.ElthricDane(Data), new GridPosition(2, 4));
        var first = CombatantFactory.FromAdversary(SampleEncounters.CowardlyBandit(SampleEncounters.RoadsideBandit), "First bandit", new GridPosition(9, 4));
        var second = CombatantFactory.FromAdversary(SampleEncounters.CowardlyBandit(SampleEncounters.RoadsideBandit), "Second bandit", new GridPosition(9, 6));
        var encounter = new Encounter(SampleEncounters.FieldWidth, SampleEncounters.FieldHeight, [elthric, first, second], Data.RangeBands,
            new ScriptedDice(dice), gm, setting: setting, environment: Data.Environment);
        encounter.TryBeginTurn(elthric);
        return (encounter, elthric, first, second);
    }

    private static Rules.Models.SpellDefinition Spell(Combatant caster, string name) => caster.Spells.Single(s => s.Name == name);

    private static string Log(Encounter encounter) => string.Join("\n", encounter.Log);

    [Fact]
    public void TheCatalogueIllusions_AreReadForWhatTheyAre()
    {
        Assert.Equal((IllusionForm.Figure, Senses.Sight), (Encounter.FormOf(Data.Spell("Phantom Double")), Encounter.SensesOf(Data.Spell("Phantom Double"))));
        Assert.Equal((IllusionForm.Reinforcements, Senses.Sound), (Encounter.FormOf(Data.Spell("False Portent")), Encounter.SensesOf(Data.Spell("False Portent"))));
        Assert.Equal((IllusionForm.Wound, Senses.Substance), (Encounter.FormOf(Data.Spell("Phantom Wound")), Encounter.SensesOf(Data.Spell("Phantom Wound"))));
        Assert.Equal((IllusionForm.Wall, Senses.Sight), (Encounter.FormOf(SampleEncounters.PhantomWall), Encounter.SensesOf(SampleEncounters.PhantomWall)));
    }

    [Fact]
    public void APhantomDouble_HasTheCastRollForItsDeceit_AndIsAnEnemy_OnlyToThoseTakenIn()
    {
        // 10+5 +4 Focus = 19, the Deceit, fixed.
        var (encounter, elthric, first, _) = Road([10, 5]);

        var cast = new SpellCast(Spell(elthric, "Phantom Double")) { Corner = new GridPosition(7, 4) };
        Assert.True(encounter.PlanCast(elthric, cast).CanCast, encounter.PlanCast(elthric, cast).Refusal);
        Assert.True(encounter.TryCast(cast), Log(encounter));

        var illusion = Assert.Single(encounter.Illusions);
        Assert.Equal(19, illusion.Deceit);
        Assert.Equal(2, illusion.RoundsLeft);
        Assert.True(encounter.Fools(illusion, first));
        Assert.False(encounter.Fools(illusion, elthric));

        var view = new BattleView(encounter);
        Assert.Contains(illusion.Phantom!, view.EnemiesOf(first));
        Assert.DoesNotContain(illusion.Phantom!, encounter.Combatants);

        // One at a time.
        Assert.True(encounter.TryBuyExtraAction());
        Assert.Contains("only one at a time", encounter.PlanCast(elthric, cast with { Corner = new GridPosition(6, 6) }).Refusal);
    }

    [Fact]
    public void ABlowAtAPhantom_PassesThrough_TheAttackerSeesThrough_AndEveryWitnessLooksAgain()
    {
        // Deceit 10+5+4 = 19. The first bandit swings at the double; the second, watching, rolls
        // Search 12+9 = 21 and sees through it too.
        Combatant? target = null;
        var gm = new OneStepGm((view, turn) => turn.Actor.Name == "First bandit" && !turn.HasAttacked && target is not null
            ? new GmStep.Attack(target)
            : GmStep.End);
        var (encounter, elthric, first, second) = Road([10, 5, 12, 9], gm);
        Assert.True(encounter.TryCast(new SpellCast(Spell(elthric, "Phantom Double")) { Corner = new GridPosition(8, 4) }), Log(encounter));
        var illusion = encounter.Illusions.Single();
        target = illusion.Phantom;

        encounter.EndTurn();
        encounter.RunAdversaryTurn();

        Assert.Contains(encounter.Log, l => l.Contains("passes straight through"));
        Assert.True(illusion.IsSeenThroughBy(first));
        Assert.True(illusion.IsSeenThroughBy(second), Log(encounter));
        Assert.Equal(elthric.BodyMax, elthric.Body);
    }

    [Fact]
    public void Testing_AnIllusion_IsAnActionAndASearchCheck_AgainstItsDeceit()
    {
        // Deceit 19; the bandit's Search 6+4 = 10 — still taken in.
        var gm = new OneStepGm((view, turn) => turn.Actor.Name == "First bandit" && !turn.HasAttacked
            ? new GmStep.Search(view.Illusions.Single())
            : GmStep.End);
        var (encounter, elthric, first, _) = Road([10, 5, 6, 4], gm);
        Assert.True(encounter.TryCast(new SpellCast(Spell(elthric, "Phantom Double")) { Corner = new GridPosition(7, 4) }));

        encounter.EndTurn();
        encounter.RunAdversaryTurn();

        Assert.Contains(encounter.Log, l => l.Contains("spends an action testing") && l.Contains("still taken in"));
        Assert.True(encounter.Fools(encounter.Illusions.Single(), first));
    }

    [Fact]
    public void APhantomWall_IsSolid_ToThoseTakenIn_AndAShotThroughIt_CallsItIntoQuestion()
    {
        // The wall over (5,4)-(6,5). Deceit 10+5+4 = 19.
        var (encounter, elthric, first, second) = Road([10, 5, 3, 2, 2, 3, 9, 7, 2]);
        Assert.True(encounter.TryCast(new SpellCast(Spell(elthric, "Phantom Wall")) { Corner = new GridPosition(5, 4) }), Log(encounter));
        var wall = encounter.Illusions.Single();

        var view = new BattleView(encounter);
        Assert.True(encounter.BelievesWall(first, new GridPosition(5, 4)));
        Assert.DoesNotContain(new GridPosition(6, 4), view.SquaresWithin(first, 2).Keys);
        Assert.False(view.PlanAttackFrom(first, new GridPosition(8, 4), elthric).CanAttack
                     && first.HasRangedAttack, "A taken-in shooter thinks the wall is in the way.");

        // Elthric, who knows better, throws a Mind Spike straight through it at the first bandit:
        // both bandits see the spell go through — 3+2 and 2+3 against 19, still taken in.
        Assert.True(encounter.TryBuyExtraAction());
        Assert.True(encounter.TryCast(new SpellCast(Spell(elthric, "Mind Spike")) { Target = first }), Log(encounter));
        Assert.Contains(encounter.Log, l => l.Contains("flies clean through"));
        Assert.Equal(2, encounter.Log.Count(l => l.Contains("looks again at")));
        Assert.True(encounter.Fools(wall, first) && encounter.Fools(wall, second));
    }

    [Fact]
    public void APhantomWound_CountsTowardsMorale_WhileItIsBelieved()
    {
        // The bandit has Body 6 and Cowardice: below half breaks it. Elthric lays a hand on him:
        // Deceit 10+5+4 = 19, and he feels 4 wounds he hasn't taken.
        var (encounter, elthric, first, _) = Road([10, 5, 4]);
        first.Position = new GridPosition(3, 4);

        Assert.True(encounter.TryCast(new SpellCast(Spell(elthric, "Phantom Wound")) { Target = first }), Log(encounter));

        Assert.Equal(first.BodyMax, first.Body);
        Assert.Equal(first.Body - 4, encounter.BelievedBody(first));
        Assert.True(first.IsBroken, Log(encounter));
    }

    [Fact]
    public void FalsePortent_BreaksOnlyTheWavering()
    {
        // Wavering is shaken by hurt: one more wound would take it below half. The bandit on 3 of 6
        // Body is; the unhurt one isn't, however close Cowardice puts it to running.
        var build = SampleEncounters.MirellaVask(Data);
        var mirella = CombatantFactory.FromCharacter(build, new GridPosition(2, 4));
        var coward = CombatantFactory.FromAdversary(SampleEncounters.CowardlyBandit(SampleEncounters.RoadsideBandit), "Coward", new GridPosition(9, 4));
        var steady = CombatantFactory.FromAdversary(SampleEncounters.CowardlyBandit(SampleEncounters.RoadsideBandit) with { Features = [new("Not a Fanatic", "")] },
            "Steady", new GridPosition(9, 6));
        Assert.Equal("Not a Fanatic", steady.Morale?.Trait);
        coward.Body = 3;
        var encounter = new Encounter(SampleEncounters.FieldWidth, SampleEncounters.FieldHeight, [mirella, coward, steady], Data.RangeBands,
            new ScriptedDice(9, 8), environment: Data.Environment);
        encounter.TryBeginTurn(mirella);

        Assert.True(encounter.TryCast(new SpellCast(Spell(mirella, "False Portent")) { Corner = new GridPosition(12, 4) }), Log(encounter));

        Assert.True(coward.IsBroken, Log(encounter));
        Assert.False(steady.IsBroken);
    }

    [Fact]
    public void ASightIllusion_IsWorthNothing_AtVisibilityNone()
    {
        var (encounter, elthric, first, _) = Road([10, 5], setting: SampleEncounters.SignpostNight);
        first.Position = new GridPosition(4, 4);

        Assert.True(encounter.TryCast(new SpellCast(Spell(elthric, "Phantom Double")) { Corner = new GridPosition(3, 5) }), Log(encounter));

        Assert.False(encounter.Fools(encounter.Illusions.Single(), first));
    }

    [Fact]
    public void AnIllusion_FadesWithItsTier_OrWithItsCaster()
    {
        var (encounter, elthric, _, _) = Road([10, 5]);
        Assert.True(encounter.TryCast(new SpellCast(Spell(elthric, "Phantom Double")) { Corner = new GridPosition(7, 4) }));

        elthric.IsUnconscious = true;
        encounter.EndTurn();

        Assert.Empty(encounter.Illusions);
        Assert.Contains(encounter.Log, l => l.Contains("fades with Elthric"));
    }

    [Fact]
    public void WhoeverSawTheDoubleMade_IsTakenIn_ButSuspicious()
    {
        var (encounter, elthric, first, second) = Road([10, 5]);
        Assert.True(encounter.TryCast(new SpellCast(Spell(elthric, "Phantom Double")) { Corner = new GridPosition(8, 4) }), Log(encounter));
        var illusion = encounter.Illusions.Single();
        var figure = illusion.Phantom!;

        Assert.True(illusion.WasWitnessedBy(first) && illusion.WasWitnessedBy(second));
        Assert.True(encounter.Fools(illusion, first)); // still taken in: it can't know
        Assert.Equal(Combat.Brains.BattleView.WitnessedFigureReal, encounter.View.BelievedReal(first, figure));
        Assert.Equal(1, encounter.View.BelievedReal(first, elthric));

        // A blow at the figure is worth to the witness only what it believes the figure real.
        var plain = new Combat.Brains.Utility.Assessor(encounter.View).Value(figure);
        var suspicious = new Combat.Brains.Utility.Assessor(encounter.View) { Observer = first }.Value(figure);
        Assert.Equal(plain * Combat.Brains.BattleView.WitnessedFigureReal, suspicious, 6);
    }
}
