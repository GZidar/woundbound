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
        Throw,
        Mount,
        DrawWeapon,
        PickUpWeapon,
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
        CombatAction? Action = null, ControlAction? Control = null, bool WithBonus = false, SpellCast? Cast = null,
        Manoeuvre? Manoeuvre = null, NimbleExploit? Exploit = null, OptionKind? Then = null, Illusion? Illusion = null,
        Rules.Models.WeaponDefinition? Weapon = null, GroundWeapon? Lying = null);

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
            var options = SoloAttacks(actor, OptionsHere(actor, hasMove, actions, real, bonusAvailable, bankFull, stillToAct), real, bonusAvailable, bankFull);
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

            // Back in the saddle: the mount's Speed and its hooves again, for the move.
            if (actor.Steed is { Body: > 0 } && !actor.IsRiding && hasMove && (!real || View.MountRefusal(actor) is null)
                && actor.DistanceTo(actor.Steed.Position) <= 1)
            {
                options.Add(new Option(OptionKind.Mount, 1.0 + 0.5 * assess.Threat(actor), "back into the saddle"));
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
                // Leaving somebody's reach invites their reaction: wounds, and at T1 often the end of
                // it — in which case what it moved to do never happens. Staying puts up with what the
                // enemy does on its own turn instead; Exposure prices that, here and there alike.
                var (leaving, dropped) = moved ? assess.LeavingRisk(actor, square) : (0.0, 0.0);
                // What standing here sets up for next turn counts whatever is done this turn, so
                // acting is never scored below standing still.
                var standing = Position(actor, square) - Peril(actor, square) - care * assess.Exposure(actor, square)
                               - care * leaving
                               + 0.5 * Approach(actor, square);
                var first = moved ? new Option(OptionKind.Move, 0, $"to {square}", Square: square) : null;

                if (actions > 0)
                {
                    foreach (var act in Actions(actor, square, real && !moved, bonusAvailable, bankFull, stillToAct, care))
                    {
                        var score = standing + (1 - dropped) * act.Score;
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

            // Harry the one beside it, then leave its reach and buy the action to strike elsewhere.
            if (skill.UsesTricks && canMove && actions == 1 && !actor.IsSolo && actor.Stamina >= 2
                && BattleView.TacticalSpendRefusal(actor) is null)
            {
                foreach (var foe in View.EnemiesOf(actor).Where(f => f.CanReach(actor) && actor.DistanceTo(f) <= 1 && !f.IsOutOfFight))
                {
                    if ((!real || BattleView.AidRefusal(actor, CombatAction.Harry, foe) is null)
                        && HarryAndGo(actor, foe, squares, real, stillToAct, care) is { } plan)
                    {
                        options.Add(plan);
                    }
                }
            }

            // Holding the ground it stands on: what its being here keeps the other side off — the
            // softer ones behind it, out of reach because it is in the way. Everything that leaves it
            // standing here keeps that; walking away gives it up (the user's direction, 2026-11-02).
            var screen = Screening(actor);
            if (screen > 0)
            {
                for (var i = 0; i < options.Count; i++)
                {
                    var stays = options[i].Kind != OptionKind.Move && (options[i].Square is null || options[i].Square == here);
                    if (stays)
                    {
                        options[i] = options[i] with { Score = options[i].Score + screen };
                    }
                }
            }

            // Standing ground: +2 DT at the price of the move, and Disadvantage on the blow.
            if (skill.Uses(Trick.Brace) && hasMove && actions > 0 && !actor.HasMovedThisTurn && View.QuietRounds == 0
                && (!real || BattleView.StandGroundRefusal(actor, CombatAction.Dodge) is null))
            {
                var braced = Position(actor, here) - care * assess.Exposure(actor, here, extraDodge: 2);
                var blow = View.EnemiesOf(actor).Select(e => assess.BestAttack(actor, here, e).Worth * 0.55).DefaultIfEmpty(0).Max();
                options.Add(new Option(OptionKind.StandGround, braced + blow + screen, "stand my ground (Dodge)", Action: CombatAction.Dodge));
            }

            return options;
        }

        /// <summary>Everything worth doing with an action, standing on <paramref name="at"/>.</summary>
        /// <summary>
        /// A Solo's turns carry one attack a round between them (GM Reference §3): once it has made
        /// it, a blow or a Harm costs a Stamina and its one tactical spend — or can't be had.
        /// </summary>
        private static List<Option> SoloAttacks(Combatant actor, List<Option> options, bool real, bool bonusAvailable = false, bool bankFull = false)
        {
            if (!actor.IsSolo || actor.AttacksThisRound == 0)
            {
                return options;
            }

            static bool Attacks(Option o) =>
                (o.Kind == OptionKind.Attack || o.Then == OptionKind.Attack)
                || ((o.Kind == OptionKind.Cast || o.Then == OptionKind.Cast) && o.Cast?.Spell.SpellType == SpellType.Harm);

            // The last Stamina is never spent on it: a creature starting a turn on 0 falls unconscious.
            var refused = Encounter.SoloAttackRefusal(actor) is not null || (actor.Side == Side.Adversaries && actor.Stamina < 2);

            // A Solo whose Opportunity bonus is an extra attack (the Ashen Warden's) buys its second
            // with the Opportunity instead, where that is cheaper than the Stamina, or the only way.
            if (bonusAvailable && Encounter.BonusIsExtraAttack(actor))
            {
                var price = bankFull ? 0 : BonusPrice;
                if (refused || price <= Assessor.SpendPrice(actor, 0) + 0.3)
                {
                    return options
                        .Select(o => Attacks(o) ? o with { Score = o.Score - price, WithBonus = o.Kind == OptionKind.Attack } : o)
                        .ToList();
                }
            }

            return options
                .Where(o => !(Attacks(o) && refused))
                .Select(o => Attacks(o) ? o with { Score = o.Score - Assessor.SpendPrice(actor, o.Score) - 0.3 } : o)
                .ToList();
        }

        /// <summary>
        /// An area attack from <paramref name="at"/>, weighed by everybody it would catch: each
        /// enemy's blow, and each ally's against it (GM Reference §3, "An area attack is one attack").
        /// </summary>
        private IEnumerable<Option> AreaAttacks(Combatant actor, GridPosition at, bool checkRules)
        {
            foreach (var line in actor.Attacks.Where(a => a.AreaSquares is not null))
            {
                if (line.EveryOtherRound && actor.LastAreaAttackRound == View.Round - 1)
                {
                    continue;
                }

                var squares = line.AreaSquares!.Value;
                var caught = View.Combatants.Where(c => c != actor && c.OccupiesSquare && !c.IsOutOfFight && actor.DistanceFrom(at, c) <= squares)
                    .Concat(View.Steeds.Where(m => m != actor.Steed && actor.DistanceFrom(at, m) <= squares)).ToList();
                var target = caught.FirstOrDefault(c => c.Side != actor.Side && !c.IsSteed);
                if (target is null || (checkRules && !View.CanAttack(actor, target)))
                {
                    continue;
                }

                double worth = 0;
                foreach (var victim in caught)
                {
                    var blow = assess.Blow(actor, line, victim, line.DamageDieSides, victim.DodgeThreshold + View.VisibilityDodgeModifier(victim, seer: actor));
                    var value = blow.ExpectedDealt * assess.PerWound(victim) + 0.3 * blow.Kill * assess.Value(victim);
                    worth += victim.Side == actor.Side ? -1.5 * value : value;
                }

                if (worth > 0)
                {
                    yield return new Option(OptionKind.Attack, worth, $"{line.Name} over {caught.Count(c => c.Side != actor.Side)}", Target: target, With: line);
                }
            }
        }

        /// <summary>
        /// What an Opportunity must buy to be spent on a bonus rather than kept for an extra turn: an
        /// extra turn is worth about a blow, a bonus on a turn being taken anyway only what it adds
        /// (GM Reference §9, "On Opportunity"). Nothing, when the bank is full and it would be lost.
        /// </summary>
        public const double BonusPrice = 1.0;

        /// <summary>
        /// What the off-hand strike an Attack action allows is worth on top of it (Core Rules §9, "Two
        /// Weapons"): the best off-hand blow from <paramref name="at"/>, less what it costs — a
        /// character's Stamina, taken only with one to spare; an adversary's Opportunity, on a turn that
        /// can spend one on its off-hand bonus (it attacks once a round otherwise — the user's rulings,
        /// 2026-11-02 and 2026-11-03).
        /// </summary>
        private double OffHandFollowUp(Combatant actor, GridPosition at, bool bonusAvailable = false, bool bankFull = false)
        {
            if (actor.OffHandAttack is not { } offHand || !skill.UsesTricks)
            {
                return 0;
            }

            var price = actor.Side == Side.Party
                ? actor.Stamina < 2 ? double.MaxValue : 0.3
                : bonusAvailable && actor.OpportunityBonus.Kind == OpportunityBonusKind.OffHand ? bankFull ? 0 : BonusPrice : double.MaxValue;
            if (price == double.MaxValue)
            {
                return 0;
            }

            var best = View.EnemiesOf(actor).Where(e => !e.IsOutOfFight)
                .Select(e => (Who: e, Plan: View.PlanAttackWith(actor, offHand, at, e)))
                .Where(x => x.Plan.CanAttack)
                .Select(x => assess.Worth(assess.Blow(actor, offHand, x.Who, x.Plan.DamageDieSides, x.Plan.DodgeThreshold), x.Who))
                .DefaultIfEmpty(0).Max();
            return Math.Max(0, best - price);
        }

        /// <summary>
        /// The best blow <paramref name="actor"/> could land on <paramref name="target"/> with its
        /// Opportunity bonus spent on it (GM Reference §9): Advantage, Direct damage, or Precise.
        /// </summary>
        private double BonusBlow(Combatant actor, GridPosition at, Combatant target) => actor.OpportunityBonus.Kind switch
        {
            OpportunityBonusKind.Direct => assess.BestAttack(actor, at, target, precise: true).Worth,
            OpportunityBonusKind.Precise => assess.BestAttack(actor, at, target, precise: true,
                extraDamage: Rules.Calculation.AdversarySkillCalculator.Baseline(actor.Tier)).Worth,
            OpportunityBonusKind.Advantage or OpportunityBonusKind.ExtraAttack => assess.BestAttack(actor, at, target, advantage: true).Worth,
            _ => 0,
        };

        private IEnumerable<Option> Actions(Combatant actor, GridPosition at, bool checkRules, bool bonusAvailable, bool bankFull,
            IReadOnlyCollection<Combatant>? stillToAct, double care)
        {
            foreach (var area in AreaAttacks(actor, at, checkRules))
            {
                yield return area;
            }

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
                if (bonusAvailable && !Encounter.BonusIsExtraAttack(actor) && Encounter.BonusAttackRefusal(actor, manoeuvre: null) is null)
                {
                    var boosted = BonusBlow(actor, at, target);
                    advantage = boosted > worth && (bankFull || boosted - worth >= BonusPrice);
                    worth = advantage ? boosted : worth;
                }

                worth += with.IsRanged
                    ? actor.Role == AdversaryRole.Skirmisher ? 0.3 : 0
                    : actor.Role == AdversaryRole.Brute ? 0.2 : 0;
                worth += OffHandFollowUp(actor, at, bonusAvailable, bankFull);
                yield return new Option(OptionKind.Attack, worth, $"{with.Name} at {target.Name}", Target: target, With: with, WithBonus: advantage);

                // A Cleave the Opportunity pays for (GM Reference §9): two characters within reach of the same blade.
                if (bonusAvailable && actor.OpportunityBonus.Kind == OpportunityBonusKind.Cleave && !with.IsRanged && !target.IsDefenceless)
                {
                    var second = enemies.Where(e => e != target && !e.IsOutOfFight && BattleView.CanReachFrom(actor, at, e))
                        .Select(e => (Who: e, Worth: assess.BestAttack(actor, at, e).Worth))
                        .OrderByDescending(x => x.Worth).FirstOrDefault();
                    var cleave = second.Who is null ? null : new Manoeuvre(WeaponManoeuvre.Cleave, second.Who);
                    if (cleave is not null && (!checkRules || BattleView.ManoeuvreRefusal(actor, target, cleave, byOpportunity: true) is null)
                        && (bankFull || 0.8 * second.Worth >= BonusPrice))
                    {
                        yield return new Option(OptionKind.Attack, worth + 0.8 * second.Worth,
                            $"Cleave through {target.Name} into {second.Who!.Name}, on the Opportunity", Target: target, With: with, Manoeuvre: cleave, WithBonus: true);
                    }
                }

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

            // Throwing what can be thrown, past Close: a shot by its own Weight — and the weapon gone
            // from the hand, which costs most where it leaves nothing else to fight with.
            if (Encounter.ThrowOf(actor) is { } thrown)
            {
                var emptied = actor.OffHandWeapon is null && actor.ReadyWeapon is null;
                var price = emptied ? 0.8 + 0.5 * assess.Threat(actor) : 0.3;
                foreach (var target in enemies.Where(e => !e.IsDefenceless && actor.DistanceFrom(at, e) > 1))
                {
                    var plan = View.PlanAttackWith(actor, thrown, at, target);
                    if (!plan.CanAttack || (checkRules && View.ThrowRefusal(actor, target) is not null))
                    {
                        continue;
                    }

                    var worth = assess.Worth(assess.Blow(actor, thrown, target, plan.DamageDieSides, plan.DodgeThreshold), target) - price;
                    yield return new Option(OptionKind.Throw, worth, $"throw the {thrown.Name} at {target.Name}", Target: target);
                }
            }

            // Empty-handed with a weapon in the pack: an action to draw it, as to take one up.
            if (actor.Build is not null && actor.InHandWeapon is null && at == actor.Position)
            {
                if (Encounter.PackWeapons(actor).FirstOrDefault(w => Encounter.DrawRefusal(actor, w) is null) is { } spare)
                {
                    yield return new Option(OptionKind.DrawWeapon, 0.8 + 1.5 * assess.Threat(actor), $"draw the {spare.Name} from the pack", Weapon: spare);
                }

                // Or one lying underfoot — a thrown one come back to hand, or anybody's.
                if (View.GroundWeapons.FirstOrDefault(g => actor.Covers(g.At) && (!checkRules || View.PickUpRefusal(actor, g) is null)) is { } lying)
                {
                    yield return new Option(OptionKind.PickUpWeapon, 0.8 + 1.5 * assess.Threat(actor), $"pick up the {lying.Weapon.Name}", Lying: lying);
                }
            }

            // An enemy's mount: the blow it takes, and the chance of bringing its rider down with it —
            // Prone in the next square, and on foot from then on (Core Rules §9, "Mounts").
            foreach (var steed in View.EnemySteeds(actor))
            {
                if (checkRules && !View.CanAttack(actor, steed))
                {
                    continue;
                }

                foreach (var line in actor.Attacks.Where(a => a.AreaSquares is null))
                {
                    var plan = View.PlanAttackWith(actor, line, at, steed);
                    if (!plan.CanAttack)
                    {
                        continue;
                    }

                    var blow = assess.Blow(actor, line, steed, plan.DamageDieSides, plan.DodgeThreshold);
                    var unhorsed = steed.Rider is { IsRiding: true } rider ? 0.4 * assess.Value(rider) : 0.2;
                    yield return new Option(OptionKind.Attack, blow.ExpectedDealt * 0.2 + blow.Kill * unhorsed,
                        $"{line.Name} at {steed.Name}", Target: steed, With: line);
                    break;
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

            foreach (var cast in spells.Options(actor, at, checkRules, care, skill.Uses(Trick.SpellTricks), stillToAct, bonusAvailable, bankFull))
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

                yield return new Option(OptionKind.Aid, HarryWorth(actor, foe, care), $"Harry {foe.Name}", Target: foe, Action: CombatAction.Harry);
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

                var worth = hurt.IsDefenceless ? HealDyingWorth(hurt) : assess.MendWorth(hurt, 1);
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
        /// <summary>
        /// What <paramref name="actor"/>'s standing where it is keeps its enemies off: for each enemy
        /// that fights hand to hand, the best blow it could land this turn on one of the actor's
        /// allies were the actor not in the way, less the best it can land with the actor there, the
        /// actor included. On open ground that is nothing — they walk round; in a corridor or a
        /// doorway it is the whole of what is behind. Discounted, since the enemy may not come that way.
        /// </summary>
        internal double Screening(Combatant actor)
        {
            var allies = View.AlliesOf(actor).Where(a => a != actor && !a.IsPhantom).ToList();
            if (allies.Count == 0 || !actor.HoldsGround)
            {
                return 0;
            }

            double denied = 0;
            foreach (var enemy in View.EnemiesOf(actor).Where(e => !e.IsPhantom && !e.IsBroken && !e.HasRangedAttack && !e.IsSwarm))
            {
                var with = assess.BestAttack(enemy, enemy.Position, actor).With ?? enemy.MeleeAttack;
                double Worth(Combatant victim) =>
                    assess.Blow(enemy, with, victim, with.DamageDieSides, victim.DodgeThreshold + View.VisibilityDodgeModifier(victim)).ExpectedDealt
                    * assess.PerWound(victim);
                double BestFrom(IEnumerable<GridPosition> squares, IEnumerable<Combatant> victims)
                {
                    var spots = squares.Append(enemy.Position).ToList();
                    return victims.Where(v => spots.Any(sq => enemy.CanReachFrom(sq, v))).Select(Worth).DefaultIfEmpty(0).Max();
                }

                var withMe = BestFrom(View.SquaresWithin(enemy, 1).Keys, allies.Append(actor));
                var withoutMe = BestFrom(View.SquaresWithinIfAbsent(enemy, actor, 1).Keys, allies);
                denied += Math.Max(0, withoutMe - withMe);
            }

            return 0.7 * denied;
        }

        /// <summary>
        /// A Harry is worth what Disadvantage takes off the foe's next roll — its next blow, at
        /// whichever of ours it can reach, each as likely as the next, as Exposure shares a blow out.
        /// What it spares the harrier itself is weighed at the harrier's care, as its own skin is
        /// everywhere else: a Brute shrugs off what a Skirmisher would pay to avoid. Nothing if the
        /// foe can reach nobody to use the roll on. On its own it seldom beats a blow of one's own;
        /// where it pays is ahead of leaving that foe's reach (see <see cref="HarryAndGo"/>).
        /// </summary>
        private double HarryWorth(Combatant actor, Combatant foe, double care)
        {
            var stride = View.SquaresFor(foe.Speed, 1);
            var spared = new List<double>();
            foreach (var victim in View.AlliesOf(actor).Where(a => !a.IsOutOfFight || a.IsDefenceless))
            {
                double best = -1;
                foreach (var attack in foe.Attacks.Where(a => a.AreaSquares is null))
                {
                    if (!attack.IsRanged && foe.DistanceTo(victim) > attack.Reach + stride)
                    {
                        continue;
                    }

                    var dodge = victim.DodgeThreshold + View.VisibilityDodgeModifier(victim, seer: foe);
                    var plain = assess.Blow(foe, attack, victim, attack.DamageDieSides, dodge);
                    var harried = assess.Blow(foe, attack, victim, attack.DamageDieSides, dodge, disadvantage: true);
                    best = Math.Max(best, assess.Worth(plain, victim) - assess.Worth(harried, victim));
                }

                if (best >= 0)
                {
                    spared.Add(victim == actor ? care * best : best);
                }
            }

            return spared.Count == 0 ? 0 : spared.Average();
        }

        /// <summary>
        /// Harry the foe beside it, step out of its reach — its reaction now rolled at Disadvantage —
        /// and buy an action with Stamina to strike somebody worth more (the user's direction,
        /// 2026-11-02). Scored as that whole plan: the Harry, the move with the cheaper reaction, the
        /// blow if it gets there, less the extra action's price. Null where there's no such plan.
        /// </summary>
        private Option? HarryAndGo(Combatant actor, Combatant foe, IReadOnlyDictionary<GridPosition, int> squares, bool real,
            IReadOnlyCollection<Combatant>? stillToAct, double care)
        {
            var best = double.MinValue;
            string? why = null;
            foreach (var square in squares.Keys.Where(sq => sq != actor.Position && !foe.CanReachBlock(sq, actor.Across)))
            {
                var (leaving, dropped) = assess.LeavingRisk(actor, square, harried: foe);
                var strike = Actions(actor, square, checkRules: false, bonusAvailable: false, bankFull: false, stillToAct, care)
                    .Where(o => o.Kind is OptionKind.Attack or OptionKind.Cast && o.Target != foe)
                    .MaxBy(o => o.Score);
                if (strike is null)
                {
                    continue;
                }

                var score = Position(actor, square) - Peril(actor, square) - care * assess.Exposure(actor, square) - care * leaving
                            + 0.5 * Approach(actor, square) + (1 - dropped) * strike.Score;
                if (score > best)
                {
                    (best, why) = (score, $"Harry {foe.Name}, slip past to {square}, then {strike.Why}");
                }
            }

            return why is null ? null : new Option(OptionKind.Aid, best - ExtraActionPrice(actor), why, Target: foe, Action: CombatAction.Harry);
        }

        /// <summary>
        /// What 1 Stamina for an extra action costs: for a character, the Stamina and the banked
        /// Opportunity it hands the GM; for an adversary, which hands nobody anything, the Stamina and
        /// the reaction it can no longer make this round.
        /// </summary>
        public double ExtraActionPrice(Combatant actor) =>
            actor.Side == Side.Party
                ? 1.0 + 0.8 * View.EnemiesOf(actor).Select(assess.Threat).DefaultIfEmpty(0).Average() * assess.PerWound(actor)
                : Assessor.SpendPrice(actor, 1.0);

        /// <summary>
        /// A Heal on somebody dying puts them back on 1 Body: worth a death put off, and their place
        /// in the fight — but only as far as they're likely to keep it until they act. Brought back
        /// beside the thing that dropped them, they go straight down again.
        /// </summary>
        private double HealDyingWorth(Combatant hurt)
        {
            var incoming = 1.5 * assess.Exposure(hurt, hurt.Position) / Math.Max(1e-6, assess.PerWound(hurt));
            var stays = Math.Max(0.15, 1 - Math.Min(1, incoming));
            return 1.5 + 0.9 * assess.Value(hurt) * stays;
        }

        private double TripWorth(Combatant actor, Combatant target, IReadOnlyCollection<Combatant>? stillToAct)
        {
            if (target.IsProne || target.IsMinion)
            {
                return 0;
            }

            // Prone is Off-guard (Core Rules §9): two off its Dodge for every blow still to come at it
            // from those of ours yet to act, and Disadvantage on its own next attack — a Harry's
            // worth. Priced in the wounds each blow puts on it, as a blow itself is, so a target with
            // a great deal of Body doesn't make the setting-up worth more than the hitting.
            double gain = 0;
            foreach (var helper in (stillToAct ?? []).Where(a => a != actor && !a.IsOutOfFight))
            {
                // Their blow from where they stand, or their blade if they can step in to strike.
                var with = assess.BestAttack(helper, helper.Position, target).With
                           ?? (View.StrikeSquare(helper, target) is not null ? helper.MeleeAttack : null);
                if (with is null)
                {
                    continue;
                }

                var dodge = target.DodgeThreshold + View.VisibilityDodgeModifier(target);
                var now = assess.Blow(helper, with, target, with.DamageDieSides, dodge);
                var down = assess.Blow(helper, with, target, with.DamageDieSides, dodge - 2);
                gain += (down.ExpectedDealt - now.ExpectedDealt) * assess.PerWound(target) + 0.3 * (down.Kill - now.Kill) * assess.Value(target);
            }

            return assess.ControlChance(actor, target, topples: true) * (gain + HarryWorth(actor, target, care: 1));
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

            // Everybody left on the board is running and nobody needs catching — or the chase has
            // gone nowhere for long enough: give them the room to go — the fight ends once they're
            // beyond Long.
            if (actor.Side == Side.Party && View.EnemiesOf(actor).All(e => e.IsBroken
                    && (!View.MustNotEscape(e) || View.QuietRounds >= GiveUpTheChaseAfter)))
            {
                // Stand and watch them go. Backing off is no better: a runner fleeing the rest of
                // the party may be running the very way you back off, and stay within Long of you
                // for ever.
                return square == actor.Position ? 0.5 : 0;
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

            best = Math.Max(best, spells.BestHarmFrom(actor, square, Pursued(actor)));
            var nearest = Pursued(actor).Select(e => actor.DistanceFrom(square, e)).DefaultIfEmpty(0).Min();
            return best - 0.05 * nearest;
        }

        /// <summary>
        /// The enemies worth closing on: all of them, but for a creature that has broken and is
        /// running, which is only worth the chase if the fight's objectives say it mustn't get away —
        /// let the rest go, and the fight ends when they're beyond Long.
        /// </summary>
        /// <remarks>
        /// A chase that goes nowhere is given up: once <see cref="GiveUpTheChaseAfter"/> rounds pass
        /// with nobody hurt, a runner the objectives want caught is let go like any other — a quarry
        /// as quick as its hunters, in the dark where nobody can shoot it, would otherwise be chased forever.
        /// </remarks>
        private IEnumerable<Combatant> Pursued(Combatant actor) =>
            View.EnemiesOf(actor).Where(e => !e.IsBroken || (View.MustNotEscape(e) && View.QuietRounds < GiveUpTheChaseAfter));

        /// <summary>Quiet rounds of chasing after which a fleeing quarry is let go.</summary>
        private const int GiveUpTheChaseAfter = 4;
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
