using WoundBound.Game.Combat;
using WoundBound.Game.Figures;
using WoundBound.Rules.Enums;

namespace WoundBound.Game.Tests;

/// <summary>What the board draws on each square: props that always fit the cover they stand for, and ground worked out where the scene says nothing.</summary>
public class SceneryPlanTests
{
    private static readonly Game.Data.GameData Data = TestData.Load();

    public static TheoryData<string> Fights() => new() { "signpost", "ambush", "ford", "camp", "clearing", "roadblock", "strongbox", "hedge", "causeway", "cloister" };

    private static Setting SettingOf(string fight)
    {
        var dice = new RandomDiceRoller(new Random(1));
        return fight switch
        {
            "signpost" => SampleEncounters.SignpostNight,
            "ambush" => SampleEncounters.SignpostAmbushNight,
            "ford" => SampleEncounters.LongreachCrossing,
            "camp" => SampleEncounters.CampAtNight,
            "clearing" => SampleEncounters.WoodlandAtDusk,
            "roadblock" => SampleEncounters.Roadblock(Data, dice).Setting,
            "strongbox" => SampleEncounters.StrongboxRaid(Data, dice).Setting,
            "hedge" => SampleEncounters.HedgeRoad,
            "causeway" => SampleEncounters.FenCauseway,
            "cloister" => SampleEncounters.ShrineGuardians(Data, dice).Setting,
            _ => throw new ArgumentException(fight),
        };
    }

    [Theory]
    [MemberData(nameof(Fights))]
    public void EverySolidSquare_GetsAPropAsTallAsItsCover(string fight)
    {
        var setting = SettingOf(fight);
        var plan = new SceneryPlan(setting);

        foreach (var wall in setting.Walls)
        {
            Assert.True(plan.PropAt(wall)!.Value.IsTall(), $"{fight}: wall at {wall} drawn as {plan.PropAt(wall)}");
        }

        foreach (var (square, grade) in setting.CoverPieces)
        {
            Assert.Equal(grade == Cover.Full, plan.PropAt(square)!.Value.IsTall());
        }

        Assert.Equal(setting.Walls.Count + setting.CoverPieces.Count, plan.Props.Count);
    }

    [Fact]
    public void ANamedPropOfTheWrongHeight_IsReplaced()
    {
        // A bush named on Full cover would show a square that hides you completely as one you could be shot over.
        var square = new GridPosition(3, 3);
        var setting = Setting.Daylight with
        {
            CoverPieces = new Dictionary<GridPosition, Cover> { [square] = Cover.Full },
            Scenery = new Scenery { Props = new Dictionary<GridPosition, Prop> { [square] = Prop.Bush } },
        };

        Assert.True(new SceneryPlan(setting).PropAt(square)!.Value.IsTall());
    }

    [Fact]
    public void ANamedPropThatFits_IsKept()
    {
        var square = new GridPosition(3, 3);
        var setting = Setting.Daylight with
        {
            CoverPieces = new Dictionary<GridPosition, Cover> { [square] = Cover.Partial },
            Scenery = new Scenery { Props = new Dictionary<GridPosition, Prop> { [square] = Prop.Barrel } },
        };

        Assert.Equal(Prop.Barrel, new SceneryPlan(setting).PropAt(square));
    }

    [Fact]
    public void CoverWornDown_KeepsWhatItWas()
    {
        // A tree hacked down to Partial is a felled log, not a bush that sprang up in its place.
        var square = new GridPosition(4, 4);
        Setting With(Cover grade) => Setting.Daylight with
        {
            CoverPieces = new Dictionary<GridPosition, Cover> { [square] = grade },
            Scenery = new Scenery { Biome = Biome.Woodland, Props = new Dictionary<GridPosition, Prop> { [square] = Prop.Tree } },
        };

        var before = new SceneryPlan(With(Cover.Full));
        var after = new SceneryPlan(With(Cover.Partial), before);

        Assert.Equal(Prop.Tree, before.PropAt(square));
        Assert.Equal(Prop.Log, after.PropAt(square));
        Assert.False(after.PropAt(square)!.Value.IsTall());
    }

    [Fact]
    public void Cover_IsDrawnForItsBiome()
    {
        var full = new GridPosition(2, 2);
        var partial = new GridPosition(6, 6);
        Setting In(Biome biome) => Setting.Daylight with
        {
            CoverPieces = new Dictionary<GridPosition, Cover> { [full] = Cover.Full, [partial] = Cover.Partial },
            Scenery = new Scenery { Biome = biome },
        };

        Assert.Equal(Prop.CrateStack, new SceneryPlan(In(Biome.Road)).PropAt(full));
        Assert.Contains(new SceneryPlan(In(Biome.Road)).PropAt(partial), new Prop?[] { Prop.Crate, Prop.Barrel });
        Assert.Contains(new SceneryPlan(In(Biome.Woodland)).PropAt(full), new Prop?[] { Prop.Tree, Prop.Pine, Prop.Birch });
        Assert.Equal(Prop.Rock, new SceneryPlan(In(Biome.Rocky)).PropAt(partial));
    }

    [Fact]
    public void ALoneWall_IsAPillar_AndARunIsAWall()
    {
        var setting = Setting.Daylight with { Walls = Setting.Squares((1, 1, 1, 1), (5, 5, 7, 5)) };
        var plan = new SceneryPlan(setting);

        Assert.Equal(Prop.Pillar, plan.PropAt(new GridPosition(1, 1)));
        Assert.Equal(Prop.StoneWall, plan.PropAt(new GridPosition(6, 5)));
    }

    [Fact]
    public void Ground_FollowsWhatTheSquareDoes()
    {
        var setting = SampleEncounters.LongreachCrossing;
        var plan = new SceneryPlan(setting);

        Assert.Equal(Surface.Sand, plan.SurfaceAt(new GridPosition(13, 2)));      // the river bed
        Assert.Equal(Surface.Road, plan.SurfaceAt(new GridPosition(4, 8)));       // named: the road down to the ford
        Assert.Equal(Surface.Sand, plan.SurfaceAt(new GridPosition(10, 2)));      // the bank
        Assert.Equal(Surface.Grass, plan.SurfaceAt(new GridPosition(3, 2)));      // open meadow
        Assert.Equal(Surface.Stone, new SceneryPlan(SampleEncounters.CampAtNight).SurfaceAt(new GridPosition(17, 2)));
    }

    [Fact]
    public void Variety_IsTheSameEveryTime()
    {
        var a = new SceneryPlan(SampleEncounters.WoodlandAtDusk);
        var b = new SceneryPlan(SampleEncounters.WoodlandAtDusk);

        Assert.Equal(a.Props, b.Props);
        Assert.Equal(SceneryPlan.Hash(new GridPosition(4, 7)), SceneryPlan.Hash(new GridPosition(4, 7)));
    }
}

