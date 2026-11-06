using WoundBound.Game.Combat;
using WoundBound.Game.Combat.Brains;
using WoundBound.Rules.Calculation;
using WoundBound.Rules.Enums;

namespace WoundBound.Game.Tests;

/// <summary>
/// Core Rules §9, "Covering somebody else": a Medium or Heavy shield raised over an ally within
/// Reach as a reaction, once the roll against them is known — the bearer's Stamina, the bearer's
/// Opportunity, and up until the bearer's next turn.
/// </summary>
public class ShieldCoverTests
{
    private static readonly Game.Data.GameData Data = TestData.Load();

    private sealed class OneStepGm(Func<BattleView, GmTurn, GmStep?> step) : SimpleGmBrain
    {
        public override GmStep NextStep(BattleView view, GmTurn turn) => step(view, turn) ?? base.NextStep(view, turn);
    }

    private static Combatant JorenWith(ShieldWeight weight, int x, int y)
    {
        var build = SampleEncounters.JorenBlacke(Data);
        build.Shields.Clear();
        build.Shields.Add(CombatantFactory.ShieldInHand("Kite shield", weight));
        return CombatantFactory.FromCharacter(build, new GridPosition(x, y));
    }

    /// <summary>The bandit beside Mirella swings at her, rolling <paramref name="total"/>.</summary>
    private static (Encounter Encounter, Combatant Joren, Combatant Mirella) SwingAtMirella(ShieldWeight weight, int jorenX, Func<Combatant, int> total)
    {
        var mirella = CombatantFactory.FromCharacter(SampleEncounters.MirellaVask(Data), new GridPosition(2, 4));
        var joren = JorenWith(weight, jorenX, 4);
        var bandit = CombatantFactory.FromAdversary(TestBandits.Roadside, "Bandit", new GridPosition(2, 5));
        var die = total(mirella) - TestBandits.Roadside.AttackModifier;
        var gm = new OneStepGm((view, turn) => turn.HasAttacked ? GmStep.End : new GmStep.Attack(mirella));
        var encounter = new Encounter(SampleEncounters.FieldWidth, SampleEncounters.FieldHeight, [joren, mirella, bandit], Data.RangeBands,
            new ScriptedDice([die / 2, die - die / 2, 3]), gm);
        encounter.TryBeginTurn(joren);
        encounter.EndTurn();
        encounter.RunAdversaryTurn();
        encounter.DeclineFreeChoices();
        return (encounter, joren, mirella);
    }

    [Fact]
    public void AShieldBesideAnAlly_IsOfferedOverThem_AndTurnsTheBlow()
    {
        // A blow that lands on Mirella's bare DT but not with Joren's Medium shield in front of her.
        var (encounter, joren, mirella) = SwingAtMirella(ShieldWeight.Medium, jorenX: 1, m => m.DodgeThreshold + 1);
        var gain = InventoryCalculator.ShieldDodgeThresholdBonus(ShieldWeight.Medium);

        var pending = Assert.IsType<PendingReaction>(encounter.Pending);
        Assert.Equal(ReactionKind.CoverAlly, pending.Kind);
        Assert.Same(joren, pending.Defender);
        Assert.Same(mirella, pending.Struck);
        Assert.Equal(gain, pending.Bonus);
        var bare = mirella.DodgeThreshold;
        Assert.True(encounter.RespondToReaction(spend: true));

        Assert.Equal(mirella.BodyMax, mirella.Body);
        Assert.Equal(mirella.StaminaMax, mirella.Stamina);
        Assert.Equal(joren.StaminaMax - 1, joren.Stamina);
        Assert.Equal(bare + gain, mirella.DodgeThreshold);
        Assert.Equal(1, encounter.GmOpportunities);
        Assert.Contains(encounter.Log, line => line.Contains("Joren Blacke's shield") && line.EndsWith("miss."));
    }

    [Fact]
    public void Declined_TheAllyMayStillPanicDodge()
    {
        var (encounter, joren, mirella) = SwingAtMirella(ShieldWeight.Medium, jorenX: 1, m => m.DodgeThreshold + 1);

        Assert.True(encounter.RespondToReaction(spend: false));

        var pending = Assert.IsType<PendingReaction>(encounter.Pending);
        Assert.Equal(ReactionKind.PanicDodge, pending.Kind);
        Assert.Same(mirella, pending.Defender);
        Assert.Equal(joren.StaminaMax, joren.Stamina);
    }

    [Fact]
    public void ALightShield_OrOneOutOfReach_OffersNothing()
    {
        var (light, _, _) = SwingAtMirella(ShieldWeight.Light, jorenX: 1, m => m.DodgeThreshold + 1);
        Assert.Equal(ReactionKind.PanicDodge, light.Pending?.Kind);

        var (far, _, _) = SwingAtMirella(ShieldWeight.Medium, jorenX: 6, m => m.DodgeThreshold + 1);
        Assert.Equal(ReactionKind.PanicDodge, far.Pending?.Kind);
    }
}
