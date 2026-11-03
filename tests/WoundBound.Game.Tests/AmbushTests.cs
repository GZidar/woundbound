using WoundBound.Game.Combat;
using WoundBound.Game.Combat.Brains;

namespace WoundBound.Game.Tests;

/// <summary>
/// An ambush (Core Rules §9, "Ambush"; the Quickstart's Roadside Ambush): one Awareness check by
/// the most attentive character against one Stealth roll for the ambushers. Spotted, the fight
/// opens as any other; missed, each adversary lying in wait takes one surprise action from where
/// it lies, unanswered by any Interrupt, before round 1 begins.
/// </summary>
public class AmbushTests
{
    private static readonly Game.Data.GameData Data = TestData.Load();

    private sealed class ScriptedGm(Func<BattleView, GmTurn, GmStep?> step) : SimpleGmBrain
    {
        public override GmStep NextStep(BattleView view, GmTurn turn) => step(view, turn) ?? GmStep.End;

        public override GmCloseIn? NextCloseIn(BattleView view, IReadOnlyList<Combatant> stillIdle) => null;

        public override bool PanicDodges(BattleView view, PendingReaction reaction) => false;
    }

    /// <summary>The signpost as the ambush is laid; the first four dice are the Stealth roll and the Awareness check.</summary>
    private static Encounter Signpost(int[] ambushDice, IGmBrain gm) =>
        SampleEncounters.SignpostAmbush(Data, new ScriptedDice([.. ambushDice, .. Enumerable.Repeat(6, 40)]), gm, ambush: true);

    private static Combatant Named(Encounter encounter, string name) => encounter.Combatants.Single(c => c.Name == name);

    private static string Log(Encounter encounter) => string.Join("\n", encounter.Log);

    [Fact]
    public void Spotted_TheFightOpensAsAnyOther()
    {
        // Stealth 1+1 and its bonuses; Awareness 12+11.
        var encounter = Signpost([1, 1, 12, 11], new ScriptedGm((_, _) => null));

        Assert.Contains(encounter.Log, l => l.Contains("seen before it is sprung"));
        Assert.False(encounter.IsSurprise);
        Assert.Equal(1, encounter.Round);
        Assert.NotEqual(EncounterPhase.AdversaryTurn, encounter.Phase);
        Assert.All(encounter.Combatants, c => Assert.False(c.LiesInWait));
    }

    [Fact]
    public void Missed_EachAmbusherStrikesFromHiding_UnansweredByAnInterrupt_ThenRoundOneBegins()
    {
        // Stealth 12+11; Awareness 1+2. Each crossbow shoots the man with the torch.
        Combatant? fighter = null;
        var gm = new ScriptedGm((_, turn) => turn.IsSurprise && turn.Attacks == 0 && !turn.HasAttacked ? new GmStep.Attack(fighter!) : null);
        var encounter = Signpost([12, 11, 1, 2], gm);
        fighter = Named(encounter, "The Dumb Fighter");

        Assert.True(encounter.IsSurprise, Log(encounter));
        Assert.Equal(EncounterPhase.AdversaryTurn, encounter.Phase);
        Assert.Equal(0, encounter.Round);

        while (encounter.IsSurprise)
        {
            encounter.RunAdversaryTurn();
            while (encounter.Pending is not null)
            {
                encounter.RespondToReaction(false);
            }
        }

        Assert.Equal(2, encounter.Log.Count(l => l.Contains("strikes from hiding")));
        Assert.Equal(2, encounter.Log.Count(l => l.Contains(" attacks The Dumb Fighter")));
        Assert.DoesNotContain(encounter.Log, l => l.Contains("may Interrupt"));
        Assert.Equal(1, encounter.Round);

        // Their surprise shots are no part of round 1: each still has its turn and its attack.
        Assert.All(encounter.Combatants.Where(c => c.Name.StartsWith("Ambusher")), a => Assert.Equal(0, a.AttacksThisRound));
    }

    [Fact]
    public void ASurpriseAction_IsNoMove_AndAskingForOneSpendsIt()
    {
        var gm = new ScriptedGm((_, turn) => turn.IsSurprise ? new GmStep.Move(new GridPosition(turn.Actor.Position.X + 1, turn.Actor.Position.Y)) : null);
        var encounter = Signpost([12, 11, 1, 2], gm);
        var ridge = Named(encounter, "Ambusher on the ridge");
        var start = ridge.Position;

        encounter.RunAdversaryTurn();

        Assert.Equal(start, ridge.Position);
        Assert.Contains(encounter.Log, l => l.Contains("keeps to its hiding place"));
    }

    [Fact]
    public void WithoutTheAmbush_TheSignpostOpensInTheOpen()
    {
        var encounter = SampleEncounters.SignpostAmbush(Data, new ScriptedDice());

        Assert.False(encounter.IsSurprise);
        Assert.DoesNotContain(encounter.Log, l => l.Contains("Stealth"));
    }
}
