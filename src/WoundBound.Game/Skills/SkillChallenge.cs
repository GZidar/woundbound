using WoundBound.Game.Combat;
using WoundBound.Game.Combat.Brains.Utility;
using WoundBound.Rules.Calculation;
using WoundBound.Rules.Enums;
using WoundBound.Rules.Models;

namespace WoundBound.Game.Skills
{
    /// <summary>
    /// One node of a Sequence (Core Rules §8, "Sequences"): a Skill Adversary, and where the party
    /// goes when it is beaten cleanly, beaten roughly, or lost. A destination is another node's
    /// name, or the end of the Challenge: <see cref="Cleared"/> or <see cref="Lost"/> with a label.
    /// </summary>
    public sealed record SkillNode(string Name, AdversaryTemplate Obstacle, string OnSuccess, string OnFailure, string? OnRough = null)
    {
        public const string Cleared = "cleared";
        public const string Lost = "lost";
    }

    /// <summary>How a Skill Challenge came out, and what it cost.</summary>
    public sealed record SkillChallengeResult(
        bool Cleared, string Ending, IReadOnlyList<(string Node, SkillThresholdOutcome Outcome)> Visited, int Rounds,
        IReadOnlyList<int> StoryBands, IReadOnlyList<string> Log);

    /// <summary>When an Active Skill Adversary's Opposition rolls (Core Rules §8, "Interrupting a Skill Adversary").</summary>
    public enum OppositionTiming
    {
        /// <summary>Declared each round, against whoever it bears on — and so open to an Interrupt.</summary>
        EachRound,

        /// <summary>Fired by a failed check — a reaction, which no Interrupt answers.</summary>
        OnFailure,
    }

    /// <summary>
    /// A Skill Challenge played out (Core Rules §8): the party works each Skill Adversary of a
    /// Sequence round by round — every character acting once a round, by Attempt, Probe or Assist,
    /// with Push and Steady for Stamina — until it is beaten or lost, then follows the node's arrow.
    ///
    /// <para>What it does, by the book: an Attempt rolls 2d12 + the skill against the approach's
    /// Difficulty, a Push adding +2; a Probe rolls its Focus skill against the Probe Threshold and,
    /// landing, unlocks the Reveal's approaches; an Assist gives the round's Attempt Advantage; a
    /// Steady negates a failure, and the round goes on. A failure costs its Stakes by how far it
    /// missed (Failing Badly). An Active adversary's Opposition rolls against a Threshold — declared
    /// each round and open to an Interrupt, or fired by a failure. Push and Steady are tactical
    /// spends within the per-round limit, each banking the GM an Opportunity; the GM spends them as
    /// an extra Opposition against an Active adversary, or against a Passive one as a step of
    /// Difficulty on the next check or a failure logged outright. Ramp raises the Difficulty a step
    /// per failure.</para>
    ///
    /// <para>Not modelled: Experience bonuses (a player's argument), Reveals that change something
    /// other than an approach's Difficulty, and Story consequences beyond counting their bands.</para>
    /// </summary>
    public sealed class SkillChallenge
    {
        private readonly IReadOnlyList<Combatant> party;
        private readonly Dictionary<string, SkillNode> nodes;
        private readonly string start;
        private readonly IDiceRoller dice;
        private readonly SkillPartyBrain brain;
        private readonly List<string> log = new();
        private readonly List<int> storyBands = new();

        public SkillChallenge(IReadOnlyList<Combatant> party, IReadOnlyList<SkillNode> sequence, IDiceRoller dice, SkillPartyBrain? brain = null)
        {
            this.party = party;
            nodes = sequence.ToDictionary(n => n.Name);
            start = sequence[0].Name;
            this.dice = dice;
            this.brain = brain ?? new SkillPartyBrain();
        }

        public IReadOnlyList<Combatant> Party => party;

        /// <summary>The GM's Skill Challenge Opportunities, capped at 3 and emptied when the scene ends.</summary>
        public int Opportunities { get; private set; }

        public IReadOnlyList<string> Log => log;

        /// <summary>Who can still act: on their feet, awake, and with Stamina to start a round on.</summary>
        private IEnumerable<Combatant> Able => party.Where(c => !c.IsOutOfFight && c.Body > 0 && c.Stamina > 0);

        public SkillChallengeResult Run()
        {
            var visited = new List<(string, SkillThresholdOutcome)>();
            var at = start;
            var rounds = 0;
            for (var hops = 0; hops < 12 && nodes.ContainsKey(at); hops++)
            {
                var node = nodes[at];
                var (outcome, took) = Work(node.Obstacle);
                rounds += took;
                visited.Add((node.Name, outcome));
                at = outcome switch
                {
                    SkillThresholdOutcome.ClearedClean => node.OnSuccess,
                    SkillThresholdOutcome.ClearedRough => node.OnRough ?? node.OnSuccess,
                    _ => node.OnFailure,
                };
                Write($"{node.Name}: {Describe(outcome)} — on to {at}.");
            }

            // The scene ends: its Opportunities go with it (Core Rules §8).
            Opportunities = 0;
            var cleared = !nodes.ContainsKey(at) ? !at.StartsWith(SkillNode.Lost, StringComparison.OrdinalIgnoreCase) && visited.Count > 0 && visited[^1].Item2 != SkillThresholdOutcome.Failed : false;
            return new SkillChallengeResult(cleared, at, visited, rounds, storyBands, log);
        }

        private static string Describe(SkillThresholdOutcome outcome) => outcome switch
        {
            SkillThresholdOutcome.ClearedClean => "beaten cleanly",
            SkillThresholdOutcome.ClearedRough => "beaten, roughly",
            SkillThresholdOutcome.Failed => "lost",
            _ => "unfinished",
        };

        /// <summary>One Skill Adversary, round by round, to its outcome.</summary>
        private (SkillThresholdOutcome Outcome, int Rounds) Work(AdversaryTemplate obstacle)
        {
            var moment = obstacle.Shape == SkillAdversaryShape.Moment;
            var (required, allowed) = moment ? (1, 1) : (Math.Max(1, obstacle.SuccessesRequired), Math.Max(1, obstacle.FailuresAllowed));
            var hurdles = obstacle.Hurdles;
            var active = obstacle.Behavior == SkillAdversaryBehavior.Active;
            var timing = TimingOf(obstacle);
            var revealed = new HashSet<int>();
            int successes = 0, failures = 0, raised = 0, rounds = 0;
            Write($"— {obstacle.Name}: {obstacle.Type}, {obstacle.Behavior}, " + (moment ? "a Moment" : $"a Track of {required} before {allowed} failures") + " —");

            SkillThresholdOutcome Now() => SkillChallengeCalculator.Outcome(successes, failures, required, allowed);

            while (Now() == SkillThresholdOutcome.InProgress && Able.Any() && rounds < 30)
            {
                rounds++;
                foreach (var c in party)
                {
                    c.TacticalSpendsThisRound = 0;
                }

                var acted = new HashSet<Combatant>();
                // A hurdle of its own where the statblock gives each one; otherwise the obstacle's
                // shared approaches serve every hurdle, which go by number.
                var named = hurdles.Count >= required;
                var hurdleIndex = successes;
                var hurdle = named ? hurdles[Math.Min(hurdleIndex, hurdles.Count - 1)] : null;
                if (!moment)
                {
                    Write($"Hurdle {hurdleIndex + 1} of {required}" + (hurdle is not null ? $": {hurdle.Description}." : "."));
                }

                // Probe first: study the hurdle, where what it would uncover is worth a character's action.
                var revealKey = hurdle is null || !obstacle.HasSharedProbe ? hurdleIndex : -1;
                if (!revealed.Contains(revealKey) && brain.Prober(this, obstacle, hurdle, Approaches(obstacle, hurdle, revealed.Contains(revealKey)), acted) is { } prober)
                {
                    acted.Add(prober);
                    var (probeDifficulty, probeSkill) = obstacle.ProbeFor(hurdle);
                    var roll = Roll(prober, probeSkill, DifficultyLadder.TargetNumber(probeDifficulty), advantage: false);
                    Write($"{prober.Name} Probes with {probeSkill}: {roll.Total} against {roll.Threshold} — " + (roll.Success == true ? "it gives something away." : "nothing to be found."));
                    if (roll.Success == true)
                    {
                        revealed.Add(revealKey);
                        var text = obstacle.RevealTextFor(hurdle);
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            Write($"Reveals: {text}");
                        }
                    }
                }

                var approaches = Approaches(obstacle, hurdle, revealed.Contains(revealKey));
                var ramp = SkillChallengeCalculator.RampSteps(obstacle.RampsOnFailure, obstacle.Shape, failures);

                // An Active adversary's Opposition, declared against whoever it bears on this round.
                var interruptedWell = false;
                Combatant? attempter = null;
                if (active && timing == OppositionTiming.EachRound && brain.Plan(this, approaches, ramp + raised, acted) is { } planned)
                {
                    var target = planned.Who;
                    Write($"{obstacle.Name} declares its Opposition against {target.Name}.");
                    if (Interrupter(planned, acted) is { } cutter)
                    {
                        // Their Skill Action now, before the Opposition rolls (Core Rules §8).
                        cutter.Mind -= 1;
                        Write($"{cutter.Name} Interrupts — 1 Mind — and acts first.");
                        var result = Attempt(planned with { Who = cutter }, obstacle, ramp + raised, ref failures, acted, allowed);
                        raised = 0;
                        attempter = cutter;
                        if (result == true)
                        {
                            successes++;
                            interruptedWell = true;
                        }
                    }

                    Oppose(obstacle, target, disadvantage: interruptedWell);
                }

                // The round's Attempt — and another, if a Steady wiped the first away.
                while (attempter is null && Now() == SkillThresholdOutcome.InProgress
                       && brain.Plan(this, approaches, ramp + raised, acted) is { } plan)
                {
                    var result = Attempt(plan, obstacle, ramp + raised, ref failures, acted, allowed);
                    raised = 0;
                    if (result == true)
                    {
                        successes++;
                        break;
                    }

                    if (result == false)
                    {
                        if (active && timing == OppositionTiming.OnFailure && plan.Who.Body > 0 && Triggers(obstacle, plan.Skill))
                        {
                            Oppose(obstacle, plan.Who, disadvantage: false);
                        }

                        break;
                    }

                    // Steadied: nothing logged, and somebody else may try.
                }

                // The GM spends what the party's Stamina handed it.
                while (Opportunities > 0 && Now() == SkillThresholdOutcome.InProgress)
                {
                    Opportunities--;
                    if (active)
                    {
                        Write("The GM spends an Opportunity: another Opposition roll.");
                        if (Able.OrderBy(c => ThresholdOf(c, obstacle.OppositionVsThreshold)).FirstOrDefault() is { } victim)
                        {
                            Oppose(obstacle, victim, disadvantage: false);
                        }
                    }
                    else if (failures + 1 >= allowed)
                    {
                        failures++;
                        Write("The GM spends an Opportunity: a failure logged outright.");
                    }
                    else
                    {
                        raised++;
                        Write("The GM spends an Opportunity: the next check is a step harder.");
                    }
                }
            }

            return (Now() == SkillThresholdOutcome.InProgress ? SkillThresholdOutcome.Failed : Now(), rounds);
        }

        /// <summary>The approaches open on <paramref name="hurdle"/>, the Reveal's among them once it is found.</summary>
        private static IReadOnlyList<SkillApproach> Approaches(AdversaryTemplate obstacle, SkillHurdle? hurdle, bool revealed)
        {
            var all = obstacle.ApproachesFor(hurdle).ToList();
            if (hurdle?.Reveal is { } reveal)
            {
                all.Add(reveal);
            }

            return all.Where(a => revealed || !a.UnlockedByReveal).ToList();
        }

        /// <summary>A Mind-secondary character yet to act, with the Mind to pay, worth putting in first.</summary>
        private Combatant? Interrupter(SkillPlan planned, HashSet<Combatant> acted) =>
            party.Where(c => c.HasInterrupt && !acted.Contains(c) && Able.Contains(c) && c.Mind >= 2)
                .OrderByDescending(c => c == planned.Who ? 1 : 0)
                .FirstOrDefault(c => c == planned.Who || brain.Chance(this, c, planned.Approach, planned.Skill, 0) >= 0.4);

        /// <summary>One Attempt: rolled, Pushed, Assisted, Steadied — true for a success, false for a logged failure, null for one Steadied away.</summary>
        private bool? Attempt(SkillPlan plan, AdversaryTemplate obstacle, int steps, ref int failures, HashSet<Combatant> acted, int allowed)
        {
            var who = plan.Who;
            acted.Add(who);
            if (plan.Assist is { } helper && !acted.Contains(helper))
            {
                acted.Add(helper);
                Write($"{helper.Name} Assists.");
            }

            var push = brain.Pushes(this, plan, obstacle, steps, failures, allowed);
            if (push)
            {
                Spend(who, "Pushes");
            }

            var target = Math.Min(DifficultyLadder.TargetNumber(CheckDifficulty.NearImpossible),
                DifficultyLadder.TargetNumber(plan.Approach.Difficulty) + steps * DifficultyLadder.Step);
            var roll = Roll(who, plan.Skill, target, advantage: plan.Assist is not null, bonus: push ? SkillChallengeCalculator.RollBonus(SkillActionKind.Push) : 0);
            var how = string.IsNullOrWhiteSpace(plan.Approach.Description) ? "the way the Probe found" : plan.Approach.Description;
            Write($"{who.Name} attempts {how} with {plan.Skill}: {roll.Total} against {target} — " + (roll.Success == true ? "it's done." : "it fails."));
            if (roll.Success == true)
            {
                return true;
            }

            var wounds = SkillChallengeCalculator.WoundsForFailedCheck(roll.Total, target, roll.IsCritical);
            if (brain.Steadies(this, who, obstacle, wounds, failures, allowed))
            {
                Spend(who, "Steadies");
                Write("The failure is wiped away — nothing logged, nothing paid.");
                return null;
            }

            failures++;
            PayStakes(who, obstacle, wounds);
            return false;
        }

        /// <summary>A tactical spend: 1 Stamina, within the per-round limit, and an Opportunity banked for the GM.</summary>
        private void Spend(Combatant who, string what)
        {
            who.Stamina -= 1;
            who.TacticalSpendsThisRound++;
            Opportunities = Math.Min(3, Opportunities + 1);
            Write($"{who.Name} {what} (1 Stamina, {who.Stamina} left) — the GM banks an Opportunity.");
        }

        /// <summary>Whether <paramref name="who"/> could make a tactical spend now.</summary>
        internal static bool CanSpend(Combatant who) =>
            who.Stamina >= 2 && !TacticalSpendCalculator.IsAtLimit(who.TacticalSpendsThisRound, who.TacticalSpendsPerRound);

        /// <summary>What a failed check costs (Core Rules §8, "Failing Badly"): wounds, Stamina or a Story band, by how far it missed.</summary>
        private void PayStakes(Combatant who, AdversaryTemplate obstacle, int band)
        {
            switch (obstacle.StakesKind)
            {
                case SkillStakesKind.Wound:
                    Wound(who, obstacle.StakesWoundType, band, obstacle.StakesIsDirect, "the failure");
                    break;
                case SkillStakesKind.Stamina:
                    var lost = Math.Min(who.Stamina, band);
                    who.Stamina -= lost;
                    Write($"{who.Name} is worn down: {lost} Stamina ({who.Stamina} left).");
                    break;
                default:
                    storyBands.Add(band);
                    Write($"A Story consequence: {SkillChallengeCalculator.DescribeStoryBand(band)}.");
                    break;
            }
        }

        /// <summary>The obstacle strikes: its Opposition roll against a Threshold, dealing its Stakes on a hit.</summary>
        private void Oppose(AdversaryTemplate obstacle, Combatant target, bool disadvantage)
        {
            if (target.Body <= 0 || target.IsOutOfFight)
            {
                return;
            }

            var threshold = ThresholdOf(target, obstacle.OppositionVsThreshold);
            var rolled = Enumerable.Range(0, RollCalculator.DiceNeeded(false, disadvantage)).Select(_ => dice.Roll(RollCalculator.HighestFace)).ToList();
            var roll = RollCalculator.Resolve(rolled, obstacle.OppositionModifier, threshold, false, disadvantage);
            Write($"{obstacle.Name}'s Opposition against {target.Name}: {roll.Total} against {obstacle.OppositionVsThreshold} {threshold}"
                  + (disadvantage ? " (Disadvantage)" : string.Empty) + (roll.Success == true ? " — it lands." : " — it misses."));
            if (roll.Success != true)
            {
                return;
            }

            if (obstacle.StakesKind == SkillStakesKind.Stamina)
            {
                var lost = Math.Min(target.Stamina, obstacle.StakesStaminaLoss);
                target.Stamina -= lost;
                Write($"{target.Name} loses {lost} Stamina ({target.Stamina} left).");
                return;
            }

            var sides = DiceNotation.ParseSides(obstacle.StakesDie);
            var damage = Enumerable.Range(0, Math.Max(1, obstacle.StakesDiceCount)).Sum(_ => dice.Roll(sides)) + obstacle.StakesModifier;
            if (obstacle.StakesKind == SkillStakesKind.Wound)
            {
                Wound(target, obstacle.StakesWoundType, damage, obstacle.StakesIsDirect, "the Opposition");
            }
            else
            {
                storyBands.Add(Math.Clamp(damage / 2, 1, 3));
            }
        }

        private void Wound(Combatant who, WoundType track, int wounds, bool direct, string from)
        {
            var absorbed = track == WoundType.Body ? InventoryCalculator.ArmourAbsorbedAgainst(who.Armour, WoundType.Body, direct) : 0;
            var dealt = Math.Max(0, wounds - absorbed);
            switch (track)
            {
                case WoundType.Body:
                    who.Body -= dealt;
                    if (who.Body <= 0)
                    {
                        who.HasBeenDropped = true;
                        who.HasCastSinceDropped = false;
                        who.IsStable = true;
                    }

                    break;
                case WoundType.Mind:
                    who.Mind = Math.Max(0, who.Mind - dealt);
                    break;
                default:
                    who.Soul = Math.Max(0, who.Soul - dealt);
                    break;
            }

            if (dealt > 0)
            {
                who.TendEligible[track] = true;
            }

            Write($"{who.Name} takes {dealt} {track} from {from}" + (absorbed > 0 && wounds > 0 ? $" (armour takes {Math.Min(absorbed, wounds)})" : string.Empty)
                  + (who.Body <= 0 ? " — and goes down." : "."));
        }

        internal static int ThresholdOf(Combatant c, ThresholdType threshold) => threshold switch
        {
            ThresholdType.Dodge => c.DodgeThreshold,
            ThresholdType.Stability => c.StabilityThreshold,
            ThresholdType.Resolve => c.ResolveThreshold ?? 99,
            _ => c.FortitudeThreshold ?? 99,
        };

        private RollResult Roll(Combatant who, string skill, int target, bool advantage, int bonus = 0)
        {
            var rolled = Enumerable.Range(0, RollCalculator.DiceNeeded(advantage, false)).Select(_ => dice.Roll(RollCalculator.HighestFace)).ToList();
            return RollCalculator.Resolve(rolled, who.SkillBonus(skill) + bonus, target, advantage, false);
        }

        /// <summary>
        /// Whether a failed check with <paramref name="skill"/> sets off a triggered Opposition: any
        /// failure, unless the Opposition line names the skills whose failure it answers — the Piston
        /// Snare's "a failed Tamper attempt".
        /// </summary>
        private static bool Triggers(AdversaryTemplate obstacle, string skill)
        {
            var named = Rules.Catalog.SkillCatalog.AllSkills.Select(k => k.Name)
                .Where(n => obstacle.OppositionEffect.Contains(n, StringComparison.OrdinalIgnoreCase)).ToList();
            return named.Count == 0 || named.Contains(skill, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Whether an Active obstacle's Opposition is declared each round, or fired by a failed check.</summary>
        public static OppositionTiming TimingOf(AdversaryTemplate obstacle) =>
            obstacle.OppositionEffect.Contains("trigger", StringComparison.OrdinalIgnoreCase)
            || obstacle.OppositionEffect.Contains("failed", StringComparison.OrdinalIgnoreCase)
            || obstacle.OppositionEffect.Contains("misjudge", StringComparison.OrdinalIgnoreCase)
                ? OppositionTiming.OnFailure
                : OppositionTiming.EachRound;

        private void Write(string line) => log.Add(line);
    }

    /// <summary>Who attempts a check, how, and with whose help.</summary>
    public sealed record SkillPlan(Combatant Who, SkillApproach Approach, string Skill, Combatant? Assist);

    /// <summary>
    /// How the party plays a Skill Challenge: the best-placed character attempts, another Assists
    /// if anyone is free, a Focus character Probes first where the Reveal is worth having, Push when
    /// a failure would be costly, Steady when one would end the obstacle or hurt.
    /// </summary>
    public class SkillPartyBrain
    {
        /// <summary>The chance <paramref name="who"/> makes <paramref name="approach"/> with <paramref name="skill"/>, <paramref name="steps"/> rungs harder.</summary>
        public double Chance(SkillChallenge challenge, Combatant who, SkillApproach approach, string skill, int steps, bool advantage = false, int bonus = 0) =>
            Odds.Of(who.SkillBonus(skill) + bonus,
                Math.Min(DifficultyLadder.TargetNumber(CheckDifficulty.NearImpossible), DifficultyLadder.TargetNumber(approach.Difficulty) + steps * DifficultyLadder.Step),
                advantage).Success;

        /// <summary>The round's Attempt: whoever is likeliest to make it, with an Assist where anyone is free.</summary>
        public virtual SkillPlan? Plan(SkillChallenge challenge, IReadOnlyList<SkillApproach> approaches, int steps, IReadOnlySet<Combatant> acted)
        {
            var free = Free(challenge, acted).ToList();
            var best = free
                .SelectMany(c => approaches.SelectMany(a => a.Skills.Select(s => (Who: c, Approach: a, Skill: s))))
                .OrderByDescending(x => Chance(challenge, x.Who, x.Approach, x.Skill, steps))
                .Select(x => ((Combatant, SkillApproach, string)?)(x.Who, x.Approach, x.Skill))
                .FirstOrDefault();
            if (best is not { } pick)
            {
                return null;
            }

            var helper = free.Where(c => c != pick.Item1).OrderBy(c => c.Stamina).FirstOrDefault();
            return new SkillPlan(pick.Item1, pick.Item2, pick.Item3, helper);
        }

        /// <summary>A Probe, where the Reveal would make the job easier and somebody other than the best attempter can roll for it.</summary>
        public virtual Combatant? Prober(SkillChallenge challenge, AdversaryTemplate obstacle, SkillHurdle? hurdle, IReadOnlyList<SkillApproach> open, IReadOnlySet<Combatant> acted)
        {
            var (difficulty, skill) = obstacle.ProbeFor(hurdle);
            if (string.IsNullOrWhiteSpace(skill))
            {
                return null;
            }

            var unlocked = obstacle.ApproachesFor(hurdle).Concat(hurdle?.Reveal is { } r ? [r] : []).Where(a => a.UnlockedByReveal).ToList();
            var free = Free(challenge, acted).ToList();
            if (unlocked.Count == 0 || free.Count < 2)
            {
                return null;
            }

            var bestNow = free.SelectMany(c => open.SelectMany(a => a.Skills.Select(s => Chance(challenge, c, a, s, 0)))).DefaultIfEmpty(0).Max();
            var bestAfter = free.SelectMany(c => unlocked.SelectMany(a => a.Skills.Select(s => Chance(challenge, c, a, s, 0)))).DefaultIfEmpty(0).Max();
            if (bestAfter <= bestNow + 0.1)
            {
                return null;
            }

            var target = DifficultyLadder.TargetNumber(difficulty);
            var prober = free.MaxBy(c => Odds.Of(c.SkillBonus(skill), target).Success)!;
            return Odds.Of(prober.SkillBonus(skill), target).Success >= 0.3 ? prober : null;
        }

        /// <summary>Push when a failure here would be dear: the last failure allowed, or wounds at stake.</summary>
        public virtual bool Pushes(SkillChallenge challenge, SkillPlan plan, AdversaryTemplate obstacle, int steps, int failures, int allowed) =>
            SkillChallenge.CanSpend(plan.Who)
            && (failures + 1 >= allowed || obstacle.StakesKind == SkillStakesKind.Wound)
            && Chance(challenge, plan.Who, plan.Approach, plan.Skill, steps, plan.Assist is not null) < 0.85;

        /// <summary>Steady a failure that would end the obstacle, or cost two wounds or more.</summary>
        public virtual bool Steadies(SkillChallenge challenge, Combatant who, AdversaryTemplate obstacle, int band, int failures, int allowed) =>
            SkillChallenge.CanSpend(who)
            && (failures + 1 >= allowed || (obstacle.StakesKind == SkillStakesKind.Wound && band >= 2));

        private static IEnumerable<Combatant> Free(SkillChallenge challenge, IReadOnlySet<Combatant> acted) =>
            challenge.Party.Where(c => !acted.Contains(c) && !c.IsOutOfFight && c.Body > 0 && c.Stamina > 0);
    }
}
