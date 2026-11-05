using WoundBound.Game.Combat;

namespace WoundBound.Game.Tests;

/// <summary>
/// An adversary's declared intent (the user's direction, 2026-11-03): what it declared is there to
/// be seen — and drawn as its own ring — while anybody weighs an answer to it, and gone once its
/// action is done.
/// </summary>
public class IntentTests
{
    private static readonly Game.Data.GameData Data = TestData.Load();

    [Fact]
    public void TheDeclaration_IsThere_WhileAnInterruptIsWeighed_AndGoneOnceTheTurnIsDone()
    {
        var salla = CombatantFactory.FromCharacter(SampleEncounters.SallaRinehart(Data), new GridPosition(3, 4));
        var drovan = CombatantFactory.FromCharacter(SampleEncounters.DrovanThale(Data), new GridPosition(0, 4));
        var bandit = CombatantFactory.FromAdversary(TestBandits.Roadside, "Bandit", new GridPosition(4, 4));
        var encounter = new Encounter(SampleEncounters.FieldWidth, SampleEncounters.FieldHeight, [salla, drovan, bandit], Data.RangeBands,
            new ScriptedDice(Enumerable.Repeat(3, 20).ToArray()));

        encounter.TryBeginTurn(salla);
        encounter.EndTurn();
        encounter.RunAdversaryTurn();

        Assert.Equal(ReactionKind.Interrupt, encounter.Pending!.Kind);
        var declared = Assert.IsType<Declaration>(encounter.Declared);
        Assert.Equal((bandit, IntentKind.Strike, "Rusty Sword", salla), (declared.Actor, declared.Kind, declared.What, declared.Target));

        encounter.RespondToReaction(false);
        while (encounter.Pending is not null)
        {
            encounter.RespondToReaction(false);
        }

        Assert.Null(encounter.Declared);
    }
}
