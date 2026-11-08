using WoundBound.Game.Combat;
using WoundBound.Game.Combat.Brains;
using WoundBound.Game.Combat.Brains.Utility;
using WoundBound.Game.Simulation;

namespace WoundBound.Game.Tests;

/// <summary>
/// How the GM brain reads at the table (the user, 2026-11-08): a pause to narrate each GM action
/// before it takes effect and another once it lands, a why-line from what the creatures could see,
/// and the brief's goal told as it stands.
/// </summary>
public class TableTests
{
    private static readonly Game.Data.GameData Data = TestData.Load();

    private static (Encounter Encounter, Combatant Salla, Combatant Bandit) Duel(IGmBrain? gm = null)
    {
        var salla = CombatantFactory.FromCharacter(SampleEncounters.SallaRinehart(Data), new GridPosition(3, 4));
        var bandit = CombatantFactory.FromAdversary(TestBandits.Roadside, "Bandit", new GridPosition(4, 4));
        var encounter = new Encounter(SampleEncounters.FieldWidth, SampleEncounters.FieldHeight, [salla, bandit], Data.RangeBands,
            new ScriptedDice(Enumerable.Repeat(3, 40).ToArray()), gm) { NarrationPauses = true };
        return (encounter, salla, bandit);
    }

    [Fact]
    public void WithAHumanGm_TheFightPauses_BeforeAGmActionTakesEffect_AndAgainOnceItLands()
    {
        var (encounter, salla, bandit) = Duel();
        encounter.TryBeginTurn(salla);
        encounter.EndTurn();
        encounter.RunAdversaryTurn();

        Assert.True(encounter.AwaitingNarration);
        Assert.Equal(ReactionKind.NarrateIntent, encounter.Pending!.Kind);
        Assert.Equal(bandit, encounter.Declared!.Actor);
        var linesBefore = encounter.Log.Count;
        Assert.DoesNotContain(encounter.Log, line => line.Contains("attacks Salla"));

        Assert.True(encounter.ContinueNarration());
        while (encounter.Pending is { } pending && !encounter.AwaitingNarration)
        {
            encounter.RespondToReaction(false);
        }

        Assert.Equal(ReactionKind.NarrateOutcome, encounter.Pending!.Kind);
        Assert.Contains(encounter.Log.Skip(linesBefore), line => line.Contains("attacks Salla"));
        Assert.True(encounter.ContinueNarration());
        Assert.False(encounter.RespondToReaction(true)); // nothing left to answer, and a pause is never a spend
    }

    [Fact]
    public void WithoutAHumanGm_NothingPauses()
    {
        var (encounter, salla, _) = Duel();
        encounter.NarrationPauses = false;
        encounter.TryBeginTurn(salla);
        encounter.EndTurn();
        encounter.RunAdversaryTurn();

        Assert.False(encounter.AwaitingNarration);
    }

    [Fact]
    public void TheWhyLine_SaysOnlyWhatTheCreaturesCouldSee()
    {
        var (encounter, salla, bandit) = Duel();
        var aim = new Option(OptionKind.Attack, 1, "test", Target: salla);
        Assert.Equal("the nearest", TableTalk.Why(encounter.View, bandit, aim));

        salla.Body = 1;
        Assert.Equal("already bloodied", TableTalk.Why(encounter.View, bandit, aim));

        // Mirella reads as a caster only once she has been seen casting, whatever the GM's sheets say.
        var mirella = CombatantFactory.FromCharacter(SampleEncounters.MirellaVask(Data), new GridPosition(4, 6));
        var fight = new Encounter(SampleEncounters.FieldWidth, SampleEncounters.FieldHeight, [mirella, salla, bandit], Data.RangeBands, new ScriptedDice());
        var atHer = new Option(OptionKind.Attack, 1, "test", Target: mirella);
        Assert.NotEqual("the caster", TableTalk.Why(fight.View, bandit, atHer));
        mirella.HasCastSpell = true;
        Assert.Equal("the caster", TableTalk.Why(fight.View, bandit, atHer));
    }

    [Fact]
    public void TheBrief_TellsHowTheGoalStands()
    {
        var hold = SampleEncounters.Roadblock(Data, new ScriptedDice());
        Assert.Contains("round 1 of 6, not yet broken", hold.GoalProgress);

        var take = SampleEncounters.StrongboxRaid(Data, new ScriptedDice());
        Assert.Contains("still to be taken", take.GoalProgress);
        var bandit = take.Combatants.Single(c => c.Name == "Bandit on the left");
        take.Prize!.At = null;
        take.Prize.Carrier = bandit;
        Assert.StartsWith("Bandit on the left has the strongbox", take.GoalProgress);

        var escape = SampleEncounters.JettyEscape(Data, new ScriptedDice());
        Assert.Contains("0 of 6 away, making for the boat", escape.GoalProgress);
    }
}
