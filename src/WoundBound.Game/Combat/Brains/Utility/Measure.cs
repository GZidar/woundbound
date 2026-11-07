namespace WoundBound.Game.Combat.Brains.Utility
{
    /// <summary>
    /// How a GM measures the party's threat instead of assuming it (the user's direction,
    /// 2026-11-07): "run some scenarios, find which opponent ends up doing the most damage, and
    /// alter strategy to target them — keep reviewing each round based on actual state." At its
    /// first decision of each round it plays the fight forward <paramref name="Games"/> times to the
    /// end of the round <paramref name="Rounds"/> past this one, credits every character with the
    /// harm they actually did in those games, and weighs its targets by that (<see cref="Assessor.Measured"/>).
    /// </summary>
    /// <param name="Games">How many games are played each round.</param>
    /// <param name="Rounds">How many rounds past this one each game runs.</param>
    /// <param name="Weight">How much of a character's worth as a target the measurement sets, 0 to 1; the Assessor's own reckoning sets the rest.</param>
    /// <remarks>
    /// It measures the party only (the user's ruling, 2026-11-07): measuring its own creatures as well
    /// made it guard the ones doing well instead of spending them, and it got weaker (champion 52–64%).
    /// </remarks>
    public sealed record ThreatMeasure(int Games, int Rounds, double Weight = 0.7)
    {
        /// <summary>
        /// A dozen games a round, to the end of this one, trusted seven parts in ten. Benched
        /// (2026-11-07, 240 fights each, look-ahead off; party wins): champion 57.5% unmeasured, 50%
        /// measured to the end of this round or the next alike; camp unmoved (76–77%). More games,
        /// more rounds, or a weight from 0.5 to 1 did no better; this round only is the cheapest.
        /// </summary>
        public static ThreatMeasure Default { get; } = new(12, 0);

        /// <summary>Not measuring: the Assessor's reckoning alone.</summary>
        public static ThreatMeasure None { get; } = new(0, 0, 0);

        /// <summary>
        /// How a GM set to <paramref name="challenge"/> measures the party (the user's direction,
        /// 2026-11-07: measuring is the default): at Best, a dozen games a round; at Balanced, half
        /// that; at Poor, not at all — it goes on what the sheets and the board say.
        /// </summary>
        public static ThreatMeasure For(BrainSkill challenge) => challenge.Name switch
        {
            "Best" => Default,
            "Balanced" => Default with { Games = 6 },
            _ => None,
        };

        public bool Measures => Games >= 1 && Weight > 0;
    }

    public partial class UtilityGmBrain
    {
        /// <summary>How this GM measures the party's threat, round by round.</summary>
        public ThreatMeasure Measure { get; init; }

        /// <summary>
        /// What each character did per turn of their own in the games played at the start of the round this GM
        /// last measured: wounds dealt to the GM's creatures, on any track, and wounds healed on their own side.
        /// </summary>
        public IReadOnlyDictionary<Combatant, double> MeasuredHarm => measured;

        private Dictionary<Combatant, double> measured = new();
        private int measuredRound = -1;

        /// <summary>
        /// At the GM's first decision of a round, the step it would take as it stands; then the games
        /// played on from it, and the harm each character did there kept for the rest of the round.
        /// True where it measured, and the decision should be made again on what it found.
        /// </summary>
        private bool MeasureRound(BattleView view, GmTurn turn)
        {
            var encounter = view.Encounter;
            if (!Measure.Measures || encounter.Round == measuredRound || !encounter.CanForkAtStep)
            {
                return false;
            }

            measuredRound = encounter.Round;
            var first = NextStep(view, turn);
            var seeds = Enumerable.Range(0, Measure.Games).Select(_ => random.Next()).ToArray();
            var who = view.Combatants
                .Where(c => c.Side == Side.Party && !c.IsPhantom && !c.IsSteed)
                .ToList();
            var harm = new double[seeds.Length, who.Count];
            var turns = new int[seeds.Length, who.Count];
            var played = new bool[seeds.Length];
            var knowledge = Knowledge;
            var peers = PeersThroughFog;

            Parallel.For(0, seeds.Length, g =>
            {
                var map = forkMap ??= new Dictionary<object, object?>(ReferenceEqualityComparer.Instance);
                try
                {
                    var gm = new UtilityGmBrain(BrainSkill.Best, new Random(seeds[g]), knowledge, peers: peers, foresight: Foresight.None, measure: ThreatMeasure.None);
                    var fork = encounter.Fork(new RandomDiceRoller(new Random(seeds[g])), gm, out var counterpart, map);
                    var index = who.Select((c, i) => ((Combatant)counterpart(c), i)).ToDictionary(x => x.Item1, x => x.i);
                    var tally = new HarmTally(fork, index);
                    var lastRound = fork.Round + Measure.Rounds;
                    fork.ResumeWith((GmStep)counterpart(first));
                    var driver = new BotDriver(fork, new UtilityPartyBrain(BrainSkill.Best, new Random(seeds[g])));
                    while (!fork.IsOver && fork.Round <= lastRound && driver.Step())
                    {
                    }

                    tally.Close();
                    for (var i = 0; i < who.Count; i++)
                    {
                        harm[g, i] = tally.Harm[i];
                        turns[g, i] = tally.Turns[i];
                    }

                    played[g] = true;
                }
                catch (InvalidOperationException)
                {
                    // A game cut short by a refusal: left out of the measure.
                }
                finally
                {
                    map.Clear();
                }
            });

            var games = Enumerable.Range(0, seeds.Length).Where(g => played[g]).ToList();
            if (games.Count == 0)
            {
                return false;
            }

            // Per turn it took, not per round: a character the GM's creatures are already pressing —
            // dropped, or spending its turns getting up and healing — would otherwise look harmless,
            // and be let off just as it comes back. Its harm is what it does with a turn of its own;
            // one that took none in any game keeps the Assessor's reckoning alone.
            var found = who.Select((c, i) => (c, Turns: games.Sum(g => turns[g, i]), Harm: games.Sum(g => harm[g, i])))
                .Where(x => x.Turns > 0)
                .ToDictionary(x => x.c, x => x.Harm / x.Turns);
            if (found.Count == 0)
            {
                // Every game was over before anybody measured took a turn: last round's measure stands.
                return false;
            }

            measured = found;
            if (BrainTrace.Sink is { } sink)
            {
                sink($"    [the GM measures the party over {games.Count} games to round {encounter.Round + Measure.Rounds}: "
                     + string.Join(", ", measured.OrderByDescending(m => m.Value).Select(m => $"{m.Key.Name} {m.Value:0.0}")) + " a turn]");
            }

            return true;
        }

        /// <summary>
        /// Who did what to whom in one game: on a creature's own turn, every wound the other side
        /// loses on any track and every wound of Body its own side gets back is its — a blade, a
        /// spell, a Cleave, a heal alike; on anybody else's turn, the reaction blows it lands.
        /// </summary>
        private sealed class HarmTally
        {
            private readonly Encounter fork;
            private readonly IReadOnlyDictionary<Combatant, int> measured;
            private (int Index, Side Side)? owner;
            private Pools atStart;

            public double[] Harm { get; }

            /// <summary>The turns each creature measured took — an Interrupt among them.</summary>
            public int[] Turns { get; }

            public HarmTally(Encounter fork, IReadOnlyDictionary<Combatant, int> measured)
            {
                this.fork = fork;
                this.measured = measured;
                Harm = new double[measured.Count];
                Turns = new int[measured.Count];
                atStart = Count();
                fork.Happened += Saw;
            }

            /// <summary>Each side's wounds left on every track, and its Body alone.</summary>
            private readonly record struct Pools(int PartyAll, int PartyBody, int FoesAll, int FoesBody)
            {
                /// <summary>What a creature of <paramref name="side"/> did between these pools and <paramref name="now"/>.</summary>
                public double Harm(Side side, Pools now) => side == Side.Party
                    ? Math.Max(0, FoesAll - now.FoesAll) + Math.Max(0, now.PartyBody - PartyBody)
                    : Math.Max(0, PartyAll - now.PartyAll) + Math.Max(0, now.FoesBody - FoesBody);
            }

            private Pools Count()
            {
                int partyAll = 0, partyBody = 0, foesAll = 0, foesBody = 0;
                foreach (var c in fork.Combatants)
                {
                    if (c.IsPhantom)
                    {
                        continue;
                    }

                    // A creature out of the fight has nothing left to take: the wounds it still had on
                    // any track go to whoever dropped it, a little for the drop itself.
                    var all = c.IsOutOfFight || c.HasFled ? 0 : Math.Max(0, c.Body) + Math.Max(0, c.Mind) + Math.Max(0, c.Soul);
                    var body = Math.Max(0, c.Body);
                    if (c.Side == Side.Adversaries)
                    {
                        foesAll += all;
                        foesBody += body;
                    }
                    else if (c.Side == Side.Party)
                    {
                        partyAll += all;
                        partyBody += body;
                    }
                }

                return new(partyAll, partyBody, foesAll, foesBody);
            }

            private void Saw(Happening happening)
            {
                switch (happening)
                {
                    case TurnBegan began:
                        Settle();
                        owner = measured.TryGetValue(began.Who, out var i) ? (i, began.Who.Side) : null;
                        if (owner is { } taking)
                        {
                            Turns[taking.Index]++;
                        }

                        break;

                    // A reaction blow on the other side's turn: the pools credit that turn's owner with
                    // their own side's harm only, so the striker's is counted here.
                    case Struck { Outcome: var o } when o.Target.Side != o.Attacker.Side
                                                       && owner?.Side != o.Attacker.Side
                                                       && measured.TryGetValue(o.Attacker, out var striker):
                        Harm[striker] += Math.Max(0, o.Dealt);
                        break;
                }
            }

            /// <summary>What changed since the last turn began goes to whoever's turn it was, if they're measured.</summary>
            private void Settle()
            {
                var now = Count();
                if (owner is { } o)
                {
                    Harm[o.Index] += atStart.Harm(o.Side, now);
                }

                atStart = now;
            }

            public void Close()
            {
                Settle();
                fork.Happened -= Saw;
            }
        }
    }
}
