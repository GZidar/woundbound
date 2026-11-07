using WoundBound.Game.Combat;
using WoundBound.Game.Combat.Brains;

namespace WoundBound.Game.Simulation
{
    /// <summary>How one bot-played fight came out.</summary>
    public sealed record FightResult(
        EncounterPhase Outcome,
        int Rounds,
        int PartyBodyLeft,
        int PartyStaminaLeft,
        int FoesDropped,
        int CharactersStanding,
        int FoesFled,
        int ObjectivesMet,
        int ObjectivesSet,
        BotTally Tally)
    {
        /// <summary>What the fight cost the party (null where nobody measured it).</summary>
        public Attrition? Cost { get; init; }

        public bool Won => Outcome == EncounterPhase.Victory;

        public bool Lost => Outcome == EncounterPhase.Defeat;

        /// <summary>The party got clear of foes still willing to fight.</summary>
        public bool Withdrew => Outcome == EncounterPhase.Withdrew;

        public bool Stalemate => !Won && !Lost && !Withdrew;
    }

    /// <summary>
    /// What a fight cost the party, whatever its outcome: wounds on each track as a share of what
    /// the party had going in, Stamina spent, characters dropped along the way, still down at the
    /// end, and dead.
    /// </summary>
    public sealed record Attrition(
        double BodyLost, double MindSoulLost, double StaminaSpent, int Dropped, int DownAtEnd, int Dead, int Characters);

    /// <summary>One adventuring day: fights back to back, a short rest between, wounds carried.</summary>
    public sealed record DayResult(IReadOnlyList<FightResult> Fights, int FightsPlanned)
    {
        /// <summary>The Skill Challenge the day opened with, if it had one.</summary>
        public Skills.SkillChallengeResult? Opening { get; init; }

        /// <summary>The party came through every fight of the day.</summary>
        public bool Survived => Fights.Count == FightsPlanned && Fights.All(f => f.Won || f.Withdrew);

        public int Dead => Fights.Count == 0 ? 0 : Fights[^1].Cost?.Dead ?? 0;
    }

    /// <summary>A tactic's record over many fights: how often it wins, how fast, at what cost, and what it actually did.</summary>
    public sealed record TacticReport(string Tactic, IReadOnlyList<FightResult> Fights)
    {
        public int Wins => Fights.Count(f => f.Won);

        public int Losses => Fights.Count(f => f.Lost);

        public int Stalemates => Fights.Count(f => f.Stalemate);

        public int Withdrawals => Fights.Count(f => f.Withdrew);

        public double WinRate => Fights.Count == 0 ? 0 : (double)Wins / Fights.Count;

        public double AverageRounds => Fights.Average(f => f.Rounds);

        /// <summary>Body the party had left, in the fights it won — how close a win was.</summary>
        public double AverageBodyLeftOnWin => Wins == 0 ? 0 : Fights.Where(f => f.Won).Average(f => f.PartyBodyLeft);

        public double AverageFoesDropped => Fights.Average(f => f.FoesDropped);

        /// <summary>The share of fights in which every objective was met; null when the fight sets none.</summary>
        public double? ObjectiveRate => Fights.Count == 0 || Fights[0].ObjectivesSet == 0
            ? null
            : (double)Fights.Count(f => f.ObjectivesMet == f.ObjectivesSet) / Fights.Count;

        public double AverageFled => Fights.Average(f => f.FoesFled);

        /// <summary>Characters still fighting at the end of the fights the party won.</summary>
        public double AverageStandingOnWin => Wins == 0 ? 0 : Fights.Where(f => f.Won).Average(f => f.CharactersStanding);

        public double Per(Func<BotTally, int> count) => Fights.Average(f => count(f.Tally));

        private IEnumerable<Attrition> Costs => Fights.Select(f => f.Cost).OfType<Attrition>();

        public double AverageCost(Func<Attrition, double> of) => Costs.Select(of).DefaultIfEmpty(0).Average();
    }

    /// <summary>
    /// Plays a fight over and over with bots at both seats, and reports how each way of playing
    /// the party fared — the GM's brain held fixed, the dice random but seeded.
    ///
    /// <para>Every tactic faces the same seeds, fight for fight, so where two tactics differ it's
    /// their choices that differ rather than their luck at the start.</para>
    /// </summary>
    public static class Simulator
    {
        /// <param name="stage">Sets up a fresh fight on the given dice: the board, the combatants and the GM brain.</param>
        /// <param name="party">Plays the party. Brains may hold state, so one is made per fight.</param>
        /// <param name="streamDice">Roll each fight on a <see cref="StreamDiceRoller"/>, which a cheating GM can copy.</param>
        public static TacticReport Run(Func<IDiceRoller, Encounter> stage, Func<IPartyBrain> party, int fights, int seed = 1, int maxRounds = 50,
            bool streamDice = false)
        {
            // Each fight stands alone on its own seed, so they play side by side; the results keep seed order.
            var results = new FightResult[fights];
            Parallel.For(0, fights, i => results[i] = PlayOne(stage(streamDice
                ? new StreamDiceRoller((ulong)(seed + i))
                : new RandomDiceRoller(new Random(seed + i))), party(), maxRounds));
            return new TacticReport(fights > 0 ? party().Name : "(none)", results.ToList());
        }

        public static FightResult PlayOne(Encounter encounter, IPartyBrain party, int maxRounds = 50)
        {
            var before = encounter.Combatants.Where(c => c.Side == Side.Party).ToDictionary(c => c, c => (c.Body, c.Mind, c.Soul, c.Stamina));
            var driver = new BotDriver(encounter, party);
            driver.PlayToEnd(maxRounds);

            var members = encounter.Combatants.Where(c => c.Side == Side.Party).ToList();
            var cost = new Attrition(
                BodyLost: Share(members.Sum(c => Math.Max(0, before[c].Body) - Math.Max(0, c.Body)), members.Sum(c => c.BodyMax)),
                MindSoulLost: Share(members.Sum(c => before[c].Mind + before[c].Soul - c.Mind - c.Soul), members.Sum(c => c.MindMax + c.SoulMax)),
                StaminaSpent: Share(members.Sum(c => before[c].Stamina - c.Stamina), members.Sum(c => before[c].Stamina)),
                Dropped: members.Count(c => c.HasBeenDropped),
                DownAtEnd: members.Count(c => c.Body <= 0 && c.BodyStatus != Rules.Enums.BodyStatus.Dead),
                Dead: members.Count(c => c.BodyStatus == Rules.Enums.BodyStatus.Dead),
                Characters: members.Count);
            return new FightResult(
                encounter.Phase,
                encounter.Round,
                members.Sum(c => c.Body),
                members.Sum(c => c.Stamina),
                encounter.Combatants.Count(c => c.Side == Side.Adversaries && c.IsOutOfFight && !c.HasFled),
                members.Count(c => !c.IsOutOfFight),
                encounter.Combatants.Count(c => c.Side == Side.Adversaries && c.HasFled),
                encounter.Objectives.Count(o => o.StatusIn(encounter) == ObjectiveStatus.Achieved),
                encounter.Objectives.Count,
                driver.Tally) { Cost = cost };

            static double Share(double part, double whole) => whole <= 0 ? 0 : part / whole;
        }

        /// <summary>
        /// Plays <paramref name="days"/> adventuring days of <paramref name="fightsPerDay"/> fights each,
        /// back to back: the party's wounds carry from one fight to the next, with a short rest
        /// between (Core Rules §4, "Recovery"): Stamina back to full, and each track that has taken a
        /// fresh wound tended once — a Heal, a Meditate, a Pray. Anyone still down is stabilised; the
        /// dead stay dead. A day ends early if the party is beaten.
        /// </summary>
        /// <param name="opening">A Skill Challenge the day opens with, played by the first fight's party before that fight
        /// begins: what it costs them, they carry into it — no rest between (Core Rules §8, "Failing Badly").</param>
        public static IReadOnlyList<DayResult> RunDays(Func<IDiceRoller, Encounter> stage, Func<IPartyBrain> party, int days, int fightsPerDay, int seed = 1, int maxRounds = 50,
            Func<IReadOnlyList<Combatant>, IDiceRoller, Skills.SkillChallengeResult>? opening = null)
        {
            // Each day stands alone on its own seeds, so they play side by side; the results keep day order.
            var results = new DayResult[days];
            Parallel.For(0, days, day =>
            {
                var fights = new List<FightResult>();
                Skills.SkillChallengeResult? opened = null;
                Dictionary<string, Carried>? carried = null;
                for (var k = 0; k < fightsPerDay; k++)
                {
                    var encounter = stage(new RandomDiceRoller(new Random(seed + day * fightsPerDay + k)));
                    if (carried is not null)
                    {
                        foreach (var member in encounter.Combatants.Where(c => c.Side == Side.Party))
                        {
                            if (carried.TryGetValue(member.Name, out var was))
                            {
                                (member.Body, member.Mind, member.Soul, member.IsStable) = (was.Body, was.Mind, was.Soul, was.Stable);
                                foreach (var (track, eligible) in was.Tend)
                                {
                                    member.TendEligible[track] = eligible;
                                }

                                member.IsUnconscious = member.Body <= 0;
                            }
                        }
                    }

                    if (k == 0 && opening is not null)
                    {
                        opened = opening(encounter.Combatants.Where(c => c.Side == Side.Party).ToList(), new RandomDiceRoller(new Random(seed * 7919 + day)));
                    }

                    var result = PlayOne(encounter, party(), maxRounds);
                    fights.Add(result);
                    if (!(result.Won || result.Withdrew))
                    {
                        break;
                    }

                    carried = encounter.Combatants.Where(c => c.Side == Side.Party).ToDictionary(c => c.Name, ShortRest);
                }

                results[day] = new DayResult(fights, fightsPerDay) { Opening = opened };
            });

            return results;
        }

        private sealed record Carried(int Body, int Mind, int Soul, bool Stable, Dictionary<Rules.Enums.WoundType, bool> Tend);

        /// <summary>A short rest between fights: Stamina comes back with the next fight; each track with a fresh wound is tended once.</summary>
        private static Carried ShortRest(Combatant c)
        {
            var tend = new Dictionary<Rules.Enums.WoundType, bool>(c.TendEligible);
            if (c.BodyStatus == Rules.Enums.BodyStatus.Dead)
            {
                return new Carried(c.Body, c.Mind, c.Soul, false, tend);
            }

            int Tended(Rules.Enums.WoundType track, int now, int max)
            {
                if (now < max && tend[track])
                {
                    tend[track] = false;
                    return now + 1;
                }

                return now;
            }

            var body = Tended(Rules.Enums.WoundType.Body, c.Body, c.BodyMax);
            return new Carried(body, Tended(Rules.Enums.WoundType.Mind, c.Mind, c.MindMax), Tended(Rules.Enums.WoundType.Soul, c.Soul, c.SoulMax), body <= 0, tend);
        }

        /// <summary>What each way of playing paid for its fights, all of them counted, won or lost.</summary>
        public static string AttritionTable(IEnumerable<TacticReport> reports)
        {
            var rows = reports.ToList();
            var width = Math.Max("Tactic".Length, rows.Count == 0 ? 0 : rows.Max(r => r.Tactic.Length));
            var lines = new List<string> { $"{"Tactic".PadRight(width)}  Win%   Body lost  Mind+Soul lost  Stamina spent  Dropped  Down at end  Dead" };
            lines.AddRange(rows.Select(r =>
                $"{r.Tactic.PadRight(width)}  {r.WinRate,5:P0}  {r.AverageCost(c => c.BodyLost),9:P0}  {r.AverageCost(c => c.MindSoulLost),14:P0}  {r.AverageCost(c => c.StaminaSpent),13:P0}  " +
                $"{r.AverageCost(c => c.Dropped),7:0.00}  {r.AverageCost(c => c.DownAtEnd),11:0.00}  {r.AverageCost(c => c.Dead),4:0.00}"));
            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>The reports side by side, best win rate first, as a plain-text table.</summary>
        public static string Table(IEnumerable<TacticReport> reports)
        {
            var rows = reports.OrderByDescending(r => r.ObjectiveRate ?? r.WinRate).ThenByDescending(r => r.WinRate).ThenBy(r => r.AverageRounds).ToList();
            var width = Math.Max("Tactic".Length, rows.Count == 0 ? 0 : rows.Max(r => r.Tactic.Length));

            var lines = new List<string>
            {
                $"{"Tactic".PadRight(width)}  Obj%  Win%   W/L/S/X       Rounds  Body left  Up   Foes  Fled  Attacks  Trips  Swaps  Extra  Braces  Dodges  Leavers  Heals  Helps  Casts  Intr  Shelt  Expl  Man",
            };

            lines.AddRange(rows.Select(r =>
                $"{r.Tactic.PadRight(width)}  {(r.ObjectiveRate is { } obj ? obj.ToString("P0") : "—"),4}  {r.WinRate,5:P0}  {$"{r.Wins}/{r.Losses}/{r.Stalemates}/{r.Withdrawals}",-12}  " +
                $"{r.AverageRounds,6:0.0}  {r.AverageBodyLeftOnWin,9:0.0}  {r.AverageStandingOnWin,3:0.0}  {r.AverageFoesDropped,4:0.0}  {r.AverageFled,4:0.0}  " +
                $"{r.Per(t => t.Attacks),7:0.0}  {r.Per(t => t.Manoeuvres),5:0.0}  {r.Per(t => t.Swaps),5:0.0}  {r.Per(t => t.ExtraActions),5:0.0}  " +
                $"{r.Per(t => t.StoodGround),6:0.0}  {r.Per(t => t.DodgesTaken),6:0.0}  {r.Per(t => t.LeaverReactions),7:0.0}  {r.Per(t => t.Tends),5:0.0}  {r.Per(t => t.Aids),5:0.0}  {r.Per(t => t.Casts),5:0.0}  {r.Per(t => t.Interrupts),4:0.0}  {r.Per(t => t.Shelters),5:0.0}  {r.Per(t => t.Exploits),4:0.0}  {r.Per(t => t.Manoeuvres),4:0.0}"));

            return string.Join(Environment.NewLine, lines);
        }
    }
}
