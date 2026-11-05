using WoundBound.Game.Combat;
using WoundBound.Game.Combat.Brains.Utility;
using WoundBound.Game.Data;
using WoundBound.Rules.Models;

namespace WoundBound.Game.Tests;

/// <summary>
/// Staying in somebody's reach against leaving it for a better target (the user's direction,
/// 2026-11-02): leaving hands the enemy its reaction, unless it has spent it, and at T1 that blow
/// can end the mover — and with it whatever it was moving to do. Sometimes it's worth it anyway.
/// </summary>
public class LeavingReachBrainTests
{
    private static readonly Game.Data.GameData Data = TestData.Load();

    /// <summary>
    /// A bandit beside <paramref name="beside"/>; Mirella, who has just cast, three squares off — and
    /// the best the bandit's plans make of staying within reach, against going for her.
    /// </summary>
    private static (double Stay, double GoForMirella, double DropChance) Weigh(Func<GameData, CharacterBuild> beside, Action<Combatant, Combatant>? set = null)
    {
        var neighbour = CombatantFactory.FromCharacter(beside(Data), new GridPosition(4, 5));
        var mirella = CombatantFactory.FromCharacter(SampleEncounters.MirellaVask(Data), new GridPosition(8, 5));
        mirella.HasCastSpell = true;
        var bandit = CombatantFactory.FromAdversary(SampleEncounters.RoadsideBandit, "Bandit", new GridPosition(5, 5));
        var encounter = new Encounter(SampleEncounters.FieldWidth, SampleEncounters.FieldHeight, [neighbour, mirella, bandit], Data.RangeBands, new ScriptedDice());
        bandit.HasMove = true;
        bandit.ActionsRemaining = 1;
        set?.Invoke(bandit, neighbour);

        var assess = new Assessor(encounter.View);
        var options = new TurnPlanner(assess, BrainSkill.Best).Options(bandit, hasMove: true, actions: 1, real: true);
        // Harry-then-go is a way of going: it ends out of the neighbour's reach, striking Mirella.
        static bool HarryAndGo(Option o) => o.Why.Contains("slip past");
        var stay = options.Where(o => !HarryAndGo(o) && neighbour.CanReach(o.Square ?? bandit.Position)).Max(o => o.Score);
        var go = options.Where(o => HarryAndGo(o) || (o.Kind == OptionKind.Attack || o.Then == OptionKind.Attack) && o.Target == mirella).Max(o => o.Score);
        return (stay, go, assess.LeavingRisk(bandit, new GridPosition(7, 5)).DropChance);
    }

    private static CharacterBuild Fighter(GameData data) => SampleEncounters.DumbFighter(data);

    [Fact]
    public void WithTheReactionSpent_LeavingIsFree_AndTheCasterIsWorthMore()
    {
        var (stay, go, dropped) = Weigh(Fighter, (_, fighter) => fighter.TacticalSpendsThisRound = fighter.TacticalSpendsPerRound);

        Assert.Equal(0, dropped);
        Assert.True(go > stay, $"stay {stay:F2}, go {go:F2}");
    }

    [Fact]
    public void WhenTheReactionWouldLikelyDropIt_ItStays()
    {
        var (stay, go, dropped) = Weigh(Fighter, (bandit, _) => bandit.Body = 2);

        Assert.True(dropped > 0.4, $"drop chance {dropped:P0}");
        Assert.True(stay > go, $"stay {stay:F2}, go {go:F2}");
    }

    [Fact]
    public void WhenTheReactionCanBarelyHurtIt_ItTakesTheBlowAndGoesForTheBetterTarget()
    {
        // Elthric's knife has a few percent of dropping a healthy bandit: leaving him is worth it.
        var (stay, go, dropped) = Weigh(data => SampleEncounters.ElthricDane(data));

        Assert.True(dropped is > 0 and < 0.1, $"drop chance {dropped:P0}");
        Assert.True(go > stay, $"stay {stay:F2}, go {go:F2}");
    }

    [Fact]
    public void TheReactionPrice_CountsTheChanceOfBeingDropped_NotJustTheWounds()
    {
        var healthy = Weigh(Fighter);
        var hurt = Weigh(Fighter, (bandit, _) => bandit.Body = 2);

        Assert.True(hurt.DropChance > healthy.DropChance);
    }

    [Fact]
    public void AgainstAWeakNeighbour_TheBanditHarriesIt_BuysAnAction_AndLeavesForTheCaster()
    {
        // Harry, so the knife it leaves behind strikes at Disadvantage; 1 Stamina for an action;
        // then out of reach and on to Mirella — its one attack of the round.
        // A GM that minds its creatures' skin fully: one held less dear just takes the knife.
        var elthric = CombatantFactory.FromCharacter(SampleEncounters.ElthricDane(Data), new GridPosition(4, 5));
        var mirella = CombatantFactory.FromCharacter(SampleEncounters.MirellaVask(Data), new GridPosition(8, 5));
        mirella.HasCastSpell = true;
        var bandit = CombatantFactory.FromAdversary(SampleEncounters.RoadsideBandit, "Bandit", new GridPosition(5, 5));
        var encounter = new Encounter(SampleEncounters.FieldWidth, SampleEncounters.FieldHeight, [elthric, mirella, bandit],
            Data.RangeBands, new RandomDiceRoller(new Random(3)), new Game.Combat.Brains.Utility.UtilityGmBrain(caution: 1, foresight: Game.Combat.Brains.Utility.Foresight.None));
        encounter.TryBeginTurn(mirella);
        encounter.EndTurn();
        var start = encounter.Log.Count;

        encounter.RunAdversaryTurn();

        var turn = encounter.Log.Skip(start).ToList();
        Assert.Contains(turn, l => l.Contains("Bandit Harries Elthric Dane"));
        Assert.Contains(turn, l => l.Contains("Bandit spends 1 Stamina for an extra action"));
        Assert.Contains(turn, l => l.Contains("leaving Elthric Dane's reach"));
    }
}
