using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WoundBound.Game.Combat;
using WoundBound.Game.Combat.Brains.Utility;
using WoundBound.Game.Skills;
using WoundBound.Rules.Calculation;
using WoundBound.Rules.Models;

namespace WoundBoundGame
{
    /// <summary>A question the Skill Challenge puts to the players, with its answers.</summary>
    public sealed record SkillChoice(string Question, IReadOnlyList<(string Label, string? Hint)> Options);

    /// <summary>
    /// The party at a Skill Challenge, played by the people at the screen: every decision the rules
    /// give the players — Probe or not, who attempts and how, who Assists, Push, Steady, Interrupt —
    /// is put to them as a <see cref="SkillChoice"/>, and the challenge waits for the answer. The
    /// challenge plays on a worker thread; the screen reads <see cref="Pending"/> each frame.
    /// </summary>
    public sealed class HumanSkillBrain : SkillPartyBrain
    {
        private readonly object gate = new();
        private TaskCompletionSource<int>? answer;
        private readonly CancellationToken cancel;

        public HumanSkillBrain(CancellationToken cancel) => this.cancel = cancel;

        /// <summary>The question waiting for an answer, if any.</summary>
        public SkillChoice? Pending { get; private set; }

        /// <summary>Answers the waiting question with the option at <paramref name="index"/>.</summary>
        public void Answer(int index)
        {
            lock (gate)
            {
                Pending = null;
                answer?.TrySetResult(index);
            }
        }

        private int Ask(string question, IReadOnlyList<(string Label, string? Hint)> options)
        {
            TaskCompletionSource<int> waiting;
            lock (gate)
            {
                waiting = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                answer = waiting;
                Pending = new SkillChoice(question, options);
            }

            using var _ = cancel.Register(() => waiting.TrySetCanceled());
            return waiting.Task.GetAwaiter().GetResult();
        }

        private static string Percent(double p) => $"{p:P0}";

        public override Combatant? Prober(SkillChallenge challenge, AdversaryTemplate obstacle, SkillHurdle? hurdle, IReadOnlyList<SkillApproach> open, IReadOnlySet<Combatant> acted)
        {
            var (difficulty, skill) = obstacle.ProbeFor(hurdle);
            if (string.IsNullOrWhiteSpace(skill))
            {
                return null;
            }

            var target = DifficultyLadder.TargetNumber(difficulty);
            var free = Free(challenge, acted).ToList();
            var options = free.Select(c => ($"{c.Name} Probes — {skill} {Signed(c.SkillBonus(skill))} against {target}: {Percent(Odds.Of(c.SkillBonus(skill), target).Success)}",
                    (string?)("Study it instead of attempting: " + obstacle.ProbeTextFor(hurdle)))).ToList();
            options.Add(("No Probe — get on with it", null));
            var pick = Ask("Probe first? A Probe is a character's action for the round; it reveals an easier way, or nothing.", options);
            return pick < free.Count ? free[pick] : null;
        }

        public override SkillPlan? Plan(SkillChallenge challenge, IReadOnlyList<SkillApproach> approaches, int steps, IReadOnlySet<Combatant> acted)
        {
            var free = Free(challenge, acted).ToList();
            var tries = free
                .SelectMany(c => approaches.SelectMany(a => a.Skills.Select(s => (Who: c, Approach: a, Skill: s, Chance: Chance(challenge, c, a, s, steps)))))
                .OrderByDescending(x => x.Chance)
                .Take(10)
                .ToList();
            if (tries.Count == 0)
            {
                return null;
            }

            var options = tries.Select(t => ($"{t.Who.Name} — {t.Skill} {Signed(t.Who.SkillBonus(t.Skill))}: {Describe(t.Approach)} ({Target(t.Approach, steps)}) — {Percent(t.Chance)}",
                (string?)null)).ToList();
            options.Add(("Nobody attempts this round", null));
            var pick = Ask(steps > 0 ? $"Who attempts it? (Checks are {steps} step(s) harder just now.)" : "Who attempts it?", options);
            if (pick >= tries.Count)
            {
                return null;
            }

            var chosen = tries[pick];
            var helpers = free.Where(c => c != chosen.Who).ToList();
            var assist = helpers.Select(h => ($"{h.Name} Assists — Advantage: {Percent(Chance(challenge, chosen.Who, chosen.Approach, chosen.Skill, steps, advantage: true))}",
                (string?)"Gives up their own action this round.")).ToList();
            assist.Add(("No Assist", null));
            var help = helpers.Count == 0 ? helpers.Count : Ask($"{chosen.Who.Name} attempts it. Does anybody Assist?", assist);
            return new SkillPlan(chosen.Who, chosen.Approach, chosen.Skill, help < helpers.Count ? helpers[help] : null);
        }

        public override bool Pushes(SkillChallenge challenge, SkillPlan plan, AdversaryTemplate obstacle, int steps, int failures, int allowed)
        {
            if (!SkillChallenge.CanSpend(plan.Who))
            {
                return false;
            }

            var now = Chance(challenge, plan.Who, plan.Approach, plan.Skill, steps, plan.Assist is not null);
            var pushed = Chance(challenge, plan.Who, plan.Approach, plan.Skill, steps, plan.Assist is not null, bonus: 2);
            return Ask($"Push? {plan.Who.Name}: +2 for 1 Stamina ({plan.Who.Stamina} left) — {Percent(now)} becomes {Percent(pushed)}. A tactical spend: the GM banks an Opportunity.",
                [("Push", null), ("Don't push", null)]) == 0;
        }

        public override bool Steadies(SkillChallenge challenge, Combatant who, AdversaryTemplate obstacle, int band, int failures, int allowed)
        {
            if (!SkillChallenge.CanSpend(who))
            {
                return false;
            }

            var cost = obstacle.StakesKind switch
            {
                WoundBound.Rules.Enums.SkillStakesKind.Wound => $"{band} {obstacle.StakesWoundType} wound(s)",
                WoundBound.Rules.Enums.SkillStakesKind.Stamina => $"{band} Stamina",
                _ => SkillChallengeCalculator.DescribeStoryBand(band),
            };
            var last = failures + 1 >= allowed ? " — and it would be the last failure this obstacle allows" : string.Empty;
            return Ask($"{who.Name} failed: {cost}{last}. Steady it away for 1 Stamina ({who.Stamina} left)? The GM banks an Opportunity.",
                [("Steady", null), ("Take the failure", null)]) == 0;
        }

        public override Combatant? Interrupts(SkillChallenge challenge, SkillPlan planned, IReadOnlyList<Combatant> may)
        {
            var options = may.Select(c => ($"{c.Name} Interrupts — 1 Mind ({c.Mind} left), and acts now: a success puts the Opposition at Disadvantage", (string?)null)).ToList();
            options.Add(("Let it come", null));
            var pick = Ask($"{challenge.Current?.Name} declares its Opposition against {planned.Who.Name}. Interrupt?", options);
            return pick < may.Count ? may[pick] : null;
        }

        private static IEnumerable<Combatant> Free(SkillChallenge challenge, IReadOnlySet<Combatant> acted) =>
            challenge.Party.Where(c => !acted.Contains(c) && !c.IsOutOfFight && c.Body > 0 && c.Stamina > 0);

        private static string Describe(SkillApproach approach) =>
            string.IsNullOrWhiteSpace(approach.Description) ? "the way the Probe found" : approach.Description;

        private static int Target(SkillApproach approach, int steps) =>
            Math.Min(DifficultyLadder.TargetNumber(WoundBound.Rules.Enums.CheckDifficulty.NearImpossible), DifficultyLadder.TargetNumber(approach.Difficulty) + steps * DifficultyLadder.Step);

        private static string Signed(int n) => n >= 0 ? $"+{n}" : $"−{-n}";
    }

    /// <summary>The bot party, slowed to a pace a watcher can follow.</summary>
    public sealed class PacedSkillBrain(int pauseMs, CancellationToken cancel) : SkillPartyBrain
    {
        public override SkillPlan? Plan(SkillChallenge challenge, IReadOnlyList<SkillApproach> approaches, int steps, IReadOnlySet<Combatant> acted)
        {
            cancel.WaitHandle.WaitOne(pauseMs);
            cancel.ThrowIfCancellationRequested();
            return base.Plan(challenge, approaches, steps, acted);
        }
    }
}
