using WoundBound.Game.Combat;
using WoundBound.Rules.Enums;

namespace WoundBound.Game.Tests;

/// <summary>Scenes painted in the editor: what each layer's characters mean to the rules, and saving and loading them.</summary>
public class SceneMapTests
{
    private static readonly Game.Data.GameData Data = TestData.Load();

    private static SceneMap Painted()
    {
        var map = SceneMap.Blank(12, 8, "The test glade");
        map.Set(map.Solid, 2, 2, '#');
        map.Set(map.Solid, 3, 2, 'F');
        map.Set(map.Solid, 4, 2, 'P');
        map.Set(map.Solid, 5, 2, 'U');
        map.Set(map.Water, 6, 6, 's');
        map.Set(map.Water, 7, 6, 'd');
        map.Set(map.Ground, 1, 5, 'b');
        map.Set(map.Ground, 1, 6, 'r');
        map.Set(map.Heights, 9, 1, 'r');
        map.Set(map.Heights, 10, 1, 'c');
        foreach (var y in new[] { 3, 4, 5 })
        {
            map.Set(map.Zones, 0, y, 'p');
            map.Set(map.Zones, 1, y, 'p');
            map.Set(map.Zones, 10, y, 'h');
        }

        map.SetProp(3, 2, Prop.Pine);
        map.SetSurface(5, 5, Surface.Road);
        return map;
    }

    [Fact]
    public void EachLayer_MeansWhatItSays()
    {
        var setting = Painted().ToSetting();

        Assert.True(setting.IsWall(new GridPosition(2, 2)));
        Assert.Equal(Cover.Full, setting.CoverAt(new GridPosition(3, 2)));
        Assert.Equal(Cover.Partial, setting.CoverAt(new GridPosition(4, 2)));
        Assert.Contains(new GridPosition(5, 2), setting.Unbreakable);
        Assert.Equal(WaterDepth.Shallow, setting.Water[new GridPosition(6, 6)]);
        Assert.True(setting.IsDeepWater(new GridPosition(7, 6)));
        Assert.Equal(Terrain.Broken, setting.GroundAt(new GridPosition(1, 5)));
        Assert.Equal(Terrain.Sure, setting.GroundAt(new GridPosition(1, 6)));
        Assert.True(setting.IsRaised(new GridPosition(9, 1)));
        Assert.Contains(new GridPosition(10, 1), setting.Scenery.Cliffs);
        Assert.Equal(Prop.Pine, setting.Scenery.Props[new GridPosition(3, 2)]);
        Assert.True(setting.Deployment.Single().Hidden);
    }

    [Fact]
    public void ASavedScene_LoadsBackTheSame()
    {
        var map = Painted();
        map.Backdrop = new Backdrop("C:/maps/glade.jpg", 3.5f, 9, 25);
        var json = map.ToJson();
        var back = SceneMap.FromJson(json);

        Assert.Equal(json, back.ToJson());
        Assert.Contains("\"..#FPU......\"", json); // the solid layer's third row, as written
        Assert.Equal(map.Backdrop, back.Backdrop);
    }

    [Fact]
    public void GmZonesBeyondLong_AreWarnedOf()
    {
        var map = SceneMap.Blank(40, 10);
        map.Set(map.Zones, 0, 5, 'p');
        map.Set(map.Zones, 39, 5, 'h');

        Assert.Contains(map.Warnings(), w => w.Contains("beyond Long"));
    }

    [Fact]
    public void AScene_IsFoughtOn_WithThePartyWhereItWasPainted_AndTheBandInItsZone()
    {
        var map = SceneMap.Blank(SampleEncounters.FieldWidth, SampleEncounters.FieldHeight, "The test field");
        for (var y = 7; y <= 10; y++)
        {
            map.Set(map.Zones, 2, y, 'p');
            map.Set(map.Zones, 3, y, 'p');
            for (var x = 10; x <= 15; x++)
            {
                map.Set(map.Zones, x, y, 'h');
            }
        }

        var fight = SampleEncounters.OnScene(Data, new RandomDiceRoller(new Random(2)), map);

        Assert.All(fight.Combatants.Where(c => c.Side == Side.Party), c => Assert.Contains(c.Position, map.PartyStart));
        Assert.All(fight.Combatants.Where(c => c.Side == Side.Adversaries && !c.IsSteed), c => Assert.InRange(c.Position.X, 10, 15));
    }
}
