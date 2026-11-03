using WoundBound.Game.Combat;
using WoundBound.Game.Data;

namespace WoundBound.Game.Tests;

/// <summary>The records the screen animates from: each one raised as its line is written, saying what the line says.</summary>
public class HappeningTests
{
    private static readonly GameData Data = TestData.Load();

    private static (Encounter Fight, Combatant Fighter, List<Happening> Seen) Fight(params int[] dice)
    {
        var encounter = SampleEncounters.FighterVersusRatkin(Data, new ScriptedDice(dice));
        var seen = new List<Happening>();
        encounter.Happened += seen.Add;
        var fighter = encounter.Combatants.Single(c => c.Side == Side.Party);
        Assert.True(encounter.TryMove(new GridPosition(4, 4)));
        encounter.EndTurn();
        return (encounter, fighter, seen);
    }

    [Fact]
    public void AHit_IsRaisedAsAStruckThatHit()
    {
        // 8+6 = 14 against DT 14, not dodged: a hit for d4 = 3.
        var (fight, fighter, seen) = Fight(8, 6, 3, 2);
        fight.RunAdversaryTurn();
        fight.RespondToReaction(spend: false);

        var struck = Assert.Single(seen.OfType<Struck>());
        Assert.Same(fighter, struck.Target);
        Assert.True(struck.Hit);
        Assert.False(struck.Dodged);
        Assert.False(struck.With.IsRanged);
    }

    [Fact]
    public void ADodgedCritical_IsRaisedAsARiposte()
    {
        var (fight, fighter, seen) = Fight(7, 7, 2, 3);
        fight.RunAdversaryTurn();
        fight.RespondToReaction(spend: true);

        var struck = seen.OfType<Struck>().First();
        Assert.Same(fighter, struck.Target);
        Assert.False(struck.Hit);
        Assert.True(struck.Dodged);
        Assert.True(struck.Riposte);
    }

    [Fact]
    public void AMove_IsRaisedAsTheRouteWalked_AheadOfWhatItLedTo()
    {
        var encounter = SampleEncounters.FighterVersusRatkin(Data, new ScriptedDice(8, 6, 3, 2));
        var seen = new List<Happening>();
        encounter.Happened += seen.Add;
        var fighter = encounter.Combatants.Single(c => c.Side == Side.Party);
        var start = fighter.Position;

        Assert.True(encounter.TryMove(new GridPosition(4, 4)));

        var walked = Assert.Single(seen.OfType<Moved>());
        Assert.Same(fighter, walked.Mover);
        Assert.Equal(start, walked.Path[0]);
        Assert.Equal(new GridPosition(4, 4), walked.Path[^1]);
        Assert.False(walked.Forced);

        encounter.EndTurn();
        encounter.RunAdversaryTurn();
        encounter.RespondToReaction(spend: false);

        // The biter closes before it bites, and its turn begins before either.
        var biter = seen.OfType<Struck>().First().Attacker;
        var order = seen.Select(h => h switch
        {
            TurnBegan t when t.Who == biter => "turn",
            Moved m when m.Mover == biter => "move",
            Struck => "strike",
            _ => null,
        }).Where(x => x is not null).ToList();
        Assert.Equal(["turn", "move", "strike"], order.Take(3));
    }

    [Fact]
    public void AGrab_IsRaisedAsHandsOnThatTook()
    {
        // 5+4 +4 = 13 against the Biter's ST 11: held.
        var encounter = SampleEncounters.FighterVersusRatkin(Data, new ScriptedDice(5, 4));
        var fighter = encounter.Active!;
        var biter = encounter.Combatants.Single(c => c.Name == "Ratkin Biter");
        fighter.Position = new GridPosition(6, 2);
        biter.Position = new GridPosition(7, 2);
        var seen = new List<Happening>();
        encounter.Happened += seen.Add;

        encounter.TryControl(ControlAction.Grab, biter);

        var grab = Assert.Single(seen.OfType<Handled>());
        Assert.Equal(ControlAction.Grab, grab.Action);
        Assert.Same(fighter, grab.Actor);
        Assert.True(grab.Worked);
    }

    [Fact]
    public void AShoveThatTakes_IsFollowedByThePush()
    {
        var encounter = SampleEncounters.FighterVersusRatkin(Data, new ScriptedDice(5, 4));
        var fighter = encounter.Active!;
        var biter = encounter.Combatants.Single(c => c.Name == "Ratkin Biter");
        fighter.Position = new GridPosition(6, 2);
        biter.Position = new GridPosition(7, 2);
        var seen = new List<Happening>();
        encounter.Happened += seen.Add;

        encounter.TryControl(ControlAction.Shove, biter);

        // Pushed out of the fighter's own reach, the Biter could be struck as it goes; let it go.
        Assert.True(encounter.RespondToLeaver(LeaverResponse.LetThemGo));

        var shove = seen.OfType<Handled>().Single();
        Assert.True(shove.Worked);
        var push = seen.OfType<Moved>().Single(m => m.Mover == biter);
        Assert.True(push.Forced);
        Assert.True(seen.IndexOf(shove) < seen.IndexOf(push));
    }
}
