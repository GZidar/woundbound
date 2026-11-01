using WoundBound.Game.Combat;
using WoundBound.Game.Combat.Brains;
using WoundBound.Rules.Enums;

namespace WoundBound.Game.Tests;

/// <summary>GM Reference §3, "Swarms": a body made of many, standing on four squares and holding none of them.</summary>
public class SwarmTests
{
    private static readonly Game.Data.GameData Data = TestData.Load();

    private sealed class OneStepGm(Func<BattleView, GmTurn, GmStep?> step) : SimpleGmBrain
    {
        public override GmStep NextStep(BattleView view, GmTurn turn) => step(view, turn) ?? base.NextStep(view, turn);

        public override bool PanicDodges(BattleView view, PendingReaction reaction) => false;
    }

    private static Combatant Ratswarm(int x, int y) =>
        CombatantFactory.FromAdversary(Data.Adversary("Fenmoor Ratswarm"), "Ratswarm", new GridPosition(x, y));

    private static Encounter Fight(int[] dice, IGmBrain? gm, params Combatant[] combatants) =>
        new(SampleEncounters.FieldWidth, SampleEncounters.FieldHeight, combatants, Data.RangeBands, new ScriptedDice(dice), gm);

    [Fact]
    public void ALargeSwarm_StandsOnFourSquares_AndReachesOnlyThose()
    {
        var swarm = Ratswarm(3, 3);

        Assert.Equal(2, swarm.Across);
        Assert.Equal([new(3, 3), new(3, 4), new(4, 3), new(4, 4)], swarm.Footprint.OrderBy(s => s.X).ThenBy(s => s.Y));
        Assert.Equal(0, swarm.Reach);
        Assert.False(swarm.HoldsGround);

        Assert.True(swarm.CanReach(new GridPosition(4, 4)));
        Assert.False(swarm.CanReach(new GridPosition(5, 4)));

        // Distance is counted from the nearest edge of the mass: (6, 5) is two from (4, 4).
        Assert.Equal(2, swarm.DistanceTo(new GridPosition(6, 5)));
        Assert.Equal(0, swarm.DistanceTo(new GridPosition(3, 4)));
    }

    [Fact]
    public void ACharacter_MayWalkIntoTheMass_AndStopThere()
    {
        var joren = CombatantFactory.FromCharacter(SampleEncounters.JorenBlacke(Data), new GridPosition(1, 3));
        var swarm = Ratswarm(3, 3);
        var encounter = Fight([], null, joren, swarm);
        encounter.TryBeginTurn(joren);

        Assert.True(encounter.TryMove(new GridPosition(4, 4)), string.Join("\n", encounter.Log));
        Assert.Equal(new GridPosition(4, 4), joren.Position);
        Assert.Null(encounter.At(new GridPosition(3, 3)));
    }

    [Fact]
    public void AWeaponHit_CostsASwarmExactlyOneWound_WhateverWasRolled()
    {
        var joren = CombatantFactory.FromCharacter(SampleEncounters.JorenBlacke(Data), new GridPosition(2, 3));
        var swarm = Ratswarm(3, 3);
        var encounter = Fight([9, 8, 6], null, joren, swarm);
        encounter.TryBeginTurn(joren);

        var outcome = encounter.TryAttack(swarm);

        Assert.True(outcome?.Roll.Success == true, string.Join("\n", encounter.Log));
        Assert.Equal(9, swarm.Body);
    }

    [Fact]
    public void AVolumeOverTheWholeMass_RollsItsDamage_ALineThroughPartOfIt_AWoundASquare()
    {
        // Elthric's Blood Mist, a Small Volume: its 2×2 block can sit exactly on the swarm.
        var elthric = CombatantFactory.FromCharacter(SampleEncounters.ElthricDane(Data), new GridPosition(2, 3));
        var swarm = Ratswarm(5, 3);
        var encounter = Fight([9, 8, 4, 11, 9], null, elthric, swarm);
        encounter.TryBeginTurn(elthric);
        var mist = elthric.Spells.Single(s => s.Name == "Blood Mist");

        var whole = encounter.PlanCast(elthric, new SpellCast(mist) { Corner = new GridPosition(5, 3) });
        Assert.True(whole.CanCast, whole.Refusal);
        Assert.Null(Encounter.SwarmCoverage(swarm, whole));
        Assert.True(encounter.TryCast(new SpellCast(mist) { Corner = new GridPosition(5, 3) }), string.Join("\n", encounter.Log));
        var full = swarm.BodyMax - swarm.Body;
        Assert.True(full >= 4, string.Join("\n", encounter.Log));

        // A Line along the top row covers two of its four squares: 2 wounds, nothing rolled.
        Assert.True(encounter.TryBuyExtraAction());
        var line = new SpellCast(mist) { Shape = SpellShape.Line, Origin = new GridPosition(4, 3), Aim = (1, 0) };
        Assert.Equal(2, Encounter.SwarmCoverage(swarm, encounter.PlanCast(elthric, line)));
        var before = swarm.Body;
        Assert.True(encounter.TryCast(line), string.Join("\n", encounter.Log));
        Assert.True(before - 2 == swarm.Body, string.Join("\n", encounter.Log));
    }

    [Fact]
    public void AMotionSpell_MustCoverTheWholeSwarm_ARayMovesNoneOfIt()
    {
        var elthric = CombatantFactory.FromCharacter(SampleEncounters.ElthricDane(Data), new GridPosition(2, 3));
        var swarm = Ratswarm(4, 3);
        var encounter = Fight([], null, elthric, swarm);
        encounter.TryBeginTurn(elthric);

        var plan = encounter.PlanCast(elthric, new SpellCast(elthric.Spells.Single(s => s.Name == "Telekinetic Shove")) { Target = swarm });

        Assert.False(plan.CanCast);
        Assert.Contains("whole of Ratswarm", plan.Refusal);
    }

    [Fact]
    public void MindDamage_FindsNothingInASwarm()
    {
        var elthric = CombatantFactory.FromCharacter(SampleEncounters.ElthricDane(Data), new GridPosition(2, 3));
        var swarm = Ratswarm(4, 3);
        var encounter = Fight([9, 8, 5], null, elthric, swarm);
        encounter.TryBeginTurn(elthric);

        Assert.True(encounter.TryCast(new SpellCast(elthric.Spells.Single(s => s.Name == "Mind Spike")) { Target = swarm }));

        Assert.Equal(swarm.BodyMax, swarm.Body);
        Assert.Contains(encounter.Log, l => l.Contains("no single mind"));
    }

    [Fact]
    public void ItsBite_FallsOnEveryCreatureStandingInIt_ItsOwnSideToo_ForOneAction()
    {
        var joren = CombatantFactory.FromCharacter(SampleEncounters.JorenBlacke(Data), new GridPosition(3, 4));
        var salla = CombatantFactory.FromCharacter(SampleEncounters.SallaRinehart(Data), new GridPosition(4, 4));
        var outside = CombatantFactory.FromCharacter(SampleEncounters.DrovanThale(Data), new GridPosition(5, 4));
        var bandit = CombatantFactory.FromAdversary(SampleEncounters.RoadsideBandit, "Bandit", new GridPosition(4, 3));
        var swarm = Ratswarm(3, 3);
        var gm = new OneStepGm((view, turn) => view.Combatants.Contains(swarm) && turn.Actor == swarm
            ? turn.HasAttacked ? GmStep.End : new GmStep.Attack(joren)
            : GmStep.End);
        var encounter = Fight([9, 8, 3, 9, 8, 3, 9, 8, 3, 9, 8, 3], gm, joren, salla, outside, swarm, bandit);
        foreach (var character in new[] { joren, salla, outside })
        {
            encounter.TryBeginTurn(character);
            encounter.EndTurn();
        }

        while (encounter.Pending is not null)
        {
            encounter.RespondToReaction(false);
        }

        encounter.RunAdversaryTurn();
        while (encounter.Pending is not null)
        {
            encounter.RespondToReaction(false);
        }

        var bitten = encounter.Log.Where(l => l.StartsWith("Ratswarm attacks")).ToList();
        Assert.True(3 == bitten.Count, string.Join("\n", encounter.Log));
        Assert.Contains(bitten, l => l.Contains("Joren"));
        Assert.Contains(bitten, l => l.Contains("Salla"));
        Assert.Contains(bitten, l => l.Contains("Bandit"));
        Assert.DoesNotContain(bitten, l => l.Contains("Drovan"));
    }

    [Fact]
    public void ALargeBrute_GoesOnlyWhereAllFourOfItsSquaresFit()
    {
        var bear = CombatantFactory.FromAdversary(Data.Adversary("Cave Bear"), "Bear", new GridPosition(3, 3));
        var joren = CombatantFactory.FromCharacter(SampleEncounters.JorenBlacke(Data), new GridPosition(6, 4));
        var encounter = Fight([], null, joren, bear);

        Assert.Equal(2, bear.Across);
        Assert.Same(bear, encounter.At(new GridPosition(4, 4)));
        Assert.Null(encounter.PlanMove(bear, new GridPosition(5, 4)));
        Assert.NotNull(encounter.PlanMove(bear, new GridPosition(3, 5)));
    }
}
