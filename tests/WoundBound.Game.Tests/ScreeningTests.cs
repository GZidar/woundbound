using WoundBound.Game.Combat;
using WoundBound.Game.Combat.Brains.Utility;

namespace WoundBound.Game.Tests;

/// <summary>
/// Holding ground (the user's direction, 2026-11-02): a martial standing in a gap is worth what it
/// keeps the other side off — the softer ones behind it — which on open ground is nothing.
/// </summary>
public class ScreeningTests
{
    private static readonly Game.Data.GameData Data = TestData.Load();

    /// <summary>
    /// Joren at (5, 5), Mirella behind him at (4, 5), a bandit in front at (8, 5) — a move from her. With
    /// <paramref name="corridor"/>, walls fill rows 4 and 6, so row 5 is the only way through.
    /// </summary>
    private static (Encounter Encounter, Combatant Joren) Gap(bool corridor)
    {
        var joren = CombatantFactory.FromCharacter(SampleEncounters.JorenBlacke(Data), new GridPosition(5, 5));
        var mirella = CombatantFactory.FromCharacter(SampleEncounters.MirellaVask(Data), new GridPosition(4, 5));
        var bandit = CombatantFactory.FromAdversary(SampleEncounters.RoadsideBandit, "Bandit", new GridPosition(8, 5));
        var setting = corridor
            ? Setting.Daylight with { Walls = Setting.Squares((0, 0, 27, 4), (0, 6, 27, 17)) }
            : Setting.Daylight;
        var encounter = new Encounter(SampleEncounters.FieldWidth, SampleEncounters.FieldHeight, [joren, mirella, bandit], Data.RangeBands,
            new ScriptedDice(), setting: setting, environment: Data.Environment);
        return (encounter, joren);
    }

    [Fact]
    public void HoldingACorridor_KeepsTheFoeOffTheOneBehind_OpenGroundDoesNot()
    {
        var (corridor, inTheGap) = Gap(corridor: true);
        var (open, inTheOpen) = Gap(corridor: false);

        var held = new TurnPlanner(new Assessor(corridor.View), BrainSkill.Best).Screening(inTheGap);
        var none = new TurnPlanner(new Assessor(open.View), BrainSkill.Best).Screening(inTheOpen);

        Assert.True(held > 0, $"corridor {held:0.00}");
        Assert.Equal(0, none, 3);
    }
}
