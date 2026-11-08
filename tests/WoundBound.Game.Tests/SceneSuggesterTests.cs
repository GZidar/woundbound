using System.Text.Json;
using Anthropic.Models.Beta.Messages;
using WoundBound.Game.Combat;

namespace WoundBound.Game.Tests;

/// <summary>Reading Claude's suggestion for a battlemap into the editor's layers — no call to the API here.</summary>
public class SceneSuggesterTests
{
    [Fact]
    public void ASuggestion_FillsTheLayers_KeepingOnlyWhatEachLayerAllows()
    {
        var map = SceneMap.Blank(4, 3);
        var reply = """
            {"solid":["#F..","..P?","...."],"water":["....","..sd","x..."],"ground":["b...","....","r..."],
             "elevation":["rc..","....","...."],
             "props":[{"x":1,"y":0,"prop":"Tree"},{"x":2,"y":1,"prop":"Tree"},{"x":2,"y":1,"prop":"Barrel"},{"x":9,"y":9,"prop":"Rock"}],
             "notes":"The ford might be deeper."}
            """;

        SceneSuggester.Apply(map, SceneSuggester.Parse(reply));
        var setting = map.ToSetting();

        Assert.True(setting.IsWall(new GridPosition(0, 0)));
        Assert.Equal(WoundBound.Rules.Enums.Cover.Full, setting.CoverAt(new GridPosition(1, 0)));
        Assert.Equal('.', SceneMap.At(map.Solid, 3, 1));            // '?' isn't a solid character
        Assert.True(setting.IsDeepWater(new GridPosition(3, 1)));
        Assert.Equal('.', SceneMap.At(map.Water, 0, 2));
        Assert.Contains(new GridPosition(1, 0), setting.Scenery.Cliffs);
        Assert.Equal(Prop.Tree, setting.Scenery.Props[new GridPosition(1, 0)]);
        Assert.Equal(Prop.Barrel, setting.Scenery.Props[new GridPosition(2, 1)]); // a tree on Partial cover doesn't fit
        Assert.Equal(2, map.Props.Count);
    }

    [Fact]
    public void ShortOrMissingRows_AreReadAsOpenGround()
    {
        var map = SceneMap.Blank(5, 3);
        SceneSuggester.Apply(map, SceneSuggester.Parse("""{"solid":["#"],"water":[],"ground":[],"elevation":[],"props":[],"notes":""}"""));

        Assert.Equal("#....", map.Solid[0]);
        Assert.Equal(".....", map.Solid[2]);
    }

    [Fact]
    public void ThePrompt_GivesTheGridInThePicturesPixels()
    {
        var prompt = SceneSuggester.Prompt(29, 43, 3.5, 9, 24.9);

        Assert.Contains("29 columns by 43 rows", prompt);
        Assert.Contains("(3.5, 9)", prompt);
        Assert.Contains("24.9 pixels", prompt);
    }

    [Fact]
    public void TheSchema_ListsEveryProp()
    {
        var schema = JsonSerializer.Serialize(SceneSuggester.Schema());

        Assert.All(Enum.GetNames<Prop>(), name => Assert.Contains($"\"{name}\"", schema));
    }

    [Fact]
    public void TheDefaultFallback_IsSentAsTheWordDefault()
    {
        Assert.Equal("\"default\"", JsonSerializer.Serialize((BetaFallbacksParam)new Default()));
    }
}
