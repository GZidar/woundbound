using WoundBound.Game.Combat;
using WoundBound.Game.Combat.Brains;
using WoundBound.Rules.Enums;

namespace WoundBound.Game.Tests;

/// <summary>Core Rules §11, "Utility Spells": Scrying sees a distant place as it is; Sensing reveals a present-tense truth.</summary>
public class SensingScryingTests
{
    private static readonly Game.Data.GameData Data = TestData.Load();

    private sealed class OneStepGm(Func<BattleView, GmTurn, GmStep?> step) : SimpleGmBrain
    {
        public override GmStep NextStep(BattleView view, GmTurn turn) => step(view, turn) ?? GmStep.End;

        public override GmCloseIn? NextCloseIn(BattleView view, IReadOnlyList<Combatant> stillIdle) => null;

        public override Combatant? ChooseExtraTurn(BattleView view) => null;
    }

    private static string Log(Encounter encounter) => string.Join(System.Environment.NewLine, encounter.Log);

    [Fact]
    public void AUtilitySpell_IsReadAsScryingSensingOrALever()
    {
        Assert.Equal(UtilityKind.Sensing, SpellArea.UtilityKindOf(SampleEncounters.Witchsight));
        Assert.Equal(UtilityKind.Scrying, SpellArea.UtilityKindOf(SampleEncounters.FarEye));
        Assert.Equal(UtilityKind.Lever, SpellArea.UtilityKindOf(SampleEncounters.SettleTheGround));
        Assert.Null(SpellArea.LeverOf(SampleEncounters.Witchsight));
    }

    [Fact]
    public void Sensing_FindsTheCasterWhoHasNotCast()
    {
        var elthric = CombatantFactory.FromCharacter(SampleEncounters.ElthricDane(Data), new GridPosition(2, 4));
        var chanter = CombatantFactory.FromAdversary(Data.Adversary("Wyrding Chanter"), "Chanter", new GridPosition(6, 4), Data.Spells);
        var bandit = CombatantFactory.FromAdversary(TestBandits.Roadside, "Bandit", new GridPosition(6, 5));
        var encounter = new Encounter(SampleEncounters.FieldWidth, SampleEncounters.FieldHeight, [elthric, chanter, bandit], Data.RangeBands,
            new ScriptedDice(), environment: Data.Environment);
        encounter.TryBeginTurn(elthric);

        Assert.False(chanter.ShowsAsCaster);
        Assert.True(encounter.TryCast(new SpellCast(elthric.Spells.Single(s => s.Name == "Witchsight")) { Corner = new GridPosition(6, 4) }), Log(encounter));

        Assert.True(chanter.IsSensedAsCaster);
        Assert.False(bandit.ShowsAsCaster);
        Assert.Contains(encounter.Log, l => l.Contains("Chanter: magic") && l.Contains("Bandit: no magic"));
    }

    [Fact]
    public void Sensing_SeesThroughAnIllusion_OnACastRollOverItsDeceit()
    {
        // A rival Elthric raises a Phantom Wall (3+4 +4 = Deceit 11); ours senses it, 10+9 +4 = 23.
        var elthric = CombatantFactory.FromCharacter(SampleEncounters.ElthricDane(Data), new GridPosition(2, 4));
        var rivalBuild = SampleEncounters.ElthricDane(Data);
        rivalBuild.CharacterName = "Rival Elthric";
        var rival = CombatantFactory.FromCharacter(rivalBuild, new GridPosition(12, 4), Side.Adversaries);
        var gm = new OneStepGm((view, turn) => turn.HasAttacked ? null
            : new GmStep.Cast(new SpellCast(turn.Actor.Spells.Single(s => s.Name == "Phantom Wall")) { Corner = new GridPosition(6, 4) }));
        var encounter = new Encounter(SampleEncounters.FieldWidth, SampleEncounters.FieldHeight, [elthric, rival], Data.RangeBands,
            new ScriptedDice(3, 4, 10, 9), gm, environment: Data.Environment);
        encounter.TryBeginTurn(elthric);
        encounter.EndTurn();
        while (encounter.Phase == EncounterPhase.AdversaryTurn)
        {
            encounter.RunAdversaryTurn();
            encounter.DeclineFreeChoices();
        }

        var wall = encounter.Illusions.Single();
        Assert.True(new BattleView(encounter).Fools(wall, elthric), Log(encounter));
        encounter.TryBeginTurn(elthric);
        Assert.True(encounter.TryCast(new SpellCast(elthric.Spells.Single(s => s.Name == "Witchsight")) { Corner = new GridPosition(6, 4) }), Log(encounter));

        Assert.True(wall.IsSeenThroughBy(elthric), Log(encounter));
        Assert.Contains(encounter.Log, l => l.Contains("seen for what it is"));
    }

    [Fact]
    public void Scrying_PutsDownAViewingPoint_ThatLastsTheSceneAndRollsNothing()
    {
        var elthric = CombatantFactory.FromCharacter(SampleEncounters.ElthricDane(Data), new GridPosition(2, 4));
        var bandit = CombatantFactory.FromAdversary(TestBandits.Roadside, "Bandit", new GridPosition(16, 4));
        var encounter = new Encounter(SampleEncounters.FieldWidth, SampleEncounters.FieldHeight, [elthric, bandit], Data.RangeBands,
            new ScriptedDice(), environment: Data.Environment);
        encounter.TryBeginTurn(elthric);

        var far = new SpellCast(elthric.Spells.Single(s => s.Name == "Far Eye")) { Corner = new GridPosition(16, 4) };
        Assert.True(encounter.TryCast(far), encounter.PlanCast(elthric, far).Refusal);

        Assert.Contains(encounter.Workings, w => w.Lever == Lever.Scrying && w.Caster == elthric);
        Assert.Contains(encounter.Log, l => l.Contains("Far Eye opens a window") && l.Contains("Bandit is there"));
    }

    [Fact]
    public void AStatblockCaster_PutsUpAPhantomDouble_InItsOwnLikeness()
    {
        var elthric = CombatantFactory.FromCharacter(SampleEncounters.ElthricDane(Data), new GridPosition(2, 4));
        var glamourer = CombatantFactory.FromAdversary(SampleEncounters.FenGlamourer(Data), "Fen Glamourer", new GridPosition(12, 4), Data.Spells);
        var gm = new OneStepGm((view, turn) => turn.HasAttacked ? null
            : new GmStep.Cast(new SpellCast(turn.Actor.Spells.Single(s => s.Name == "Phantom Double")) { Corner = new GridPosition(10, 6) }));
        var encounter = new Encounter(SampleEncounters.FieldWidth, SampleEncounters.FieldHeight, [elthric, glamourer], Data.RangeBands,
            new ScriptedDice(6, 5), gm, environment: Data.Environment);
        encounter.TryBeginTurn(elthric);
        encounter.EndTurn();
        while (encounter.Phase == EncounterPhase.AdversaryTurn)
        {
            encounter.RunAdversaryTurn();
            encounter.DeclineFreeChoices();
        }

        var phantom = encounter.Illusions.Single().Phantom;
        Assert.NotNull(phantom);
        Assert.Equal("Fen Glamourer", phantom!.Name);
        Assert.Equal(Side.Adversaries, phantom.Side);
        Assert.True(phantom.IsPhantom);
        Assert.Equal(6 + 5 + glamourer.StatblockCastBonus, encounter.Illusions.Single().Deceit);
    }
}
