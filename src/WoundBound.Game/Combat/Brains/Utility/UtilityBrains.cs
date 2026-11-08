using WoundBound.Rules.Enums;

namespace WoundBound.Game.Combat.Brains.Utility
{
    /// <summary>
    /// The GM played to win: before every decision it runs through everything its creatures could
    /// do and takes the option that does the most for its side (<see cref="TurnPlanner"/>), as well
    /// as its <see cref="BrainSkill"/> lets it. The default goal is to kill the party; a creature
    /// that has broken runs, as the simple GM's do.
    /// </summary>
    /// <summary>
    /// For tuning: where a brain's choices are written out — every option it weighed, best first,
    /// and which it took. Null, as in play, writes nothing.
    /// </summary>
    public static class BrainTrace
    {
        public static Action<string>? Sink { get; set; }

        /// <summary>How many of the best options each decision shows.</summary>
        public static int Shown { get; set; } = 6;

        internal static void Decision(Combatant actor, IReadOnlyList<Option> options, Option chosen)
        {
            if (Sink is not { } sink)
            {
                return;
            }

            sink($"    [{actor.Name} weighs {options.Count} options]");
            foreach (var option in options.OrderByDescending(o => o.Score).Take(Shown))
            {
                sink($"    {(option == chosen ? "→" : " ")} {option.Score,6:0.00}  {option.Kind}: {option.Why}");
            }

            if (!options.OrderByDescending(o => o.Score).Take(Shown).Contains(chosen))
            {
                sink($"    → {chosen.Score,6:0.00}  {chosen.Kind}: {chosen.Why}");
            }
        }
    }

    public partial class UtilityGmBrain : SimpleGmBrain, IGmBrain
    {
        private readonly Random random;

        public UtilityGmBrain(BrainSkill? skill = null, Random? random = null, Knowledge? knowledge = null, double caution = GmCaution, bool? peers = null,
            Foresight? foresight = null, ThreatMeasure? measure = null, GmIntent? intent = null)
        {
            Skill = skill ?? BrainSkill.Best;
            chosenIntent = intent;
            Intent = intent ?? GmIntent.Ruthless;
            Ruthlessness = Intent.Ruthlessness;
            Foresight = foresight ?? Foresight.For(Skill);
            Measure = measure ?? ThreatMeasure.For(Skill);
            planning = Plays with { Care = Plays.Care * caution };
            this.random = random ?? new Random(1);
            Knowledge = knowledge ?? KnowledgeFor(Skill);
            PeersThroughFog = peers ?? PeersFor(Skill);
        }

        /// <summary>
        /// Whether it knows where every character stands, out of its creatures' sight or not — a GM
        /// at the table sees the whole map. Below that, it plays them on what they've seen: the
        /// characters in sight, and where the rest were last seen.
        /// </summary>
        public bool PeersThroughFog { get; }

        /// <summary>Whether a GM of <paramref name="skill"/> peers through the fog when nobody says: at Best, as it reads the sheets.</summary>
        public static bool PeersFor(BrainSkill skill) => skill.Name == BrainSkill.Best.Name;

        /// <summary>
        /// The challenge it is set to. A GM's creatures are always played as well as it knows how —
        /// a weaker GM that blunders on purpose only makes a duller fight (the user's direction,
        /// 2026-11-05) — so the challenge sets how far ahead it looks (<see cref="Foresight.For"/>),
        /// and, unless told otherwise, what it knows of the party and whether it peers through the fog.
        /// </summary>
        public BrainSkill Skill { get; }

        /// <summary>How it plays each creature, whatever the challenge: at its best.</summary>
        private static BrainSkill Plays => BrainSkill.Best;

        /// <summary>
        /// How much of its skill's Care the GM spends on its own creatures. A player guards a
        /// character they have carried for a campaign; a GM's bandit is there to make the fight, and
        /// one that hangs back to keep its own skin whole loses it — sims (2026-11-03) put the GM's
        /// best around four-tenths of the party's caution. What it does mind is priced in the harm the
        /// creature would still do (<see cref="Assessor.Value"/>): it heals, dodges or raises a shield
        /// to stay in the fight and hurt the party later, and spends itself where that hurts them more
        /// (the user's direction, 2026-11-05: adversaries are there to take the party down, not to live).
        /// </summary>
        public const double GmCaution = 0.4;

        /// <summary>The skill it plans with: the same eye for a choice, its own creatures held less dear.</summary>
        private readonly BrainSkill planning;

        /// <summary>What it knows of the party: the sheets at Best, what it has seen below that.</summary>
        public Knowledge Knowledge { get; }

        /// <summary>The knowledge that goes with a skill when none is named: the challenge scales both together.</summary>
        public static Knowledge KnowledgeFor(BrainSkill skill) => skill.Name == BrainSkill.Best.Name ? Knowledge.Sheets : Knowledge.Seen;

        /// <summary>
        /// How it sets its ruthlessness: fixed, or steered round by round towards a challenge. Where
        /// nobody chose, a fight with a brief steers towards the challenge its stakes suggest
        /// (<see cref="SceneBrief.DefaultChallenge"/>), and one without plays ruthless.
        /// </summary>
        public GmIntent Intent { get; private set; }

        /// <summary>The intent it was given, if any: a choice made overrides the brief's default.</summary>
        private readonly GmIntent? chosenIntent;

        /// <summary>How hard it plays for the kill this round, 0 to 1 (<see cref="Assessor.Ruthlessness"/>).</summary>
        public double Ruthlessness { get; private set; }

        /// <summary>Each round's ruthlessness as the director set it, from round 1 — for the sim and the table to read.</summary>
        public IReadOnlyList<double> RuthlessnessByRound => steeredRounds;

        private readonly List<double> steeredRounds = [];
        private int steeredRound;

        /// <summary>
        /// Once a round, before anything in it is chosen, the director reads the party's lead and
        /// eases off or presses on towards its challenge. Round 1 keeps the starting ruthlessness.
        /// </summary>
        private void SteerRound(BattleView view)
        {
            if (view.Round == steeredRound)
            {
                return;
            }

            if (steeredRound == 0 && chosenIntent is null && view.Brief is { } brief)
            {
                Intent = GmIntent.Directed(brief.DefaultChallenge);
                Ruthlessness = Intent.Ruthlessness;
            }

            steeredRound = view.Round;
            if (Intent.Steer is { } challenge && view.Round > 1)
            {
                Ruthlessness = GmIntent.Steered(Ruthlessness, challenge, GmIntent.Lead(view));
            }

            steeredRounds.Add(Ruthlessness);
        }

        /// <summary>
        /// A GM short of ruthless lets its creatures break as GM Reference §8 suggests, so the fight ends
        /// in a rout rather than a body count: below half its own Body, or once half its side is down.
        /// Breaking at the first ally down routed whole bands in two rounds (sims, 2026-11-08), which is no fight at all.
        /// </summary>
        public bool LetsBreak(BattleView view, Combatant creature, int alliesDown)
        {
            // Commitment overrides the intent: a hireling runs once it's hurt whoever is GMing, a fanatic never does.
            var commitment = view.Brief?.Commitment ?? Commitment.Committed;
            if (commitment == Commitment.Fanatic || (commitment == Commitment.Committed && Ruthlessness >= LetsBreakBelow))
            {
                return false;
            }

            var band = view.Combatants.Count(c => c.Side == creature.Side && !c.IsSteed && !c.IsPhantom);
            return creature.Body * 2 < creature.BodyMax || alliesDown * 2 >= band;
        }

        /// <summary>The ruthlessness under which the GM lets its creatures break (<see cref="LetsBreak"/>).</summary>
        public const double LetsBreakBelow = 0.3;

        /// <summary>An assessor that sees the party as this GM does.</summary>
        private Assessor Assess(BattleView view) => new(view, Knowledge, Side.Adversaries)
        {
            Ruthlessness = Ruthlessness,
            FinishesTheDowned = Intent.FinishesTheDowned && view.Brief?.Goal is not AdversaryGoal.Capture,
            SparesTheDowned = view.Brief?.Goal is AdversaryGoal.Capture,
            Menaced = menaced,
            Measured = Measure.Measures && measured.Count > 0 ? measured : null,
            MeasuredWeight = Measure.Weight,
        };

        private TurnPlanner Planner(BattleView view, out Assessor assess)
        {
            assess = Assess(view);
            return new TurnPlanner(assess, planning, characterCare: Plays.Care);
        }

        /// <summary>What a fresh turn would be worth to this adversary — its best plan from where it stands.</summary>
        private double FreshTurnWorth(BattleView view, Combatant adversary)
        {
            if (adversary.IsBroken)
            {
                return 0.5;
            }

            var planner = Planner(view, out var assess);

            // On 0 Stamina, a turn begun is the creature lost: it falls unconscious.
            if (adversary.Stamina <= 0)
            {
                return -assess.Value(adversary);
            }

            return planner.Options(adversary, hasMove: true, actions: 1, real: false, stillToAct: StillToAct(view, adversary))
                .Select(o => o.Score).DefaultIfEmpty(0).Max();
        }

        /// <summary>The GM's other creatures that may yet act this round — the ones a Trip would set up.</summary>
        /// <remarks>A creature that has broken runs on its turn: no blow of its follows a Trip, and no Help would be spent.</remarks>
        private static IReadOnlyCollection<Combatant> StillToAct(BattleView view, Combatant actor) =>
            view.Adversaries.Where(a => a != actor && a.HasTurnLeftThisRound && !a.IsBroken).ToList();

        public override Combatant ChooseOwedTurn(BattleView view)
        {
            SteerRound(view);
            var candidates = view.YetToAct.ToList();
            return Choose.Pick(candidates, a => FreshTurnWorth(view, a), Plays, random);
        }

        /// <summary>Buys an extra turn where one is worth more than an Opportunity kept.</summary>
        public override Combatant? ChooseExtraTurn(BattleView view)
        {
            SteerRound(view);
            var best = view.Adversaries.Where(a => a.CanTakeExtraTurn && !a.IsBroken)
                .Select(a => (Adversary: a, Worth: FreshTurnWorth(view, a)))
                .OrderByDescending(x => x.Worth)
                .FirstOrDefault();
            // A banked Opportunity keeps, so it waits for a turn worth having; an Immediate one
            // lapses before its spender's next turn, and a modest turn beats none at all.
            // A merciful GM buys fewer: each extra turn is more harm piled on the party.
            var bar = (view.ImmediateOpportunities > 0 ? ImmediateBar : 1.0) * (2 - Math.Pow(Math.Clamp(Ruthlessness, 0, 1), 2));
            return best.Adversary is not null && best.Worth >= bar ? best.Adversary : null;
        }

        /// <summary>What a turn must be worth to spend an Immediate Opportunity on, which would otherwise lapse.</summary>
        public const double ImmediateBar = 0.4;

        /// <summary>What a rival's off-hand blow must be worth to spend the Stamina on.</summary>
        public const double OffHandBar = 0.3;

        public override GmStep NextStep(BattleView view, GmTurn turn)
        {
            // A new round: what the party actually does, measured from where the fight stands, before
            // anything is chosen on it (the user's direction, 2026-11-07).
            MeasureRound(view, turn);
            SteerRound(view);

            var actor = turn.Actor;
            if (actor.IsBroken)
            {
                return Run(view, turn);
            }

            var planner = Planner(view, out _);
            if (turn.IsSurprise)
            {
                // One action from where it lies: the best blow, shot or spell it has, or nothing.
                var surprise = Planner(view, out _).Options(actor, hasMove: false, actions: 1, real: true)
                    .Where(o => o.Kind != OptionKind.Move && o.Then is null).ToList();
                var pick = surprise.Count == 0 ? null : Choose.Pick(surprise, o => o.Score, Plays, random);
                BrainTrace.Decision(actor, surprise, pick ?? new Option(OptionKind.End, 0, "keep hidden"));
                return pick is null ? GmStep.End : StepFor(pick, turn);
            }

            // A rival's Attack action just opened its off-hand strike: taken where the blow is worth the Stamina.
            if (actor.OffHandOwed && CanSpare(actor) && Plays.UsesTricks && actor.OffHandAttack is { } offHand)
            {
                var assess = Assess(view);
                var best = view.EnemiesOf(actor).Where(e => !e.IsOutOfFight && !e.IsPhantom)
                    .Where(e => view.Encounter.OffHandRefusal(actor, e) is null)
                    .Select(e => (Who: e, Plan: view.PlanAttackWith(actor, offHand, actor.Position, e)))
                    .Select(x => (x.Who, Worth: assess.Worth(assess.Blow(actor, offHand, x.Who, x.Plan.DamageDieSides, assess.PlanThreshold(x.Who, x.Plan)), x.Who)))
                    .OrderByDescending(x => x.Worth).FirstOrDefault();
                if (best.Who is not null && best.Worth >= OffHandBar)
                {
                    return new GmStep.OffHand(best.Who);
                }
            }

            var options = planner.Options(actor, actor.HasMove, actor.ActionsRemaining, real: true,
                turn.BonusAvailable, view.BankedOpportunities >= view.MaxBankedOpportunities, StillToAct(view, actor));

            // Rampage: its blow just dropped somebody, and a Stamina buys a second at somebody else —
            // closing with its move first if it must — where that blow beats keeping the Stamina.
            if (actor.RampageFrom is { } dropped && actor.Stamina >= 2 && Plays.UsesTricks && BattleView.TacticalSpendRefusal(actor) is null)
            {
                var second = planner.Options(actor, actor.HasMove, 1, real: false, bonusAvailable: false, stillToAct: StillToAct(view, actor))
                    .Where(o => o.Target is { } t && t != dropped && t.Side != actor.Side
                                && (o.Kind == OptionKind.Attack || (o.Kind == OptionKind.Move && o.Then == OptionKind.Attack)))
                    .MaxBy(o => o.Score);
                var keep = options.Select(o => o.Score).DefaultIfEmpty(0).Max();
                if (second is not null && second.Score - keep > planner.ExtraActionPrice(actor))
                {
                    if (second.Kind == OptionKind.Move)
                    {
                        return new GmStep.Move(second.Square!.Value);
                    }

                    if (view.RampageRefusal(actor, second.Target!, second.With) is null)
                    {
                        return new GmStep.Rampage(second.Target!, second.With);
                    }
                }
            }

            // Out of actions: another, for Stamina, where the best thing it buys beats keeping the
            // Stamina — and the reaction that Stamina would otherwise pay for this round.
            if (actor.ActionsRemaining == 0 && actor.Stamina >= 2 && BattleView.TacticalSpendRefusal(actor) is null && Plays.UsesTricks)
            {
                var bought = planner.Options(actor, actor.HasMove, 1, real: false, turn.BonusAvailable, stillToAct: StillToAct(view, actor))
                    .Where(o => o.Kind is not (OptionKind.End or OptionKind.Move))
                    .Select(o => o.Score).DefaultIfEmpty(double.MinValue).Max();
                var keep = options.Select(o => o.Score).DefaultIfEmpty(0).Max();
                if (bought - keep > planner.ExtraActionPrice(actor))
                {
                    return new GmStep.BuyExtraAction();
                }
            }

            if (BonusStep(view, planner, actor, turn, options) is { } bonusStep)
            {
                return bonusStep;
            }

            if (options.Count == 0)
            {
                return GmStep.End;
            }

            var choice = Foresee(view, turn, options) ?? Choose.Pick(options, o => o.Score, Plays, random);
            BrainTrace.Decision(actor, options, choice);
            return StepFor(choice, turn);
        }

        /// <summary>The step an option chosen on <paramref name="turn"/> asks the encounter for.</summary>
        private GmStep StepFor(Option choice, GmTurn turn)
        {
            if (choice.Kind == OptionKind.Attack && choice.Target is { IsDefenceless: true, Side: not Side.Adversaries } downed)
            {
                menaced.Add(downed);
            }

            return Step(choice, turn);
        }

        /// <summary>The downed characters it has laid its one blow on (<see cref="Assessor.Menaced"/>).</summary>
        private readonly HashSet<Combatant> menaced = [];

        private static GmStep Step(Option choice, GmTurn turn) =>
            choice.Kind switch
            {
                OptionKind.Move => new GmStep.Move(choice.Square!.Value),
                OptionKind.Attack => new GmStep.Attack(choice.Target!, choice.WithBonus && turn.BonusAvailable, choice.With, choice.Manoeuvre),
                OptionKind.TakeUpWeapon => new GmStep.TakeUpWeapon(),
                OptionKind.Mount => new GmStep.Mount(),
                OptionKind.Throw => new GmStep.Throw(choice.Target!, choice.WithBonus && turn.BonusAvailable),
                OptionKind.DrawWeapon => new GmStep.DrawWeapon(choice.Weapon!),
                OptionKind.PickUpWeapon => new GmStep.PickUpWeapon(choice.Lying!),
                OptionKind.Exploit => new GmStep.Exploit(choice.Exploit!.Value),
                OptionKind.RaiseShield => new GmStep.RaiseShield(choice.Target),
                OptionKind.DuckBack => new GmStep.DuckBack(),
                OptionKind.Cast => new GmStep.Cast(choice.Cast!, choice.WithBonus && turn.BonusAvailable),
                OptionKind.Search => new GmStep.Search(choice.Illusion!),
                OptionKind.Control => new GmStep.Control(choice.Control!.Value, choice.Target!),
                OptionKind.Aid => new GmStep.Aid(choice.Action!.Value, choice.Target!),
                OptionKind.Tend => new GmStep.Tend(choice.Action!.Value, choice.Target!),
                OptionKind.StandGround => new GmStep.StandGround(choice.Action!.Value),
                OptionKind.StandUp => new GmStep.StandUp(),
                OptionKind.BreakFree => new GmStep.BreakFree(),
                OptionKind.RegainFooting => new GmStep.RegainFooting(),
                OptionKind.SwapLoadout => new GmStep.SwapLoadout(),
                _ => GmStep.End,
            };

        /// <summary>
        /// An Opportunity bonus that isn't spent on a blow or a spell (GM Reference §9): another move,
        /// where the turn it opens up beats the one without it by more than an extra turn's worth; or
        /// +2 to the Threshold the coming blows are rolled against, where the blows it turns are worth
        /// as much — weighed once the creature has nothing better left to do with its turn.
        /// </summary>
        private GmStep? BonusStep(BattleView view, TurnPlanner planner, Combatant actor, GmTurn turn, List<Option> options)
        {
            if (!turn.BonusAvailable || !Plays.UsesTricks)
            {
                return null;
            }

            var price = view.BankedOpportunities >= view.MaxBankedOpportunities ? 0 : TurnPlanner.BonusPrice;
            var now = options.Select(o => o.Score).DefaultIfEmpty(0).Max();
            switch (actor.OpportunityBonus.Kind)
            {
                case OpportunityBonusKind.ExtraMove when !actor.HasMove && Encounter.CanMoveItself(actor):
                {
                    var moved = planner.Options(actor, hasMove: true, actor.ActionsRemaining, real: false, stillToAct: StillToAct(view, actor))
                        .Select(o => o.Score).DefaultIfEmpty(0).Max();
                    return moved - now >= Math.Max(price, 0.05) ? new GmStep.BonusMove() : null;
                }

                case OpportunityBonusKind.OffHand when turn.HasAttacked && actor.AttacksThisRound > 0 && actor.OffHandAttack is { } offHand:
                {
                    var assess = Assess(view);
                    var best = view.EnemiesOf(actor).Where(e => !e.IsOutOfFight && !e.IsPhantom)
                        .Select(e => (Who: e, Plan: view.PlanAttackWith(actor, offHand, actor.Position, e)))
                        .Where(x => x.Plan.CanAttack)
                        .Select(x => (x.Who, Worth: assess.Worth(assess.Blow(actor, offHand, x.Who, x.Plan.DamageDieSides, assess.PlanThreshold(x.Who, x.Plan)), x.Who)))
                        .OrderByDescending(x => x.Worth).FirstOrDefault();
                    return best.Who is not null && best.Worth >= Math.Max(price, 0.3) ? new GmStep.OffHand(best.Who) : null;
                }

                case OpportunityBonusKind.ThresholdBoost when actor.ActionsRemaining == 0 && !actor.HasMove:
                {
                    var assess = new Assessor(view);
                    var turned = assess.Exposure(actor, actor.Position) - assess.Exposure(actor, actor.Position, extraDodge: Encounter.OpportunityThresholdBoost);
                    return turned >= Math.Max(price, 0.05) ? new GmStep.BoostThreshold(ThresholdType.Dodge) : null;
                }

                default:
                    return null;
            }
        }

        /// <summary>Strikes at a leaver where the blow is worth the Stamina — and never while running.</summary>
        public override bool StrikesAtLeaver(BattleView view, Combatant adversary, Combatant leaver) =>
            !adversary.IsBroken && CanSpare(adversary) && Assess(view).BestAttack(adversary, adversary.Position, leaver).Worth >= 0.4;

        /// <summary>
        /// Strike, Grab or Trip a leaver — whichever is worth most, if anything is worth the Stamina:
        /// a blow worth having, or the leaver stopped where catching them is worth more than the blow.
        /// </summary>
        public LeaverResponse RespondsToLeaver(BattleView view, Combatant adversary, Combatant leaver, IReadOnlyList<LeaverResponse> options)
        {
            if (adversary.IsBroken || !CanSpare(adversary))
            {
                return LeaverResponse.LetThemGo;
            }

            var assess = Assess(view);
            var worths = new List<(LeaverResponse Response, double Worth)>();
            if (options.Contains(LeaverResponse.Strike))
            {
                worths.Add((LeaverResponse.Strike, assess.BestAttack(adversary, adversary.Position, leaver).Worth));
            }

            if (Plays.Uses(Trick.Grapple))
            {
                var stopping = 0.2 * assess.Value(leaver);
                foreach (var reach in new[] { LeaverResponse.Grab, LeaverResponse.Trip }.Where(options.Contains))
                {
                    worths.Add((reach, assess.ControlChance(adversary, leaver, againstDodgeToo: true, topples: reach != LeaverResponse.Grab) * stopping * (reach == LeaverResponse.Grab ? 1 : 0.6)));
                }
            }

            var best = worths.OrderByDescending(w => w.Worth).FirstOrDefault();
            return best.Worth >= 0.4 ? best.Response : LeaverResponse.LetThemGo;
        }

        /// <summary>
        /// Worth the Stamina where it spares enough; a shield over somebody else is worth it too where
        /// the one it covers is the charge a Sentinel stands guard over.
        /// </summary>
        public override bool PanicDodges(BattleView view, PendingReaction reaction) => reaction.Kind == ReactionKind.CoverAlly
            ? CanSpare(reaction.Defender) && (WorthAvoiding(view, reaction) || (reaction.Defender.Role == AdversaryRole.Sentinel && reaction.Struck.Spells.Count > 0))
            : (CanSpare(reaction.Defender) && WorthAvoiding(view, reaction)) || SavesItself(reaction.Defender, reaction);

        public override bool StandsFirm(BattleView view, PendingReaction reaction) => CanSpare(reaction.Defender);

        /// <summary>
        /// A rival Interrupts as a character would: where its turn taken now, with what it spares of the
        /// declared action, beats the same turn taken later by more than the Mind it costs.
        /// </summary>
        public bool Interrupts(BattleView view, PendingReaction reaction)
        {
            var me = reaction.Defender;
            if (me.Mind < 2 || me.IsBroken || reaction.Attacker.IsBroken || !Plays.UsesTricks)
            {
                return false;
            }

            var planner = Planner(view, out var assess);
            var interruption = new Interruption(reaction.Attacker, reaction.Against);
            var sentry = view.Encounter.InterruptsAsSentry(me);
            var options = planner.Options(me, hasMove: !sentry, actions: 1, real: false, stillToAct: StillToAct(view, me));
            var plain = sentry ? 0 : options.Select(o => o.Score).DefaultIfEmpty(0).Max();
            var now = options.Select(o => o.Score + planner.Spared(me, o, interruption)).DefaultIfEmpty(0).Max()
                      - (sentry ? planner.ExtraActionPrice(me) : 0);
            var mindIsFuel = me.Spells.Count > 0 && me.Build?.ClassPrinciple == ClassPrinciple.Cerebral;
            var cost = (mindIsFuel ? 0.8 : 0.3) * assess.PerWound(me) + 0.4;
            return now - plain > cost;
        }

        /// <summary>A foe built with Shelter weighs it as a character would.</summary>
        public int ShelterAmount(BattleView view, PendingReaction reaction) => Sheltering.Amount(new Assessor(view), reaction);

        /// <summary>Spends the Stamina when the blow it turns is worth more than keeping it.</summary>
        private static bool WorthAvoiding(BattleView view, PendingReaction reaction)
        {
            var assess = new Assessor(view);
            var (average, absorbs) = Incoming(reaction);
            var prevented = Math.Max(0, average - absorbs) * assess.PerWound(reaction.Struck) + (reaction.WouldRiposte ? 1 : 0);
            return prevented >= 0.4 || reaction.Struck.IsMinion;
        }

        /// <summary>What the blow being answered would do on average, and what armour takes off it — a spell's own dice where it is one.</summary>
        internal static (double Average, int Absorbs) Incoming(PendingReaction reaction)
        {
            var attack = reaction.Attacker.Attack;
            var average = reaction.ExpectedDamage ?? attack.DamageDiceCount * (attack.DamageDieSides + 1) / 2.0 + attack.DamageModifier;
            var absorbs = reaction.DamageType == WoundType.Body
                ? Rules.Calculation.InventoryCalculator.ArmourAbsorbedAgainst(reaction.Struck.Armour, WoundType.Body, isDirect: false)
                : 0;
            return (average, absorbs);
        }

        /// <summary>Each idle creature's round-end move goes wherever its best next turn is set up from — the runners first.</summary>
        public override GmCloseIn? NextCloseIn(BattleView view, IReadOnlyList<Combatant> stillIdle)
        {
            if (stillIdle.Any(a => a.IsBroken || a.IsProne))
            {
                var simple = base.NextCloseIn(view, stillIdle.Where(a => a.IsBroken || a.IsProne).ToList());
                if (simple is not null)
                {
                    return simple;
                }
            }

            var planner = Planner(view, out _);
            GmCloseIn? best = null;
            var bestGain = 0.05;
            foreach (var adversary in stillIdle.Where(a => !a.IsBroken && !a.IsStandingGround && Encounter.CanMoveItself(a)))
            {
                var options = planner.Options(adversary, hasMove: true, actions: 0, real: false);
                var stay = options.Where(o => o.Kind == OptionKind.End).Select(o => o.Score).DefaultIfEmpty(0).Max();
                var go = options.Where(o => o.Kind == OptionKind.Move).MaxBy(o => o.Score);
                if (go is not null && go.Score - stay > bestGain)
                {
                    bestGain = go.Score - stay;
                    best = new GmCloseIn(adversary, go.Square!.Value);
                }
            }

            return best;
        }
    }

    /// <summary>
    /// The party played to win: who goes next, what they do, and every reaction, each chosen by
    /// running through the options and taking the one that does the most for the party, as well
    /// as its <see cref="BrainSkill"/> lets it. It keeps a Stamina in hand, since a character who
    /// starts a turn on none falls unconscious.
    /// </summary>
    public class UtilityPartyBrain : IPartyBrain
    {
        private const int StaminaReserve = 1;
        private readonly Random random;

        public UtilityPartyBrain(BrainSkill? skill = null, Random? random = null)
        {
            Skill = skill ?? BrainSkill.Best;
            this.random = random ?? new Random(2);
        }

        public BrainSkill Skill { get; }

        public string Name => $"Utility ({Skill.Name})";

        private TurnPlanner Planner(BattleView view, out Assessor assess)
        {
            assess = new Assessor(view);
            return new TurnPlanner(assess, Skill);
        }

        /// <summary>
        /// The order is the party's to choose afresh each round (Core Rules §9). By default the one
        /// with the most to do goes, which puts the heavy blows in before the other side's turns come
        /// round. Two kinds of character go earlier:
        /// <list type="bullet">
        /// <item>a caster whose best move is a Bane on a foe the others are about to swing at — worth
        /// something only before their blows — goes first for it;</item>
        /// <item>one already in place whose best is a Help or a Harry, made without moving, and who has
        /// no other good option — nothing else worth half what the best of the others has — goes
        /// ahead of the blades: Advantage set up for the big martial, or Disadvantage laid on the big
        /// enemy before it acts.</item>
        /// </list>
        /// A blade with a good blow of its own doesn't spend it helping first: simulation showed that
        /// cost the party more than the Advantage gave it (2026-11-02).
        /// </summary>
        public Combatant ChooseNextCharacter(BattleView view, IReadOnlyList<Combatant> stillToAct)
        {
            var planner = Planner(view, out _);
            List<Option> All(Combatant c, IReadOnlyCollection<Combatant> others) =>
                planner.Options(c, hasMove: true, actions: 1, real: false, stillToAct: others);
            static bool Aids(Option o) => o.Kind == OptionKind.Aid || o.Then == OptionKind.Aid;

            var options = stillToAct.ToDictionary(c => c, c => All(c, stillToAct));
            double BestOf(Combatant c) => options[c].Select(o => o.Score).DefaultIfEmpty(0).Max();

            var scored = stillToAct.ToDictionary(c => c, c =>
            {
                var now = options[c].MaxBy(o => o.Score);
                var score = now?.Score ?? 0;
                if (stillToAct.Count < 2 || now is null)
                {
                    return score;
                }

                if (now.Cast?.Spell.SpellType == Rules.Enums.SpellType.Bane)
                {
                    var urgency = score - All(c, [c]).Select(o => o.Score).DefaultIfEmpty(0).Max();
                    return urgency > UrgencyThreshold ? Urgent + urgency : score;
                }

                // Only one already in place — with a Help or Harry to hand from where they stand, about
                // as good as their best: a character who'd have to move to help is usually out of reach
                // of the fight, and waiting lets it come to them (simulation, 2026-11-02).
                var inPlace = options[c].Where(o => o.Kind == OptionKind.Aid).Select(o => o.Score).DefaultIfEmpty(double.MinValue).Max();
                if (Aids(now) && inPlace >= score - 0.25)
                {
                    var otherwise = options[c].Where(o => !Aids(o)).Select(o => o.Score).DefaultIfEmpty(0).Max();
                    var bestOfTheRest = stillToAct.Where(o => o != c).Max(BestOf);
                    return otherwise < 0.5 * bestOfTheRest ? Urgent + score : score;
                }

                return score;
            });
            return Choose.Pick(stillToAct, c => scored[c], Skill, random);
        }

        /// <summary>How much more going now must be worth than going last before it decides the order.</summary>
        private const double UrgencyThreshold = 0.15;

        /// <summary>Puts any character whose best move is worth more going first ahead of every one whose isn't.</summary>
        private const double Urgent = 100;

        public PartyStep NextStep(BattleView view, Combatant character)
        {
            var planner = Planner(view, out var assess);
            var stillToAct = view.PartyStillToAct;

            // The off-hand strike its Attack action allows, where a blow with it is worth the Stamina.
            if (OffHandStrike(view, assess, character) is { } offHand)
            {
                return offHand;
            }
            var options = planner.Options(character, character.HasMove, character.ActionsRemaining, real: true, stillToAct: stillToAct,
                interrupting: view.Interrupting);

            // Out of actions: another, for Stamina, if the best thing it would buy is worth the
            // Stamina and the Opportunity it hands the GM.
            if (character.ActionsRemaining == 0 && CanSpare(character) && BattleView.TacticalSpendRefusal(character) is null)
            {
                var bought = planner.Options(character, character.HasMove, 1, real: false, stillToAct: stillToAct)
                    .Where(o => o.Kind is not (OptionKind.End or OptionKind.Move))
                    .Select(o => o.Score).DefaultIfEmpty(0).Max();
                var keep = options.Select(o => o.Score).DefaultIfEmpty(0).Max();
                var cost = 1.0 + 0.8 * view.Adversaries.Select(assess.Threat).DefaultIfEmpty(0).Average() * assess.PerWound(character);
                if (bought - keep > cost)
                {
                    return new PartyStep.BuyExtraAction();
                }
            }

            if (options.Count == 0)
            {
                return PartyStep.End;
            }

            var choice = Choose.Pick(options, o => o.Score, Skill, random);
            return choice.Kind switch
            {
                OptionKind.Move => new PartyStep.Move(choice.Square!.Value),
                OptionKind.Attack => new PartyStep.Attack(choice.Target!, Manoeuvre: choice.Manoeuvre),
                OptionKind.Exploit => new PartyStep.Exploit(choice.Exploit!.Value),
                OptionKind.DuckBack => new PartyStep.DuckBack(),
                OptionKind.Mount => new PartyStep.Mount(),
                OptionKind.Throw => new PartyStep.Throw(choice.Target!),
                OptionKind.DrawWeapon => new PartyStep.DrawWeapon(choice.Weapon!),
                OptionKind.PickUpWeapon => new PartyStep.PickUpWeapon(choice.Lying!),
                OptionKind.Cast => new PartyStep.Cast(choice.Cast!),
                OptionKind.Control => new PartyStep.Control(choice.Control!.Value, choice.Target!),
                OptionKind.Aid => new PartyStep.Aid(choice.Action!.Value, choice.Target!),
                OptionKind.Tend => new PartyStep.Tend(choice.Action!.Value, choice.Target!),
                OptionKind.RaiseShield => new PartyStep.RaiseShield(choice.Target),
                OptionKind.StandGround => new PartyStep.StandGround(choice.Action!.Value),
                OptionKind.StandUp => new PartyStep.StandUp(),
                OptionKind.BreakFree => new PartyStep.BreakFree(),
                OptionKind.TakeUpWeapon => new PartyStep.TakeUpWeapon(),
                OptionKind.RegainFooting => new PartyStep.RegainFooting(),
                OptionKind.SwapLoadout => new PartyStep.Swap(),
                _ => PartyStep.End,
            };
        }

        /// <summary>
        /// Pays to go on holding a spell on the terms it was cast on: never the last Stamina (that
        /// would put the caster down at their next turn, and the spell with them), and only while
        /// it is still on somebody in the fight.
        /// </summary>
        public bool Sustains(BattleView view, PendingReaction reaction) =>
            reaction.Defender.Stamina > 1
            && view.SpellsInPlay.Any(s => s.Caster == reaction.Defender && s.Sustained && !s.Target.IsOutOfFight);

        public bool PanicDodges(BattleView view, PendingReaction reaction)
        {
            if (!CanSpare(reaction.Defender))
            {
                return false;
            }

            var assess = new Assessor(view);
            var (average, absorbs) = UtilityGmBrain.Incoming(reaction);
            var left = reaction.DamageType == WoundType.Body ? reaction.Defender.Body : reaction.Defender.Track(reaction.DamageType).Current;
            var prevented = Math.Max(0, average - absorbs) * assess.PerWound(reaction.Defender) + (reaction.WouldRiposte ? 1.5 : 0);
            return prevented >= 0.8 || average - absorbs >= left;
        }

        public bool StandsFirm(BattleView view, PendingReaction reaction) => CanSpare(reaction.Defender);

        public bool ArrestsShove(BattleView view, PendingReaction reaction) => reaction.ImpactWounds > 0 && CanSpare(reaction.Defender);

        /// <summary>Strike, grab, trip or let go — whichever is worth most, if anything is worth the Stamina and the GM's Opportunity.</summary>
        public LeaverResponse ReactsToLeaver(BattleView view, PendingReaction reaction)
        {
            var me = reaction.Defender;
            var leaver = reaction.Attacker;
            var options = reaction.Options ?? [];
            if (!CanSpare(me))
            {
                return LeaverResponse.LetThemGo;
            }

            var assess = new Assessor(view);
            var stopping = leaver.IsBroken ? assess.Value(leaver) * (view.MustNotEscape(leaver) ? 1.5 : 1) : 0.2 * assess.Value(leaver);
            var worths = new List<(LeaverResponse Response, double Worth)>();
            if (options.Contains(LeaverResponse.Strike))
            {
                worths.Add((LeaverResponse.Strike, assess.BestAttack(me, me.Position, leaver).Worth));
            }

            foreach (var reach in new[] { LeaverResponse.Grab, LeaverResponse.Trip })
            {
                if (options.Contains(reach) && Skill.Uses(Trick.Grapple))
                {
                    worths.Add((reach, assess.ControlChance(me, leaver, againstDodgeToo: true, topples: reach != LeaverResponse.Grab) * stopping * (reach == LeaverResponse.Grab ? 1 : 0.6)));
                }
            }

            var cost = 0.6 + 0.4 * assess.Threat(leaver) * assess.PerWound(me);
            var best = worths.OrderByDescending(w => w.Worth).FirstOrDefault();
            return best.Worth > cost ? best.Response : LeaverResponse.LetThemGo;
        }

        private static bool CanSpare(Combatant c) => c.Stamina - 1 >= StaminaReserve;

        /// <summary>
        /// The off-hand blow that's worth most, once the Attack action has allowed one: Stamina from the
        /// equipment, so no Opportunity — only the Stamina itself, which keeps a character on their feet.
        /// </summary>
        private static PartyStep? OffHandStrike(BattleView view, Assessor assess, Combatant character)
        {
            if (!character.OffHandOwed || character.OffHandAttack is not { } offHand || !CanSpare(character)
                || character.NeedsFooting || character.IsOutOfFight)
            {
                return null;
            }

            var best = view.EnemiesOf(character)
                .Select(e => (Who: e, Plan: view.PlanAttackWith(character, offHand, character.Position, e)))
                .Where(x => x.Plan.CanAttack && !x.Who.IsOutOfFight)
                .Select(x => (x.Who, Worth: assess.Worth(assess.Blow(character, offHand, x.Who, x.Plan.DamageDieSides, assess.PlanThreshold(x.Who, x.Plan),
                    view.PackMateOf(character, x.Who) is not null), x.Who)))
                .OrderByDescending(x => x.Worth)
                .FirstOrDefault();

            return best.Who is not null && best.Worth >= 0.3 ? new PartyStep.OffHand(best.Who) : null;
        }

        /// <summary>
        /// Interrupts where the turn taken now beats the same turn taken later by more than the Mind it
        /// costs: the best plan with what it spares of the declared action counted — its maker dropped
        /// or caught mid-motion, or its target stepped out of reach — against the best plan without.
        /// A turn that only acts earlier, and touches the declared action not at all, isn't worth it.
        /// </summary>
        public bool Interrupts(BattleView view, PendingReaction reaction)
        {
            var me = reaction.Defender;
            if (me.Mind < 2 || reaction.Attacker.IsBroken)
            {
                return false;
            }

            var planner = Planner(view, out var assess);
            var interruption = new Interruption(reaction.Attacker, reaction.Against);

            // A Sentry who has acted gets one action, not a turn — and against doing nothing at all
            // this round, not against the same turn taken later; it pays a Stamina and an Opportunity besides.
            var sentry = !view.PartyStillToAct.Contains(me);
            var options = planner.Options(me, hasMove: !sentry, actions: 1, real: false, stillToAct: view.PartyStillToAct);
            var plain = sentry
                ? options.Where(o => o.Kind == OptionKind.End).Select(o => o.Score).DefaultIfEmpty(0).Max()
                : options.Select(o => o.Score).DefaultIfEmpty(0).Max();
            var now = options.Select(o => o.Score + planner.Spared(me, o, interruption)).DefaultIfEmpty(0).Max()
                      - (sentry ? planner.ExtraActionPrice(me) : 0);

            // The Mind it costs — dear to a caster whose spells run on it — and a turn spent before
            // the round has shown what it most needs.
            var mindIsFuel = me.Spells.Count > 0 && me.Build?.ClassPrinciple == ClassPrinciple.Cerebral;
            var cost = (mindIsFuel ? 0.8 : 0.3) * assess.PerWound(me) + 0.4;
            return now - plain > cost;
        }

        public bool Shelters(BattleView view, PendingReaction reaction) => ShelterAmount(view, reaction) > 0;

        public int ShelterAmount(BattleView view, PendingReaction reaction) => Sheltering.Amount(new Assessor(view), reaction);
    }

    /// <summary>
    /// How much of a blow to Shelter — "any part of it" (Core Rules §3) — weighed wound by wound: each
    /// one spared the ally is worth what a wound to them is, and keeping them on their feet is worth a
    /// great deal more; each one taken costs the shelterer Soul, which is dear only to a caster whose
    /// spells run on it, or as the track runs low enough to Numb them.
    /// </summary>
    public static class Sheltering
    {
        public static int Amount(Assessor assess, PendingReaction reaction)
        {
            var ally = reaction.Attacker;
            var me = reaction.Defender;
            var incoming = reaction.ImpactWounds;
            var left = reaction.DamageType == WoundType.Body ? ally.Body : ally.Track(reaction.DamageType).Current;
            var soulIsFuel = me.Spells.Count > 0 && me.Build?.ClassPrinciple == ClassPrinciple.Spiritual;
            var perSoul = (soulIsFuel ? 0.8 : 0.3) * assess.PerWound(me);
            var perAllyWound = assess.PerWound(ally) * (reaction.DamageType == WoundType.Body ? 1 : 0.7);
            var dropWorth = 0.6 * assess.Value(ally);

            var best = (Take: 0, Net: 0.0);
            for (var take = 1; take <= incoming && take < me.Soul; take++)
            {
                // Keep two Soul in hand unless it's the last wound between the ally and the floor.
                var keepsAlly = incoming >= left && incoming - take < left;
                if (me.Soul - take < 2 && !keepsAlly)
                {
                    break;
                }

                var net = take * (perAllyWound - perSoul) + (keepsAlly ? dropWorth : 0) - (me.Soul - take <= 1 ? 0.5 * perSoul : 0);
                if (net > best.Net)
                {
                    best = (take, net);
                }
            }

            return best.Net >= 0.5 ? best.Take : 0;
        }
    }
}
