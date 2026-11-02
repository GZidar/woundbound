using WoundBound.Game.Combat;

namespace WoundBound.Game.Tests;

/// <summary>Core Rules §15, "Everything else goes through the pack", and §9's thrown weapons.</summary>
public class PackAndThrowTests
{
    private static readonly Game.Data.GameData Data = TestData.Load();

    private static Combatant Bandit(string name, int x, int y)
    {
        var bandit = CombatantFactory.FromAdversary(SampleEncounters.RoadsideBandit, name, new GridPosition(x, y));
        bandit.Stamina = 1;
        return bandit;
    }

    /// <summary>Joren, with what's given in hand and in the pack instead of his axe and buckler.</summary>
    private static Combatant Joren(string[] inHand, string[] inPack, int x = 2, int y = 4)
    {
        var build = SampleEncounters.JorenBlacke(Data);
        build.Weapons.Clear();
        build.Shields.Clear();
        foreach (var name in inHand)
        {
            build.Weapons.Add(CombatantFactory.InHand(Data.Weapon(name)));
        }

        foreach (var name in inPack)
        {
            build.Weapons.Add(CombatantFactory.InPack(Data.Weapon(name)));
        }

        return CombatantFactory.FromCharacter(build, new GridPosition(x, y));
    }

    private static Encounter Fight(int[] dice, params Combatant[] combatants)
    {
        var encounter = new Encounter(SampleEncounters.FieldWidth, SampleEncounters.FieldHeight, combatants, Data.RangeBands, new ScriptedDice(dice));
        encounter.DeclineFreeChoices();
        return encounter;
    }

    [Fact]
    public void DrawingFromThePack_CostsAnAction_AndAHeavyOneAStamina()
    {
        var joren = Joren([], ["Warhammer"]);
        var encounter = Fight([], joren, Bandit("Bandit", 12, 12));
        encounter.TryBeginTurn(joren);
        var hammer = Encounter.PackWeapons(joren).Single();
        var stamina = joren.Stamina;

        Assert.True(encounter.TryDrawWeapon(hammer));

        Assert.Equal("Warhammer", joren.InHandWeapon?.Name);
        Assert.Equal(0, joren.ActionsRemaining);
        Assert.Equal(stamina - 1, joren.Stamina);
        Assert.Equal(0, joren.TacticalSpendsThisRound);
    }

    [Fact]
    public void FullHands_MustDropOrStowFirst_AndDroppingIsFree()
    {
        var joren = Joren(["Battleaxe"], ["Longsword"]);
        var encounter = Fight([], joren, Bandit("Bandit", 12, 12));
        encounter.TryBeginTurn(joren);
        var sword = Encounter.PackWeapons(joren).Single();

        // A Longsword isn't Light, so it can't be the second of two.
        Assert.Contains("No room in hand", Encounter.DrawRefusal(joren, sword));

        Assert.True(encounter.TryDropWeapon(joren.InHandWeapon!));
        Assert.Equal(1, joren.ActionsRemaining);
        Assert.Single(encounter.GroundWeapons);
        Assert.Null(Encounter.DrawRefusal(joren, sword));
    }

    [Fact]
    public void PickingUp_IsAnAction_StandingOverIt()
    {
        var joren = Joren(["Battleaxe"], []);
        var encounter = Fight([], joren, Bandit("Bandit", 12, 12));
        encounter.TryBeginTurn(joren);
        encounter.TryDropWeapon(joren.InHandWeapon!);
        var axe = encounter.GroundWeapons.Single();

        Assert.True(encounter.TryPickUpWeapon(axe));

        Assert.Equal("Battleaxe", joren.InHandWeapon?.Name);
        Assert.Empty(encounter.GroundWeapons);
        Assert.Equal(0, joren.ActionsRemaining);
    }

    [Fact]
    public void AHandAxe_IsThrownAsAShot_AndLiesWhereItStruck()
    {
        var joren = Joren(["Hand-axe"], []);
        var bandit = Bandit("Bandit", 5, 4);
        var encounter = Fight([10, 9, 2], joren, bandit, Bandit("Other", 15, 15));
        encounter.TryBeginTurn(joren);

        Assert.True(Encounter.ThrowOf(joren)!.IsRanged);
        Assert.NotNull(encounter.TryThrow(bandit));

        Assert.Null(joren.InHandWeapon);
        Assert.Equal(bandit.Position, encounter.GroundWeapons.Single().At);
        Assert.Contains(encounter.Log, l => l.Contains("Hand-axe leaves"));
    }

    [Fact]
    public void AtClose_AThrowableWeaponIsSwung_NotThrown()
    {
        var joren = Joren(["Hand-axe"], []);
        var bandit = Bandit("Bandit", 3, 4);
        var encounter = Fight([], joren, bandit);
        encounter.TryBeginTurn(joren);

        Assert.Contains("swung, not thrown", encounter.ThrowRefusal(joren, bandit));
        Assert.Contains("Nothing in hand", Fight([], Joren(["Longsword"], []), bandit).ThrowRefusal(Joren(["Longsword"], []), bandit));
    }
}
