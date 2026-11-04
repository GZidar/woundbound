using WoundBound.Game.Combat;
using WoundBound.Game.Figures;

namespace WoundBound.Game.Tests;

/// <summary>Each creature's body, picked from its statblock's Type and Size and refined by its name.</summary>
public class BodyShapeTests
{
    private static readonly Game.Data.GameData Data = TestData.Load();

    private static BodyShape Of(string adversary) =>
        BodyShape.Of(CombatantFactory.FromAdversary(Data.Adversary(adversary), adversary, new GridPosition(0, 0), Data.Spells));

    [Theory]
    [InlineData("Tollroad Bandit", BodyPlan.Humanoid, HeadShape.Human, BodyStuff.Flesh)]
    [InlineData("Bramble Ratkin", BodyPlan.Humanoid, HeadShape.Rat, BodyStuff.Fur)]
    [InlineData("Duskfang Gnoll", BodyPlan.Humanoid, HeadShape.Hyena, BodyStuff.Fur)]
    [InlineData("Timber Wolf", BodyPlan.Quadruped, HeadShape.Hound, BodyStuff.Fur)]
    [InlineData("Cave Bear", BodyPlan.Quadruped, HeadShape.Bear, BodyStuff.Fur)]
    [InlineData("Vampire Bat", BodyPlan.Winged, HeadShape.Bat, BodyStuff.Fur)]
    [InlineData("Fenmoor Ratswarm", BodyPlan.Swarm, HeadShape.Rat, BodyStuff.Fur)]
    [InlineData("Grave-Bound Husk", BodyPlan.Humanoid, HeadShape.Human, BodyStuff.Dead)]
    [InlineData("Animated Suit of Mail", BodyPlan.Humanoid, HeadShape.Hollow, BodyStuff.Metal)]
    [InlineData("Stoneheart Golem", BodyPlan.Humanoid, HeadShape.Human, BodyStuff.Stone)]
    [InlineData("Weeping Sentinel", BodyPlan.Humanoid, HeadShape.Human, BodyStuff.Stone)]
    [InlineData("The Ashen Warden", BodyPlan.Spirit, HeadShape.Human, BodyStuff.Ghost)]
    public void EachAdversary_GetsTheBodyItsStatblockDescribes(string adversary, BodyPlan plan, HeadShape head, BodyStuff stuff)
    {
        var shape = Of(adversary);

        Assert.Equal((plan, head, stuff), (shape.Plan, shape.Head, shape.Stuff));
    }

    [Fact]
    public void LargeHumanoids_AreHulking()
    {
        Assert.True(Of("Quarrymarch Ogrekin").Hulking);
        Assert.False(Of("Tollroad Bandit").Hulking);
    }

    [Fact]
    public void TheHollowStag_HasAntlers_AndCharacters_ArePeople()
    {
        var stag = CombatantFactory.FromAdversary(SampleEncounters.HollowStag(Data), "The Hollow Stag", new GridPosition(0, 0), Data.Spells);
        Assert.Equal(new BodyShape(BodyPlan.Quadruped, HeadShape.Deer, BodyStuff.Fur, Tail: true, Antlers: true), BodyShape.Of(stag));

        var fighter = CombatantFactory.FromCharacter(SampleEncounters.DumbFighter(Data), new GridPosition(0, 0));
        Assert.Equal(BodyShape.Person, BodyShape.Of(fighter));
    }

    [Fact]
    public void ASteed_IsAHorse()
    {
        var rider = CombatantFactory.Mounted(CombatantFactory.FromAdversary(Data.Adversary("Tollroad Bandit"), "Outrider", new GridPosition(0, 0)));

        Assert.Equal(BodyShape.Person, BodyShape.Of(rider));
        Assert.Equal(BodyShape.Horse, BodyShape.Of(rider.Steed!));
    }
}
