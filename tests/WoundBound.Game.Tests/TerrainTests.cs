using WoundBound.Game.Combat;
using WoundBound.Game.Combat.Brains;
using WoundBound.Game.Combat.Brains.Utility;
using WoundBound.Rules.Enums;

namespace WoundBound.Game.Tests;

/// <summary>
/// High ground and breaking cover (Core Rules §9; the user's rulings, 2026-11-08): +1 to strike down
/// at someone below, +1 Dodge Threshold against someone striking up, climbing costs as Broken ground,
/// and cover worn down by blades until it drops a grade.
/// </summary>
public class TerrainTests
{
    private static readonly Game.Data.GameData Data = TestData.Load();

    private static (Encounter Encounter, Combatant Fighter, Combatant Biter) Board(Setting setting, GridPosition fighterAt, GridPosition biterAt, bool bow, params int[] dice)
    {
        var fighter = CombatantFactory.FromCharacter(SampleEncounters.DumbFighter(Data), fighterAt);
        var biter = CombatantFactory.FromAdversary(Data.Adversary("Bramble Ratkin"), "Ratkin Biter", biterAt);
        var encounter = new Encounter(SampleEncounters.BoardWidth, SampleEncounters.BoardHeight, new[] { fighter, biter },
            Data.RangeBands, new ScriptedDice(dice), setting: setting, environment: Data.Environment);
        if (bow)
        {
            encounter.TrySwapLoadout();
        }

        return (encounter, fighter, biter);
    }

    private static Setting Ridge(params (int, int, int, int)[] squares) => new() { Name = "A ridge", Raised = Setting.Squares(squares) };

    [Fact]
    public void FromHighGround_AnAttackerStrikesDownAtPlusOne_AndAnyoneStrikingUpFacesPlusOneDodge()
    {
        var (flat, archer0, target0) = Board(Setting.Daylight, new GridPosition(2, 4), new GridPosition(8, 4), bow: true);
        var bare = flat.PlanAttack(archer0, target0);

        var (above, archer, target) = Board(Ridge((2, 4, 2, 4)), new GridPosition(2, 4), new GridPosition(8, 4), bow: true);
        var down = above.PlanAttack(archer, target);
        Assert.Equal(1, down.HeightAttackBonus);
        Assert.Equal(bare.DodgeThreshold, down.DodgeThreshold);

        var (below, archer2, target2) = Board(Ridge((8, 4, 8, 4)), new GridPosition(2, 4), new GridPosition(8, 4), bow: true);
        var up = below.PlanAttack(archer2, target2);
        Assert.Equal(0, up.HeightAttackBonus);
        Assert.Equal(bare.DodgeThreshold + 1, up.DodgeThreshold);

        var (level, archer3, target3) = Board(Ridge((2, 4, 2, 4), (8, 4, 8, 4)), new GridPosition(2, 4), new GridPosition(8, 4), bow: true);
        Assert.Equal(bare.DodgeThreshold, level.PlanAttack(archer3, target3).DodgeThreshold);
        Assert.Equal(0, level.PlanAttack(archer3, target3).HeightAttackBonus);
    }

    [Fact]
    public void SteppingUpOntoHighGround_CostsAsBrokenGroundDoes_AndSteppingDownIsFree()
    {
        var (encounter, fighter, _) = Board(Ridge((3, 4, 3, 4)), new GridPosition(2, 4), new GridPosition(8, 4), bow: false);
        var up = encounter.PlanMove(fighter, new GridPosition(3, 4))!;
        var flat = encounter.PlanMove(fighter, new GridPosition(2, 5))!;
        Assert.True(up.Cost > flat.Cost, $"up {up.Cost}, flat {flat.Cost}");

        var (top, onRidge, _) = Board(Ridge((2, 4, 2, 4)), new GridPosition(2, 4), new GridPosition(8, 4), bow: false);
        Assert.Equal(flat.Cost, top.PlanMove(onRidge, new GridPosition(3, 4))!.Cost);
    }

    [Fact]
    public void ABladeTakenToPartialCover_WearsItDown_AndSmashesItOnceWornThrough()
    {
        var setting = new Setting { Name = "A barrel", CoverPieces = Setting.CoverOf(Cover.Partial, (3, 4, 3, 4)) };
        var (encounter, fighter, _) = Board(setting, new GridPosition(2, 4), new GridPosition(8, 4), false, 1, 1, 1, 1, 1, 1);
        encounter.TryBeginTurn(fighter);
        var before = encounter.CoverLeft(new GridPosition(3, 4))!.Value;
        Assert.Equal(Encounter.PartialToughness, before);

        Assert.True(encounter.TryBreakCover(new GridPosition(3, 4)));

        var left = encounter.CoverLeft(new GridPosition(3, 4));
        Assert.True(left is null || left < before);
        Assert.Contains(encounter.Log, line => line.Contains("cover at (3,4)"));
    }

    [Fact]
    public void WornThrough_FullCoverDropsToPartial_AndPartialIsGone()
    {
        var setting = new Setting
        {
            Name = "Crates",
            CoverPieces = Setting.CoverOf(Cover.Full, (3, 4, 3, 4)),
            CoverToughness = new Dictionary<GridPosition, int> { [new GridPosition(3, 4)] = 1 },
        };
        var (encounter, fighter, _) = Board(setting, new GridPosition(2, 4), new GridPosition(8, 4), false, 1, 1, 1, 1, 1, 1);
        encounter.TryBeginTurn(fighter);

        Assert.True(encounter.TryBreakCover(new GridPosition(3, 4)));
        Assert.Equal(Cover.Partial, encounter.Setting.CoverAt(new GridPosition(3, 4)));

        fighter.ActionsRemaining = 1;
        Assert.True(encounter.TryBreakCover(new GridPosition(3, 4)));
        Assert.Equal(Cover.None, encounter.Setting.CoverAt(new GridPosition(3, 4)));
    }

    [Fact]
    public void CoverOutOfReach_OrUnbreakable_CantBeBroken()
    {
        var setting = new Setting
        {
            Name = "Stones",
            CoverPieces = Setting.CoverOf(Cover.Partial, (3, 4, 3, 4), (6, 4, 6, 4)),
            Unbreakable = Setting.Squares((3, 4, 3, 4)),
        };
        var (encounter, fighter, _) = Board(setting, new GridPosition(2, 4), new GridPosition(8, 4), bow: false);
        encounter.TryBeginTurn(fighter);

        Assert.NotNull(encounter.BreakCoverRefusal(fighter, new GridPosition(3, 4)));
        Assert.NotNull(encounter.BreakCoverRefusal(fighter, new GridPosition(6, 4)));
    }

    [Fact]
    public void TheBrain_WeighsBreakingCover_ThatShieldsAnEnemyBehindIt()
    {
        var setting = new Setting { Name = "Crates", CoverPieces = Setting.CoverOf(Cover.Full, (3, 4, 3, 4)) };
        var (encounter, fighter, biter) = Board(setting, new GridPosition(2, 4), new GridPosition(4, 4), bow: false);
        var planner = new TurnPlanner(new Assessor(encounter.View), BrainSkill.Best);

        var options = planner.Options(fighter, hasMove: false, actions: 1, real: true);

        Assert.Contains(options, o => o.Kind == OptionKind.BreakCover && o.Square == new GridPosition(3, 4));
    }
}
