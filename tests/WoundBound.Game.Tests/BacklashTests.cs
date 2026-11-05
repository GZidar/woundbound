using WoundBound.Game.Combat;
using WoundBound.Game.Combat.Brains.Utility;

namespace WoundBound.Game.Tests;

/// <summary>
/// Core Rules §4, "Metaphysical Backlash", for everybody it applies to: Minions (GM Reference §3),
/// adversaries, a caster paying the last of a pool, and an Interrupt on the last Mind.
/// </summary>
public class BacklashTests
{
    private static readonly Game.Data.GameData Data = TestData.Load();

    private static Encounter Fight(int[] dice, params Combatant[] combatants) =>
        new(SampleEncounters.FieldWidth, SampleEncounters.FieldHeight, combatants, Data.RangeBands, new ScriptedDice(dice),
            environment: Data.Environment);

    private static Rules.Models.SpellDefinition Spell(Combatant caster, string name) => caster.Spells.Single(s => s.Name == name);

    [Fact]
    public void AMinion_HitOnMind_IsStunned_NotRemoved_AndStillLiesThere()
    {
        // Mind Spike: 9+8 +4 = 21, a hit; d6 = 2 Mind against the ratkin's pinned 1. The GM lets it go down.
        var elthric = CombatantFactory.FromCharacter(SampleEncounters.ElthricDane(Data), new GridPosition(2, 4));
        var ratkin = CombatantFactory.FromAdversary(Data.Adversary("Bramble Ratkin"), "Ratkin", new GridPosition(5, 4));
        var encounter = Fight([9, 8, 2], elthric, ratkin);
        encounter.TryBeginTurn(elthric);

        Assert.True(encounter.TryCast(new SpellCast(Spell(elthric, "Mind Spike")) { Target = ratkin }), string.Join("\n", encounter.Log));

        Assert.True(ratkin.IsStunned, string.Join("\n", encounter.Log));
        Assert.True(ratkin.IsOutOfFight);
        Assert.True(ratkin.OccupiesSquare);
        Assert.Equal(ratkin.BodyMax, ratkin.Body);
    }

    [Fact]
    public void AnAdversary_BacklashesItsLastMind_OntoBody_WhenItCanStandTheHit()
    {
        // A Roadside Bandit, Mind 2 and Body 6: 4 Mind would empty it. Held at 1, the 3 excess
        // comes off Body, Direct — 3 left, standing.
        var elthric = CombatantFactory.FromCharacter(SampleEncounters.ElthricDane(Data), new GridPosition(2, 4));
        var bandit = CombatantFactory.FromAdversary(TestBandits.Roadside, "Bandit", new GridPosition(5, 4));
        var encounter = Fight([9, 8, 4], elthric, bandit);
        encounter.TryBeginTurn(elthric);
        var (mind, body) = (bandit.Mind, bandit.Body);

        Assert.True(encounter.TryCast(new SpellCast(Spell(elthric, "Mind Spike")) { Target = bandit }), string.Join("\n", encounter.Log));

        Assert.Equal(1, bandit.Mind);
        Assert.Equal(body - (4 - (mind - 1)), bandit.Body);
        Assert.False(bandit.IsOutOfFight, string.Join("\n", encounter.Log));
    }

    [Fact]
    public void ACasterOnTheirLastMind_WeighsBacklashingTheCost_AmongTheWaysToPay()
    {
        // One Mind left, Overclock spent this round: a Spike paid from Mind would Stun him; paid with
        // Backlash it holds him at 1 and takes a Body.
        var elthric = CombatantFactory.FromCharacter(SampleEncounters.ElthricDane(Data), new GridPosition(2, 4));
        var bandit = CombatantFactory.FromAdversary(TestBandits.Roadside, "Bandit", new GridPosition(5, 4));
        var encounter = Fight([], elthric, bandit);
        encounter.TryBeginTurn(elthric);
        elthric.Mind = 1;
        elthric.HasOverclockedThisRound = true;

        var spikes = new TurnPlanner(new Assessor(encounter.View), BrainSkill.Best).Options(elthric, false, 1, real: true)
            .Where(o => o.Cast?.Spell.Name == "Mind Spike").ToList();

        Assert.NotEmpty(spikes);
        Assert.All(spikes, o => Assert.True(o.Cast!.Backlash || o.Cast.PayInBody, o.Why));
    }

    [Fact]
    public void AnInterruptOnTheLastMind_OffersBacklash_OrDropsToZero_OnlyAfterTheTurn()
    {
        // Drovan (Mind-secondary) on 1 Mind Interrupts the bandit's declared blow and won't Backlash:
        // he takes his turn at 1 Mind, and only then is Stunned.
        var drovan = CombatantFactory.FromCharacter(SampleEncounters.DrovanThale(Data), new GridPosition(2, 4));
        var mirella = CombatantFactory.FromCharacter(SampleEncounters.MirellaVask(Data), new GridPosition(2, 6));
        var bandit = CombatantFactory.FromAdversary(TestBandits.Roadside, "Bandit", new GridPosition(3, 4));
        var encounter = Fight([3, 2, 3, 2], drovan, mirella, bandit);
        Assert.True(drovan.HasInterrupt);
        drovan.Mind = 1;
        encounter.TryBeginTurn(mirella);
        encounter.EndTurn();
        encounter.RunAdversaryTurn();

        while (encounter.Pending is { Kind: not ReactionKind.Interrupt } other)
        {
            encounter.RespondToReaction(false);
        }

        Assert.Equal(ReactionKind.Interrupt, encounter.Pending?.Kind);
        encounter.RespondToReaction(true);
        Assert.Equal(ReactionKind.Backlash, encounter.Pending?.Kind);
        encounter.RespondToReaction(false);

        Assert.Equal(drovan, encounter.Active);
        Assert.Equal(1, drovan.Mind);
        encounter.EndTurn();
        while (encounter.Pending is not null)
        {
            encounter.RespondToReaction(false);
        }

        Assert.Equal(0, drovan.Mind);
        Assert.True(drovan.IsStunned, string.Join("\n", encounter.Log));
    }

    [Fact]
    public void ACharacterOnOneMindAndOneBody_CanStillInterrupt_ByDroppingToZero()
    {
        var drovan = CombatantFactory.FromCharacter(SampleEncounters.DrovanThale(Data), new GridPosition(2, 4));
        drovan.Mind = 1;
        drovan.Body = 1;

        Assert.Null(Encounter.InterruptRefusal(drovan));
    }
}
