using WoundBound.Rules.Calculation;
using WoundBound.Rules.Catalog;
using WoundBound.Rules.Enums;

namespace WoundBound.Game.Combat.Brains
{
    /// <summary>
    /// How well a GM sets up its creatures before a fight — the third lever of the GM brain, beside
    /// how cleverly it chooses (BrainSkill) and how much it knows of the party (Knowledge): the
    /// user's direction, 2026-11-03.
    /// </summary>
    public enum SetupSkill
    {
        /// <summary>Every creature where it does most: its best range, in cover, in the dark, spread round the party.</summary>
        Smart,

        /// <summary>A band facing the party at a sensible distance, taking the ground as it comes.</summary>
        Neutral,

        /// <summary>Bunched together, far back, in the open.</summary>
        Poor,
    }

    /// <summary>
    /// Places a GM's creatures for a fight nobody laid out by hand — a random encounter, a patrol
    /// met on the road (the user, 2026-11-03: "most scenarios will be built by the GM or scenario
    /// designer, but it would be good to have a smart AI decide placement for random encounters").
    ///
    /// <para>Every creature is given a square from the zone the scene allows. A smart setup weighs
    /// each square by what it does for that creature: shooters and casters at the band their attacks
    /// like best, with a line to the party; blades a move and a reach out, close enough to strike in
    /// the first round; Minions forward as a screen; cover that shelters the square from where the
    /// party stands, and the dark their lights don't reach; nobody side by side, where the smallest
    /// area spell takes both; a pack started near its kin, since Pack Tactics wants two at the same
    /// throat; and the rest round the party rather than in one clump. Prepared creatures
    /// lie in wait, for an ambush (Core Rules §9, "Ambush") — and a band placed badly, in the light,
    /// is the easier to spot.</para>
    /// </summary>
    public static class Placement
    {
        /// <summary>
        /// Gives each of <paramref name="adversaries"/> a starting square from <paramref name="zone"/>,
        /// against <paramref name="party"/> where it stands.
        /// </summary>
        /// <param name="prepared">Whether they are lying in wait: an ambush, rolled before round 1.</param>
        public static void Place(
            IReadOnlyList<Combatant> adversaries, IReadOnlyList<Combatant> party, Setting setting, EnvironmentTables? environment,
            RangeBandTables bands, int width, int height, IReadOnlyCollection<GridPosition> zone, SetupSkill skill, bool prepared,
            Random? random = null)
        {
            random ??= new Random(7);
            var dark = DarkMap(party, setting, environment, bands, width, height);
            var taken = party.SelectMany(c => c.Footprint).ToHashSet();
            var centre = new GridPosition((int)Math.Round(party.Average(p => p.Position.X)), (int)Math.Round(party.Average(p => p.Position.Y)));
            var placed = new List<Combatant>();

            // The exposed first: whoever has to stand off and shoot needs the best of the ground.
            var order = skill == SetupSkill.Smart
                ? adversaries.OrderBy(c => IsShooter(c) ? 0 : c.IsMinion ? 2 : 1).ToList()
                : adversaries.ToList();
            foreach (var creature in order)
            {
                var best = zone
                    .Where(sq => Fits(creature, sq, setting, taken, width, height))
                    .Select(sq => (Square: sq, Score: Score(creature, sq, skill, party, placed, centre, setting, dark, bands, zone) + random.NextDouble() * 0.01))
                    .OrderByDescending(x => x.Score)
                    .Select(x => (GridPosition?)x.Square)
                    .FirstOrDefault();
                if (best is not { } square)
                {
                    continue; // nowhere left in the zone: it stays where it was put
                }

                creature.Position = square;
                if (creature.Steed is { } steed && creature.IsRiding)
                {
                    steed.Position = square;
                }

                creature.LiesInWait = prepared;
                taken.UnionWith(creature.Footprint);
                placed.Add(creature);
            }
        }

        /// <summary>
        /// The squares from <paramref name="minimum"/> to <paramref name="maximum"/> squares from the
        /// nearest of <paramref name="party"/>, on the board — where a band met at random can start.
        /// </summary>
        public static IReadOnlyCollection<GridPosition> ZoneAround(IReadOnlyList<Combatant> party, int width, int height, int minimum = 3, int maximum = 12) =>
            Enumerable.Range(0, width).SelectMany(x => Enumerable.Range(0, height).Select(y => new GridPosition(x, y)))
                .Where(sq => party.Min(p => p.DistanceTo(sq)) is var d && d >= minimum && d <= maximum)
                .ToList();

        private static bool Fits(Combatant creature, GridPosition corner, Setting setting, HashSet<GridPosition> taken, int width, int height) =>
            Footprints.At(corner, creature.Across).All(sq => sq.X >= 0 && sq.Y >= 0 && sq.X < width && sq.Y < height
                                                              && !setting.IsSolid(sq) && !taken.Contains(sq) && !setting.IsDeepWater(sq));

        /// <summary>A creature that does its harm from range: a shot it would rather take, or a spell.</summary>
        private static bool IsShooter(Combatant c) =>
            c.Spells.Count > 0 || (c.HasRangedAttack && (c.Role == AdversaryRole.Skirmisher || !c.Attacks.Any(a => !a.IsRanged)));

        private static double Score(
            Combatant creature, GridPosition square, SetupSkill skill, IReadOnlyList<Combatant> party, List<Combatant> placed,
            GridPosition centre, Setting setting, IReadOnlyDictionary<GridPosition, Visibility> dark, RangeBandTables bands,
            IReadOnlyCollection<GridPosition> zone)
        {
            var nearest = party.Min(p => p.DistanceTo(square));
            switch (skill)
            {
                case SetupSkill.Poor:
                    // As far back as the zone goes, and huddled together.
                    return nearest - (placed.Count == 0 ? 0 : 1.5 * placed.Min(a => a.DistanceTo(square)));

                case SetupSkill.Neutral:
                    // The right distance, in a line facing the party: nothing more is weighed.
                    return 3 * RangeFit(creature, square, party, setting, bands)
                           - 0.15 * (placed.Count == 0 ? 0 : placed.Min(a => a.DistanceTo(square)));
            }

            var score = 3 * RangeFit(creature, square, party, setting, bands);

            // Cover from where the party stands: full for a shooter is a wall to lean round.
            score += 2 * party.Average(p => setting.CoverAgainst(p.Position, square, creature.Across) switch
            {
                Cover.Full => 1.0,
                Cover.Partial => 0.6,
                _ => 0,
            });

            // The dark the party's lights don't reach.
            if (dark.TryGetValue(square, out var seen))
            {
                score += seen switch { Visibility.None => 1.5, Visibility.Poor => 0.75, _ => 0 };
            }

            // Not side by side, where the smallest area spell takes both; a little wary at two apart.
            score -= placed.Count(a => a.DistanceTo(square) <= 1);
            score -= 0.3 * placed.Count(a => a.DistanceTo(square) == 2);

            // A pack hunts together: Pack Tactics wants another of its kin at the same throat, so it
            // starts near one rather than round the far side of the party.
            if (creature.HasPackTactics)
            {
                var kin = placed.Where(a => a.Kind == creature.Kind).Select(a => a.DistanceTo(square)).DefaultIfEmpty(int.MaxValue).Min();
                score += kin is >= 2 and <= 4 ? 1.5 : kin <= 6 ? 0.5 : 0;
            }
            else if (placed.Count > 0)
            {
                // The rest round the party rather than in one clump — worth less than arriving together.
                var angle = Math.Atan2(square.Y - centre.Y, square.X - centre.X);
                var apart = placed.Min(a => AngleBetween(angle, Math.Atan2(a.Position.Y - centre.Y, a.Position.X - centre.X)));
                score += 0.5 * apart / Math.PI;
            }

            // Ground that costs it: rubble underfoot, water, a hazard.
            score -= setting.GroundAt(square) == Terrain.Broken ? 0.5 : 0;
            score -= setting.HazardAt(square) is not null ? 3 : 0;
            return score;
        }

        /// <summary>
        /// How near the band a creature's harm wants <paramref name="square"/> is, 0 to 1: a shooter's
        /// best band with a line to the party; a blade's one move and a reach out; a Minion a step
        /// further in, to screen.
        /// </summary>
        private static double RangeFit(Combatant creature, GridPosition square, IReadOnlyList<Combatant> party, Setting setting, RangeBandTables bands)
        {
            var nearest = party.Min(p => p.DistanceTo(square));
            if (IsShooter(creature))
            {
                var ideal = creature.Spells.Count > 0 ? 6 : BestShotDistance(creature, bands);
                var seen = party.Any(p => setting.LineOpen(square, p.Position) && p.DistanceTo(square) <= ideal + 2);
                return seen ? Math.Max(0, 1 - Math.Abs(nearest - ideal) / 6.0) : 0.1;
            }

            var stride = creature.Speed switch { Speed.Slow => 2, Speed.Fast or Speed.Racing => 8, _ => 4 };
            var reach = Math.Max(1, creature.Reach);
            var wanted = stride + reach - (creature.IsMinion ? 1 : 0);
            return Math.Max(0, 1 - Math.Abs(nearest - wanted) / 6.0);
        }

        /// <summary>The distance a creature's ranged attack takes its shots from at no penalty, as far out as that goes.</summary>
        private static int BestShotDistance(Combatant creature, RangeBandTables bands)
        {
            var shot = creature.Attacks.FirstOrDefault(a => a.IsRanged && a.Weapon is not null);
            if (shot is null)
            {
                return 5;
            }

            var best = 4;
            for (var d = 2; d <= 16; d++)
            {
                var range = RangeBandCalculator.BandForSquares(bands, d) switch
                {
                    DistanceBand.Close => TargetRange.Close,
                    DistanceBand.Short => TargetRange.Short,
                    DistanceBand.Middle => TargetRange.Middle,
                    DistanceBand.Long => TargetRange.Long,
                    _ => TargetRange.BeyondLong,
                };
                var at = WeaponCalculator.ShotAt(shot.Weapon!, shot.DamageModifier, range);
                if (at.CanAttack && at.DodgeThresholdModifier == 0)
                {
                    best = d;
                }
            }

            return best;
        }

        private static double AngleBetween(double a, double b)
        {
            var d = Math.Abs(a - b) % (2 * Math.PI);
            return d > Math.PI ? 2 * Math.PI - d : d;
        }

        /// <summary>How well each square can be seen by the party's own lights — empty where the scene is lit.</summary>
        private static IReadOnlyDictionary<GridPosition, Visibility> DarkMap(
            IReadOnlyList<Combatant> party, Setting setting, EnvironmentTables? environment, RangeBandTables bands, int width, int height)
        {
            if (environment is null || !setting.IsUnlit)
            {
                return new Dictionary<GridPosition, Visibility>();
            }

            // A fight of the party alone, to read the light by: no weather, so nobody's Stamina is touched.
            var probe = new Encounter(width, height, party, bands, new RandomDiceRoller(new Random(1)),
                setting: setting with { Exposure = Exposure.Normal }, environment: environment);
            return Enumerable.Range(0, width).SelectMany(x => Enumerable.Range(0, height).Select(y => new GridPosition(x, y)))
                .ToDictionary(sq => sq, probe.VisibilityAtSquare);
        }
    }
}
