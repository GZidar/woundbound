using WoundBound.Rules.Enums;

namespace WoundBound.Game.Combat.Brains.Utility
{
    public enum OptionKind
    {
        Move,
        Attack,
        Control,
        Aid,
        Tend,
        RaiseShield,
        StandGround,
        StandUp,
        BreakFree,
        TakeUpWeapon,
        BuyExtraAction,
        RegainFooting,
        Cast,
        Exploit,
        Search,
        End,
    }

    /// <summary>
    /// The next thing a creature could do, with what the whole plan it begins is worth: a move
    /// that sets up a blow is scored as the blow from the square it leads to.
    /// </summary>
    public sealed record Option(OptionKind Kind, double Score, string Why,
        GridPosition? Square = null, Combatant? Target = null, AttackProfile? With = null,
        CombatAction? Action = null, ControlAction? Control = null, bool WithAdvantage = false, SpellCast? Cast = null,
        Manoeuvre? Manoeuvre = null, NimbleExploit? Exploit = null, OptionKind? Then = null, Illusion? Illusion = null);

    /// <summary>
    /// Runs through everything a creature could do with what it has left of its turn — every
    /// square it could stand on, and from each every blow, shot, Grab, Shove, Trip, Help, Harry,
    /// Heal or shield it could follow with — and scores each plan by what it does for the
    /// creature's side, less the blows it invites. Scores are wounds' worth (see <see cref="Assessor"/>).
    /// Roles weigh in: a Skirmisher would rather shoot from Short and keep clear, a Brute would
    /// rather close and swing, a Minion barely minds what it walks into.
    /// </summary>
    public sealed class TurnPlanner
    {
        private readonly Assessor assess;
        private readonly BrainSkill skill;
        private readonly SpellPlanner spells;

        public TurnPlanner(Assessor assess, BrainSkill skill)
        {
            this.assess = assess;
            this.skill = skill;
            spells = new SpellPlanner(assess);
        }

        private BattleView View => assess.View;

        /// <param name="hasMove">The turn's move is still to spend.</param>
        /// <param name="actions">Actions left.</param>
        /// <param name="real">Planning the creature's actual next step: options from where it stands are checked against the rules.</param>
        /// <param name="bonusAvailable">An attack this turn may spend a GM Opportunity on Advantage.</param>
        /// <param name="bankFull">The GM's bank is full, so an Opportunity unspent is one wasted.</param>
        /// <param name="stillToAct">Allies who'll act before this creature does again — the ones Help and a Trip set up.</param>
        /// <param name="interrupting">The declared action this turn is taken inside, for an Interrupt: what lands on its maker, or takes its target out of reach, is worth what it spares.</param>
        public List<Option> Options(Combatant actor, bool hasMove, int actions, bool real,
            bool bonusAvailable = false, bool bankFull = false, IReadOnlyCollection<Combatant>? stillToAct = null, Interruption? interrupting = null)
        {
            var options = OptionsHere(actor, hasMove, actions, real, bonusAvailable, bankFull, stillToAct);
            return interrupting is null ? options : options.Select(o => o with { Score = o.Score + Spared(actor, o, interrupting) }).ToList();
        }

        /// <summary>
        /// What an option taken inside an Interrupt spares of the declared action (Core Rules §3): its
        /// maker dropped first, all of it; landed on, the difference Disadvantage makes; its target —
        /// the one choosing — out of its reach, all of it, the action left with nothing to do.
        /// </summary>
        public double Spared(Combatant actor, Option option, Interruption interrupting)
        {
            var foe = interrupting.Actor;
            var victim = interrupting.Against is { Side: Side.Party } v ? v : actor;
            if (foe.IsOutOfFight || foe.IsBroken)
            {
                return 0;
            }

            var harm = foe.Attacks.Select(a => assess.Blow(foe, a, victim, a.DamageDieSides, victim.DodgeThreshold)).MaxBy(b => b.ExpectedDealt);
            var harmDis = foe.Attacks.Select(a => assess.Blow(foe, a, victim, a.DamageDieSides, victim.DodgeThreshold, disadvantage: true)).Max(b => b.ExpectedDealt);
            var perWound = assess.PerWound(victim);
            var whole = Math.Max(harm.ExpectedDealt, assess.Threat(foe)) * perWound;

            // The one it's aimed at, out of a blade's reach — nothing left to do.
            if (victim == actor && option.Square is { } square && !foe.HasRangedAttack && foe.Spells.Count == 0
                && !foe.CanReachBlock(square, actor.Across))
            {
                return whole;
            }

            var kind = option.Kind == OptionKind.Move ? option.Then : option.Kind;
            if (option.Target != foe || kind is not (OptionKind.Attack or OptionKind.Control or OptionKind.Cast or OptionKind.Exploit))
            {
                return 0;
            }

            var from = option.Square ?? actor.Position;
            var (land, kill) = kind switch
            {
                OptionKind.Control => (assess.ControlChance(actor, foe), 0.0),
                OptionKind.Cast => (0.6, 0.0),
                _ => assess.BestAttack(actor, from, foe).With is { } with
                    ? assess.Blow(actor, with, foe, with.DamageDieSides, foe.DodgeThreshold) is var b ? (b.Hit, b.Kill) : (0, 0)
                    : (0, 0),
            };

            var caught = foe.Role == AdversaryRole.Solo ? 0 : (harm.ExpectedDealt - harmDis) * perWound;
            return kill * whole + (land - kill) * caught;
        }

        private List<Option> OptionsHere(Combatant actor, bool hasMove, int actions, bool real,
            bool bonusAvailable, bool bankFull, IReadOnlyCollection<Combatant>? stillToAct)
        {
            var options = new List<Option>();

            // A standoff wears caution down: each quiet round, the blows a square invites matter less
            // than ending it — somebody has to close.
            var care = skill.Care * Assessor.RiskAppetite(actor) / (1 + View.QuietRounds);
            var here = actor.Position;

            // Its footing back first: no action does anything else until then.
            if (actor.NeedsFooting && actions > 0 && (!real || BattleView.RegainFootingRefusal(actor) is null))
            {
                options.Add(new Option(OptionKind.RegainFooting, 5, "regain my footing"));
                return options;
            }

            // On its feet first: Prone is Off-guard, and crawling is dear.
            if (actor.IsProne && hasMove && (!real || BattleView.StandUpRefusal(actor) is null))
            {
                options.Add(new Option(OptionKind.StandUp, 5, "Up off the ground"));
                return options;
            }

            var canMove = hasMove && Encounter.CanMoveItself(actor);
            var squares = new Dictionary<GridPosition, int> { [here] = 0 };
            if (canMove)
            {
                foreach (var (square, cost) in View.SquaresWithin(actor, 1))
                {
                    squares[square] = cost;
                }
            }

            foreach (var (square, _) in squares)
            {
                var moved = square != here;
                // What standing here sets up for next turn counts whatever is done this turn, so
                // acting is never scored below standing still.
                var standing = Position(actor, square) - Peril(actor, square) - care * assess.Exposure(actor, square)
                               - (moved ? care * assess.LeavingCost(actor, square) : 0)
                               + 0.5 * Approach(actor, square);
                var first = moved ? new Option(OptionKind.Move, 0, $"to {square}", Square: square) : null;

                if (actions > 0)
                {
                    foreach (var act in Actions(actor, square, real && !moved, bonusAvailable, bankFull, stillToAct, care))
                    {
                        var score = standing + act.Score;
                        options.Add(first is null ? act with { Score = score } : first with { Score = score, Why = $"{first.Why}, then {act.Why}", Target = act.Target, Then = act.Kind, Cast = act.Cast });
                    }
                }

                var waiting = standing;
                options.Add(first is null
                    ? new Option(OptionKind.End, waiting, "hold here")
                    : first with { Score = waiting, Why = $"{first.Why}, and wait" });
            }

            // Slip first, where the best way out of here draws blades worth more than the Stamina.
            if (skill.Uses(Trick.Exploits) && actor.IsNimble && canMove && actor.Stamina >= 2 && (!real || BattleView.ExploitRefusal(actor, NimbleExploit.Slip) is null))
            {
                var slipped = squares.Keys.Where(sq => sq != here)
                    .Select(sq => (Square: sq, Saved: care * assess.LeavingCost(actor, sq)))
                    .Where(x => x.Saved > 0)
                    .Select(x => x.Saved + options.Where(o => o.Square == x.Square).Select(o => o.Score).DefaultIfEmpty(double.MinValue).Max())
                    .DefaultIfEmpty(double.MinValue)
                    .Max();
                if (slipped > double.MinValue / 2)
                {
                    options.Add(new Option(OptionKind.Exploit, slipped - 0.6, "Slip, then go", Exploit: NimbleExploit.Slip));
                }
            }

            // A dash: the move and an action both spent on ground, nothing struck.
            var dashMoves = (hasMove ? 1 : 0) + actions;
            if (Encounter.CanMoveItself(actor) && dashMoves > (hasMove ? 1 : 0))
            {
                foreach (var (square, cost) in View.SquaresWithin(actor, dashMoves).Where(o => !squares.ContainsKey(o.Key)))
                {
                    var score = Position(actor, square) - Peril(actor, square) - care * assess.Exposure(actor, square)
                                - care * assess.LeavingCost(actor, square) + 0.5 * Approach(actor, square);
                    options.Add(new Option(OptionKind.Move, score, $"dash to {square} ({cost} moves)", Square: square));
                }
            }

            // Standing ground: +2 DT at the price of the move, and Disadvantage on the blow.
            if (skill.Uses(Trick.Brace) && hasMove && actions > 0 && !actor.HasMovedThisTurn && View.QuietRounds == 0
                && (!real || BattleView.StandGroundRefusal(actor, CombatAction.Dodge) is null))
            {
                var braced = Position(actor, here) - care * assess.Exposure(actor, here, extraDodge: 2);
                var blow = View.EnemiesOf(actor).Select(e => assess.BestAttack(actor, here, e).Worth * 0.55).DefaultIfEmpty(0).Max();
                options.Add(new Option(OptionKind.StandGround, braced + blow, "stand my ground (Dodge)", Action: CombatAction.Dodge));
            }

            return options;
        }

        /// <summary>Everything worth doing with an action, standing on <paramref name="at"/>.</summary>
        private IEnumerable<Option> Actions(Combatant actor, GridPosition at, bool checkRules, bool bonusAvailable, bool bankFull,
            IReadOnlyCollection<Combatant>? stillToAct, double care)
        {
            var enemies = View.EnemiesOf(actor).ToList();
            var spendCost = SpendCostFor(actor);
            var bestBlow = enemies.Select(e => assess.BestAttack(actor, at, e).Worth).DefaultIfEmpty(0).Max();
            spendCost += Assessor.SpendPrice(actor, bestBlow) - 0.5;

            // The defenceless are still to be finished — the default goal is to kill.
            var targets = actor.Side == Side.Adversaries
                ? enemies.Concat(View.PartyOnBoard.Where(c => c.IsDefenceless)).ToList()
                : enemies;

            foreach (var target in targets)
            {
                var (worth, with) = assess.BestAttack(actor, at, target);
                if (with is null || (checkRules && !View.CanAttack(actor, target)))
                {
                    continue;
                }

                if (target.IsDefenceless)
                {
                    // Finishing the fallen is the last thing on the list: a fraction of a blow at anyone still standing.
                    worth = Math.Min(worth, Math.Min(0.1 * assess.Value(target), 1.0));
                }

                var advantage = false;
                if (bonusAvailable)
                {
                    var boosted = assess.BestAttack(actor, at, target, advantage: true).Worth;
                    advantage = bankFull || boosted - worth >= 1.0;
                    worth = advantage ? boosted : worth;
                }

                worth += with.IsRanged
                    ? actor.Role == AdversaryRole.Skirmisher ? 0.3 : 0
                    : actor.Role == AdversaryRole.Brute ? 0.2 : 0;
                yield return new Option(OptionKind.Attack, worth, $"{with.Name} at {target.Name}", Target: target, With: with, WithAdvantage: advantage);

                if (!skill.UsesTricks || actor.Stamina < 2 || BattleView.TacticalSpendRefusal(actor) is not null || target.IsDefenceless)
                {
                    continue;
                }

                // A Nimble character's Exploits, bought first and spent on this blow.
                if (actor.IsNimble && skill.Uses(Trick.Exploits))
                {
                    var moved = actor.HasMovedSinceLastAttack || at != actor.Position;
                    if (moved && !actor.HoldsPrecise && (!checkRules || BattleView.ExploitRefusal(actor, NimbleExploit.Precise) is null))
                    {
                        var precise = assess.BestAttack(actor, at, target, precise: true).Worth;
                        yield return new Option(OptionKind.Exploit, precise - Assessor.SpendPrice(actor, worth), $"Precise, then {with.Name} at {target.Name}", Target: target, Exploit: NimbleExploit.Precise);
                    }

                    if (!actor.HasBeenAttackedSinceTurnStart && !actor.HoldsPatient && (!checkRules || BattleView.ExploitRefusal(actor, NimbleExploit.Patient) is null))
                    {
                        var patient = assess.BestAttack(actor, at, target, advantage: true).Worth;
                        yield return new Option(OptionKind.Exploit, patient - Assessor.SpendPrice(actor, worth), $"Patient, then {with.Name} at {target.Name}", Target: target, Exploit: NimbleExploit.Patient);
                    }
                }

                // A Martial character's manoeuvres, declared with the blow — 1 Stamina and a banked Opportunity.
                var permitted = skill.Uses(Trick.Manoeuvres) ? Encounter.ManoeuvresOf(actor) : [];
                if (with.IsRanged || permitted.Count == 0)
                {
                    continue;
                }

                if (permitted.Contains(WeaponManoeuvre.Cleave))
                {
                    var second = enemies.Where(e => e != target && BattleView.CanReachFrom(actor, at, e))
                        .Select(e => (Who: e, Worth: assess.BestAttack(actor, at, e).Worth))
                        .OrderByDescending(x => x.Worth).FirstOrDefault();
                    if (second.Who is not null)
                    {
                        var cleave = new Manoeuvre(WeaponManoeuvre.Cleave, second.Who);
                        if (!checkRules || BattleView.ManoeuvreRefusal(actor, target, cleave) is null)
                        {
                            yield return new Option(OptionKind.Attack, worth + 0.8 * second.Worth - spendCost, $"Cleave through {target.Name} into {second.Who.Name}",
                                Target: target, With: with, Manoeuvre: cleave);
                        }
                    }
                }

                if (permitted.Contains(WeaponManoeuvre.Topple) && !target.IsProne)
                {
                    var topple = new Manoeuvre(WeaponManoeuvre.Topple);
                    if (!checkRules || BattleView.ManoeuvreRefusal(actor, target, topple) is null)
                    {
                        var toppled = TripWorth(actor, target, stillToAct);
                        yield return new Option(OptionKind.Attack, worth + toppled - spendCost, $"Topple {target.Name}", Target: target, With: with, Manoeuvre: topple);
                    }
                }

                var takes = assess.ManoeuvreChance(actor, with, target, at, target.StabilityThreshold);
                foreach (var (kind, gain) in new[]
                {
                    (WeaponManoeuvre.Disarm, takes * DisarmWorth(actor, target, stillToAct)),
                    (WeaponManoeuvre.Sunder, SunderWorth(actor, with, target, at, takes)),
                    (WeaponManoeuvre.Ring, assess.ManoeuvreChance(actor, with, target, at, target.ResolveThreshold ?? 99) * RingWorth(target, stillToAct)),
                })
                {
                    if (!permitted.Contains(kind) || gain <= 0)
                    {
                        continue;
                    }

                    var manoeuvre = new Manoeuvre(kind);
                    if (!checkRules || BattleView.ManoeuvreRefusal(actor, target, manoeuvre) is null)
                    {
                        yield return new Option(OptionKind.Attack, worth + gain - spendCost, $"{kind} {target.Name}", Target: target, With: with, Manoeuvre: manoeuvre);
                    }
                }
            }

            // A weapon knocked to the ground: taking it up is an action, and ends being Off-guard.
            if (actor.DroppedWeapon is not null && actor.DroppedAt == at && (!checkRules || BattleView.TakeUpRefusal(actor) is null))
            {
                yield return new Option(OptionKind.TakeUpWeapon, 0.8 + 1.5 * assess.Threat(actor), $"take up the {actor.DroppedWeapon.Name}");
            }

            // Testing what it is taken in by: it can't know the thing is false, so it is worth no more
            // than a nagging doubt — a figure that stands too still, a wall it never saw built. Rarely
            // better than a blow; a loose chooser takes it now and then.
            if (at == actor.Position && actor.ActionsRemaining > 0)
            {
                foreach (var illusion in View.Illusions.Where(i => i.Form != IllusionForm.Reinforcements && View.Fools(i, actor)
                             && (!checkRules || View.SearchRefusal(actor, i) is null)
                             && (i.Subject == actor || i.Squares.Any(sq => actor.DistanceTo(sq) <= 8))))
                {
                    yield return new Option(OptionKind.Search, 0.12, $"test {illusion.Spell}", Illusion: illusion);
                }
            }

            foreach (var cast in spells.Options(actor, at, checkRules, care, skill.Uses(Trick.SpellTricks), stillToAct))
            {
                yield return cast;
            }

            if (actor.HeldBy is { } holder && at == actor.Position && (!checkRules || BattleView.BreakFreeRefusal(actor) is null))
            {
                var chance = Odds.Of(actor.UnarmedAttackModifier, holder.StabilityThreshold).Success;
                yield return new Option(OptionKind.BreakFree, chance * (actor.IsBroken ? assess.Value(actor) : 0.6), $"break {holder.Name}'s hold");
            }

            if (!skill.UsesTricks)
            {
                yield break;
            }

            foreach (var target in enemies.Where(e => skill.Uses(Trick.Grapple) && actor.DistanceFrom(at, e) <= actor.OwnReach))
            {
                foreach (var (control, worth) in new[]
                {
                    (ControlAction.Trip, TripWorth(actor, target, stillToAct)),
                    (ControlAction.Grab, GrabWorth(actor, target)),
                })
                {
                    if (worth <= 0 || !ControlLegal(actor, control, target, at, checkRules))
                    {
                        continue;
                    }

                    yield return new Option(OptionKind.Control, worth, $"{control} {target.Name}", Target: target, Control: control);
                }
            }

            // Help an ally beside me who acts before I do again, or Harry a foe beside me.
            foreach (var ally in (stillToAct ?? []).Where(a => skill.Uses(Trick.Aid) && a != actor && !a.IsHelped && actor.DistanceFrom(at, a) <= 1))
            {
                if (checkRules && BattleView.AidRefusal(actor, CombatAction.Help, ally) is not null)
                {
                    continue;
                }

                var gain = View.EnemiesOf(ally)
                    .Select(e => assess.BestAttack(ally, ally.Position, e, advantage: true).Worth - assess.BestAttack(ally, ally.Position, e).Worth)
                    .DefaultIfEmpty(0)
                    .Max();
                if (gain > 0)
                {
                    yield return new Option(OptionKind.Aid, gain * 0.8, $"Help {ally.Name}", Target: ally, Action: CombatAction.Help);
                }
            }

            foreach (var foe in enemies.Where(e => skill.Uses(Trick.Aid) && !e.IsHarried && actor.DistanceFrom(at, e) <= 1))
            {
                if (checkRules && BattleView.AidRefusal(actor, CombatAction.Harry, foe) is not null)
                {
                    continue;
                }

                yield return new Option(OptionKind.Aid, 0.3 * assess.Threat(foe) * assess.PerWound(actor), $"Harry {foe.Name}", Target: foe, Action: CombatAction.Harry);
            }

            // Heal: the dying first — a body back in the fight, and a death put off.
            foreach (var hurt in View.PartyOnBoard.Concat(View.Adversaries).Where(c => skill.Uses(Trick.Tend) && c.Side == actor.Side && c.CanBeTended(WoundType.Body)))
            {
                if (hurt != actor && actor.DistanceFrom(at, hurt) > 1)
                {
                    continue;
                }

                if (checkRules && BattleView.TendRefusal(actor, CombatAction.Heal, hurt) is not null)
                {
                    continue;
                }

                var worth = hurt.IsDefenceless ? 2 + 0.9 * assess.Value(hurt) : assess.MendWorth(hurt, 1);
                yield return new Option(OptionKind.Tend, worth, $"Heal {hurt.Name}", Target: hurt, Action: CombatAction.Heal);
            }

            if (skill.Uses(Trick.Shield) && actor.InHandShield is { } shield && actor.CoveredBy.All(c => c.Bearer != actor) && (!checkRules || BattleView.ShieldRefusal(actor) is null))
            {
                var bonus = Rules.Calculation.InventoryCalculator.ShieldDodgeThresholdBonus(shield.Weight);
                var spared = care * (assess.Exposure(actor, at) - assess.Exposure(actor, at, extraDodge: bonus));
                yield return new Option(OptionKind.RaiseShield, spared, "raise my shield");
            }
        }

        /// <summary>
        /// A Trip is worth what it hands everybody else: −2 DT to swing at for each ally who'll act
        /// before the target stands, and the target's own blows at Disadvantage meanwhile. Never at
        /// a Minion — any blow drops it anyway.
        /// </summary>
        private double TripWorth(Combatant actor, Combatant target, IReadOnlyCollection<Combatant>? stillToAct)
        {
            if (target.IsProne || target.IsMinion)
            {
                return 0;
            }

            var helpers = (stillToAct ?? []).Count(a => a != actor && (View.PlanAttack(a, target).CanAttack || View.StrikeSquare(a, target) is not null));
            return assess.ControlChance(actor, target) * (0.12 * assess.Value(target) * helpers + 0.15 * assess.Threat(target) * assess.PerWound(actor));
        }

        /// <summary>
        /// What a manoeuvre's Stamina costs: the Stamina, and for the party the Opportunity it banks
        /// the GM — an adversary's spend hands nobody anything (GM Reference §9).
        /// </summary>
        private double SpendCostFor(Combatant actor) => actor.Side == Side.Party ? 1.0 : 0.6;

        /// <summary>
        /// A Disarm: the target Off-guard — easier to hit for everyone still to act, worse at hitting
        /// back — until it spends an action taking the weapon up, the turn's harm it doesn't do.
        /// </summary>
        private double DisarmWorth(Combatant actor, Combatant target, IReadOnlyCollection<Combatant>? stillToAct)
        {
            if (!target.WieldsWeapon || target.IsMinion || target.IsOffGuard)
            {
                return 0;
            }

            var helpers = (stillToAct ?? []).Count(a => a != actor && (View.PlanAttack(a, target).CanAttack || View.StrikeSquare(a, target) is not null));
            return 0.08 * assess.Value(target) * helpers + 0.6 * assess.Threat(target) * assess.PerWound(actor);
        }

        /// <summary>
        /// A Sunder: on a hit, a type of armour gone for the rest of the fight — a wound more through
        /// on every blow this side lands on it from here; on a miss only the raised shield caused, the
        /// shield split, its Dodge Threshold gone for good.
        /// </summary>
        private double SunderWorth(Combatant actor, AttackProfile with, Combatant target, GridPosition at, double takes)
        {
            if (target.HasBeenSundered || target.IsMinion)
            {
                return 0;
            }

            var attackers = View.EnemiesOf(target).Count(e => !e.IsOutOfFight);
            var armourGain = target.Armour == ArmourWeight.None ? 0
                : takes * Math.Min(Assessor.Horizon, target.Body / 4.0 + 1) * attackers * 0.5 * assess.PerWound(target);
            var shieldGain = target.ShieldBonus > 0
                ? assess.ShieldBreakChance(actor, with, target, at) * target.ShieldBonus * 0.06 * assess.Value(target) * attackers
                : 0;
            return armourGain + shieldGain;
        }

        /// <summary>A Ring: no panic dodge, no shield raised, no extra action from the target until its next turn — worth the blows still to come at it.</summary>
        private double RingWorth(Combatant target, IReadOnlyCollection<Combatant>? stillToAct)
        {
            if (target.IsRinged || target.ResolveThreshold is null || target.Stamina <= 1 || BattleView.TacticalSpendRefusal(target) is not null)
            {
                return 0;
            }

            var blows = 1 + (stillToAct ?? []).Count(a => a.Side != target.Side);
            return 0.05 * assess.Value(target) * blows;
        }

        /// <summary>A Grab is worth most against something running, or that mustn't get away; otherwise a little, for pinning it.</summary>
        private double GrabWorth(Combatant actor, Combatant target)
        {
            if (target.HeldBy is not null)
            {
                return 0;
            }

            var stake = target.IsBroken ? assess.Value(target) * (View.MustNotEscape(target) ? 1.5 : 1) : 0.1 * assess.Value(target);
            return assess.ControlChance(actor, target) * stake;
        }

        private static bool ControlLegal(Combatant actor, ControlAction control, Combatant target, GridPosition at, bool checkRules)
        {
            if (checkRules)
            {
                return BattleView.ControlRefusal(actor, control, target) is null;
            }

            return !target.IsSwarm && !actor.IsSwarm && actor.DistanceFrom(at, target) <= actor.OwnReach
                   && (control != ControlAction.Grab || (actor.HasFreeHand && actor.Holding is null && target.HeldBy is null))
                   && (control != ControlAction.Trip || !target.IsProne);
        }

        /// <summary>
        /// What the ground on the way to <paramref name="square"/> risks: a hazard there, by the
        /// chance of failing against it; deep water, by the chance of a wasted move.
        /// </summary>
        private double Peril(Combatant actor, GridPosition square)
        {
            double peril = 0;
            if (View.Setting.HazardAt(square) is { } hazard && hazard.Avoid.Count > 0)
            {
                var best = hazard.Avoid.Max(c => Odds.Of(actor.SkillBonus(c.Skill), c.Difficulty).Success);
                peril += (1 - best) * 1.5;
            }

            if (square != actor.Position && View.PlanMove(actor, square) is { SwimsDeepWater: true })
            {
                peril += (1 - Odds.Of(actor.SkillBonus("Athletics"), View.Setting.SwimDifficulty).Success) * 1.0;
            }

            return peril;
        }

        /// <summary>
        /// Where a creature's Role would rather stand: a Skirmisher with a shot at Short range and
        /// out of reach, a Brute toe to toe with somebody.
        /// </summary>
        private double Position(Combatant actor, GridPosition square)
        {
            var nearest = View.EnemiesOf(actor).Select(e => actor.DistanceFrom(square, e)).DefaultIfEmpty(int.MaxValue).Min();

            // Out past Long of every enemy is out of the fight: for a creature that means to win,
            // that's walking away from it — the party getting clear, or the GM's quarry escaping.
            if (!actor.IsBroken && nearest > 16 && View.EnemiesOf(actor).Any(e => !e.IsBroken))
            {
                return -3;
            }

            // Everybody left on the board is running and nobody needs catching: give them the
            // room to go — the fight ends once they're beyond Long.
            if (actor.Side == Side.Party && View.EnemiesOf(actor).All(e => e.IsBroken && !View.MustNotEscape(e)))
            {
                return 0.1 * Math.Min(nearest, 20);
            }

            // A caster fights like a Skirmisher: from Short, out of reach.
            var keepsDistance = actor.Role == AdversaryRole.Skirmisher && actor.HasRangedAttack
                                || actor.Spells.Any(s => s.SpellType == SpellType.Harm);
            return actor.Role switch
            {
                _ when keepsDistance => nearest <= 1 ? -0.6 : nearest <= 4 ? 0.4 : nearest <= 8 ? 0.1 : -0.1,
                AdversaryRole.Brute => nearest <= 1 ? 0.3 : 0,
                _ => 0,
            };
        }

        /// <summary>
        /// What standing on <paramref name="square"/> sets up for next turn: the best blow within a
        /// move's reach of it, and a pull towards the nearest enemy so nobody idles.
        /// </summary>
        private double Approach(Combatant actor, GridPosition square)
        {
            var stride = View.SquaresFor(actor.Speed, 1);
            double best = 0;
            foreach (var enemy in Pursued(actor))
            {
                var distance = actor.DistanceFrom(square, enemy);
                foreach (var attack in actor.Attacks)
                {
                    var reachable = attack.IsRanged
                        ? View.PlanAttackWith(actor, attack, square, enemy).CanAttack
                        : distance <= attack.Reach + stride;
                    if (!reachable)
                    {
                        continue;
                    }

                    best = Math.Max(best, assess.Worth(assess.Blow(actor, attack, enemy, attack.DamageDieSides, enemy.DodgeThreshold), enemy));
                }
            }

            best = Math.Max(best, spells.BestHarmFrom(actor, square));
            var nearest = Pursued(actor).Select(e => actor.DistanceFrom(square, e)).DefaultIfEmpty(0).Min();
            return best - 0.05 * nearest;
        }

        /// <summary>
        /// The enemies worth closing on: all of them, but for a creature that has broken and is
        /// running, which is only worth the chase if the fight's objectives say it mustn't get away —
        /// let the rest go, and the fight ends when they're beyond Long.
        /// </summary>
        private IEnumerable<Combatant> Pursued(Combatant actor) =>
            View.EnemiesOf(actor).Where(e => !e.IsBroken || View.MustNotEscape(e));
    }

    /// <summary>How a brain picks from what it has scored: the best, or — further down the skill dial — loosely among the best.</summary>
    public static class Choose
    {
        public static T Pick<T>(IReadOnlyList<T> options, Func<T, double> score, BrainSkill skill, Random random)
        {
            if (options.Count == 0)
            {
                throw new ArgumentException("Nothing to choose from.", nameof(options));
            }

            if (skill.Temperature <= 0 || options.Count == 1)
            {
                return options.MaxBy(score)!;
            }

            // Scored once each, best first; the loose pick is made among the top few only.
            var ranked = options.Select(o => (Option: o, Score: score(o))).OrderByDescending(x => x.Score).Take(skill.Breadth).ToList();
            var top = ranked[0].Score;
            var weights = ranked.Select(x => Math.Exp((x.Score - top) / skill.Temperature)).ToList();
            var roll = random.NextDouble() * weights.Sum();
            for (var i = 0; i < ranked.Count; i++)
            {
                roll -= weights[i];
                if (roll <= 0)
                {
                    return ranked[i].Option;
                }
            }

            return ranked[0].Option;
        }
    }
}
