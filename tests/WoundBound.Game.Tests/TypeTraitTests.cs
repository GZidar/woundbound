using WoundBound.Game.Combat;
using WoundBound.Rules.Enums;

namespace WoundBound.Game.Tests;

/// <summary>Pack Tactics and the Type traits (GM Reference §9, "On Type"): the Undead's edges, and what has no mind.</summary>
public class TypeTraitTests
{
    private static readonly Game.Data.GameData Data = TestData.Load();

    private static Encounter Fight(int[] dice, params Combatant[] combatants) =>
        new(SampleEncounters.FieldWidth, SampleEncounters.FieldHeight, combatants, Data.RangeBands, new ScriptedDice(dice), environment: Data.Environment);

    [Fact]
    public void ARatkin_KnowsItsKind_AndHasPackTactics()
    {
        var ratkin = CombatantFactory.FromAdversary(Data.Adversary("Bramble Ratkin"), "Ratkin Biter", new GridPosition(5, 4));

        Assert.Equal("Bramble Ratkin", ratkin.Kind);
        Assert.True(ratkin.HasPackTactics);
    }

    [Fact]
    public void PackTactics_NeedsASecondOfItsKind_WithinReachOfTheSameTarget()
    {
        var joren = CombatantFactory.FromCharacter(SampleEncounters.JorenBlacke(Data), new GridPosition(4, 4));
        var biter = CombatantFactory.FromAdversary(Data.Adversary("Bramble Ratkin"), "Ratkin Biter", new GridPosition(5, 4));
        var shiv = CombatantFactory.FromAdversary(Data.Adversary("Bramble Ratkin"), "Ratkin Shiv", new GridPosition(12, 4));
        var wolf = CombatantFactory.FromAdversary(Data.Adversary("Timber Wolf"), "Timber Wolf", new GridPosition(3, 4));
        var encounter = Fight([], joren, biter, shiv, wolf);

        // A wolf beside Joren is not a second Ratkin.
        Assert.Null(encounter.PackMateOf(biter, joren));

        shiv.Position = new GridPosition(4, 5);
        Assert.Same(shiv, encounter.PackMateOf(biter, joren));
    }

    [Fact]
    public void PackTactics_GivesTheRatkinAdvantage()
    {
        var joren = CombatantFactory.FromCharacter(SampleEncounters.JorenBlacke(Data), new GridPosition(4, 4));
        var biter = CombatantFactory.FromAdversary(Data.Adversary("Bramble Ratkin"), "Ratkin Biter", new GridPosition(5, 4));
        var shiv = CombatantFactory.FromAdversary(Data.Adversary("Bramble Ratkin"), "Ratkin Shiv", new GridPosition(4, 5));
        var encounter = Fight(Enumerable.Repeat(3, 20).ToArray(), joren, biter, shiv);
        encounter.DeclineFreeChoices();
        joren.Stamina = 0;

        encounter.TryBeginTurn(joren);
        encounter.EndTurn();
        encounter.RunAdversaryTurn();

        Assert.Contains(encounter.Log, l => l.Contains("has Pack Tactics"));
    }

    [Theory]
    [InlineData("Iron Cudgel (Heavy, Breaking)", WeaponEdge.Breaking)]
    [InlineData("Rusted Blade (Cutting)", WeaponEdge.Cutting)]
    [InlineData("Unarmed", WeaponEdge.Breaking)]
    [InlineData("Bite", null)]
    public void AStatblockLine_TakesItsEdgeFromItsName(string name, WeaponEdge? edge) =>
        Assert.Equal(edge, CombatantFactory.StatblockLine(name, 0, 1, "d6", 0, 1).Edge);

    [Fact]
    public void TheUndead_TakeBreakingHarder_AndCuttingHalved()
    {
        var husk = CombatantFactory.FromAdversary(Data.Adversary("Charnel Revenant"), "Revenant", new GridPosition(5, 4));

        Assert.Equal(9, Encounter.ByEdge(WeaponEdge.Breaking, husk, 7, armour: 0).Dealt);
        Assert.Equal(3, Encounter.ByEdge(WeaponEdge.Cutting, husk, 7, armour: 0).Dealt);
        Assert.Equal(7, Encounter.ByEdge(null, husk, 7, armour: 0).Dealt);
        // The halving comes after armour; the +2 before it.
        Assert.Equal(2, Encounter.ByEdge(WeaponEdge.Cutting, husk, 7, armour: 2).Dealt);
        Assert.Equal(7, Encounter.ByEdge(WeaponEdge.Breaking, husk, 7, armour: 2).Dealt);
    }

    [Fact]
    public void JorensAxe_IsHalvedOnARevenant()
    {
        var joren = CombatantFactory.FromCharacter(SampleEncounters.JorenBlacke(Data), new GridPosition(4, 4));
        var revenant = CombatantFactory.FromAdversary(Data.Adversary("Charnel Revenant"), "Revenant", new GridPosition(5, 4));
        var encounter = Fight([11, 10, 4], joren, revenant);
        encounter.DeclineFreeChoices();
        revenant.Stamina = 0;
        Assert.Equal(WeaponEdge.Cutting, joren.Attack.Edge);

        encounter.TryBeginTurn(joren);
        encounter.TryAttack(revenant);

        Assert.Contains(encounter.Log, l => l.Contains("Resistant to Cutting"));
        var rolled = 4 + joren.Attack.DamageModifier;
        Assert.Equal(revenant.BodyMax - rolled / 2, revenant.Body);
    }

    [Fact]
    public void AConstruct_IsBoundNotAlive_AndARevenantHasNoMindToHurt()
    {
        var mail = CombatantFactory.FromAdversary(Data.Adversary("Animated Suit of Mail"), "Mail", new GridPosition(5, 4));
        var revenant = CombatantFactory.FromAdversary(Data.Adversary("Charnel Revenant"), "Revenant", new GridPosition(5, 4));

        Assert.True(mail.IsBoundNotAlive);
        Assert.True(mail.IsMindImmune);
        Assert.Equal(0, mail.MindMax);
        Assert.True(revenant.IsMindImmune);
        Assert.False(revenant.IsBoundNotAlive);
    }
}
