using WoundBound.Game.Combat;
using WoundBound.Game.Combat.Brains;

namespace WoundBound.Game.Tests;

/// <summary>
/// An adversary's Stamina (the user's ruling, 2026-11-02): spent as a character's is, with no
/// Opportunity for anybody; never on a second attack in a round outside an extra turn; and a
/// creature that starts its turn on 0 falls unconscious.
/// </summary>
public class AdversaryStaminaTests
{
    private static readonly Game.Data.GameData Data = TestData.Load();

    /// <summary>The GM, made to take the steps given, in order, then end.</summary>
    private sealed class StepsGm(params GmStep[] steps) : SimpleGmBrain
    {
        private int next;

        public override GmStep NextStep(BattleView view, GmTurn turn) => next < steps.Length ? steps[next++] : GmStep.End;

        public override bool PanicDodges(BattleView view, PendingReaction reaction) => false;
    }

    private static (Encounter Encounter, Combatant Fighter, Combatant Bandit) Scene(IGmBrain gm, int[] dice)
    {
        var fighter = CombatantFactory.FromCharacter(SampleEncounters.DumbFighter(Data), new GridPosition(4, 5));
        fighter.Stamina = 1;
        var bandit = CombatantFactory.FromAdversary(SampleEncounters.RoadsideBandit, "Bandit", new GridPosition(5, 5));
        var encounter = new Encounter(SampleEncounters.FieldWidth, SampleEncounters.FieldHeight, [fighter, bandit], Data.RangeBands, new ScriptedDice(dice), gm);
        encounter.DeclineFreeChoices();
        encounter.TryBeginTurn(fighter);
        encounter.EndTurn();
        return (encounter, fighter, bandit);
    }

    [Fact]
    public void AnAdversary_StartingItsTurnOnNoStamina_FallsUnconscious()
    {
        var (encounter, _, bandit) = Scene(new StepsGm(), []);
        bandit.Stamina = 0;

        encounter.RunAdversaryTurn();

        Assert.True(bandit.IsUnconscious);
        Assert.Contains(encounter.Log, l => l.Contains("Bandit starts its turn with no Stamina and falls unconscious"));
    }

    [Fact]
    public void AnAdversary_MayBuyAnExtraAction_ButNotASecondAttack()
    {
        var fighter = CombatantFactory.FromCharacter(SampleEncounters.DumbFighter(Data), new GridPosition(4, 5));
        fighter.Stamina = 1;
        var bandit = CombatantFactory.FromAdversary(SampleEncounters.RoadsideBandit, "Bandit", new GridPosition(5, 5));
        var gm = new StepsGm(new GmStep.Attack(fighter), new GmStep.BuyExtraAction(), new GmStep.Attack(fighter));
        var encounter = new Encounter(SampleEncounters.FieldWidth, SampleEncounters.FieldHeight, [fighter, bandit], Data.RangeBands,
            new ScriptedDice(3, 3, 1, 1, 1, 1), gm);
        encounter.TryBeginTurn(fighter);
        encounter.EndTurn();
        var stamina = bandit.Stamina;

        var thrown = Assert.Throws<InvalidOperationException>(encounter.RunAdversaryTurn);

        Assert.Contains("Stamina buys it anything but a second", thrown.Message);
        Assert.Equal(stamina - 1, bandit.Stamina);
        Assert.Contains(encounter.Log, l => l.Contains("Bandit spends 1 Stamina for an extra action"));
        Assert.Equal(0, encounter.BankedOpportunities);
    }

    [Fact]
    public void OnceARound_IsLiftedByAnExtraTurn_AndNeverAppliesToASolo()
    {
        var bandit = CombatantFactory.FromAdversary(SampleEncounters.RoadsideBandit, "Bandit", new GridPosition(5, 5));
        bandit.AttacksThisRound = 1;

        Assert.NotNull(Encounter.OnceARoundRefusal(bandit, new GmTurn(bandit, isExtraTurn: false, bonusAvailable: false)));
        Assert.Null(Encounter.OnceARoundRefusal(bandit, new GmTurn(bandit, isExtraTurn: true, bonusAvailable: false)));
    }

    [Fact]
    public void AnExtraTurn_HasOneAttack_StaminaBuysItNoSecond()
    {
        var bandit = CombatantFactory.FromAdversary(SampleEncounters.RoadsideBandit, "Bandit", new GridPosition(5, 5));
        var extra = new GmTurn(bandit, isExtraTurn: true, bonusAvailable: false) { Attacks = 1 };

        Assert.Contains("this extra turn's attack", Encounter.OnceARoundRefusal(bandit, extra));
    }

    [Theory]
    [InlineData("Bramble Ratkin", 1)]   // a Minion: as written
    [InlineData("Charnel Revenant", 3)] // T2, written 2: the floor is Tier + 1
    [InlineData("Noble Champion", 4)]   // T3, written 3
    [InlineData("Tollroad Bandit", 2)]  // T1, written 2: already at it
    public void OnlyAMinion_StandsOnLessThanTierPlusOneStamina(string name, int stamina)
    {
        var creature = CombatantFactory.FromAdversary(Data.Adversary(name), name, new GridPosition(1, 1));

        Assert.Equal(stamina, creature.StaminaMax);
    }
}
