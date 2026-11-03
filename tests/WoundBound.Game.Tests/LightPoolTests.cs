using WoundBound.Game.Combat;
using WoundBound.Rules.Enums;

namespace WoundBound.Game.Tests;

/// <summary>
/// Lights together (Core Rules §9): a light within one light's reach (Short, 4 squares) of another
/// pools with it, and pools chain — whoever carries them. A pool of two or more reads the "Two or
/// more carried" row: Good to Short, Poor to Middle.
/// </summary>
public class LightPoolTests
{
    private static readonly Game.Data.GameData Data = TestData.Load();

    private static Setting Dark => new() { Name = "Dark", Visibility = Visibility.None, Cause = VisibilityCause.Unlit };

    /// <summary>Ratkin on a wide dark field, those at <paramref name="lit"/> each holding one light.</summary>
    private static Encounter Field(GridPosition[] lit, params GridPosition[] dark)
    {
        var ratkin = Data.Adversary("Bramble Ratkin");
        var creatures = lit.Select((at, i) => Lit(CombatantFactory.FromAdversary(ratkin, $"Bearer {i}", at)))
            .Concat(dark.Select((at, i) => CombatantFactory.FromAdversary(ratkin, $"Watcher {i}", at)))
            .Append(CombatantFactory.FromCharacter(SampleEncounters.DumbFighter(Data), new GridPosition(0, 0)));
        return new Encounter(30, 10, creatures, Data.RangeBands, new ScriptedDice(), setting: Dark, environment: Data.Environment);

        static Combatant Lit(Combatant c)
        {
            c.StatblockLights = 1;
            return c;
        }
    }

    private static Combatant Named(Encounter e, string name) => e.Combatants.Single(c => c.Name == name);

    [Fact]
    public void ALightAlone_LightsShortAtPoor_AndNoFurther()
    {
        var e = Field([new GridPosition(10, 5)]);

        Assert.Equal(Visibility.Poor, e.VisibilityAtSquare(new GridPosition(13, 5)));
        Assert.Equal(Visibility.None, e.VisibilityAtSquare(new GridPosition(16, 5)));
    }

    [Fact]
    public void TwoLightsWithinReachOfEachOther_Pool()
    {
        // Three squares apart — not side by side, but each lighting the other.
        var e = Field([new GridPosition(10, 5), new GridPosition(13, 5)]);

        Assert.Equal(Visibility.Good, e.VisibilityAtSquare(new GridPosition(7, 5)));  // Short of the first
        Assert.Equal(Visibility.Poor, e.VisibilityAtSquare(new GridPosition(20, 5))); // Middle of the second
    }

    [Fact]
    public void LightsTooFarApart_DoNotPool()
    {
        var e = Field([new GridPosition(5, 5), new GridPosition(11, 5)]);

        Assert.Equal(Visibility.Poor, e.VisibilityAtSquare(new GridPosition(8, 5)));
        Assert.Equal(Visibility.None, e.VisibilityAtSquare(new GridPosition(16, 5)));
    }

    [Fact]
    public void PoolsChain_FromEndToEnd()
    {
        // Each four from the next: the ends are eight apart, but one pool through the middle.
        var e = Field([new GridPosition(4, 5), new GridPosition(8, 5), new GridPosition(12, 5)]);

        Assert.Equal(Visibility.Good, e.VisibilityAtSquare(new GridPosition(0, 5)));
        Assert.Equal(Visibility.Good, e.VisibilityAtSquare(new GridPosition(16, 5)));
    }

    [Fact]
    public void WhoseLightItIs_DoesNotMatter()
    {
        // The signpost at night: the fighter's torch and the bandits' lantern, on opposite sides.
        var e = SampleEncounters.SignpostAmbush(Data, new ScriptedDice());
        var fighter = Named(e, "The Dumb Fighter");
        var bearer = Named(e, "Lantern-bearer");
        Assert.True(fighter.CarriedLights > 0 && bearer.CarriedLights > 0);
        var beside = new GridPosition(fighter.Position.X, fighter.Position.Y - 3);
        bearer.Position = new GridPosition(fighter.Position.X + 9, fighter.Position.Y);
        Assert.Equal(Visibility.Poor, e.VisibilityAtSquare(beside));

        bearer.Position = new GridPosition(fighter.Position.X + 4, fighter.Position.Y);
        Assert.Equal(Visibility.Good, e.VisibilityAtSquare(beside));
    }

    [Fact]
    public void APooledLight_LightsWhoeverStandsInIt()
    {
        var e = Field([new GridPosition(10, 5), new GridPosition(13, 5)], new GridPosition(6, 5));

        Assert.Equal(Visibility.Good, e.VisibilityOf(Named(e, "Watcher 0")));
    }
}
