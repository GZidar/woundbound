using WoundBound.Game.Combat;
using WoundBound.Rules.Enums;

namespace WoundBound.Game.Tests;

/// <summary>Core Rules §9, "Environment": Visibility and the lights that answer it, Exposure, and the ground underfoot.</summary>
public class SettingTests
{
    private static readonly Game.Data.GameData Data = TestData.Load();

    private static Combatant Named(Encounter encounter, string name) => encounter.Combatants.Single(c => c.Name == name);

    private static Encounter Night(params int[] dice) => SampleEncounters.SignpostAmbush(Data, new ScriptedDice(dice));

    /// <summary>The Dumb Fighter and the three ratkin, wherever <paramref name="setting"/> says.</summary>
    private static Encounter RatkinIn(Setting setting, params int[] dice)
    {
        var ratkin = Data.Adversary("Bramble Ratkin");
        return new Encounter(SampleEncounters.BoardWidth, SampleEncounters.BoardHeight, new[]
        {
            CombatantFactory.FromCharacter(SampleEncounters.DumbFighter(Data), new GridPosition(1, 4)),
            CombatantFactory.FromAdversary(ratkin, "Ratkin Biter", new GridPosition(7, 2)),
            CombatantFactory.FromAdversary(ratkin, "Ratkin Sneak", new GridPosition(8, 4)),
            CombatantFactory.FromAdversary(ratkin, "Ratkin Shiv", new GridPosition(7, 6)),
        }, Data.RangeBands, new ScriptedDice(dice), setting: setting, environment: Data.Environment);
    }

    [Fact]
    public void InTheDark_WhoeverIsNearALight_IsSeen_AndNobodyElse()
    {
        // One carried light lights Close and Short at Poor, and nothing beyond (Core Rules §9).
        var encounter = Night();
        var ridge = Named(encounter, "Ambusher on the ridge");
        Assert.Equal(Visibility.Poor, encounter.VisibilityOf(ridge)); // three squares from the lantern
        ridge.Position = new GridPosition(12, 1);

        Assert.Equal(Visibility.Poor, encounter.VisibilityOf(Named(encounter, "The Dumb Fighter"))); // his own torch
        Assert.Equal(Visibility.Poor, encounter.VisibilityOf(Named(encounter, "Salla Rinehart")));   // beside it
        Assert.Equal(Visibility.Poor, encounter.VisibilityOf(Named(encounter, "Lantern-bearer")));   // his lantern
        Assert.Equal(Visibility.None, encounter.VisibilityOf(ridge)); // out past Short of every light
    }

    [Fact]
    public void TheDark_RaisesDodge_AndHidesAnythingBeyondClose()
    {
        var encounter = Night();
        var fighter = Named(encounter, "The Dumb Fighter");
        var drovan = Named(encounter, "Drovan Thale");
        var ridge = Named(encounter, "Ambusher on the ridge");

        // Lit at Poor, the fighter is +2 to hit from the dark as well as +2 for Middle.
        ridge.Position = new GridPosition(fighter.Position.X + 6, fighter.Position.Y);
        Assert.Equal(fighter.DodgeThreshold + 4, encounter.PlanAttack(ridge, fighter).DodgeThreshold);

        // Out in the dark, beyond Short of every light, the ambusher can't be shot at all.
        ridge.Position = new GridPosition(8, 12);
        Assert.Equal(Visibility.None, encounter.VisibilityOf(ridge));
        Assert.False(encounter.PlanAttack(drovan, ridge).CanAttack);
        Assert.Contains("dark", encounter.PlanAttack(drovan, ridge).Refusal!);

        // Step into the edge of the fighter's torchlight and he's there to be seen, at Poor.
        ridge.Position = new GridPosition(fighter.Position.X + 3, fighter.Position.Y + 3);
        Assert.Equal(Visibility.Poor, encounter.VisibilityOf(ridge));
    }

    [Fact]
    public void ACarriedLight_TakesAHand()
    {
        var fighter = Named(Night(), "The Dumb Fighter");

        Assert.Equal(1, fighter.CarriedLights);
        Assert.False(fighter.HasFreeHand); // longsword and torch
    }

    [Fact]
    public void ADroppedLight_GoesOnBurning_AndTheOneWhoDroppedIt_CanStepOutOfIt()
    {
        var encounter = Night();
        var fighter = Named(encounter, "The Dumb Fighter");
        Assert.True(encounter.TryBeginTurn(fighter));

        Assert.True(encounter.TryDropLight());

        Assert.Equal(0, fighter.CarriedLights);
        Assert.True(fighter.HasFreeHand);
        Assert.Equal(new[] { new GridPosition(1, 4) }, encounter.LightsOnTheGround);
        Assert.Equal(Visibility.Poor, encounter.VisibilityOf(fighter));

        fighter.Position = new GridPosition(1, 12); // eight squares from the torch, and from the lantern: beyond Short
        Assert.Equal(Visibility.None, encounter.VisibilityOf(fighter));
    }

    [Fact]
    public void LightingATorch_TakesTheAction_AndAFreeHand()
    {
        var encounter = SampleEncounters.SignpostAmbush(Data, new ScriptedDice(), setting: SampleEncounters.SignpostNight with { Name = "unlit, no torch" });
        var fighter = Named(encounter, "The Dumb Fighter");
        encounter.TryBeginTurn(fighter);
        encounter.TryDropLight();

        Assert.True(encounter.TryLight());

        Assert.Equal(1, fighter.CarriedLights);
        Assert.Equal(0, fighter.ActionsRemaining);
        Assert.NotNull(Encounter.LightRefusal(fighter));
    }

    [Fact]
    public void HarshExposure_LowersResolve_AndTakesStaminaBeforeTheFirstBlow()
    {
        var encounter = RatkinIn(new Setting { Name = "Sleet", Exposure = Exposure.Harsh });
        var fighter = encounter.Combatants.Single(c => c.Side == Side.Party);

        Assert.Equal(fighter.StaminaMax - 1, fighter.Stamina);
        Assert.Equal(fighter.BaseResolveThreshold - 2, fighter.ResolveThreshold);
        Assert.Contains(encounter.Log, line => line.StartsWith("The harsh weather takes 1 Stamina"));
    }

    [Fact]
    public void BrokenGround_ShakesYourFooting_AndCostsAMoreToCross()
    {
        var rubble = new Setting { Name = "Rubble", Ground = Setting.Patches(Terrain.Broken, (3, 4, 3, 4)) };
        var encounter = RatkinIn(rubble);
        var fighter = encounter.Combatants.Single(c => c.Side == Side.Party);

        // Onto the rubble: a Short move, and one more for the ground. Past it, the dry way round is
        // no longer, so that's the way taken, at the plain price.
        Assert.Equal(2, encounter.MoveCostTo(fighter, new GridPosition(3, 4)));
        Assert.Equal(1, encounter.MoveCostTo(fighter, new GridPosition(5, 4)));
        Assert.DoesNotContain(new GridPosition(3, 4), encounter.PlanMove(fighter, new GridPosition(5, 4))!.Path);

        var standing = fighter.StabilityThreshold;
        fighter.Position = new GridPosition(3, 4);
        Assert.Equal(standing - 2, fighter.StabilityThreshold);
    }

    [Fact]
    public void SureFooting_Steadies()
    {
        var encounter = RatkinIn(new Setting { Name = "Drill yard", Ground = Setting.Patches(Terrain.Sure, (1, 4, 1, 4)) });
        var fighter = encounter.Combatants.Single(c => c.Side == Side.Party);

        Assert.Equal(fighter.BaseStabilityThreshold + 2, fighter.StabilityThreshold);
    }

    [Fact]
    public void ACriticalFailure_OnBrokenGround_PutsYouOnIt()
    {
        // 1 and 1 on the fighter's swing: a critical failure — he takes the wound for it, and the
        // rubble underfoot puts him down as well.
        var rubble = new Setting { Name = "Rubble", Ground = Setting.Patches(Terrain.Broken, (6, 2, 6, 2)) };
        var encounter = RatkinIn(rubble, 1, 1);
        var fighter = encounter.Combatants.Single(c => c.Side == Side.Party);
        fighter.Position = new GridPosition(6, 2);

        encounter.TryAttack(Named(encounter, "Ratkin Biter"));

        Assert.True(fighter.IsProne);
        Assert.Contains(encounter.Log, line => line == "The Dumb Fighter loses their footing on the broken ground, and goes down Prone.");
    }
}
