using WoundBound.Game.Combat;
using WoundBound.Rules.Enums;

namespace WoundBound.Game.Tests;

/// <summary>
/// Cover (Core Rules §9, "Cover"): partial +2 DT, full can't be targeted, which side it is on,
/// attacking out of full cover drops to partial unless 1 Stamina ducks back, and a Volume ignores it.
/// On the grid a piece shelters whoever stands right beside it, from a line that runs through it.
/// </summary>
public class CoverTests
{
    private static readonly Game.Data.GameData Data = TestData.Load();

    /// <summary>The Dumb Fighter, bow in hand, and the Biter on a 10×8 field, in <paramref name="setting"/>.</summary>
    private static (Encounter Encounter, Combatant Fighter, Combatant Biter) Board(Setting setting, GridPosition fighterAt, GridPosition biterAt, params int[] dice)
    {
        var fighter = CombatantFactory.FromCharacter(SampleEncounters.DumbFighter(Data), fighterAt);
        var biter = CombatantFactory.FromAdversary(Data.Adversary("Bramble Ratkin"), "Ratkin Biter", biterAt);
        var encounter = new Encounter(SampleEncounters.BoardWidth, SampleEncounters.BoardHeight, new[] { fighter, biter },
            Data.RangeBands, new ScriptedDice(dice), setting: setting, environment: Data.Environment);
        encounter.TrySwapLoadout(); // the short bow
        return (encounter, fighter, biter);
    }

    private static Setting With(Cover grade, params (int, int, int, int)[] squares) =>
        new() { Name = "Cover", CoverPieces = Setting.CoverOf(grade, squares) };

    [Fact]
    public void PartialCover_AddsTwoToDodgeThreshold_AgainstAShotThroughIt()
    {
        var (open, fighter, biter) = Board(Setting.Daylight, new GridPosition(2, 4), new GridPosition(8, 4));
        var bare = open.PlanAttack(fighter, biter).DodgeThreshold;

        // A barrel right in front of the Biter, on the line from the archer.
        var (encounter, archer, target) = Board(With(Cover.Partial, (7, 4, 7, 4)), new GridPosition(2, 4), new GridPosition(8, 4));
        var plan = encounter.PlanAttack(archer, target);

        Assert.True(plan.CanAttack);
        Assert.Equal(bare + 2, plan.DodgeThreshold);
        Assert.Equal(2, plan.CoverDodgeModifier);
    }

    [Fact]
    public void Cover_IsAMatterOfWhichSideItIsOn()
    {
        // The same barrel, but the archer has walked round to shoot from the north: nothing in the way.
        var (encounter, archer, target) = Board(With(Cover.Partial, (7, 4, 7, 4)), new GridPosition(8, 0), new GridPosition(8, 4));

        Assert.Equal(Cover.None, encounter.CoverFrom(archer.Position, target));
        Assert.Equal(0, encounter.PlanAttack(archer, target).CoverDodgeModifier);
    }

    [Fact]
    public void APieceFarFromTheTarget_GivesItNoCover()
    {
        // A low wall in the middle of the field, three squares short of the Biter, hides nothing.
        var (encounter, archer, target) = Board(With(Cover.Partial, (5, 4, 5, 4)), new GridPosition(2, 4), new GridPosition(8, 4));

        Assert.Equal(Cover.None, encounter.CoverFrom(archer.Position, target));
    }

    [Fact]
    public void FullCover_CannotBeTargeted()
    {
        var (encounter, archer, target) = Board(With(Cover.Full, (7, 4, 7, 4)), new GridPosition(2, 4), new GridPosition(8, 4));

        var plan = encounter.PlanAttack(archer, target);

        Assert.False(plan.CanAttack);
        Assert.Contains("full cover", plan.Refusal);
    }

    [Fact]
    public void APillarBesideTheArcher_IsLeantRound_NotShotThrough()
    {
        var (encounter, archer, target) = Board(With(Cover.Full, (3, 4, 3, 4)), new GridPosition(2, 4), new GridPosition(8, 4));

        Assert.True(encounter.PlanAttack(archer, target).CanAttack);
        Assert.Equal(Cover.Full, encounter.CoverFrom(target.Position, archer));
    }

    [Fact]
    public void APillarBesideNeither_BlocksTheLine()
    {
        var (encounter, archer, target) = Board(With(Cover.Full, (5, 4, 5, 4)), new GridPosition(2, 4), new GridPosition(8, 4));

        Assert.Contains("solid", encounter.PlanAttack(archer, target).Refusal);
    }

    [Fact]
    public void NobodyWalksThroughOrStandsInAPieceOfCover()
    {
        var (encounter, archer, _) = Board(With(Cover.Partial, (3, 4, 3, 4)), new GridPosition(2, 4), new GridPosition(8, 0));

        Assert.Null(encounter.PlanMove(archer, new GridPosition(3, 4)));
        var round = encounter.PlanMove(archer, new GridPosition(4, 4))!;
        Assert.DoesNotContain(new GridPosition(3, 4), round.Path);
    }

    [Fact]
    public void ShootingOutOfFullCover_DropsToPartial_UntilTheirNextTurn()
    {
        // 2+2 misses. The archer leant out from behind the pillar to loose it.
        var (encounter, archer, target) = Board(With(Cover.Full, (3, 4, 3, 4)), new GridPosition(2, 4), new GridPosition(8, 4), 2, 2);

        Assert.NotNull(encounter.TryAttack(target));

        Assert.True(archer.IsShowingFromCover);
        Assert.Equal(Cover.Partial, encounter.CoverFrom(target.Position, archer));
        Assert.Contains(encounter.Log, line => line.Contains("shows themselves"));
    }

    [Fact]
    public void DuckingBack_Costs1Stamina_AsATacticalSpend_AndHandsTheGmAnOpportunity()
    {
        var (encounter, archer, target) = Board(With(Cover.Full, (3, 4, 3, 4)), new GridPosition(2, 4), new GridPosition(8, 4), 2, 2);
        var stamina = archer.Stamina;
        Assert.NotNull(Encounter.DuckBackRefusal(archer)); // nothing to duck back from yet
        encounter.TryAttack(target);

        Assert.True(encounter.TryDuckBack());

        Assert.Equal(stamina - 1, archer.Stamina);
        Assert.Equal(1, archer.TacticalSpendsThisRound);
        Assert.Equal(1, encounter.ImmediateOpportunities);
        Assert.Equal(Cover.Full, encounter.CoverFrom(target.Position, archer));
        Assert.False(encounter.TryDuckBack()); // once is enough
    }

    [Fact]
    public void ShootingWithoutCover_ShowsNothing()
    {
        var (encounter, archer, target) = Board(Setting.Daylight, new GridPosition(2, 4), new GridPosition(8, 4), 2, 2);

        encounter.TryAttack(target);

        Assert.False(archer.IsShowingFromCover);
        Assert.NotNull(Encounter.DuckBackRefusal(archer));
    }

    [Fact]
    public void ARay_IsStoppedByFullCover_ButAVolumeFlowsRoundIt()
    {
        // Mirella at (1, 4); a bandit at (6, 4) crouched behind a pillar at (5, 4).
        var mirella = CombatantFactory.FromCharacter(SampleEncounters.MirellaVask(Data), new GridPosition(1, 4));
        var bandit = CombatantFactory.FromAdversary(TestBandits.Roadside, "Bandit", new GridPosition(6, 4));
        var encounter = new Encounter(SampleEncounters.FieldWidth, SampleEncounters.FieldHeight, [mirella, bandit], Data.RangeBands,
            new ScriptedDice(), setting: With(Cover.Full, (5, 4, 5, 4)), environment: Data.Environment);
        encounter.TryBeginTurn(mirella);
        var smite = mirella.Spells.Single(s => s.Name == "Smite");
        var scourging = mirella.Spells.Single(s => s.Name == "Scourging Light");

        Assert.Contains("full cover", encounter.PlanCast(mirella, new SpellCast(smite) { Target = bandit }).Refusal);
        var volume = encounter.PlanCast(mirella, new SpellCast(scourging) { Corner = new GridPosition(6, 4) });
        Assert.True(volume.CanCast, volume.Refusal);
        Assert.Contains(bandit, volume.Targets);
    }
}
