using WoundBound.Game.Combat;
using WoundBound.Game.Combat.Brains;
using WoundBound.Rules.Enums;

namespace WoundBound.Game.Tests;

/// <summary>
/// The GM's setup (the third lever, the user's direction 2026-11-03): placing a band met at random.
/// A smart setup takes cover and spreads round the party; a poor one huddles in the open.
/// </summary>
public class PlacementTests
{
    private static readonly Game.Data.GameData Data = TestData.Load();

    /// <summary>A line of bushes four squares east of the fighter, rows 5 to 13.</summary>
    private static readonly Setting Bushes = new() { Name = "Bushes", CoverPieces = Setting.CoverOf(Cover.Partial, (9, 5, 9, 13)) };

    private static Combatant Fighter() => CombatantFactory.FromCharacter(SampleEncounters.DumbFighter(Data), new GridPosition(5, 9));

    private static List<Combatant> Band() =>
    [
        CombatantFactory.FromAdversary(TestBandits.Ambusher, "Crossbow", new GridPosition(0, 0)),
        CombatantFactory.FromAdversary(Data.Adversary("Tollroad Bandit"), "Tollroad Bandit", new GridPosition(0, 1)),
        CombatantFactory.FromAdversary(Data.Adversary("Timber Wolf"), "Wolf", new GridPosition(0, 2)),
        CombatantFactory.FromAdversary(TestBandits.Roadside, "Bandit", new GridPosition(0, 3)),
    ];

    private static List<Combatant> Placed(SetupSkill skill, bool prepared = false, Setting? setting = null)
    {
        var party = new[] { Fighter() };
        var band = Band();
        Placement.Place(band, party, setting ?? Bushes, Data.Environment, Data.RangeBands, SampleEncounters.FieldWidth, SampleEncounters.FieldHeight,
            Placement.ZoneAround(party, SampleEncounters.FieldWidth, SampleEncounters.FieldHeight), skill, prepared);
        return band;
    }

    [Fact]
    public void ASmartSetup_PutsTheShooterBehindCover_FromTheParty()
    {
        var crossbow = Placed(SetupSkill.Smart).Single(c => c.Name == "Crossbow");

        Assert.NotEqual(Cover.None, Bushes.CoverAgainst(new GridPosition(5, 9), crossbow.Position));
    }

    [Fact]
    public void ASmartSetup_SpreadsTheBand_SoNoAreaSpellTakesTwo()
    {
        var band = Placed(SetupSkill.Smart);

        foreach (var a in band)
        {
            Assert.All(band.Where(b => b != a), b => Assert.True(a.DistanceTo(b) > 2, $"{a.Name} at {a.Position}, {b.Name} at {b.Position}"));
        }
    }

    [Fact]
    public void APoorSetup_KnotsTheBandTogether_InTheOpen_AtAMiddlingDistance()
    {
        var band = Placed(SetupSkill.Poor);
        var fighter = new GridPosition(5, 9);

        var widest = band.SelectMany(a => band.Select(b => a.DistanceTo(b))).Max();
        Assert.True(widest <= 3, $"spread {widest}");
        Assert.All(band, c => Assert.InRange(c.Position.DistanceTo(fighter), 5, 11));
        Assert.All(band, c => Assert.Equal(Cover.None, Bushes.CoverAgainst(fighter, c.Position)));
    }

    [Theory]
    [InlineData(SetupSkill.Smart)]
    [InlineData(SetupSkill.Neutral)]
    [InlineData(SetupSkill.Poor)]
    public void EveryCreature_StandsOnOpenGround_InTheZone_AndAPreparedBandLiesInWait(SetupSkill skill)
    {
        var band = Placed(skill, prepared: true);
        var fighter = new GridPosition(5, 9);

        Assert.Equal(band.Count, band.Select(c => c.Position).Distinct().Count());
        Assert.All(band, c =>
        {
            Assert.False(Bushes.IsSolid(c.Position));
            Assert.InRange(c.Position.DistanceTo(fighter), 3, 12);
            Assert.True(c.LiesInWait);
        });
    }

    [Fact]
    public void TheClearing_IsAFight_ThatStartsWithTheBandPlaced()
    {
        var encounter = SampleEncounters.WoodlandClearing(Data, new RandomDiceRoller(new Random(1)), prepared: false);

        Assert.All(encounter.Combatants.Where(c => c.Side == Side.Adversaries),
            a => Assert.InRange(encounter.Combatants.Where(c => c.Side == Side.Party).Min(p => p.DistanceTo(a)), 3, 12));
    }

    [Theory]
    [InlineData(SetupSkill.Smart)]
    [InlineData(SetupSkill.Neutral)]
    [InlineData(SetupSkill.Poor)]
    public void AZonedFight_PlacedByTheGm_PutsEveryCreatureInsideItsZones(SetupSkill skill)
    {
        var encounter = SampleEncounters.CampfireAmbush(Data, new RandomDiceRoller(new Random(1)), setup: new GmSetup(skill, Prepared: false, Seed: 3));
        var zones = SampleEncounters.CampAtNight.Deployment.SelectMany(z => z.Squares).ToHashSet();
        var band = encounter.Combatants.Where(c => c.Side == Side.Adversaries).ToList();

        Assert.All(band, c => Assert.Contains(c.Position, zones));
        Assert.Equal(band.Count, band.Select(c => c.Position).Distinct().Count());
    }

    [Fact]
    public void WithoutASetup_TheScenariosOwnSquaresStand()
    {
        var encounter = SampleEncounters.CampfireAmbush(Data, new RandomDiceRoller(new Random(1)));

        Assert.Equal(new GridPosition(20, 9), encounter.Combatants.Single(c => c.Name == "Tollroad Bandit").Position);
    }
}
