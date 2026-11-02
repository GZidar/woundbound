using WoundBound.Game.Combat;
using WoundBound.Rules.Enums;

namespace WoundBound.Game.Tests;

/// <summary>
/// What's on the map: walls and the shortest route round them (Core Rules §9, "Counting the
/// squares"), who can be passed through, line of sight, water by depth, and a hazard (GM Reference
/// §13) — the Undertow at Longreach Crossing.
/// </summary>
public class MapTests
{
    private static readonly Game.Data.GameData Data = TestData.Load();

    /// <summary>The Dumb Fighter and the Biter on a 10×8 field, wherever the test puts them, in <paramref name="setting"/>.</summary>
    private static (Encounter Encounter, Combatant Fighter, Combatant Biter) Board(Setting setting, GridPosition fighterAt, GridPosition biterAt, params int[] dice)
    {
        var fighter = CombatantFactory.FromCharacter(SampleEncounters.DumbFighter(Data), fighterAt);
        var biter = CombatantFactory.FromAdversary(Data.Adversary("Bramble Ratkin"), "Ratkin Biter", biterAt);
        var encounter = new Encounter(SampleEncounters.BoardWidth, SampleEncounters.BoardHeight, new[] { fighter, biter },
            Data.RangeBands, new ScriptedDice(dice), setting: setting, environment: Data.Environment);
        return (encounter, fighter, biter);
    }

    private static Combatant Named(Encounter encounter, string name) => encounter.Combatants.Single(c => c.Name == name);

    /// <summary>A corridor along row 4 from x 2 to 6, walled above and below.</summary>
    private static HashSet<GridPosition> Corridor() => Setting.Squares((2, 3, 6, 3), (2, 5, 6, 5));

    [Fact]
    public void AMove_GoesTheShortestWayRoundAWall_AndIsPricedByThatRoute()
    {
        // A wall down column 3 from row 2 to 6: four squares as the crow flies, six round the end.
        var setting = new Setting { Name = "Wall", Walls = Setting.Squares((3, 2, 3, 6)) };
        var (encounter, fighter, _) = Board(setting, new GridPosition(1, 4), new GridPosition(9, 0));

        var plan = encounter.PlanMove(fighter, new GridPosition(5, 4))!;

        Assert.Equal(7, plan.Path.Count);
        Assert.DoesNotContain(plan.Path, setting.IsWall);
        Assert.Equal(2, plan.Cost); // six squares is Middle
        Assert.Null(encounter.PlanMove(fighter, new GridPosition(3, 4))); // nobody stands in a wall
    }

    [Fact]
    public void AnEnemysSquare_IsNeverOpen()
    {
        // The corridor's only way through is past the Biter standing in it.
        var setting = new Setting { Name = "Corridor", Walls = Corridor() };
        var (encounter, fighter, _) = Board(setting, new GridPosition(1, 4), new GridPosition(4, 4));

        var plan = encounter.PlanMove(fighter, new GridPosition(7, 4));

        Assert.True(plan is null || !plan.Path.Contains(new GridPosition(4, 4)));
    }

    [Fact]
    public void PassingThroughAnEnemysReach_AndOut_DrawsItsReaction()
    {
        // The Biter stands in a gap in the corridor's south wall. Running the corridor end to end
        // starts and ends out of its reach, but passes through it — and leaving it is the trigger.
        // It bites as the fighter goes: 2+3, a miss.
        var walls = Corridor();
        walls.Remove(new GridPosition(4, 5));
        var (encounter, fighter, biter) = Board(new Setting { Name = "Corridor", Walls = walls }, new GridPosition(1, 4), new GridPosition(4, 5), 2, 3);
        biter.Stamina = 2;

        Assert.True(encounter.TryMove(new GridPosition(7, 4)));

        Assert.Contains(encounter.Log, line => line.StartsWith("The Dumb Fighter leaves Ratkin Biter's reach, and it strikes as they go"));
        Assert.Equal(new GridPosition(7, 4), fighter.Position);
    }

    [Fact]
    public void AWall_BlocksAShot()
    {
        var setting = new Setting { Name = "Wall", Walls = Setting.Squares((5, 0, 5, 7)) };
        var (encounter, fighter, biter) = Board(setting, new GridPosition(2, 4), new GridPosition(8, 4));
        encounter.TrySwapLoadout(); // the short bow

        Assert.False(encounter.PlanAttack(fighter, biter).CanAttack);
        Assert.Contains("solid", encounter.PlanAttack(fighter, biter).Refusal!);
    }

    [Fact]
    public void AWall_BlocksLight()
    {
        // Night, and the fighter's torch: the Biter is three squares off, within Short — but behind a wall.
        var night = SampleEncounters.SignpostNight with { Ground = new Dictionary<GridPosition, Terrain>(), Walls = Setting.Squares((3, 0, 3, 7)) };
        var fighter = CombatantFactory.FromCharacter(SampleEncounters.DumbFighter(Data, torch: true), new GridPosition(1, 4));
        var biter = CombatantFactory.FromAdversary(Data.Adversary("Bramble Ratkin"), "Ratkin Biter", new GridPosition(4, 4));
        var encounter = new Encounter(10, 8, new[] { fighter, biter }, Data.RangeBands, new ScriptedDice(), setting: night, environment: Data.Environment);

        Assert.Equal(Visibility.None, encounter.VisibilityOf(biter));
        Assert.Equal(Visibility.Poor, encounter.VisibilityOf(fighter));
    }

    [Fact]
    public void ShovedIntoAWall_TheDistanceLeftIsBodyWounds()
    {
        // 5+4 +4 = 13 against ST 11: the Biter is shoved into the wall behind it and, a Minion, dies of the wound.
        var setting = new Setting { Name = "Wall", Walls = Setting.Squares((8, 0, 8, 7)) };
        var (encounter, _, biter) = Board(setting, new GridPosition(6, 2), new GridPosition(7, 2), 5, 4);
        biter.Stamina = 0;

        encounter.TryControl(ControlAction.Shove, biter);

        Assert.True(biter.IsOutOfFight);
        Assert.Contains(encounter.Log, line => line.StartsWith("Ratkin Biter is driven into the wall"));
    }

    [Fact]
    public void Water_IsBrokenGround_WhateverItsDepth()
    {
        var crossing = SampleEncounters.LongreachCrossing;

        Assert.Equal(Terrain.Broken, crossing.GroundAt(new GridPosition(12, 8)));  // the ford
        Assert.Equal(Terrain.Broken, crossing.GroundAt(new GridPosition(12, 2)));  // deep, upstream
        Assert.True(crossing.IsDeepWater(new GridPosition(12, 2)));
        Assert.False(crossing.IsDeepWater(new GridPosition(12, 8)));
        Assert.Equal(Terrain.Firm, crossing.GroundAt(new GridPosition(5, 8)));     // the bank
    }

    [Fact]
    public void DeepWater_HasToBeSwum_AndAFailedSwimGetsNowhere()
    {
        // Athletics +5 (Tier 1, Strength 3, Martial's Vitality 1) against 18: 2+3 = 10 fails, 9+8 = 22 gets there.
        var setting = new Setting { Name = "River", Water = Setting.Squares((4, 0, 4, 7)).ToDictionary(sq => sq, _ => WaterDepth.Deep) };
        var (encounter, fighter, biter) = Board(setting, new GridPosition(3, 4), new GridPosition(9, 0), 2, 3, 9, 8);
        biter.Stamina = 0; // it closes in between the tries; no reaction as he swims off
        Assert.Equal(5, fighter.SkillBonus("Athletics"));

        // Into the water is Broken ground: the whole turn, move and action.
        Assert.Equal(2, encounter.MoveCostTo(fighter, new GridPosition(5, 4)));
        Assert.True(encounter.TryMove(new GridPosition(5, 4)));
        Assert.Equal(new GridPosition(3, 4), fighter.Position);
        Assert.Contains(encounter.Log, line => line.StartsWith("The Dumb Fighter struggles in the deep water and gets nowhere"));

        // Next turn, he tries again. The biter is awake for its own turn (on 0 it would fall
        // unconscious), and spent again before he swims off.
        encounter.EndTurn();
        biter.Stamina = 1;
        while (encounter.Phase == EncounterPhase.AdversaryTurn)
        {
            encounter.RunAdversaryTurn();
        }

        biter.Stamina = 0;

        Assert.True(encounter.TryMove(new GridPosition(5, 4)));
        Assert.Equal(new GridPosition(5, 4), fighter.Position);
    }

    [Fact]
    public void TheUndertow_SweepsAFailedWaderDownstream_OffTheirFooting()
    {
        // Mid-ford, the fighter wades a square on: Athletics +5 against 18, and 2+3 fails. Swept a
        // Short distance — four squares — downstream; he doesn't spend Stamina to arrest it.
        var encounter = SampleEncounters.FerryShrineAmbush(Data, new ScriptedDice(2, 3));
        var fighter = encounter.Combatants.Single(c => c.Name == "The Dumb Fighter");
        encounter.TryBeginTurn(fighter);

        Assert.True(encounter.TryMove(new GridPosition(13, 8)));
        Assert.Equal(ReactionKind.Arrest, encounter.Pending?.Kind);
        Assert.True(encounter.RespondToReaction(spend: false));

        Assert.Equal(new GridPosition(13, 12), fighter.Position);
        Assert.True(fighter.NeedsFooting);
        Assert.Contains(encounter.Log, line => line.StartsWith("The Dumb Fighter meets the Undertow: Athletics"));

        // Wading cost him the action as well as the move (Broken ground), so he buys another —
        // and nothing but regaining his footing can have it.
        Assert.Contains("takes an action", Encounter.RegainFootingRefusal(fighter));
        Assert.True(encounter.TryBuyExtraAction());
        Assert.NotNull(Encounter.AidRefusal(fighter, CombatAction.Help, Named(encounter, "Salla Rinehart")));
        Assert.True(encounter.TryRegainFooting());
        Assert.False(fighter.NeedsFooting);
    }

    [Fact]
    public void BotsPlayTheFord_WithinTheRules()
    {
        var report = Game.Simulation.Simulator.Run(
            dice => SampleEncounters.FerryShrineAmbush(Data, dice, new Combat.Brains.Utility.UtilityGmBrain()),
            () => new Combat.Brains.Utility.UtilityPartyBrain(), fights: 6);

        Assert.Equal(6, report.Fights.Count);
    }
}
