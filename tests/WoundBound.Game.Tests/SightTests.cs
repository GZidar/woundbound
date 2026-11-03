using WoundBound.Game.Combat;
using WoundBound.Rules.Enums;

namespace WoundBound.Game.Tests;

/// <summary>
/// What a creature can see (Core Rules §9): a line walls and mid-room full cover break, and light —
/// nothing past Close at Visibility None — and the fog of war one side keeps from it.
/// </summary>
public class SightTests
{
    private static readonly Game.Data.GameData Data = TestData.Load();

    private static (Encounter Encounter, Combatant Fighter, Combatant Biter) Board(Setting setting, GridPosition fighterAt, GridPosition biterAt)
    {
        var fighter = CombatantFactory.FromCharacter(SampleEncounters.DumbFighter(Data), fighterAt);
        var biter = CombatantFactory.FromAdversary(Data.Adversary("Bramble Ratkin"), "Ratkin Biter", biterAt);
        var encounter = new Encounter(SampleEncounters.BoardWidth, SampleEncounters.BoardHeight, new[] { fighter, biter },
            Data.RangeBands, new ScriptedDice(), setting: setting, environment: Data.Environment);
        return (encounter, fighter, biter);
    }

    private static Setting Dark => new() { Name = "Dark", Visibility = Visibility.None, Cause = VisibilityCause.Unlit };

    [Fact]
    public void InDaylight_TheWholeOpenFieldIsSeen()
    {
        var (encounter, fighter, biter) = Board(Setting.Daylight, new GridPosition(1, 4), new GridPosition(8, 4));

        Assert.True(encounter.CanSee(fighter, biter));
        Assert.Equal(SampleEncounters.BoardWidth * SampleEncounters.BoardHeight, encounter.SightOf(Side.Party).Count);
    }

    [Fact]
    public void AWall_HidesWhatIsBehindIt_ButNotTheWallItself()
    {
        var walled = new Setting { Name = "Wall", Walls = Setting.Squares((5, 0, 5, 7)) };
        var (encounter, fighter, biter) = Board(walled, new GridPosition(1, 4), new GridPosition(8, 4));

        Assert.False(encounter.CanSee(fighter, biter));
        Assert.True(encounter.CanSee(fighter, new GridPosition(5, 4)));
        Assert.False(encounter.CanSee(fighter, new GridPosition(7, 4)));
        Assert.False(encounter.SideSees(Side.Party, biter));
        Assert.True(encounter.SideSees(Side.Adversaries, biter)); // its own side always knows
    }

    [Fact]
    public void InTheDark_OnlyClose_IsSeen_WithoutLight()
    {
        var (encounter, fighter, biter) = Board(Dark, new GridPosition(1, 4), new GridPosition(8, 4));
        foreach (var item in fighter.Build!.InventoryItems.Where(i => i.IsLit))
        {
            item.IsLit = false;
        }

        Assert.False(encounter.CanSee(fighter, biter));
        Assert.True(encounter.CanSee(fighter, new GridPosition(2, 4)));
        Assert.False(encounter.CanSee(fighter, new GridPosition(5, 4)));

        biter.Position = new GridPosition(2, 5);
        Assert.True(encounter.CanSee(fighter, biter));
    }

    [Fact]
    public void ATorchbearer_IsSeenFromTheDark_ThatTheyCannotSeeInto()
    {
        // The night at the signpost: the ridge ambusher out past every light sees the lantern-bearer,
        // who can't see it back.
        var encounter = SampleEncounters.SignpostAmbush(Data, new ScriptedDice());
        var ridge = encounter.Combatants.Single(c => c.Name == "Ambusher on the ridge");
        var bearer = encounter.Combatants.Single(c => c.Name == "Lantern-bearer");
        var drovan = encounter.Combatants.Single(c => c.Name == "Drovan Thale");
        ridge.Position = new GridPosition(8, 12);
        if (ridge.SeesInDark)
        {
            return;
        }

        Assert.Equal(Visibility.None, encounter.VisibilityOf(ridge));
        Assert.False(encounter.CanSee(drovan, ridge));
        Assert.True(encounter.CanSee(ridge, bearer));
    }

    [Fact]
    public void Nightvision_SeesInTheDark()
    {
        var (encounter, fighter, _) = Board(Dark, new GridPosition(1, 4), new GridPosition(8, 4));
        var bat = CombatantFactory.FromAdversary(Data.Adversary("Vampire Bat"), "Vampire Bat", new GridPosition(8, 2));
        var withBat = new Encounter(SampleEncounters.BoardWidth, SampleEncounters.BoardHeight, new[] { fighter, bat },
            Data.RangeBands, new ScriptedDice(), setting: Dark, environment: Data.Environment);
        _ = encounter;

        Assert.True(bat.SeesInDark);
        Assert.True(withBat.CanSee(bat, new GridPosition(1, 1)));
    }

    [Fact]
    public void TheFog_RemembersGroundOnceSeen_ButNotWhoStandsThere()
    {
        var walled = new Setting { Name = "Wall", Walls = Setting.Squares((5, 0, 5, 6)) };
        var (encounter, fighter, biter) = Board(walled, new GridPosition(1, 4), new GridPosition(8, 4));
        var fog = new FogOfWar(encounter);

        Assert.Equal(FogState.Unseen, fog.StateOf(new GridPosition(8, 4)));
        Assert.False(fog.Shows(biter));
        Assert.True(fog.Shows(fighter));

        // Round the end of the wall, and the far side opens up.
        fighter.Position = new GridPosition(5, 7);
        Assert.True(fog.Update());
        Assert.Equal(FogState.Seen, fog.StateOf(new GridPosition(8, 4)));
        Assert.True(fog.Shows(biter));

        // Back again: the ground is remembered, the Biter isn't shown.
        fighter.Position = new GridPosition(1, 4);
        fog.Update();
        Assert.Equal(FogState.Remembered, fog.StateOf(new GridPosition(8, 4)));
        Assert.False(fog.Shows(biter));
        Assert.Equal((0, 0, 9, 7), fog.ExploredBounds);
    }
}
