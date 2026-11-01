using WoundBound.Rules.Enums;

namespace WoundBound.Game.Combat.Brains.Utility
{
    /// <summary>
    /// The GM played to win: before every decision it runs through everything its creatures could
    /// do and takes the option that does the most for its side (<see cref="TurnPlanner"/>), as well
    /// as its <see cref="BrainSkill"/> lets it. The default goal is to kill the party; a creature
    /// that has broken runs, as the simple GM's do.
    /// </summary>
    public class UtilityGmBrain : SimpleGmBrain
    {
        private readonly Random random;

        public UtilityGmBrain(BrainSkill? skill = null, Random? random = null)
        {
            Skill = skill ?? BrainSkill.Best;
            this.random = random ?? new Random(1);
        }

        public BrainSkill Skill { get; }

        private TurnPlanner Planner(BattleView view, out Assessor assess)
        {
            assess = new Assessor(view);
            return new TurnPlanner(assess, Skill);
        }

        /// <summary>What a fresh turn would be worth to this adversary — its best plan from where it stands.</summary>
        private double FreshTurnWorth(BattleView view, Combatant adversary)
        {
            if (adversary.IsBroken)
            {
                return 0.5;
            }

            var planner = Planner(view, out _);
            return planner.Options(adversary, hasMove: true, actions: 1, real: false, stillToAct: StillToAct(view, adversary))
                .Select(o => o.Score).DefaultIfEmpty(0).Max();
        }

        /// <summary>The GM's other creatures that may yet act this round — the ones a Trip would set up.</summary>
        private static IReadOnlyCollection<Combatant> StillToAct(BattleView view, Combatant actor) =>
            view.Adversaries.Where(a => a != actor && !a.HasActedThisRound).ToList();

        public override Combatant ChooseOwedTurn(BattleView view)
        {
            var candidates = view.Adversaries.ToList();
            return Choose.Pick(candidates, a => FreshTurnWorth(view, a), Skill, random);
        }

        /// <summary>Buys an extra turn where one is worth more than an Opportunity kept.</summary>
        public override Combatant? ChooseExtraTurn(BattleView view)
        {
            var best = view.Adversaries.Where(a => !a.IsBroken)
                .Select(a => (Adversary: a, Worth: FreshTurnWorth(view, a)))
                .OrderByDescending(x => x.Worth)
                .FirstOrDefault();
            return best.Adversary is not null && best.Worth >= 1.0 ? best.Adversary : null;
        }

        public override GmStep NextStep(BattleView view, GmTurn turn)
        {
            var actor = turn.Actor;
            if (actor.IsBroken)
            {
                return Run(view, turn);
            }

            var planner = Planner(view, out _);
            var options = planner.Options(actor, actor.HasMove, actor.ActionsRemaining, real: true,
                turn.BonusAvailable, view.BankedOpportunities >= view.MaxBankedOpportunities, StillToAct(view, actor));
            if (options.Count == 0)
            {
                return GmStep.End;
            }

            var choice = Choose.Pick(options, o => o.Score, Skill, random);
            return choice.Kind switch
            {
                OptionKind.Move => new GmStep.Move(choice.Square!.Value),
                OptionKind.Attack => new GmStep.Attack(choice.Target!, choice.WithAdvantage && turn.BonusAvailable, choice.With),
                OptionKind.Cast => new GmStep.Cast(choice.Cast!),
                OptionKind.Control => new GmStep.Control(choice.Control!.Value, choice.Target!),
                OptionKind.Aid => new GmStep.Aid(choice.Action!.Value, choice.Target!),
                OptionKind.Tend => new GmStep.Tend(choice.Action!.Value, choice.Target!),
                OptionKind.StandGround => new GmStep.StandGround(choice.Action!.Value),
                OptionKind.StandUp => new GmStep.StandUp(),
                OptionKind.BreakFree => new GmStep.BreakFree(),
                OptionKind.RegainFooting => new GmStep.RegainFooting(),
                _ => GmStep.End,
            };
        }

        /// <summary>Strikes at a leaver where the blow is worth the Stamina — and never while running.</summary>
        public override bool StrikesAtLeaver(BattleView view, Combatant adversary, Combatant leaver) =>
            !adversary.IsBroken && new Assessor(view).BestAttack(adversary, adversary.Position, leaver).Worth >= 0.4;

        public override bool PanicDodges(BattleView view, PendingReaction reaction) => WorthAvoiding(view, reaction);

        public override bool StandsFirm(BattleView view, PendingReaction reaction) => reaction.Defender.Stamina > 0;

        /// <summary>Spends the Stamina when the blow it turns is worth more than keeping it.</summary>
        private static bool WorthAvoiding(BattleView view, PendingReaction reaction)
        {
            var assess = new Assessor(view);
            var (average, absorbs) = Incoming(reaction);
            var prevented = Math.Max(0, average - absorbs) * assess.PerWound(reaction.Defender) + (reaction.WouldRiposte ? 1 : 0);
            return prevented >= 0.4 || reaction.Defender.IsMinion;
        }

        /// <summary>What the blow being answered would do on average, and what armour takes off it — a spell's own dice where it is one.</summary>
        internal static (double Average, int Absorbs) Incoming(PendingReaction reaction)
        {
            var attack = reaction.Attacker.Attack;
            var average = reaction.ExpectedDamage ?? attack.DamageDiceCount * (attack.DamageDieSides + 1) / 2.0 + attack.DamageModifier;
            var absorbs = reaction.DamageType == WoundType.Body
                ? Rules.Calculation.InventoryCalculator.ArmourAbsorbedAgainst(reaction.Defender.Armour, WoundType.Body, isDirect: false)
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

        public Combatant ChooseNextCharacter(BattleView view, IReadOnlyList<Combatant> stillToAct)
        {
            var planner = Planner(view, out _);
            return Choose.Pick(stillToAct,
                c => planner.Options(c, hasMove: true, actions: 1, real: false, stillToAct: stillToAct).Select(o => o.Score).DefaultIfEmpty(0).Max(),
                Skill, random);
        }

        public PartyStep NextStep(BattleView view, Combatant character)
        {
            var planner = Planner(view, out var assess);
            var stillToAct = view.PartyStillToAct;
            var options = planner.Options(character, character.HasMove, character.ActionsRemaining, real: true, stillToAct: stillToAct);

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
                OptionKind.Cast => new PartyStep.Cast(choice.Cast!),
                OptionKind.Control => new PartyStep.Control(choice.Control!.Value, choice.Target!),
                OptionKind.Aid => new PartyStep.Aid(choice.Action!.Value, choice.Target!),
                OptionKind.Tend => new PartyStep.Tend(choice.Action!.Value, choice.Target!),
                OptionKind.RaiseShield => new PartyStep.RaiseShield(),
                OptionKind.StandGround => new PartyStep.StandGround(choice.Action!.Value),
                OptionKind.StandUp => new PartyStep.StandUp(),
                OptionKind.BreakFree => new PartyStep.BreakFree(),
                OptionKind.RegainFooting => new PartyStep.RegainFooting(),
                _ => PartyStep.End,
            };
        }

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
                if (options.Contains(reach) && Skill.UsesTricks)
                {
                    worths.Add((reach, assess.ControlChance(me, leaver, againstDodgeToo: true) * stopping * (reach == LeaverResponse.Grab ? 1 : 0.6)));
                }
            }

            var cost = 0.6 + 0.4 * assess.Threat(leaver) * assess.PerWound(me);
            var best = worths.OrderByDescending(w => w.Worth).FirstOrDefault();
            return best.Worth > cost ? best.Response : LeaverResponse.LetThemGo;
        }

        private static bool CanSpare(Combatant c) => c.Stamina - 1 >= StaminaReserve;

        /// <summary>
        /// Interrupts where the turn taken now does more than the same turn later: the declared action
        /// caught mid-motion — about a third of its harm spared, or all of it if the blow could drop its
        /// maker first — worth more than the Mind it costs.
        /// </summary>
        public bool Interrupts(BattleView view, PendingReaction reaction)
        {
            var me = reaction.Defender;
            var foe = reaction.Attacker;
            if (me.Mind < 2 || foe.IsBroken)
            {
                return false;
            }

            var assess = new Assessor(view);
            var squares = view.SquaresWithin(me, 1).Keys.Append(me.Position);
            var (hit, kill) = squares
                .Select(sq => assess.BestAttack(me, sq, foe))
                .Where(b => b.With is not null)
                .Select(b => (b.Worth, b.With!))
                .Select(b => (Hit: b.Worth, Kill: Math.Min(1, b.Worth / Math.Max(0.1, assess.Value(foe)))))
                .DefaultIfEmpty((0, 0))
                .MaxBy(b => b.Hit);
            if (hit <= 0)
            {
                return false;
            }

            var victim = reaction.Against is { Side: Side.Party } v ? v : me;
            var spared = assess.Threat(foe) * assess.PerWound(victim) * (0.35 + 0.65 * kill);
            return spared > 0.7;
        }

        /// <summary>Shelters an ally a hit would drop, or a caster the party can't afford to lose, where Soul can take it.</summary>
        public bool Shelters(BattleView view, PendingReaction reaction)
        {
            var ally = reaction.Attacker;
            var me = reaction.Defender;
            var left = reaction.DamageType == WoundType.Body ? ally.Body : ally.Track(reaction.DamageType).Current;
            var drops = reaction.ImpactWounds >= left;
            return me.Soul > reaction.ImpactWounds && (drops || (ally.HasCastSpell && reaction.ImpactWounds >= 3));
        }
    }
}
