using WoundBound.Game.Combat;
using WoundBound.Game.Figures;

namespace WoundBound.Game.Tests;

/// <summary>A character's figure: gender, height and girth, for telling characters apart on the board.</summary>
public class AppearanceTests
{
    private static readonly Game.Data.GameData Data = TestData.Load();

    [Fact]
    public void TheQuickstartFive_StartAsTheBooksDescribeThem_AndNoTwoAlike()
    {
        var five = new[]
        {
            SampleEncounters.SallaRinehart(Data), SampleEncounters.JorenBlacke(Data), SampleEncounters.DrovanThale(Data),
            SampleEncounters.MirellaVask(Data), SampleEncounters.ElthricDane(Data),
        }.Select(b => CombatantFactory.FromCharacter(b, new GridPosition(0, 0))).ToList();

        Assert.All(five, c => Assert.NotNull(c.Appearance));
        Assert.Equal(FigureGender.Female, five[0].Appearance!.Gender);
        Assert.Equal(FigureGender.Male, five[2].Appearance!.Gender);
        Assert.Equal(5, five.Select(c => c.Appearance).Distinct().Count());
    }

    [Fact]
    public void Adversaries_HaveNoAppearance_AndAreDrawnPlain()
    {
        var bandit = CombatantFactory.FromAdversary(Data.Adversary("Tollroad Bandit"), "Tollroad Bandit", new GridPosition(0, 0), Data.Spells);

        Assert.Null(bandit.Appearance);
    }

    [Theory]
    [InlineData(Stature.Tall, Girth.Thin)]
    [InlineData(Stature.Short, Girth.Large)]
    public void HeightAndGirth_ScaleTheFigure(Stature height, Girth girth)
    {
        var look = new Appearance(FigureGender.Other, height, girth);

        Assert.Equal(height == Stature.Tall, look.HeightScale > 1);
        Assert.Equal(girth == Girth.Large, look.GirthScale > 1);
    }
}
