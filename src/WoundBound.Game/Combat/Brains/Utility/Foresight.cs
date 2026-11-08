using WoundBound.Rules.Enums;

namespace WoundBound.Game.Combat.Brains.Utility
{
    /// <summary>
    /// How far a GM looks ahead before it commits a creature to a plan (the user's direction,
    /// 2026-11-04: "run the scenario forward from each decision and pick the one that leads to the
    /// best outcome"). The fight is forked where it stands — never replayed — and each of the few
    /// best plans is played forward <see cref="Games"/> times on fresh dice, the party played by its
    /// best brain and the GM's other creatures by a quick one, to the end of the round and
    /// <see cref="Rounds"/> more; the plan whose games end best on average is taken.
    /// </summary>
    /// <param name="Candidates">How many of the best-scored plans are played forward.</param>
    /// <param name="Games">How many games each is played forward.</param>
    /// <param name="Rounds">How many rounds past this one each game runs.</param>
    /// <param name="OwnWorth">
    /// What the GM's own creatures are worth against the party's in a game's outcome — under 1, as
    /// in <see cref="UtilityGmBrain.GmCaution"/>: an adversary is there to challenge the party, and is
    /// worth keeping only for the harm it does later (the user's direction, 2026-11-05).
    /// </param>
    /// <param name="Others">
    /// Who plays the GM's creatures in those games once the plan weighed is made: a quick brain, or
    /// the GM's own at its best, without looking ahead itself — dearer, but a game played further
    /// ahead is only as good as the play in it.
    /// </param>
    /// <param name="Prescient">
    /// A cheat, for finding the ceiling of how well a GM could play (2026-11-07): where the fight's
    /// dice are a <see cref="StreamDiceRoller"/>, every game is played on a copy of them, so each plan
    /// is weighed on the rolls it will really get. Never for play.
    /// </param>
    public sealed record Foresight(int Candidates, int Games, int Rounds, double OwnWorth = UtilityGmBrain.GmCaution,
        GamesPlayedBy Others = GamesPlayedBy.Quick, bool Prescient = false)
    {
        /// <summary>Four plans, a dozen games each, to the end of the round.</summary>
        public static Foresight Default { get; } = new(4, 12, 0);

        /// <summary>Not looking ahead at all: the best-scored plan is taken as it stands.</summary>
        public static Foresight None { get; } = new(0, 0, 0);

        /// <summary>
        /// How far ahead a GM set to <paramref name="challenge"/> looks (the user's direction,
        /// 2026-11-05: the challenge dial is how far it looks, every brain at its best): at Best, four
        /// plans a dozen games each; at Balanced, two plans six games each; at Poor, not at all.
        /// </summary>
        public static Foresight For(BrainSkill challenge) => challenge.Name switch
        {
            "Best" => Default,
            "Balanced" => new(2, 6, 0),
            _ => None,
        };

        /// <summary>Whether a GM with this foresight looks ahead at all.</summary>
        public bool LooksAhead => Candidates >= 2 && Games >= 1;
    }

    /// <summary>Who plays the GM's side in the games a GM looks ahead on.</summary>
    public enum GamesPlayedBy
    {
        /// <summary>The simple GM: closes and strikes.</summary>
        Quick,

        /// <summary>The utility GM at Best, knowing what the GM looking ahead knows, not looking ahead itself.</summary>
        Best,
    }

    public partial class UtilityGmBrain
    {
        /// <summary>How far ahead this GM looks before committing a creature to a plan.</summary>
        public Foresight Foresight { get; }

        /// <summary>Decisions looked ahead on, how many came out other than the plain best score would have had them, and games lost to a refusal.</summary>
        public (int Weighed, int Changed, int Failed) ForesightTally => (weighed, changed, failed);

        private int weighed, changed, failed;

        /// <summary>
        /// The plan this GM's foresight picks for <paramref name="turn"/> from <paramref name="options"/>,
        /// or null where it doesn't look ahead: no foresight, a single kind of plan, or a moment a fork
        /// can't be taken at.
        /// </summary>
        private Option? Foresee(BattleView view, GmTurn turn, List<Option> options)
        {
            var sight = Foresight;
            // Its games are scored on who wins, so it looks ahead only when it plays for the kill:
            // a merciful GM's plans would be pulled back towards the ruthless one (2026-11-08).
            if (!sight.LooksAhead || options.Count < 2 || !view.Encounter.CanForkAtStep || Ruthlessness < 1)
            {
                return null;
            }

            // The best square for each different thing it could do: four squares to make the same
            // blow from are one plan, not four.
            var candidates = options.OrderByDescending(o => o.Score).GroupBy(PlanKey).Select(g => g.First())
                .Take(sight.Candidates).ToList();
            if (candidates.Count < 2)
            {
                return null;
            }

            var assess = Assess(view);
            var worth = view.Combatants.Where(c => !c.IsPhantom && !c.IsSteed).Select(c => (c, assess.Value(c))).ToList();
            var seeds = Enumerable.Range(0, sight.Games).Select(_ => random.Next()).ToArray();

            double?[,] outcomes;
            try
            {
                outcomes = PlayForward(view.Encounter, turn, candidates, seeds, worth, sight, Knowledge, PeersThroughFog);
            }
            catch (AggregateException)
            {
                // Something in this fight can't be copied: it is played on its plain scores.
                failed += candidates.Count * sight.Games;
                return null;
            }

            var results = candidates.Select((option, c) =>
            {
                var played = Enumerable.Range(0, seeds.Length).Select(g => outcomes[c, g]).OfType<double>().ToList();
                return (Option: option, Mean: played.Count == 0 ? double.MinValue : played.Average(), Played: played.Count);
            }).ToList();
            failed += results.Sum(r => seeds.Length - r.Played);
            if (results.All(r => r.Played == 0))
            {
                return null;
            }

            var best = results.Where(r => r.Played > 0).MaxBy(r => r.Mean);
            weighed++;
            changed += best.Option == candidates[0] ? 0 : 1;
            if (BrainTrace.Sink is { } sink)
            {
                sink($"    [{turn.Actor.Name} looks ahead: {sight.Games} games each, to round {view.Encounter.Round + sight.Rounds}]");
                foreach (var (option, mean, played) in results)
                {
                    sink($"    {(option == best.Option ? "⇒" : " ")} {mean,7:0.00} (score {option.Score:0.00}, {played} played)  {option.Kind}: {option.Why}");
                }
            }

            return best.Option;
        }

        private static object PlanKey(Option o) =>
            (o.Kind, o.Then, o.Target, o.With?.Name, o.Cast?.Spell.Name, o.Control, o.Action, o.Exploit, o.WithBonus);

        /// <summary>
        /// Plays each of <paramref name="candidates"/> forward once on each of <paramref name="seeds"/> —
        /// the same dice for every plan, so the plans are told apart by what they do, not by their
        /// luck — all side by side, and where each game left the GM: null for one a refusal cut short.
        /// </summary>
        private static double?[,] PlayForward(Encounter live, GmTurn turn, IReadOnlyList<Option> candidates, int[] seeds,
            IReadOnlyList<(Combatant Creature, double Worth)> worth, Foresight sight, Knowledge knowledge, bool peers)
        {
            var steps = candidates.Select(option => (First: Step(option, turn),
                Then: option.Kind == OptionKind.Move && option.Then is { } next ? FollowUp(option with { Kind = next, Square = null }, turn) : null)).ToList();
            var outcomes = new double?[candidates.Count, seeds.Length];
            Parallel.For(0, candidates.Count * seeds.Length, n =>
            {
                var (c, g) = (n / seeds.Length, n % seeds.Length);
                var map = forkMap ??= new Dictionary<object, object?>(ReferenceEqualityComparer.Instance);
                try
                {
                    var quick = new FollowThrough(sight.Others == GamesPlayedBy.Best
                        ? new UtilityGmBrain(BrainSkill.Best, new Random(seeds[g]), knowledge, peers: peers, foresight: Foresight.None, measure: ThreatMeasure.None)
                        : new SimpleGmBrain());
                    IDiceRoller dice = sight.Prescient && live.Dice is StreamDiceRoller stream ? stream.Copy() : new RandomDiceRoller(new Random(seeds[g]));
                    var fork = live.Fork(dice, quick, out var counterpart, map);
                    if (steps[c].Then is { } then)
                    {
                        quick.Plan((Combatant)counterpart(turn.Actor), (GmStep)counterpart(then));
                    }

                    var lastRound = fork.Round + sight.Rounds;
                    fork.ResumeWith((GmStep)counterpart(steps[c].First));
                    var driver = new BotDriver(fork, new UtilityPartyBrain(BrainSkill.Best, new Random(seeds[g])));
                    while (!fork.IsOver && fork.Round <= lastRound && driver.Step())
                    {
                    }

                    outcomes[c, g] = Standing(fork, worth, counterpart, sight.OwnWorth);
                }
                catch (InvalidOperationException)
                {
                    // A quick brain asked for something the rules refuse: the game is lost, not the plan.
                    outcomes[c, g] = null;
                }
                finally
                {
                    // Done with this game's copies: nothing of it is kept alive till the thread's next.
                    map.Clear();
                }
            });

            return outcomes;
        }

        /// <summary>
        /// The map each thread copies its games in, kept from one to the next: a fight's graph runs to
        /// thousands of objects, and a map grown afresh for every game was most of what the collector
        /// had to sweep from the large-object heap. A game never forks inside itself, so one a thread is enough.
        /// </summary>
        [ThreadStatic]
        private static Dictionary<object, object?>? forkMap;

        /// <summary>
        /// The step a move was planned for, where the option carries enough to name it — a move
        /// planned for a Harry carries the foe but not always the Harry; the quick GM finishes those.
        /// </summary>
        private static GmStep? FollowUp(Option then, GmTurn turn)
        {
            try
            {
                return Step(then, turn) is var step && step != GmStep.End ? step : null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        /// <summary>
        /// Where a game looked ahead on leaves the GM: each creature's worth to its side by how much
        /// of it is left standing — the GM's counted for, the party's against — and the fight's end
        /// worth half the whole board besides.
        /// </summary>
        private static double Standing(Encounter end, IReadOnlyList<(Combatant Creature, double Worth)> worth, Func<object, object> counterpart, double ownWorth)
        {
            var total = 0.0;
            foreach (var (creature, value) in worth)
            {
                var now = (Combatant)counterpart(creature);
                total += (creature.Side == Side.Adversaries ? ownWorth : -1) * value * Left(now);
            }

            var board = worth.Sum(w => w.Worth);
            return total + end.Phase switch
            {
                EncounterPhase.Defeat => 0.5 * board,
                EncounterPhase.Victory => -0.5 * board,
                _ => 0,
            };
        }

        /// <summary>The share of a creature's worth that is simply being up; the rest goes with its Body.</summary>
        private const double StandingShare = 0.5;

        /// <summary>How much of a creature is still in the fight: its Body left, nothing once it is down or gone, a little for one broken and running.</summary>
        private static double Left(Combatant c)
        {
            if (c.HasFled || c.HasSurrendered || c.IsOutOfFight)
            {
                return c.DiesLikeACharacter && c.BodyStatus == BodyStatus.Dead ? -0.5 : 0;
            }

            // Still standing is most of it: a character on 2 Body swings as hard as one on 8, so two
            // wounds that drop somebody are worth far more than two spread over two of them.
            var share = Math.Max(0, c.Body) / (double)Math.Max(1, c.BodyMax);
            var left = StandingShare + (1 - StandingShare) * share;
            return c.IsBroken ? 0.25 * left : left;
        }

        /// <summary>
        /// The GM of a game looked ahead on, which first sees the plan being weighed through — the blow,
        /// spell or hand a move was made for, once the move is made — and leaves every other choice to
        /// <paramref name="inner"/>.
        /// </summary>
        private sealed class FollowThrough(IGmBrain inner) : IGmBrain
        {
            private Combatant? actor;
            private GmStep? planned;

            public void Plan(Combatant who, GmStep step) => (actor, planned) = (who, step);

            public GmStep NextStep(BattleView view, GmTurn turn)
            {
                if (planned is { } step && turn.Actor == actor && turn.Actor.ActionsRemaining > 0)
                {
                    planned = null;
                    return step;
                }

                planned = null;
                return inner.NextStep(view, turn);
            }

            public bool PeersThroughFog => inner.PeersThroughFog;

            public Combatant ChooseOwedTurn(BattleView view) => inner.ChooseOwedTurn(view);

            public Combatant? ChooseExtraTurn(BattleView view) => inner.ChooseExtraTurn(view);

            public GmCloseIn? NextCloseIn(BattleView view, IReadOnlyList<Combatant> stillIdle) => inner.NextCloseIn(view, stillIdle);

            public bool StrikesAtLeaver(BattleView view, Combatant adversary, Combatant leaver) => inner.StrikesAtLeaver(view, adversary, leaver);

            public bool PanicDodges(BattleView view, PendingReaction reaction) => inner.PanicDodges(view, reaction);

            public bool StandsFirm(BattleView view, PendingReaction reaction) => inner.StandsFirm(view, reaction);

            public bool ArrestsShove(BattleView view, PendingReaction reaction) => inner.ArrestsShove(view, reaction);

            public bool Backlashes(BattleView view, PendingReaction reaction) => inner.Backlashes(view, reaction);

            public bool Sustains(BattleView view, PendingReaction reaction) => inner.Sustains(view, reaction);

            public int ShelterAmount(BattleView view, PendingReaction reaction) => inner.ShelterAmount(view, reaction);

            public bool Interrupts(BattleView view, PendingReaction reaction) => inner.Interrupts(view, reaction);

            public LeaverResponse RespondsToLeaver(BattleView view, Combatant adversary, Combatant leaver, IReadOnlyList<LeaverResponse> options) =>
                inner.RespondsToLeaver(view, adversary, leaver, options);
        }
    }
}
