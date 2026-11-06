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
    public sealed record ThreatMeasure(int Games, int Rounds, double Weight = 0.7)
    {
        /// <summary>A dozen games a round, to the end of the next round, trusted seven parts in ten.</summary>
        public static ThreatMeasure Default { get; } = new(12, 1);

        /// <summary>Not measuring: the Assessor's reckoning alone.</summary>
        public static ThreatMeasure None { get; } = new(0, 0, 0);

        public bool Measures => Games >= 1 && Weight > 0;
    }

    public partial class UtilityGmBrain
    {
        /// <summary>How this GM measures the party's threat, round by round.</summary>
        public ThreatMeasure Measure { get; init; } = ThreatMeasure.None;

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
            var party = view.Combatants.Where(c => c.Side == Side.Party && !c.IsPhantom && !c.IsSteed).ToList();
            var harm = new double[seeds.Length, party.Count];
            var turns = new int[seeds.Length, party.Count];
            var played = new bool[seeds.Length];
            var knowledge = Knowledge;
            var peers = PeersThroughFog;

            Parallel.For(0, seeds.Length, g =>
            {
                var map = forkMap ??= new Dictionary<object, object?>(ReferenceEqualityComparer.Instance);
                try
                {
                    var gm = new UtilityGmBrain(BrainSkill.Best, new Random(seeds[g]), knowledge, peers: peers, foresight: Foresight.None);
                    var fork = encounter.Fork(new RandomDiceRoller(new Random(seeds[g])), gm, out var counterpart, map);
                    var who = party.Select((c, i) => ((Combatant)counterpart(c), i)).ToDictionary(x => x.Item1, x => x.i);
                    var tally = new HarmTally(fork, who);
                    var lastRound = fork.Round + Measure.Rounds;
                    fork.ResumeWith((GmStep)counterpart(first));
                    var driver = new BotDriver(fork, new UtilityPartyBrain(BrainSkill.Best, new Random(seeds[g])));
                    while (!fork.IsOver && fork.Round <= lastRound && driver.Step())
                    {
                    }

                    tally.Close();
                    for (var i = 0; i < party.Count; i++)
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
            var found = party.Select((c, i) => (c, Turns: games.Sum(g => turns[g, i]), Harm: games.Sum(g => harm[g, i])))
                .Where(x => x.Turns > 0)
                .ToDictionary(x => x.c, x => x.Harm / x.Turns);
            if (found.Count == 0)
            {
                // Every game was over before a character took a turn: last round's measure stands.
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
        /// Who did what to whom in one game: on a character's own turn, every wound the GM's side
        /// loses on any track and every wound their own side gets back is theirs — a blade, a spell,
        /// a Cleave, a heal alike; on anybody else's turn, the reaction blows they land.
        /// </summary>
        private sealed class HarmTally
        {
            private readonly Encounter fork;
            private readonly IReadOnlyDictionary<Combatant, int> party;
            private int? owner;
            private (int Foes, int Friends) atStart;

            public double[] Harm { get; }

            /// <summary>The turns each character took — an Interrupt among them.</summary>
            public int[] Turns { get; }

            public HarmTally(Encounter fork, IReadOnlyDictionary<Combatant, int> party)
            {
                this.fork = fork;
                this.party = party;
                Harm = new double[party.Count];
                Turns = new int[party.Count];
                atStart = Pools();
                fork.Happened += Saw;
            }

            private (int Foes, int Friends) Pools()
            {
                int foes = 0, friends = 0;
                foreach (var c in fork.Combatants)
                {
                    // A creature out of the fight has nothing left to take: the wounds it still had on
                    // any track go to whoever dropped it, a little for the drop itself.
                    var pool = c.IsOutOfFight || c.HasFled ? 0 : Math.Max(0, c.Body) + Math.Max(0, c.Mind) + Math.Max(0, c.Soul);
                    if (c.Side == Side.Adversaries && !c.IsPhantom)
                    {
                        foes += pool;
                    }
                    else if (c.Side == Side.Party && !c.IsPhantom)
                    {
                        friends += Math.Max(0, c.Body);
                    }
                }

                return (foes, friends);
            }

            private void Saw(Happening happening)
            {
                switch (happening)
                {
                    case TurnBegan began:
                        Settle();
                        owner = party.TryGetValue(began.Who, out var i) ? i : null;
                        if (owner is { } taking)
                        {
                            Turns[taking]++;
                        }

                        break;
                    case Struck { Outcome: var o } when owner is null && o.Target.Side == Side.Adversaries && party.TryGetValue(o.Attacker, out var striker):
                        Harm[striker] += Math.Max(0, o.Dealt);
                        break;
                }
            }

            /// <summary>What changed since the last turn began goes to whoever's turn it was, if it was a character's.</summary>
            private void Settle()
            {
                var now = Pools();
                if (owner is { } i)
                {
                    Harm[i] += Math.Max(0, atStart.Foes - now.Foes) + Math.Max(0, now.Friends - atStart.Friends);
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
