using WoundBound.Game.Combat;
using WoundBound.Rules.Enums;

namespace WoundBound.Game.Tests;

/// <summary>Core Rules §11, "Spell Shape": Line and Cone, and the Shape chosen in the moment of casting.</summary>
public class SpellShapeTests
{
    private static readonly Game.Data.GameData Data = TestData.Load();

    private static bool Open(GridPosition _) => false;

    [Theory]
    [InlineData(SpellSize.Small, 4)]
    [InlineData(SpellSize.Medium, 9)]
    [InlineData(SpellSize.Large, 16)]
    public void ALine_RunsAsLongAsItsSize_StraightOrCornerToCorner(SpellSize size, int length)
    {
        var straight = SpellArea.Line(new GridPosition(0, 0), (1, 0), size, Open);
        var diagonal = SpellArea.Line(new GridPosition(0, 0), (1, 1), size, Open);

        Assert.Equal(length, straight.Count);
        Assert.Equal(new GridPosition(length - 1, 0), straight[^1]);
        Assert.Equal(new GridPosition(length - 1, length - 1), diagonal[^1]);
    }

    [Fact]
    public void ALine_StopsAtAWall()
    {
        var line = SpellArea.Line(new GridPosition(0, 0), (1, 0), SpellSize.Medium, sq => sq == new GridPosition(3, 0));
        Assert.Equal(3, line.Count);
    }

    [Theory]
    [InlineData(SpellSize.Small, 4, 3)]
    [InlineData(SpellSize.Medium, 9, 10)]
    [InlineData(SpellSize.Large, 16, 15)]
    public void ACone_FansOutInRows_AndAlongADiagonalIsTheStaircase(SpellSize size, int straight, int diagonal)
    {
        var along = SpellArea.Cone(new GridPosition(10, 10), (0, 1), size, Open);
        var corner = SpellArea.Cone(new GridPosition(10, 10), (-1, 1), size, Open);

        Assert.Equal(straight, along.Distinct().Count());
        Assert.Equal(diagonal, corner.Distinct().Count());
        Assert.Contains(new GridPosition(9, 11), along);
        Assert.Contains(new GridPosition(11, 11), along);
        Assert.DoesNotContain(new GridPosition(10, 9), along);
    }

    /// <summary>Mirella at (1, 4), three bandits in a row east of her.</summary>
    private static (Encounter Encounter, Combatant Mirella, Combatant[] Bandits) Row()
    {
        var mirella = CombatantFactory.FromCharacter(SampleEncounters.MirellaVask(Data), new GridPosition(1, 4));
        var bandits = Enumerable.Range(0, 3)
            .Select(i => CombatantFactory.FromAdversary(TestBandits.Roadside, $"Bandit {i + 1}", new GridPosition(3 + i, 4)))
            .ToArray();
        var encounter = new Encounter(SampleEncounters.FieldWidth, SampleEncounters.FieldHeight, [mirella, .. bandits], Data.RangeBands, new ScriptedDice());
        encounter.TryBeginTurn(mirella);
        return (encounter, mirella, bandits);
    }

    [Fact]
    public void AnAreaSpell_CastAsALine_CatchesARowNoBlockCould_AtTheSameCost()
    {
        var (encounter, mirella, bandits) = Row();
        var scourging = mirella.Spells.Single(s => s.Name == "Scourging Light");
        Assert.Equal(SpellShape.Volume, scourging.Shape);

        var line = encounter.PlanCast(mirella, new SpellCast(scourging) { Shape = SpellShape.Line, Origin = new GridPosition(2, 4), Aim = (1, 0) });
        var block = encounter.PlanCast(mirella, new SpellCast(scourging) { Corner = new GridPosition(3, 4) });

        Assert.True(line.CanCast, line.Refusal);
        Assert.Equal(bandits, line.Targets.OrderBy(t => t.Position.X));
        Assert.DoesNotContain(mirella, line.Targets);
        Assert.Equal(2, block.Targets.Count);
        Assert.Equal(block.Cost, line.Cost);
    }

    [Fact]
    public void TheShape_IsTheCastersToName_WithinWhatTheSpellIsCastAt()
    {
        var (encounter, mirella, bandits) = Row();
        var smite = mirella.Spells.Single(s => s.Name == "Smite");
        var scourging = mirella.Spells.Single(s => s.Name == "Scourging Light");

        Assert.Contains("cast at one", encounter.PlanCast(mirella, new SpellCast(smite) { Shape = SpellShape.Line, Origin = new GridPosition(2, 4), Aim = (1, 0) }).Refusal);
        Assert.Contains("cast over many", encounter.PlanCast(mirella, new SpellCast(scourging) { Shape = SpellShape.Ray, Target = bandits[0] }).Refusal);
        Assert.Contains("eight ways", encounter.PlanCast(mirella, new SpellCast(scourging) { Shape = SpellShape.Cone, Origin = new GridPosition(2, 4) }).Refusal);
    }

    [Fact]
    public void TheBrain_LaysItsAreaHarmWhereItCatchesMost()
    {
        var (encounter, mirella, _) = Row();
        var planner = new Combat.Brains.Utility.TurnPlanner(new Combat.Brains.Utility.Assessor(encounter.View), Combat.Brains.Utility.BrainSkill.Best);

        var area = planner.Options(mirella, hasMove: false, actions: 1, real: true)
            .Where(o => o.Cast is { Spell.Name: "Scourging Light" })
            .MaxBy(o => o.Score);

        Assert.NotNull(area);
        Assert.Equal(3, encounter.PlanCast(mirella, area.Cast!).Targets.Count);
    }
}
