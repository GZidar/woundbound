using WoundBound.Game.Combat;

namespace WoundBound.Game.Tests;

/// <summary>Core Rules §9, "Rounds &amp; Turn Order": the fixed turn cap, and idle adversaries closing in.</summary>
public class RoundEndTests
{
    private static readonly Game.Data.GameData Data = TestData.Load();

    private static (Encounter Encounter, Combatant Fighter, Combatant Biter, Combatant Sneak, Combatant Shiv) Fight(params int[] dice)
    {
        var encounter = SampleEncounters.FighterVersusRatkin(Data, new ScriptedDice(dice));
        Combatant Named(string name) => encounter.Combatants.Single(c => c.Name == name);
        return (encounter, encounter.Combatants.Single(c => c.Side == Side.Party),
            Named("Ratkin Biter"), Named("Ratkin Sneak"), Named("Ratkin Shiv"));
    }

    [Fact]
    public void ClosingIn_IsOneMoveAndNoMore()
    {
        // The two ratkin left out take a single move each: Short, at most 4 squares, where a turn's
        // move and action together would have carried them 8. No dice scripted: a blow would throw.
        var (encounter, fighter, biter, sneak, shiv) = Fight();
        fighter.Position = new GridPosition(0, 0);
        biter.Position = new GridPosition(6, 0);  // the nearest: it takes the round's one turn
        sneak.Position = new GridPosition(9, 0);
        shiv.Position = new GridPosition(9, 7);
        encounter.EndTurn();

        encounter.RunAdversaryTurn();

        Assert.Equal(4, sneak.Position.DistanceTo(new GridPosition(9, 0)));
        Assert.Equal(5, sneak.Position.DistanceTo(fighter.Position));
        Assert.Equal(4, shiv.Position.DistanceTo(new GridPosition(9, 7)));
        Assert.Equal(5, shiv.Position.DistanceTo(fighter.Position));
        Assert.Equal(1, sneak.Stamina);
        Assert.Equal(2, encounter.Round);
    }

    [Fact]
    public void AnIdleAdversaryAlreadyWithinReach_StaysWhereItIs()
    {
        // Biter and Sneak both stand beside the fighter. Biter takes the turn (2+3, a miss); Sneak
        // is left out, and has no need to close in.
        var (encounter, fighter, biter, sneak, _) = Fight(2, 3);
        fighter.Position = new GridPosition(4, 4);
        biter.Position = new GridPosition(5, 4);
        sneak.Position = new GridPosition(4, 5);
        encounter.EndTurn();

        encounter.RunAdversaryTurn();

        Assert.Equal(new GridPosition(4, 5), sneak.Position);
        Assert.DoesNotContain(encounter.Log, line => line.StartsWith("Ratkin Sneak"));
    }

    [Fact]
    public void ClosingIn_IsOnlyForAdversariesThatTookNoTurn_OwedOrBought()
    {
        // Biter takes the round's one owed turn, and a banked Opportunity buys it a second, chained
        // on (2+3, then 3+2: both miss). Sneak and Shiv took none, and close in.
        var (encounter, fighter, biter, sneak, shiv) = Fight(2, 3, 3, 2);
        encounter.BankedOpportunities = 1;
        fighter.Position = new GridPosition(4, 4);
        biter.Position = new GridPosition(5, 4);
        sneak.Position = new GridPosition(4, 6);
        shiv.Position = new GridPosition(9, 0);
        encounter.EndTurn();

        encounter.RunAdversaryTurn();
        encounter.RunAdversaryTurn();

        Assert.Equal(0, encounter.BankedOpportunities);
        Assert.Equal(2, encounter.Log.Count(line => line.Contains("closes in")));
        Assert.DoesNotContain(encounter.Log, line => line.StartsWith("Ratkin Biter didn't act"));
        Assert.Equal(2, encounter.Round);
    }

    [Fact]
    public void TheCap_IsSetWhenTheFightBegins_AndDoesNotFallWhenACharacterDrops()
    {
        // Two fighters, so two adversary turns a round. The second goes down before their turn: the
        // GM still takes the turn that would have followed it, and the cap stays at two.
        var second = SampleEncounters.DumbFighter(Data);
        second.CharacterName = "The Other Fighter";
        var ratkin = Data.Adversary("Bramble Ratkin");
        var encounter = new Encounter(SampleEncounters.BoardWidth, SampleEncounters.BoardHeight, new[]
        {
            CombatantFactory.FromCharacter(SampleEncounters.DumbFighter(Data), new GridPosition(0, 4)),
            CombatantFactory.FromCharacter(second, new GridPosition(0, 0)),
            CombatantFactory.FromAdversary(ratkin, "Ratkin Biter", new GridPosition(9, 2)),
            CombatantFactory.FromAdversary(ratkin, "Ratkin Sneak", new GridPosition(9, 4)),
            CombatantFactory.FromAdversary(ratkin, "Ratkin Shiv", new GridPosition(9, 6)),
        }, Data.RangeBands, new ScriptedDice(2, 3));
        Assert.Equal(2, encounter.AdversaryTurnsPerRound);

        Assert.Equal(EncounterPhase.ChoosingCharacter, encounter.Phase);
        Assert.True(encounter.TryBeginTurn(encounter.Combatants.Single(c => c.Name == "The Dumb Fighter")));
        encounter.Combatants.Single(c => c.Name == "The Other Fighter").Body = 0;
        encounter.EndTurn();
        encounter.RunAdversaryTurn();
        encounter.RunAdversaryTurn();

        Assert.Contains(encounter.Log, line => line == "The Other Fighter has no turn to take.");
        // Both turns go to the Biter: it runs in on the first, and bites (2+3, a miss) on the second.
        Assert.Single(encounter.Log, line => line.StartsWith("Ratkin Biter moves "));
        Assert.Single(encounter.Log, line => line.StartsWith("Ratkin Biter attacks "));
        Assert.Equal(2, encounter.Log.Count(line => line.Contains("closes in")));
        Assert.Equal(2, encounter.Round);
        Assert.Equal(2, encounter.AdversaryTurnsPerRound);
    }
}
