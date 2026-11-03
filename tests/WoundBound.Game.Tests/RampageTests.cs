using WoundBound.Game.Combat;
using WoundBound.Game.Combat.Brains;

namespace WoundBound.Game.Tests;

/// <summary>
/// Rampage (GM Reference §11, the Duskfang Gnoll): once a round, when its attack drops a creature
/// to 0 Body, 1 Stamina buys an extra action that is a second attack, at a different creature — a
/// tactical spend that hands nobody an Opportunity.
/// </summary>
public class RampageTests
{
    private static readonly Game.Data.GameData Data = TestData.Load();

    private sealed class ScriptedGm(Func<BattleView, GmTurn, GmStep?> step) : SimpleGmBrain
    {
        public override GmStep NextStep(BattleView view, GmTurn turn) => step(view, turn) ?? GmStep.End;

        public override GmCloseIn? NextCloseIn(BattleView view, IReadOnlyList<Combatant> stillIdle) => null;

        public override bool PanicDodges(BattleView view, PendingReaction reaction) => false;
    }

    private static Combatant Gnoll(int x, int y) => CombatantFactory.FromAdversary(Data.Adversary("Duskfang Gnoll"), "Gnoll", new GridPosition(x, y));

    private static Combatant Joren(int x, int y) => CombatantFactory.FromCharacter(SampleEncounters.JorenBlacke(Data), new GridPosition(x, y));

    private static Combatant Mirella(int x, int y) => CombatantFactory.FromCharacter(SampleEncounters.MirellaVask(Data), new GridPosition(x, y));

    private static Encounter Fight(IGmBrain gm, params Combatant[] combatants)
    {
        var encounter = new Encounter(SampleEncounters.FieldWidth, SampleEncounters.FieldHeight, combatants, Data.RangeBands,
            new ScriptedDice(Enumerable.Repeat(8, 40).ToArray()), gm);
        encounter.DeclineFreeChoices();
        return encounter;
    }

    /// <summary>One character takes and ends a turn, then the GM takes one, every prompt let pass.</summary>
    private static void OneTurnEach(Encounter encounter, Combatant character)
    {
        encounter.TryBeginTurn(character);
        encounter.EndTurn();
        encounter.RunAdversaryTurn();
        while (encounter.Pending is not null)
        {
            encounter.RespondToReaction(false);
        }
    }

    [Fact]
    public void TheGnoll_IsBuiltWithRampage()
    {
        Assert.True(Gnoll(0, 0).HasRampage);
        Assert.False(CombatantFactory.FromAdversary(Data.Adversary("Timber Wolf"), "Wolf", new GridPosition(0, 0)).HasRampage);
    }

    [Fact]
    public void ADrop_OpensARampage_AndAStaminaBuysASecondAttackAtSomebodyElse()
    {
        var gnoll = Gnoll(5, 5);
        var first = Joren(4, 5);
        var second = Mirella(6, 5);
        first.Body = 1;
        var gm = new ScriptedGm((_, turn) => turn.Actor != gnoll ? null
            : gnoll.RampageFrom is not null ? new GmStep.Rampage(second)
            : !turn.HasAttacked ? new GmStep.Attack(first)
            : null);
        var encounter = Fight(gm, first, second, gnoll);
        var stamina = gnoll.Stamina;

        OneTurnEach(encounter, second);

        var log = string.Join("\n", encounter.Log);
        Assert.True(first.Body <= 0, log);
        Assert.Contains("Gnoll Rampages", log);
        Assert.Contains(encounter.Log, l => l.StartsWith("Gnoll attacks Mirella"));
        Assert.Equal(stamina - 1, gnoll.Stamina);
        Assert.Equal(1, gnoll.TacticalSpendsThisRound);
        Assert.True(gnoll.HasRampagedThisRound);
        Assert.Equal(0, encounter.GmOpportunities);
    }

    [Fact]
    public void Rampage_NeedsADrop_AndADifferentTarget()
    {
        var gnoll = Gnoll(5, 5);
        var first = Joren(4, 5);
        var second = Mirella(6, 5);
        var encounter = Fight(new ScriptedGm((_, _) => null), first, second, gnoll);

        Assert.Contains("dropped", encounter.RampageRefusal(gnoll, second));

        gnoll.RampageFrom = first;
        Assert.Contains("different", encounter.RampageRefusal(gnoll, first));
        Assert.Null(encounter.RampageRefusal(gnoll, second));

        gnoll.HasRampagedThisRound = true;
        Assert.Contains("once a round", encounter.RampageRefusal(gnoll, second));
    }

    [Fact]
    public void AHitThatDropsNobody_OpensNoRampage()
    {
        var gnoll = Gnoll(5, 5);
        var first = Joren(4, 5);
        var second = Mirella(6, 5);
        first.Body = 40;
        var gm = new ScriptedGm((_, turn) => turn.Actor == gnoll && !turn.HasAttacked ? new GmStep.Attack(first) : null);
        var encounter = Fight(gm, first, second, gnoll);

        OneTurnEach(encounter, second);

        Assert.DoesNotContain(encounter.Log, l => l.Contains("Rampage"));
        Assert.False(gnoll.HasRampagedThisRound);
    }
}
