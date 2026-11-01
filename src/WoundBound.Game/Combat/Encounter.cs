using WoundBound.Game.Combat.Brains;
using WoundBound.Rules.Calculation;
using WoundBound.Rules.Catalog;
using WoundBound.Rules.Enums;
using WoundBound.Rules.Models;

namespace WoundBound.Game.Combat
{
    public enum EncounterPhase
    {
        /// <summary>
        /// The players are choosing which of them acts next (Core Rules §9, "Players choose their
        /// own order"); <see cref="Encounter.TryBeginTurn"/> starts that character's turn. Skipped
        /// when only one character is left to act.
        /// </summary>
        ChoosingCharacter,

        /// <summary>A party member is taking their turn and the player is choosing what they do.</summary>
        PartyTurn,

        /// <summary>The GM is owed an adversary turn, or means to buy one; <see cref="Encounter.RunAdversaryTurn"/> takes it.</summary>
        AdversaryTurn,

        /// <summary>
        /// A roll is on the table and the player may answer it; <see cref="Encounter.Pending"/> says
        /// what with, and <see cref="Encounter.RespondToReaction"/> carries the fight on.
        /// </summary>
        AwaitingReaction,

        Victory,
        Defeat
    }

    /// <summary>Everything one attack roll did, for the log and for anything that animates it.</summary>
    /// <param name="Roll">The roll as it finally stood — against the raised Threshold, if the target dodged.</param>
    /// <param name="Dodged">The target spent Stamina for +2 Dodge Threshold after seeing the roll.</param>
    public sealed record AttackOutcome(
        Combatant Attacker,
        Combatant Target,
        RollResult Roll,
        AttackPlan Plan,
        int RolledDamage,
        int Absorbed,
        int Dealt,
        bool AttackerWounded,
        bool UsedOpportunity,
        bool AsReaction,
        bool Dodged);

    /// <summary>
    /// Something the defender can still answer for 1 Stamina, now they've seen it coming:
    /// <list type="bullet">
    /// <item>a hit that +2 Dodge Threshold would turn into a miss — a Riposte against a critical
    /// (Core Rules §10, "Dodge Threshold");</item>
    /// <item>a Grab, Shove, Trip or break-free that +2 Stability Threshold would stop (§10, "Stability Threshold");</item>
    /// <item>a Shove about to carry them, which they may arrest (§9, "Forced Movement").</item>
    /// </list>
    /// </summary>
    /// <param name="Roll">The roll being answered; null for an arrest, which answers a push rather than a roll.</param>
    /// <param name="Threshold">The Threshold the roll met — DT for a dodge, ST for standing firm.</param>
    /// <param name="Prompt">What's happening and what the spend would do, in a line for the player.</param>
    /// <param name="ImpactWounds">For an arrest: the Body wounds, before armour, the Shove would put them into something solid for.</param>
    /// <param name="Options">For a reaction to somebody leaving reach: what the character may do about it, letting them go always among them.</param>
    /// <remarks>
    /// <see cref="Defender"/> is always whoever is being asked — for a reaction to a leaver, the
    /// character whose reach is being left, with the leaver as <see cref="Attacker"/>.
    /// </remarks>
    public sealed record PendingReaction(
        ReactionKind Kind,
        Combatant Defender,
        Combatant Attacker,
        RollResult? Roll,
        int Threshold,
        string Prompt,
        int ImpactWounds = 0,
        IReadOnlyList<LeaverResponse>? Options = null)
    {
        public int RaisedThreshold => Threshold + ThresholdModifierCalculator.ReactionBonus;

        /// <summary>Only a Dodge Threshold boost can Riposte (Core Rules §9, "Criticals").</summary>
        public bool WouldRiposte => Kind == ReactionKind.PanicDodge && Roll?.IsCritical == true;
    }

    /// <summary>
    /// One combat challenge on a grid (Core Rules §9). Owns the turn structure and asks the Rules
    /// library for every number: what a move costs, whether an attack lands, what armour takes off.
    ///
    /// <para>Round structure follows "Rounds &amp; Turn Order": every party member takes one turn,
    /// and in the gap after each one the GM is owed one adversary turn, never more owed turns in a
    /// round than there are characters. That cap is counted when the fight begins and never falls,
    /// however many characters go down. Opportunities buy turns past it ("GM Opportunities"). At
    /// the round's end every adversary that took no turn closes in with a single move. The players
    /// choose their own order: at the start of each round and after each gap, whoever is left to
    /// act may go next.</para>
    /// </summary>
    public sealed class Encounter
    {
        /// <summary>"The GM may hold a maximum of 3 banked Opportunities at any time" (Core Rules §9).</summary>
        public const int MaxBankedOpportunities = 3;

        private readonly List<Combatant> combatants;
        private readonly RangeBandTables bands;
        private readonly IDiceRoller dice;
        private readonly List<string> log = new();
        private readonly List<Combatant> partyStillToAct = new();

        /// <summary>
        /// Immediate Opportunities, each remembered by the character whose spend made it: it "must
        /// be used before that character's next turn" (Core Rules §9), and lapses then.
        /// </summary>
        private readonly List<Combatant> immediateOpportunities = new();

        private int adversaryTurnsRemaining;
        private int owedInGap;

        /// <summary>The adversary turn cap, locked when the first round starts (Core Rules §9). Null until then.</summary>
        private int? adversaryTurnCap;
        private EncounterPhase phaseBeforeReaction;
        private Action<bool>? resumeAfterReaction;
        private Action<LeaverResponse>? resumeLeaver;

        /// <summary>The extra turn the GM chose to buy in this gap, decided when the gap was opened.</summary>
        private Combatant? extraTurnChosen;

        private readonly IGmBrain gm;
        private readonly BattleView view;

        /// <param name="gm">Who plays the GM's side. Defaults to <see cref="SimpleGmBrain"/>.</param>
        /// <param name="objectives">What the fight is about besides dropping everybody; none by default.</param>
        public Encounter(int width, int height, IEnumerable<Combatant> combatants, RangeBandTables bands, IDiceRoller dice, IGmBrain? gm = null,
            IEnumerable<Objective>? objectives = null)
        {
            Width = width;
            Height = height;
            this.combatants = combatants.ToList();
            this.bands = bands;
            this.dice = dice;
            this.gm = gm ?? new SimpleGmBrain();
            Objectives = objectives?.ToList() ?? new List<Objective>();
            view = new BattleView(this);

            StartRound();
        }

        /// <summary>The fight as a brain sees it — for bots at either seat.</summary>
        internal BattleView View => view;

        public int Width { get; }

        /// <summary>The fight's goals besides dropping everybody (Core Rules §9, "Scenes and Challenges").</summary>
        public IReadOnlyList<Objective> Objectives { get; }

        /// <summary>
        /// Whether <paramref name="square"/> is within Long — 16 squares — of a character still
        /// fighting. An adversary no character has within Long has left the board: the fight goes
        /// on until every one of them is down, gone that far, or has given up.
        /// </summary>
        public bool IsWithinLongOfParty(GridPosition square) =>
            Fighting(Side.Party).Any(c => RangeBandCalculator.BandForSquares(bands, c.Position.DistanceTo(square)) is not null);

        /// <summary>The furthest a band reaches, in squares — Long's 16.</summary>
        public int LongRangeSquares => bands.Bands.Max(b => b.MaxSquares);

        public int Height { get; }

        public IReadOnlyList<Combatant> Combatants => combatants;

        public int Round { get; private set; }

        public EncounterPhase Phase { get; private set; }

        /// <summary>Whoever is taking a turn. Null between turns and once the fight is over.</summary>
        public Combatant? Active { get; private set; }

        /// <summary>The choice the player is being asked to make, while <see cref="Phase"/> is <see cref="EncounterPhase.AwaitingReaction"/>.</summary>
        public PendingReaction? Pending { get; private set; }

        /// <summary>Opportunities from extra actions — held for any later gap, up to <see cref="MaxBankedOpportunities"/>.</summary>
        public int BankedOpportunities { get; internal set; }

        /// <summary>Opportunities from reactions and Threshold boosts — not banked, and gone at the spender's next turn.</summary>
        public int ImmediateOpportunities => immediateOpportunities.Count;

        public int GmOpportunities => BankedOpportunities + ImmediateOpportunities;

        public IReadOnlyList<string> Log => log;

        /// <summary>The party members yet to take a turn this round, in the order they're listed — not the order they'll act in.</summary>
        public IReadOnlyList<Combatant> PartyStillToAct => partyStillToAct;

        public event Action<string>? Logged;

        public bool IsOver => Phase is EncounterPhase.Victory or EncounterPhase.Defeat;

        public IEnumerable<Combatant> Fighting(Side side) => combatants.Where(c => c.Side == side && !c.IsOutOfFight);

        public Combatant? At(GridPosition square) => combatants.FirstOrDefault(c => c.Position == square && !c.IsOutOfFight);

        public bool IsOnBoard(GridPosition square) =>
            square.X >= 0 && square.Y >= 0 && square.X < Width && square.Y < Height;

        /// <summary>Moves still available this turn: the turn's own move, plus every action left, since an action can be spent as a move.</summary>
        public static int MovesAvailable(Combatant combatant) => (combatant.HasMove ? 1 : 0) + combatant.ActionsRemaining;

        /// <summary>
        /// What moving <paramref name="combatant"/> to <paramref name="square"/> would cost in moves,
        /// or null if they can't: off the board, somebody standing there, or more than they have
        /// left. A move is priced by the band its own distance falls in (Core Rules §9, "Speed").
        /// </summary>
        public int? MoveCostTo(Combatant combatant, GridPosition square) =>
            CanMoveItself(combatant) && PriceOfMove(combatant, square) is { } cost && cost <= MovesAvailable(combatant) ? cost : null;

        /// <summary>Every square the active character could move to now, with what each would cost. None while they stand their ground or are Held.</summary>
        public IReadOnlyDictionary<GridPosition, int> ReachableSquares(Combatant combatant) =>
            CanMoveItself(combatant)
                ? SquaresWithin(combatant, MovesAvailable(combatant))
                : new Dictionary<GridPosition, int>();

        /// <summary>
        /// Whether <paramref name="combatant"/> may change square under its own power: not while
        /// standing its ground (Core Rules §10), and not while Held (§9, "Held"). Prone is no bar —
        /// it crawls.
        /// </summary>
        public static bool CanMoveItself(Combatant combatant) =>
            !combatant.IsStandingGround && ConditionCatalog.CanMove(combatant.Conditions);

        /// <summary>
        /// Why <paramref name="combatant"/> can't take <paramref name="action"/> — Dodge, Grounded or
        /// Resolute — right now, or null if they can (Core Rules §10, "Standing Your Ground"). Each
        /// takes the place of the move: the first is paid with the move, a second with the action,
        /// a third with an extra action bought with Stamina. Nobody who has moved this turn can
        /// take one, and none can be taken twice in a turn.
        /// </summary>
        public static string? StandGroundRefusal(Combatant combatant, CombatAction action)
        {
            if (CombatActionCatalog.For(action).Braces is not { } threshold)
            {
                return $"{action} isn't a way of standing your ground.";
            }

            if (combatant.IsOutOfFight)
            {
                return $"{combatant.Name} is in no state to.";
            }

            if (combatant.HasMovedThisTurn)
            {
                return "Already moved this turn — standing your ground means making no move at all.";
            }

            if (combatant.StandingGround.Contains(action))
            {
                return $"Already taking the {action} action — it can't be taken twice in a turn.";
            }

            if (threshold == ThresholdType.Resolve && combatant.BaseResolveThreshold is null)
            {
                return $"{combatant.Name} has no Resolve to steel.";
            }

            return MovesAvailable(combatant) < 1
                ? "Nothing left to pay with: the first takes the move, a second the action, a third an extra action bought with Stamina."
                : null;
        }

        /// <summary>
        /// Why <paramref name="actor"/> can't Grab, Shove or Trip <paramref name="target"/> right
        /// now, or null if they can (Core Rules §9, "Grab, Shove, Trip and Drag"): an enemy within
        /// their own reach — the body's, not a weapon's — and an action to spend. A Grab wants a
        /// free hand, and nobody holds two creatures at once. A Swarm can't be put hands on at all.
        /// </summary>
        public static string? ControlRefusal(Combatant actor, ControlAction action, Combatant target) =>
            ControlRefusal(actor, action, target, asReaction: false);

        /// <summary>
        /// As <see cref="ControlRefusal(Combatant, ControlAction, Combatant)"/>, or — with
        /// <paramref name="asReaction"/> — as the reaction leaving the actor's reach triggers: no
        /// action needed, but a tactical spend (Core Rules §9, "Against a creature on the move").
        /// </summary>
        public static string? ControlRefusal(Combatant actor, ControlAction action, Combatant target, bool asReaction)
        {
            if (actor.IsOutOfFight)
            {
                return $"{actor.Name} is in no state to.";
            }

            if (target.Side == actor.Side || target.IsOutOfFight)
            {
                return $"{target.Name} isn't an enemy still fighting.";
            }

            if (asReaction && TacticalSpendRefusal(actor) is { } cannotSpend)
            {
                return cannotSpend;
            }

            if (!asReaction && actor.ActionsRemaining < 1)
            {
                return "No action left to do it with.";
            }

            if (actor.IsSwarm)
            {
                return "A Swarm has no hands to put on anybody.";
            }

            if (target.IsSwarm)
            {
                return "A Swarm can be neither grabbed, shoved nor tripped: there's no body in it to take hold of.";
            }

            var distance = actor.Position.DistanceTo(target.Position);
            if (distance > actor.OwnReach)
            {
                return $"Out of reach — hands reach {actor.OwnReach} square(s), whatever is in them, and {target.Name} is {distance} away.";
            }

            return action switch
            {
                ControlAction.Grab when actor.Holding is { } held => $"Already holding {held.Name} — one creature at a time.",
                ControlAction.Grab when target.HeldBy is { } holder => $"{holder.Name} already has hold of {target.Name}.",
                ControlAction.Grab when !actor.HasFreeHand => "No free hand — both are full. Grabbing needs a hand holding no weapon, shield or light.",
                ControlAction.Trip when target.IsProne => $"{target.Name} is already on the ground.",
                _ => null,
            };
        }

        /// <summary>Why <paramref name="combatant"/> can't stand up from Prone now, or null if they can. Standing costs a move (Core Rules §9, "Prone").</summary>
        public static string? StandUpRefusal(Combatant combatant)
        {
            if (!combatant.IsProne)
            {
                return $"{combatant.Name} isn't on the ground.";
            }

            if (combatant.IsStandingGround)
            {
                return "Standing your ground — no move of any kind until your next turn.";
            }

            return MovesAvailable(combatant) < 1 ? "Standing costs a move, and there's nothing left this turn to pay for it with." : null;
        }

        /// <summary>Why <paramref name="combatant"/> can't try to break free of a hold now, or null if they can. It takes the action (Core Rules §9, "Held").</summary>
        public static string? BreakFreeRefusal(Combatant combatant)
        {
            if (combatant.HeldBy is null)
            {
                return $"Nobody has hold of {combatant.Name}.";
            }

            return combatant.ActionsRemaining < 1 ? "Breaking free takes an action, and there's none left." : null;
        }

        public bool CanAttack(Combatant attacker, Combatant target) =>
            attacker.Side != target.Side
            && !target.IsOutOfFight
            && attacker.ActionsRemaining > 0
            && PlanAttack(attacker, target).CanAttack;

        /// <summary>
        /// Whether <paramref name="attacker"/>'s attack, as they're armed right now, could be made
        /// at <paramref name="target"/> from where they stand — leaving aside whether they have an
        /// action left to make it with. A blade asks about Reach, in squares; a bow asks about the
        /// band, which moves the Dodge Threshold and the damage die (Core Rules §9, "Weapon Range").
        /// </summary>
        public AttackPlan PlanAttack(Combatant attacker, Combatant target) => PlanAttackFrom(attacker, attacker.Position, target);

        /// <summary>As <see cref="PlanAttack"/>, as though <paramref name="attacker"/> stood on <paramref name="from"/>.</summary>
        internal AttackPlan PlanAttackFrom(Combatant attacker, GridPosition from, Combatant target)
        {
            var attack = attacker.Attack;
            var distance = from.DistanceTo(target.Position);

            if (!attack.IsRanged)
            {
                if (attack.IsUnwieldy && distance <= 1)
                {
                    return Refused($"The {attack.Name} is Unwieldy — it can't strike somebody standing this close.");
                }

                return distance <= attack.Reach
                    ? new AttackPlan(null, target.DodgeThreshold, 0, attack.DamageDieSides, $"Melee, within Reach {attack.Reach}.")
                    : Refused($"Out of reach — the {attack.Name} reaches {attack.Reach} square(s) and they're {distance} away.");
            }

            var range = RangeBandCalculator.BandForSquares(bands, distance) switch
            {
                DistanceBand.Close => TargetRange.Close,
                DistanceBand.Short => TargetRange.Short,
                DistanceBand.Middle => TargetRange.Middle,
                DistanceBand.Long => TargetRange.Long,
                _ => TargetRange.BeyondLong,
            };
            var shot = WeaponCalculator.ShotAt(attack.Weapon!, attack.DamageModifier, range);

            return shot.CanAttack
                ? new AttackPlan(
                    Refusal: null,
                    DodgeThreshold: target.DodgeThreshold + shot.DodgeThresholdModifier,
                    RangeDodgeModifier: shot.DodgeThresholdModifier,
                    DamageDieSides: shot.DamageDie is { } die ? DiceNotation.ParseSides(die) : null,
                    Note: shot.Summary)
                : Refused(shot.Summary);

            static AttackPlan Refused(string why) => new(why, 0, 0, null, why);
        }

        /// <summary>
        /// Why <paramref name="combatant"/> can't swap their loadout in hand for the one at the
        /// ready right now, or null if they can. A swap costs a move (Core Rules §15), so it can be
        /// paid for with the turn's move or, once that's gone, with an action.
        /// </summary>
        public static string? SwapRefusal(Combatant combatant)
        {
            if (combatant.Build is not { } build)
            {
                return $"{combatant.Name} has no loadouts to swap.";
            }

            if (InventoryCalculator.WhyCannotSwap(build) is { } why)
            {
                return why;
            }

            return MovesAvailable(combatant) < 1
                ? "Swapping costs a move, and there's nothing left this turn to pay for it with."
                : null;
        }

        /// <summary>
        /// Why <paramref name="combatant"/> can't make a tactical Stamina spend right now — an extra
        /// action, a reaction or a Threshold boost — or null if they can (Core Rules §6, "Stamina Limit").
        /// </summary>
        public static string? TacticalSpendRefusal(Combatant combatant)
        {
            if (combatant.IsOutOfFight)
            {
                return $"{combatant.Name} is in no state to.";
            }

            if (combatant.Stamina < CombatActionCatalog.ReactionStaminaCost)
            {
                return "No Stamina left to spend.";
            }

            return TacticalSpendCalculator.IsAtLimit(combatant.TacticalSpendsThisRound, combatant.TacticalSpendsPerRound)
                ? $"Already made {combatant.TacticalSpendsThisRound} tactical spend(s) this round — the limit at this Tier."
                : null;
        }

        /// <summary>Why the active character can't buy an extra action with Stamina right now, or null if they can.</summary>
        public string? ExtraActionRefusal(Combatant combatant) => TacticalSpendRefusal(combatant);

        public bool TryMove(GridPosition square)
        {
            if (Phase != EncounterPhase.PartyTurn || Active is not { } mover || MoveCostTo(mover, square) is not { } cost)
            {
                return false;
            }

            Move(mover, square, cost, () => CheckForEnd());
            return true;
        }

        /// <summary>
        /// Answers a <see cref="ReactionKind.LeaverReaction"/>: one of <see cref="PendingReaction.Options"/>.
        /// Anything but letting them go costs 1 Stamina, a tactical spend that hands the GM an
        /// Immediate Opportunity. False if there was no such question, or that wasn't an option.
        /// </summary>
        public bool RespondToLeaver(LeaverResponse response)
        {
            if (Phase != EncounterPhase.AwaitingReaction || Pending is not { Kind: ReactionKind.LeaverReaction } pending
                || resumeLeaver is not { } resume || pending.Options?.Contains(response) != true)
            {
                return false;
            }

            if (response != LeaverResponse.LetThemGo)
            {
                PayTacticalSpend(pending.Defender, CombatActionCatalog.ReactionStaminaCost);
                GrantImmediateOpportunity(pending.Defender);
            }

            Pending = null;
            resumeLeaver = null;
            Phase = phaseBeforeReaction;
            resume(response);
            return true;
        }

        public AttackOutcome? TryAttack(Combatant target)
        {
            if (Phase != EncounterPhase.PartyTurn || Active is not { } attacker || !CanAttack(attacker, target))
            {
                return null;
            }

            AttackOutcome? result = null;
            ResolveAttack(attacker, target, asReaction: false, opportunityBonus: false, outcome =>
            {
                result = outcome;
                CheckForEnd();
            });

            return result;
        }

        /// <summary>
        /// Puts the loadout in hand away and takes up the one at the ready (Core Rules §15,
        /// "Swapping costs a move"). Paid out of the move, but it isn't movement — nobody's reach is
        /// left — and it's no tactical spend, so the GM gains no Opportunity.
        /// </summary>
        public bool TrySwapLoadout()
        {
            if (Phase != EncounterPhase.PartyTurn || Active is not { } swapper || SwapRefusal(swapper) is not null)
            {
                return false;
            }

            var putAway = swapper.InHandWeapon?.Name ?? "empty hands";
            var paidWith = PayMoves(swapper, 1);
            InventoryCalculator.SwapLoadouts(swapper.Build!);
            var takenUp = swapper.InHandWeapon?.Name ?? "empty hands";

            Write($"{swapper.Name} swaps loadouts — {putAway} away, {takenUp} in hand — for {paidWith}.");
            CheckHolds();
            return true;
        }

        /// <summary>
        /// Takes the Dodge, Grounded or Resolute action: +2 to that Threshold until the start of
        /// the next turn, paid for like a move — and from now until then, no moving and
        /// Disadvantage on every attack roll (Core Rules §10, "Standing Your Ground").
        /// </summary>
        public bool TryStandGround(CombatAction action)
        {
            if (Phase != EncounterPhase.PartyTurn || Active is not { } bracer || StandGroundRefusal(bracer, action) is not null)
            {
                return false;
            }

            StandGround(bracer, action);
            return true;
        }

        private void StandGround(Combatant bracer, CombatAction action)
        {
            var threshold = CombatActionCatalog.For(action).Braces!.Value;
            var paidWith = PayMoves(bracer, 1);

            bracer.ThresholdModifiers = ThresholdModifierCalculator.Add(bracer.ThresholdModifiers, new ThresholdModifier
            {
                Threshold = threshold,
                Amount = ThresholdModifierCalculator.BracingActionBonus,
                Source = $"{action} action",
                Duration = ThresholdModifierDuration.UntilMyTurn,
                StackKey = ThresholdModifierCalculator.BracingActionKey,
            });

            var now = threshold switch
            {
                ThresholdType.Dodge => $"DT {bracer.DodgeThreshold}",
                ThresholdType.Stability => $"ST {bracer.StabilityThreshold}",
                _ => $"RT {bracer.ResolveThreshold}",
            };
            Write($"{bracer.Name} {CombatActionCatalog.ActionLogLine(action, extra: false).TrimEnd('.')} — {now}, for {paidWith}.");
        }

        /// <summary>
        /// Grabs, shoves or trips <paramref name="target"/> with the action: an unarmed attack roll
        /// against their Stability Threshold (Core Rules §9, "Grab, Shove, Trip and Drag").
        /// </summary>
        public bool TryControl(ControlAction action, Combatant target)
        {
            if (Phase != EncounterPhase.PartyTurn || Active is not { } actor || ControlRefusal(actor, action, target) is not null)
            {
                return false;
            }

            ResolveControl(actor, action, target, opportunityBonus: false, () => CheckForEnd());
            return true;
        }

        /// <summary>Stands up from Prone, for a move. Like a swap it isn't movement: nobody's reach is left (Core Rules §9, "Prone").</summary>
        public bool TryStandUp()
        {
            if (Phase != EncounterPhase.PartyTurn || Active is not { } stander || StandUpRefusal(stander) is not null)
            {
                return false;
            }

            StandUp(stander);
            return true;
        }

        /// <summary>Spends the action trying to break the hold: the Grab's roll, against the holder's Stability Threshold (Core Rules §9, "Held").</summary>
        public bool TryBreakFree()
        {
            if (Phase != EncounterPhase.PartyTurn || Active is not { } breaker || BreakFreeRefusal(breaker) is not null)
            {
                return false;
            }

            ResolveBreakFree(breaker, () => CheckForEnd());
            return true;
        }

        /// <summary>Lets go of whoever the active character is holding. Costs nothing (Core Rules §9, "Held").</summary>
        public bool TryLetGo()
        {
            if (Phase != EncounterPhase.PartyTurn || Active is not { Holding: not null } holder)
            {
                return false;
            }

            Release(holder, "lets go");
            return true;
        }

        /// <summary>Spends 1 Stamina for an extra action this turn (Core Rules §6), handing the GM a banked Opportunity.</summary>
        public bool TryBuyExtraAction()
        {
            if (Phase != EncounterPhase.PartyTurn || Active is not { } buyer || ExtraActionRefusal(buyer) is not null)
            {
                return false;
            }

            PayTacticalSpend(buyer, CombatActionCatalog.ExtraActionStaminaCost);
            buyer.ActionsRemaining++;

            Write($"{buyer.Name} spends 1 Stamina for an extra action ({buyer.Stamina} left).");
            GrantBankedOpportunity();

            if (buyer.Stamina == 0)
            {
                Write($"{buyer.Name} is out of Stamina, and will fall unconscious if they start a turn that way.");
            }

            return true;
        }

        public void EndTurn()
        {
            if (Phase != EncounterPhase.PartyTurn || Active is not { } ending)
            {
                return;
            }

            FinishTurn(ending);
            AfterPartyTurn();
        }

        /// <summary>
        /// Answers <see cref="Pending"/>: <paramref name="spend"/> pays 1 Stamina for what it offers
        /// — a panic dodge, standing firm, or arresting a Shove — a tactical spend that hands the GM
        /// an Immediate Opportunity; otherwise it happens as rolled. Returns false if there was
        /// nothing to answer, or the spend can no longer be paid for.
        /// </summary>
        public bool RespondToReaction(bool spend)
        {
            if (Phase != EncounterPhase.AwaitingReaction || Pending is not { } pending || resumeAfterReaction is not { } resume
                || pending.Kind == ReactionKind.LeaverReaction)
            {
                return false;
            }

            if (spend)
            {
                if (TacticalSpendRefusal(pending.Defender) is not null)
                {
                    return false;
                }

                PayTacticalSpend(pending.Defender, CombatActionCatalog.ReactionStaminaCost);
                GrantImmediateOpportunity(pending.Defender);
            }

            Pending = null;
            resumeAfterReaction = null;
            Phase = phaseBeforeReaction;
            resume(spend);
            return true;
        }

        /// <summary>
        /// Takes the adversary turn the GM has coming: an owed one if any is left in this gap,
        /// otherwise one bought with an Opportunity. A separate step from <see cref="EndTurn"/> so
        /// whatever shows the fight can pause between turns.
        /// </summary>
        public void RunAdversaryTurn()
        {
            if (Phase != EncounterPhase.AdversaryTurn)
            {
                return;
            }

            Combatant actor;
            var extraTurn = false;

            if (owedInGap > 0)
            {
                owedInGap--;
                adversaryTurnsRemaining--;
                actor = CheckAdversary(gm.ChooseOwedTurn(view), "an owed turn");
            }
            else if (extraTurnChosen is { } bought)
            {
                extraTurnChosen = null;
                Write($"The GM spends {SpendOpportunity()} Opportunity on an extra turn for {bought.Name}.");
                actor = bought;
                extraTurn = true;
            }
            else
            {
                ContinueGap();
                return;
            }

            TakeAdversaryTurn(actor, allowOpportunityBonus: !extraTurn, () =>
            {
                actor.HasActedThisRound = true;
                ContinueGap();
            });
        }

        private void StartRound()
        {
            Round++;
            Write($"— Round {Round} —");

            foreach (var combatant in combatants)
            {
                combatant.TacticalSpendsThisRound = 0;
                combatant.HasActedThisRound = false;
            }

            // Everyone on the party's side keeps their place in the round, fighting or not: a
            // character with no turn to take still leaves the GM the adversary turn that follows it.
            partyStillToAct.Clear();
            partyStillToAct.AddRange(combatants.Where(c => c.Side == Side.Party));

            // "The cap is set when the fight begins, and it never falls" (Core Rules §9): counted
            // once, raised by anyone who joins, never lowered by anyone who drops.
            adversaryTurnCap = ChallengeCalculator.AdversaryTurnCap(adversaryTurnCap, partyStillToAct.Count);
            adversaryTurnsRemaining = adversaryTurnCap.Value;
            BeginNextPartyTurn();
        }

        public int AdversaryTurnsPerRound => adversaryTurnCap ?? 0;

        /// <summary>
        /// Whose turn is next: a character with no turn to take is passed over first, still
        /// leaving the GM the adversary turn that follows it (Core Rules §9, "The cap is set when
        /// the fight begins"); otherwise the players choose, unless only one of them is left.
        /// </summary>
        private void BeginNextPartyTurn()
        {
            Active = null;

            if (partyStillToAct.FirstOrDefault(c => c.IsOutOfFight) is { } passedOver)
            {
                partyStillToAct.Remove(passedOver);
                Write($"{passedOver.Name} has no turn to take.");
                AfterPartyTurn();
                return;
            }

            switch (partyStillToAct.Count)
            {
                case 0:
                    AfterPartyTurn();
                    return;
                case 1:
                    StartPartyTurn(partyStillToAct[0]);
                    return;
                default:
                    Phase = EncounterPhase.ChoosingCharacter;
                    return;
            }
        }

        /// <summary>
        /// Starts <paramref name="character"/>'s turn, while the players are choosing who goes next
        /// (Core Rules §9, "Players choose their own order"). False if it isn't that moment, or
        /// they've already had their turn this round.
        /// </summary>
        public bool TryBeginTurn(Combatant character)
        {
            if (Phase != EncounterPhase.ChoosingCharacter || !partyStillToAct.Contains(character))
            {
                return false;
            }

            StartPartyTurn(character);
            return true;
        }

        private void StartPartyTurn(Combatant next)
        {
            partyStillToAct.Remove(next);
            ExpireImmediateOpportunities(next);
            StartTurnFor(next);

            if (next.Stamina <= 0)
            {
                // "If a character starts their turn with 0 Stamina, they fall unconscious" (Core Rules §6).
                next.IsUnconscious = true;
                Write($"{next.Name} starts their turn with no Stamina and falls unconscious.");
                AfterPartyTurn();
                return;
            }

            Active = next;
            next.HasMove = true;
            next.ActionsRemaining = 1;
            next.HasActedThisRound = true;
            Phase = EncounterPhase.PartyTurn;
            Write($"{next.Name}'s turn.");
        }

        private void AfterPartyTurn()
        {
            Active = null;
            if (CheckForEnd())
            {
                return;
            }

            // After a character's turn the GM is owed one adversary turn; after the round's last
            // character, every turn still held (Core Rules §9, "Rounds & Turn Order").
            owedInGap = partyStillToAct.Count > 0 ? Math.Min(1, adversaryTurnsRemaining) : adversaryTurnsRemaining;
            ContinueGap();
        }

        /// <summary>Decides what happens next in the gap after a character's turn: another adversary turn, owed or bought, or the next character.</summary>
        private void ContinueGap()
        {
            Active = null;
            if (CheckForEnd())
            {
                return;
            }

            extraTurnChosen = owedInGap == 0 && GmOpportunities > 0 && gm.ChooseExtraTurn(view) is { } bought
                ? CheckAdversary(bought, "an extra turn")
                : null;

            if (owedInGap > 0 || extraTurnChosen is not null)
            {
                Phase = EncounterPhase.AdversaryTurn;
                return;
            }

            owedInGap = 0;
            if (partyStillToAct.Count > 0)
            {
                BeginNextPartyTurn();
            }
            else
            {
                CloseIn(Fighting(Side.Adversaries).Where(a => !a.HasActedThisRound).ToList(), () =>
                {
                    if (!CheckForEnd())
                    {
                        StartRound();
                    }
                });
            }
        }

        /// <summary>
        /// "Adversaries that didn't act close in" (Core Rules §9): after the GM's last turn of the
        /// round, each adversary that took no turn may take one move — no action, nothing riding on
        /// it — and it isn't a turn. The GM brain picks who goes where, one at a time.
        /// </summary>
        private void CloseIn(List<Combatant> idle, Action done)
        {
            idle.RemoveAll(a => a.IsOutOfFight);
            if (IsOver || idle.Count == 0 || gm.NextCloseIn(view, idle) is not { } closeIn)
            {
                done();
                return;
            }

            var adversary = closeIn.Adversary;
            if (!idle.Remove(adversary))
            {
                throw new InvalidOperationException($"The GM brain closed in with {adversary.Name}, which took a turn this round or has already moved.");
            }

            void Next() => CloseIn(idle, done);

            if (closeIn.StandUp)
            {
                if (!adversary.IsProne)
                {
                    throw new InvalidOperationException($"The GM brain stood {adversary.Name} up at the round's end, but it isn't Prone.");
                }

                adversary.SetCondition(CharacterCondition.Prone, false);
                Write($"{adversary.Name} didn't act this round, and gets to its feet.");
                Next();
                return;
            }

            if (!CanMoveItself(adversary) || PriceOfMove(adversary, closeIn.Square) is not { } cost || cost > 1)
            {
                throw new InvalidOperationException($"The GM brain closed {adversary.Name} in to {closeIn.Square}, which isn't a single free move away.");
            }

            // Ordinary movement in every other way: leaving a character's reach draws reactions.
            Relocate(adversary, closeIn.Square, arrivedAt =>
            {
                var crossed = adversary.Position.DistanceTo(arrivedAt);
                if (!adversary.IsOutOfFight)
                {
                    var from = adversary.Position;
                    adversary.Position = arrivedAt;
                    var band = RangeBandCalculator.BandForSquares(bands, from.DistanceTo(arrivedAt));
                    Write($"{adversary.Name} didn't act this round, and closes in {crossed} square(s) ({band}).");
                    CheckHolds();
                }

                Next();
            });
        }

        private Combatant CheckAdversary(Combatant chosen, string forWhat) =>
            chosen.Side == Side.Adversaries && !chosen.IsOutOfFight && combatants.Contains(chosen)
                ? chosen
                : throw new InvalidOperationException($"The GM brain chose {chosen.Name} for {forWhat}, but it isn't an adversary still fighting.");

        /// <summary>
        /// One adversary's turn: a move and an action, spent however the GM brain chooses, a step
        /// at a time. Each step is checked against the rules before it happens.
        /// </summary>
        /// <summary>
        /// What the start of any creature's turn clears: everything that lasted "until the start
        /// of the next turn" — standing their ground among it (Core Rules §10).
        /// </summary>
        private void StartTurnFor(Combatant combatant)
        {
            var wasStanding = combatant.StandingGround.ToList();
            combatant.ThresholdModifiers = ThresholdModifierCalculator.AtTurnStart(combatant.ThresholdModifiers);
            combatant.HasMovedThisTurn = false;

            if (wasStanding.Count > 0)
            {
                Write($"{combatant.Name} stops standing their ground ({string.Join(", ", wasStanding)}).");
            }
        }

        private void TakeAdversaryTurn(Combatant actor, bool allowOpportunityBonus, Action done)
        {
            StartTurnFor(actor);
            Active = actor;
            actor.HasMove = true;
            actor.ActionsRemaining = 1;

            // The Opportunity bonus goes to "an adversary yet to act this round" (Core Rules §9).
            var turn = new GmTurn(actor, isExtraTurn: !allowOpportunityBonus,
                bonusAvailable: allowOpportunityBonus && !actor.HasActedThisRound && GmOpportunities > 0);

            TakeStep(turn, done);
        }

        private void TakeStep(GmTurn turn, Action done)
        {
            var actor = turn.Actor;

            if (CheckForEnd() || actor.IsOutOfFight)
            {
                FinishTurn(actor);
                done();
                return;
            }

            // Every step spends a move or an action, and an illegal one throws, so a turn always ends.
            turn.BonusAvailable &= GmOpportunities > 0;
            var step = gm.NextStep(view, turn);
            turn.Steps++;

            switch (step)
            {
                case GmStep.Move move:
                    if (MoveCostTo(actor, move.Square) is not { } cost)
                    {
                        throw new InvalidOperationException($"The GM brain moved {actor.Name} to {move.Square}, which it can't reach with what it has left.");
                    }

                    turn.HasMoved = true;
                    Move(actor, move.Square, cost, () => TakeStep(turn, done));
                    return;


                case GmStep.Attack attack:
                    if (!CanAttack(actor, attack.Target))
                    {
                        throw new InvalidOperationException($"The GM brain had {actor.Name} attack {attack.Target.Name}, which it can't do now.");
                    }

                    if (attack.WithAdvantage && !turn.BonusAvailable)
                    {
                        throw new InvalidOperationException($"The GM brain spent an Opportunity on {actor.Name}'s bonus, which this turn can't take.");
                    }

                    turn.HasAttacked = true;
                    turn.BonusAvailable &= !attack.WithAdvantage;
                    ResolveAttack(actor, attack.Target, asReaction: false, attack.WithAdvantage, _ => TakeStep(turn, done));
                    return;

                case GmStep.Control control:
                    if (ControlRefusal(actor, control.Action, control.Target) is { } refused)
                    {
                        throw new InvalidOperationException($"The GM brain had {actor.Name} {control.Action} {control.Target.Name}: {refused}");
                    }

                    if (control.WithAdvantage && !turn.BonusAvailable)
                    {
                        throw new InvalidOperationException($"The GM brain spent an Opportunity on {actor.Name}'s bonus, which this turn can't take.");
                    }

                    turn.HasAttacked = true;
                    turn.BonusAvailable &= !control.WithAdvantage;
                    ResolveControl(actor, control.Action, control.Target, control.WithAdvantage, () => TakeStep(turn, done));
                    return;

                case GmStep.StandUp:
                    if (StandUpRefusal(actor) is { } cannotStand)
                    {
                        throw new InvalidOperationException($"The GM brain stood {actor.Name} up: {cannotStand}");
                    }

                    StandUp(actor);
                    TakeStep(turn, done);
                    return;

                case GmStep.BreakFree:
                    if (BreakFreeRefusal(actor) is { } cannotBreak)
                    {
                        throw new InvalidOperationException($"The GM brain had {actor.Name} break free: {cannotBreak}");
                    }

                    turn.HasAttacked = true;
                    ResolveBreakFree(actor, () => TakeStep(turn, done));
                    return;

                case GmStep.LetGo when actor.Holding is not null:
                    Release(actor, "lets go");
                    TakeStep(turn, done);
                    return;

                case GmStep.LetGo:
                    throw new InvalidOperationException($"The GM brain had {actor.Name} let go, but it's holding nobody.");

                case GmStep.StandGround brace:
                    if (StandGroundRefusal(actor, brace.Action) is { } why)
                    {
                        throw new InvalidOperationException($"The GM brain had {actor.Name} take the {brace.Action} action: {why}");
                    }

                    StandGround(actor, brace.Action);
                    turn.HasStoodGround = true;
                    TakeStep(turn, done);
                    return;

                default:
                    if (!turn.HasAttacked && !turn.HasStoodGround && actor.HasMove)
                    {
                        Write($"{actor.Name} holds its ground.");
                    }

                    FinishTurn(actor);
                    done();
                    return;
            }
        }

        /// <summary>
        /// Lets each enemy whose reach <paramref name="mover"/> is leaving strike at them as they
        /// go — for 1 Stamina, a tactical spend like any reaction (Core Rules §9, "Movement"). An
        /// adversary's spend hands the GM nothing; Opportunities come only from the players' own
        /// choices (GM Reference §1).
        /// </summary>
        /// <summary>
        /// The squares a straight hop from <paramref name="from"/> to <paramref name="to"/> passes
        /// over, <paramref name="from"/> first and <paramref name="to"/> last — a diagonal step for
        /// as long as both axes still need one. The slice has no walls, so the straight line is
        /// the route.
        /// </summary>
        private static List<GridPosition> PathBetween(GridPosition from, GridPosition to)
        {
            var path = new List<GridPosition> { from };
            var at = from;
            while (at != to)
            {
                at = new GridPosition(at.X + Math.Sign(to.X - at.X), at.Y + Math.Sign(to.Y - at.Y));
                path.Add(at);
            }

            return path;
        }

        /// <summary>
        /// Everybody whose reach <paramref name="mover"/> leaves going from where it stands to
        /// <paramref name="to"/>. "Leaving an enemy's reach is a trigger" however it was caused
        /// (Core Rules §9, "Movement").
        /// </summary>
        private Queue<Combatant> LeavingReachOf(Combatant mover, GridPosition to) =>
            new(Fighting(mover.Side == Side.Party ? Side.Adversaries : Side.Party)
                .Where(e => e.CanReach(mover.Position) && !e.CanReach(to)));

        /// <summary>
        /// Carries <paramref name="mover"/> towards <paramref name="to"/>, offering a reaction to
        /// each enemy whose reach it leaves. Says where it ends up: <paramref name="to"/>, or the
        /// square it was caught in — "the rest of that move is lost" (Core Rules §9). Leaves the
        /// position for the caller to set, along with the log line.
        /// </summary>
        private void Relocate(Combatant mover, GridPosition to, Action<GridPosition> then) =>
            ReactToLeaving(mover, PathBetween(mover.Position, to), LeavingReachOf(mover, to), caughtAt => then(caughtAt ?? to));

        private void Move(Combatant mover, GridPosition square, int cost, Action then)
        {
            var paidWith = PayMoves(mover, cost);
            mover.HasMovedThisTurn = true;

            Relocate(mover, square, arrivedAt =>
            {
                if (!mover.IsOutOfFight)
                {
                    var from = mover.Position;
                    mover.Position = arrivedAt;
                    var band = RangeBandCalculator.BandForSquares(bands, from.DistanceTo(arrivedAt));
                    Write(arrivedAt == from
                        ? $"{mover.Name} is stopped before it gets anywhere — {paidWith} spent for nothing."
                        : $"{mover.Name} moves {from.DistanceTo(arrivedAt)} square(s) ({band}) for {paidWith}" +
                          (arrivedAt == square ? "." : $", and is stopped at {arrivedAt}."));
                    CheckHolds();
                }

                then();
            });
        }

        /// <summary>
        /// Offers each enemy in <paramref name="leftBehind"/> the reaction <paramref name="mover"/>
        /// leaving its reach triggers, one at a time — for 1 Stamina, a tactical spend (Core Rules
        /// §9, "Movement"). The GM's creatures strike or don't, as its brain says; a character may
        /// strike, or Grab, Shove or Trip the leaver, rolled against Dodge and Stability both (§9,
        /// "Against a creature on the move"). A catch stops the move: the callback gets the square
        /// it happened in, or null if the mover got through. An adversary's spend hands the GM
        /// nothing; a character's hands it an Immediate Opportunity.
        /// </summary>
        /// <param name="path">The squares the move passes over, where it starts first.</param>
        private void ReactToLeaving(Combatant mover, List<GridPosition> path, Queue<Combatant> leftBehind, Action<GridPosition?> then)
        {
            while (leftBehind.TryDequeue(out var enemy))
            {
                if (mover.IsOutOfFight || enemy.IsOutOfFight || TacticalSpendRefusal(enemy) is not null)
                {
                    continue;
                }

                if (enemy.Side == Side.Adversaries)
                {
                    if (!PlanAttack(enemy, mover).CanAttack)
                    {
                        continue;
                    }

                    if (!gm.StrikesAtLeaver(view, enemy, mover))
                    {
                        Write($"{mover.Name} leaves {enemy.Name}'s reach, and it lets them go.");
                        continue;
                    }

                    PayTacticalSpend(enemy, CombatActionCatalog.ReactionStaminaCost);
                    Write($"{mover.Name} leaves {enemy.Name}'s reach, and it strikes as they go (1 Stamina).");
                    ResolveAttack(enemy, mover, asReaction: true, opportunityBonus: false, _ => ReactToLeaving(mover, path, leftBehind, then));
                    return;
                }

                var options = new List<LeaverResponse> { LeaverResponse.LetThemGo };
                if (PlanAttack(enemy, mover).CanAttack)
                {
                    options.Add(LeaverResponse.Strike);
                }

                options.AddRange(new[] { LeaverResponse.Grab, LeaverResponse.Shove, LeaverResponse.Trip }
                    .Where(r => ControlRefusal(enemy, ControlFor(r), mover, asReaction: true) is null));

                if (options.Count == 1)
                {
                    continue;
                }

                var catchAt = path.LastOrDefault(square => square.DistanceTo(enemy.Position) <= enemy.OwnReach
                    && (square == mover.Position || At(square) is null), mover.Position);
                var reacting = enemy;

                Pending = new PendingReaction(ReactionKind.LeaverReaction, reacting, mover, Roll: null, Threshold: 0,
                    $"{mover.Name} is leaving {reacting.Name}'s reach. Strike as they go, or Grab, Shove or Trip them — " +
                    $"against their DT and ST both — for 1 Stamina?",
                    Options: options);
                phaseBeforeReaction = Phase;
                Phase = EncounterPhase.AwaitingReaction;
                resumeLeaver = response =>
                {
                    switch (response)
                    {
                        case LeaverResponse.LetThemGo:
                            Write($"{mover.Name} leaves {reacting.Name}'s reach, and {reacting.Name} lets them go.");
                            ReactToLeaving(mover, path, leftBehind, then);
                            return;

                        case LeaverResponse.Strike:
                            Write($"{mover.Name} leaves {reacting.Name}'s reach, and {reacting.Name} strikes as they go (1 Stamina).");
                            ResolveAttack(reacting, mover, asReaction: true, opportunityBonus: false, _ => ReactToLeaving(mover, path, leftBehind, then));
                            return;

                        default:
                            ResolveReactiveControl(reacting, ControlFor(response), mover, catchAt, caught =>
                            {
                                if (caught)
                                {
                                    then(catchAt);
                                }
                                else
                                {
                                    ReactToLeaving(mover, path, leftBehind, then);
                                }
                            });
                            return;
                    }
                };
                Write(Pending.Prompt);
                return;
            }

            then(null);
        }

        private static ControlAction ControlFor(LeaverResponse response) => response switch
        {
            LeaverResponse.Grab => ControlAction.Grab,
            LeaverResponse.Shove => ControlAction.Shove,
            _ => ControlAction.Trip,
        };

        /// <summary>
        /// A Grab, Shove or Trip made as the reaction to a creature leaving reach (Core Rules §9,
        /// "Against a creature on the move"): one roll, against Dodge Threshold and Stability
        /// Threshold both, and it takes effect only if it meets both. The target answers as it
        /// would a manoeuvre — 1 Stamina for +2 to either — and a Dodge boost against a critical can
        /// Riposte it. Caught, the creature stops at <paramref name="catchAt"/>: held there, felled
        /// there, or pushed on from there.
        /// </summary>
        private void ResolveReactiveControl(Combatant actor, ControlAction action, Combatant target, GridPosition catchAt, Action<bool> then)
        {
            var dt = target.DodgeThreshold;
            var st = target.StabilityThreshold;
            var roll = RollAgainst(actor.UnarmedAttackModifier, Math.Max(dt, st), advantage: false, AttacksAtDisadvantage(actor));
            var verb = Verb(action);

            void Finish(bool dodged, bool stoodFirm)
            {
                var finalDt = dt + (dodged ? ThresholdModifierCalculator.ReactionBonus : 0);
                var finalSt = st + (stoodFirm ? ThresholdModifierCalculator.ReactionBonus : 0);
                var final = Reread(roll, Math.Max(finalDt, finalSt));
                var line = $"{actor.Name} tries to {verb} {target.Name} on the move: {DescribeRoll(final)} vs DT {finalDt} and ST {finalSt}" +
                           (dodged ? " (+2 panic dodge)" : stoodFirm ? " (+2 standing firm)" : string.Empty);

                if (final.Success != true)
                {
                    // A dodged critical is a Riposte: it comes back on the one who reached (§9, "Criticals").
                    var overbalanced = final.IsCritical && (roll.Success != true || dodged);
                    Write(line + (overbalanced
                        ? $" — {(dodged ? "Riposte" : "critical failure")}! {actor.Name} overbalances and goes down, Prone."
                        : $" — {target.Name} slips past."));
                    if (overbalanced)
                    {
                        actor.SetCondition(CharacterCondition.Prone, true);
                    }

                    then(false);
                    return;
                }

                target.Position = catchAt;
                ApplyControl(actor, action, target, final.IsCritical, line + $" — caught at {catchAt}", () => then(true));
            }

            if (BoostWouldTurn(roll, dt))
            {
                OfferReaction(
                    new PendingReaction(ReactionKind.PanicDodge, target, actor, roll, dt,
                        $"{actor.Name} reaches for {target.Name} on the move: {roll.Total} against DT {dt}. A panic dodge (1 Stamina, +2) would slip it."),
                    dodged =>
                    {
                        if (!dodged && BoostWouldTurn(roll, st))
                        {
                            OfferReaction(
                                new PendingReaction(ReactionKind.StandFirm, target, actor, roll, st,
                                    $"{actor.Name} reaches for {target.Name} on the move: {roll.Total} against ST {st}. Standing firm (1 Stamina, +2 ST) would stop it."),
                                stood => Finish(false, stood));
                            return;
                        }

                        Finish(dodged, false);
                    });
                return;
            }

            if (BoostWouldTurn(roll, st))
            {
                OfferReaction(
                    new PendingReaction(ReactionKind.StandFirm, target, actor, roll, st,
                        $"{actor.Name} reaches for {target.Name} on the move: {roll.Total} against ST {st}. Standing firm (1 Stamina, +2 ST) would stop it."),
                    stood => Finish(false, stood));
                return;
            }

            Finish(false, false);
        }

        /// <summary>
        /// Pays <paramref name="cost"/> moves: the turn's own move first, then actions, since a move
        /// is an action. Says what was spent, for the log.
        /// </summary>
        private static string PayMoves(Combatant combatant, int cost)
        {
            var usedMove = combatant.HasMove && cost > 0;
            var actions = usedMove ? cost - 1 : cost;

            combatant.HasMove &= !usedMove;
            combatant.ActionsRemaining -= actions;

            var plural = actions == 1 ? "action" : "actions";
            return (usedMove, actions) switch
            {
                (true, 0) => "the move",
                (true, _) => $"the move and {actions} {plural}",
                _ => $"{actions} {plural} (the move is spent)",
            };
        }

        /// <summary>
        /// One attack roll: 2d12 + attack modifier against the target's Dodge Threshold, raised by
        /// range for a shot (Core Rules §9, "Attack Roll", "Weapon Range"). Once the roll is known
        /// the target may answer a hit that +2 would turn — the player is asked, the GM decides for
        /// its own creatures — and then damage lands less what armour absorbs (Core Rules §4), with
        /// a critical's consequences either way (Core Rules §10).
        /// </summary>
        private void ResolveAttack(Combatant attacker, Combatant target, bool asReaction, bool opportunityBonus, Action<AttackOutcome> then)
        {
            var plan = PlanAttack(attacker, target);

            if (opportunityBonus)
            {
                Write($"The GM spends {SpendOpportunity()} Opportunity on {attacker.Name}'s bonus: Advantage.");
            }

            // Standing your ground costs the blow: Disadvantage on every attack roll while it lasts,
            // reaction attacks included (Core Rules §10) — as does being Off-guard (§9,
            // "Conditions"). An Advantage cancels it.
            var disadvantage = AttacksAtDisadvantage(attacker);
            var rolled = Enumerable.Range(0, RollCalculator.DiceNeeded(opportunityBonus, disadvantage))
                .Select(_ => dice.Roll(RollCalculator.HighestFace))
                .ToList();
            var roll = RollCalculator.Resolve(rolled, attacker.Attack.AttackModifier, plan.DodgeThreshold, opportunityBonus, disadvantage);

            if (!asReaction)
            {
                attacker.ActionsRemaining--;
            }

            if (!BoostWouldTurn(roll, plan.DodgeThreshold))
            {
                then(FinishAttack(attacker, target, roll, plan, opportunityBonus, asReaction, dodged: false));
                return;
            }

            var riposte = roll.IsCritical ? " — a critical, so a dodge would Riposte it" : string.Empty;
            OfferReaction(
                new PendingReaction(ReactionKind.PanicDodge, target, attacker, roll, plan.DodgeThreshold,
                    $"{attacker.Name}'s {attacker.Attack.Name} rolls {roll.Total} against {target.Name}'s DT {plan.DodgeThreshold}{riposte}. " +
                    "A panic dodge (1 Stamina, +2) would make it miss."),
                dodged => then(FinishAttack(attacker, target, roll, plan, opportunityBonus, asReaction, dodged)));
        }

        /// <summary>Standing their ground or Off-guard: either puts Disadvantage on an attacker's roll (Core Rules §9, §10).</summary>
        private static bool AttacksAtDisadvantage(Combatant attacker) => attacker.IsStandingGround || attacker.IsOffGuard;

        /// <summary>
        /// Whether +2 to the Threshold, decided after seeing the roll, would turn this success into
        /// a failure. Nothing answers a double twelve — "there is no Threshold to reach" (Core Rules §10).
        /// </summary>
        private static bool BoostWouldTurn(RollResult roll, int threshold) =>
            roll.Success == true
            && !roll.IsAbsoluteSuccess
            && roll.Total < threshold + ThresholdModifierCalculator.ReactionBonus;

        /// <summary>
        /// Offers <paramref name="offer"/>'s defender their 1-Stamina answer, if they can pay for it,
        /// and carries on with whether they took it — already paid for when they did. The GM brain
        /// answers for its own creatures, whose spends hand nobody an Opportunity; a character's
        /// answer is waited for, through <see cref="Pending"/>.
        /// </summary>
        private void OfferReaction(PendingReaction offer, Action<bool> then)
        {
            var defender = offer.Defender;
            if (TacticalSpendRefusal(defender) is not null)
            {
                then(false);
                return;
            }

            if (defender.Side == Side.Adversaries)
            {
                var takes = offer.Kind switch
                {
                    ReactionKind.PanicDodge => gm.PanicDodges(view, offer),
                    ReactionKind.StandFirm => gm.StandsFirm(view, offer),
                    _ => gm.ArrestsShove(view, offer),
                };

                if (takes)
                {
                    PayTacticalSpend(defender, CombatActionCatalog.ReactionStaminaCost);
                    Write(offer.Kind switch
                    {
                        ReactionKind.PanicDodge => $"{defender.Name} panic dodges (1 Stamina).",
                        ReactionKind.StandFirm => $"{defender.Name} stands firm (1 Stamina, +2 ST).",
                        _ => $"{defender.Name} arrests the shove (1 Stamina).",
                    });
                }

                then(takes);
                return;
            }

            Pending = offer;
            phaseBeforeReaction = Phase;
            Phase = EncounterPhase.AwaitingReaction;
            resumeAfterReaction = then;
            Write(offer.Prompt);
        }

        /// <summary>
        /// A Grab, Shove or Trip: an unarmed attack roll — 2d12 + Tier + the unarmed stat, or a foe's
        /// Attack bonus — against the target's Stability Threshold, with whatever Advantage or
        /// Disadvantage an attack would have. It deals no damage. The target may stand firm after
        /// seeing the roll (Core Rules §9, "Grab, Shove, Trip and Drag"; §10, "Stability Threshold").
        /// </summary>
        private void ResolveControl(Combatant actor, ControlAction action, Combatant target, bool opportunityBonus, Action then)
        {
            if (opportunityBonus)
            {
                Write($"The GM spends {SpendOpportunity()} Opportunity on {actor.Name}'s bonus: Advantage.");
            }

            var threshold = target.StabilityThreshold;
            var roll = RollAgainst(actor.UnarmedAttackModifier, threshold, opportunityBonus, AttacksAtDisadvantage(actor));
            actor.ActionsRemaining--;

            var verb = Verb(action);
            if (!BoostWouldTurn(roll, threshold))
            {
                FinishControl(actor, action, target, roll, threshold, stoodFirm: false, then);
                return;
            }

            OfferReaction(
                new PendingReaction(ReactionKind.StandFirm, target, actor, roll, threshold,
                    $"{actor.Name} tries to {verb} {target.Name}: {roll.Total} against ST {threshold}. " +
                    "Standing firm (1 Stamina, +2 ST) would stop it."),
                stood => FinishControl(actor, action, target, roll, threshold, stood, then));
        }

        private void FinishControl(Combatant actor, ControlAction action, Combatant target, RollResult rolled, int threshold, bool stoodFirm, Action then)
        {
            var roll = stoodFirm ? Reread(rolled, threshold + ThresholdModifierCalculator.ReactionBonus) : rolled;
            var line = $"{actor.Name} tries to {Verb(action)} {target.Name}: {DescribeRoll(roll)} vs ST {roll.Threshold}" +
                       (stoodFirm ? $" (+{ThresholdModifierCalculator.ReactionBonus} standing firm)" : string.Empty);

            if (roll.Success != true)
            {
                // A critical failure puts the one who lunged on the ground, in place of the wound a
                // failed attack costs (Core Rules §9, "Criticals"). Only a Dodge boost can Riposte,
                // so a critical that standing firm turned aside is merely a failure.
                var overbalanced = roll.IsCritical && rolled.Success != true;
                Write(line + (overbalanced ? $" — critical failure! {actor.Name} overbalances and goes down, Prone." : " — fails."));
                if (overbalanced)
                {
                    actor.SetCondition(CharacterCondition.Prone, true);
                }

                then();
                return;
            }

            ApplyControl(actor, action, target, roll.IsCritical, line, then);
        }

        /// <summary>
        /// What a Grab, Shove or Trip that worked does. "A critical success adds another effect
        /// instead": a Shove also trips, a Trip also shoves, a Grab leaves the target Off-guard for
        /// as long as the hold lasts (Core Rules §9, "Criticals").
        /// </summary>
        private void ApplyControl(Combatant actor, ControlAction action, Combatant target, bool critical, string line, Action then)
        {
            switch (action)
            {
                case ControlAction.Grab:
                    Write(line + (critical ? " — critical! Held, and Off-guard while the hold lasts." : $" — {target.Name} is Held."));
                    TakeHold(actor, target, offGuard: critical);
                    then();
                    return;

                case ControlAction.Trip:
                    Write(line + (critical ? $" — critical! {target.Name} goes down, and is shoved too." : $" — {target.Name} goes down, Prone."));
                    target.SetCondition(CharacterCondition.Prone, true);
                    if (critical)
                    {
                        Shove(actor, target, then);
                        return;
                    }

                    then();
                    return;

                default:
                    Write(line + (critical ? " — critical! A shove that also trips." : " — a shove."));
                    Shove(actor, target, () =>
                    {
                        if (critical && !target.IsOutOfFight)
                        {
                            target.SetCondition(CharacterCondition.Prone, true);
                            Write($"{target.Name} ends the push on the ground, Prone.");
                        }

                        then();
                    });
                    return;
            }
        }

        /// <summary>
        /// Forced Movement from a Shove (Core Rules §9): up to the shover's own reach, in a straight
        /// line away from them. It stops at anything solid — a creature, or the board's edge — and the
        /// distance left uncrossed is Body wounds, armour absorbing as usual. The target may arrest
        /// it for 1 Stamina, and leaving an enemy's reach on the way draws reactions.
        /// </summary>
        private void Shove(Combatant shover, Combatant target, Action then)
        {
            var step = (X: Math.Sign(target.Position.X - shover.Position.X), Y: Math.Sign(target.Position.Y - shover.Position.Y));
            var distance = shover.OwnReach;
            var destination = target.Position;
            var crossed = 0;
            string? stoppedBy = null;

            while (crossed < distance)
            {
                var next = new GridPosition(destination.X + step.X, destination.Y + step.Y);
                if (!IsOnBoard(next))
                {
                    stoppedBy = "the edge of the field";
                    break;
                }

                if (At(next) is { } inTheWay)
                {
                    stoppedBy = inTheWay.Name;
                    break;
                }

                destination = next;
                crossed++;
            }

            var impact = distance - crossed;
            var into = stoppedBy is null ? string.Empty : $" into {stoppedBy} — {impact} Body before armour";

            OfferReaction(
                new PendingReaction(ReactionKind.Arrest, target, shover, Roll: null, Threshold: 0,
                    $"{shover.Name} shoves {target.Name} {crossed} square(s){into}. Arresting it (1 Stamina) keeps them where they stand.",
                    ImpactWounds: impact),
                arrested =>
                {
                    if (arrested)
                    {
                        Write($"{target.Name} digs in and goes nowhere.");
                        then();
                        return;
                    }

                    // Shoved out of an enemy's reach is leaving it (Core Rules §9, "Forced Movement") —
                    // the shover's own included — and a foe shoved past somebody can be caught on the
                    // way through, where the push ends.
                    ReactToLeaving(target, PathBetween(target.Position, destination), LeavingReachOf(target, destination), caughtAt =>
                    {
                        // Caught on the way through, the push ends where the catch put them.
                        if (caughtAt is null && !target.IsOutOfFight)
                        {
                            target.Position = destination;
                            if (crossed > 0)
                            {
                                Write($"{target.Name} is pushed {crossed} square(s).");
                            }

                            if (impact > 0)
                            {
                                var absorbed = Math.Min(impact, InventoryCalculator.ArmourAbsorbedAgainst(target.Armour, WoundType.Body, isDirect: false));
                                TakeBodyWounds(target, impact - absorbed);
                                Write($"{target.Name} is driven into {stoppedBy}: {impact} Body" +
                                      (absorbed > 0 ? $", armour absorbs {absorbed}" : string.Empty) +
                                      (target.IsOutOfFight ? $", and is out of the fight." : $" — {target.Body}/{target.BodyMax} Body."));
                            }
                        }

                        CheckHolds();
                        then();
                    });
                });
        }

        /// <summary>
        /// The Held creature's action: "the same roll a Grab uses" against the holder's Stability
        /// Threshold, which the holder may raise by standing firm (Core Rules §9, "Held").
        /// </summary>
        private void ResolveBreakFree(Combatant breaker, Action then)
        {
            var holder = breaker.HeldBy!;
            var threshold = holder.StabilityThreshold;
            var roll = RollAgainst(breaker.UnarmedAttackModifier, threshold, advantage: false, AttacksAtDisadvantage(breaker));
            breaker.ActionsRemaining--;

            void Finish(bool stoodFirm)
            {
                var final = stoodFirm ? Reread(roll, threshold + ThresholdModifierCalculator.ReactionBonus) : roll;
                var line = $"{breaker.Name} strains against {holder.Name}'s hold: {DescribeRoll(final)} vs ST {final.Threshold}" +
                           (stoodFirm ? $" (+{ThresholdModifierCalculator.ReactionBonus} standing firm)" : string.Empty);
                if (final.Success == true)
                {
                    Write(line + " — breaks free!");
                    Release(holder, "loses their grip");
                }
                else
                {
                    Write(line + " — held fast.");
                }

                then();
            }

            if (!BoostWouldTurn(roll, threshold))
            {
                Finish(false);
                return;
            }

            OfferReaction(
                new PendingReaction(ReactionKind.StandFirm, holder, breaker, roll, threshold,
                    $"{breaker.Name} tries to break {holder.Name}'s hold: {roll.Total} against ST {threshold}. " +
                    "Standing firm (1 Stamina, +2 ST) would keep hold."),
                Finish);
        }

        /// <summary>Stands <paramref name="stander"/> up for a move — paid like a swap, and like a swap not movement.</summary>
        private void StandUp(Combatant stander)
        {
            var paidWith = PayMoves(stander, 1);
            stander.SetCondition(CharacterCondition.Prone, false);
            Write($"{stander.Name} gets to their feet, for {paidWith}.");
        }

        private void TakeHold(Combatant holder, Combatant target, bool offGuard)
        {
            holder.Holding = target;
            target.HeldBy = holder;
            target.SetCondition(CharacterCondition.Held, true);
            if (offGuard)
            {
                target.SetCondition(CharacterCondition.OffGuard, true);
            }
        }

        /// <summary>Ends <paramref name="holder"/>'s hold, and with it the Held — and a critical Grab's Off-guard.</summary>
        private void Release(Combatant holder, string how)
        {
            if (holder.Holding is not { } held)
            {
                return;
            }

            holder.Holding = null;
            held.HeldBy = null;
            held.SetCondition(CharacterCondition.Held, false);
            held.SetCondition(CharacterCondition.OffGuard, false);
            Write($"{holder.Name} {how} — {held.Name} is no longer Held.");
        }

        /// <summary>
        /// Ends every hold the rules say is over (Core Rules §9, "Held"): the holder out of the fight,
        /// either of them out of the holder's own reach, or the holding hand taken up with something.
        /// Called after anything that could change one of those.
        /// </summary>
        private void CheckHolds()
        {
            foreach (var holder in combatants.Where(c => c.Holding is not null).ToList())
            {
                var held = holder.Holding!;
                var why = holder.IsOutOfFight ? "is in no state to hold on"
                    : held.IsOutOfFight ? "lets the body drop"
                    : holder.Position.DistanceTo(held.Position) > holder.OwnReach ? "can no longer reach to hold on"
                    : !holder.HasFreeHand ? "has no hand free to hold with"
                    : null;

                if (why is not null)
                {
                    Release(holder, why);
                }
            }
        }

        /// <summary>2d12 + <paramref name="modifier"/> against <paramref name="threshold"/>, a third die rolled for Advantage or Disadvantage.</summary>
        private RollResult RollAgainst(int modifier, int threshold, bool advantage, bool disadvantage)
        {
            var rolled = Enumerable.Range(0, RollCalculator.DiceNeeded(advantage, disadvantage))
                .Select(_ => dice.Roll(RollCalculator.HighestFace))
                .ToList();
            return RollCalculator.Resolve(rolled, modifier, threshold, advantage, disadvantage);
        }

        /// <summary>The same dice, read against a raised Threshold.</summary>
        private static RollResult Reread(RollResult roll, int threshold) =>
            RollCalculator.Resolve(roll.AllRolledDice, roll.Modifier, threshold, roll.AppliedAdvantage, roll.AppliedDisadvantage);

        private static string Verb(ControlAction action) => action switch
        {
            ControlAction.Grab => "grab",
            ControlAction.Shove => "shove",
            _ => "trip",
        };

        /// <summary>"(5+3)+4 = 12", with any dropped third die.</summary>
        private static string DescribeRoll(RollResult roll)
        {
            var sign = roll.Modifier >= 0 ? "+" : "−";
            var dropped = RollCalculator.DroppedDice(roll);
            var extra = roll.AppliedAdvantage ? $" (Advantage, dropped {string.Join(", ", dropped)})"
                : roll.AppliedDisadvantage ? $" (Disadvantage, dropped {string.Join(", ", dropped)})"
                : string.Empty;
            return $"({string.Join("+", roll.CountedDice)}){sign}{Math.Abs(roll.Modifier)} = {roll.Total}{extra}";
        }

        private AttackOutcome FinishAttack(
            Combatant attacker, Combatant target, RollResult rolled, AttackPlan plan, bool usedOpportunity, bool asReaction, bool dodged)
        {
            // Re-read the same dice against the raised Threshold. A dodged critical comes back a
            // critical failure, which is exactly what a Riposte is (Core Rules §10).
            var roll = dodged ? Reread(rolled, plan.DodgeThreshold + ThresholdModifierCalculator.ReactionBonus) : rolled;

            int rolledDamage = 0, absorbed = 0, dealt = 0;
            var attackerWounded = false;

            if (roll.Success == true)
            {
                rolledDamage = RollDamage(attacker.Attack, plan, roll.IsCritical);
                absorbed = Math.Min(rolledDamage,
                    InventoryCalculator.ArmourAbsorbedAgainst(target.Armour, WoundType.Body, isDirect: false));
                dealt = rolledDamage - absorbed;
                TakeBodyWounds(target, dealt);
            }
            else if (roll.IsCritical)
            {
                // A critical failure earns the attacker a wound of the type their attack would have
                // dealt (Core Rules §10) — their own overcommitment, which no armour stands between.
                attackerWounded = true;
                TakeBodyWounds(attacker, 1);
            }

            var outcome = new AttackOutcome(attacker, target, roll, plan, rolledDamage, absorbed, dealt, attackerWounded,
                usedOpportunity, asReaction, dodged);
            Write(Describe(outcome));
            return outcome;
        }

        /// <summary>
        /// The damage dice plus modifier, or on a critical "the maximum result for that action plus
        /// an additional dice" (Core Rules §10). Range may have shrunk the die or taken it away, but
        /// a critical's bonus die is always the weapon's true base die (Core Rules §9, "Critical hits
        /// at range").
        /// </summary>
        private int RollDamage(AttackProfile attack, AttackPlan plan, bool critical)
        {
            var dieTotal = plan.DamageDieSides is { } sides
                ? critical
                    ? attack.DamageDiceCount * sides
                    : Enumerable.Range(0, attack.DamageDiceCount).Sum(_ => dice.Roll(sides))
                : 0;

            if (critical)
            {
                dieTotal += dice.Roll(attack.DamageDieSides);
            }

            return Math.Max(PlayStateCalculator.DamageFloor, dieTotal + attack.DamageModifier);
        }

        private static void TakeBodyWounds(Combatant combatant, int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            if (combatant.Side == Side.Party)
            {
                (combatant.Body, combatant.IsStable) = PlayStateCalculator.ApplyBodyDamage(combatant.Body, combatant.IsStable, amount);
            }
            else
            {
                combatant.Body = combatant.IsMinion ? 0 : Math.Max(0, combatant.Body - amount);
            }
        }

        private static void PayTacticalSpend(Combatant spender, int stamina)
        {
            spender.Stamina -= stamina;
            spender.TacticalSpendsThisRound++;
        }

        private void GrantBankedOpportunity()
        {
            if (BankedOpportunities >= MaxBankedOpportunities)
            {
                Write($"The GM's bank already holds {MaxBankedOpportunities} Opportunities, so this one is lost.");
                return;
            }

            BankedOpportunities++;
            Write("The GM banks an Opportunity.");
        }

        private void GrantImmediateOpportunity(Combatant spender)
        {
            immediateOpportunities.Add(spender);
            Write($"The GM gains an Immediate Opportunity, to use before {spender.Name}'s next turn.");
        }

        /// <summary>Spends one Opportunity — an Immediate one first, since it lapses — and says which kind, for the log.</summary>
        private string SpendOpportunity()
        {
            if (immediateOpportunities.Count > 0)
            {
                immediateOpportunities.RemoveAt(0);
                return "an Immediate";
            }

            BankedOpportunities--;
            return "a banked";
        }

        private void ExpireImmediateOpportunities(Combatant startingTurn)
        {
            var lapsed = immediateOpportunities.RemoveAll(source => source == startingTurn);
            if (lapsed > 0)
            {
                Write($"{lapsed} Immediate Opportunity from {startingTurn.Name}'s spend lapses unused.");
            }
        }

        private static void FinishTurn(Combatant combatant)
        {
            combatant.HasMove = false;
            combatant.ActionsRemaining = 0;
        }

        private bool CheckForEnd()
        {
            if (IsOver)
            {
                return true;
            }

            // Whoever just went down lets go, or is let go of.
            CheckHolds();
            CheckMorale();

            if (!Fighting(Side.Party).Any())
            {
                Phase = EncounterPhase.Defeat;
                Active = null;
                Write("The party has fallen.");
                ReportObjectives();
                return true;
            }

            CheckDepartures();

            // The fight stays in effect until every adversary has left the board — down, gone
            // beyond Long of every character, or given up. Broken and held fast, with nobody left
            // free to fight on beside them, they give up.
            var stillOnField = Fighting(Side.Adversaries).ToList();
            if (stillOnField.Count > 0 && stillOnField.All(a => a.IsBroken && a.HeldBy is not null))
            {
                foreach (var caught in stillOnField)
                {
                    caught.HasSurrendered = true;
                    Write($"{caught.Name}, broken and held fast by {caught.HeldBy!.Name}, gives up.");
                }

                stillOnField.Clear();
            }

            if (stillOnField.Count == 0)
            {
                Phase = EncounterPhase.Victory;
                Active = null;
                Write(combatants.Any(c => c.Side == Side.Adversaries && c.HasFled)
                    ? "No foe is left on the board. Victory!"
                    : "The last foe falls. Victory!");
                ReportObjectives();
                return true;
            }

            return false;
        }

        /// <summary>Every free square <paramref name="mover"/> could reach for at most <paramref name="moves"/> moves, with the price of each.</summary>
        internal Dictionary<GridPosition, int> SquaresWithin(Combatant mover, int moves)
        {
            var squares = new Dictionary<GridPosition, int>();
            for (var y = 0; y < Height; y++)
            {
                for (var x = 0; x < Width; x++)
                {
                    var square = new GridPosition(x, y);
                    if (PriceOfMove(mover, square) is { } cost && cost <= moves)
                    {
                        squares[square] = cost;
                    }
                }
            }

            return squares;
        }

        /// <summary>What a move to <paramref name="square"/> is priced at, whatever is left to pay with; null where nobody could stop there.</summary>
        private int? PriceOfMove(Combatant mover, GridPosition square) =>
            !IsOnBoard(square) || square == mover.Position || At(square) is not null
                ? null
                : RangeBandCalculator.MoveCost(bands, mover.Position.DistanceTo(square), mover.Speed);

        private static string Describe(AttackOutcome outcome)
        {
            var roll = outcome.Roll;
            var dice = string.Join("+", roll.CountedDice);
            var dropped = RollCalculator.DroppedDice(roll);
            var advantage = roll.AppliedAdvantage ? $" (Advantage, dropped {string.Join(", ", dropped)})"
                : roll.AppliedDisadvantage ? $" (Disadvantage for {DisadvantageReason(outcome.Attacker)}, dropped {string.Join(", ", dropped)})"
                : outcome.UsedOpportunity ? " (Advantage and Disadvantage cancel)"
                : string.Empty;
            var sign = roll.Modifier >= 0 ? "+" : "−";
            var range = outcome.Plan.RangeDodgeModifier > 0 ? $" (+{outcome.Plan.RangeDodgeModifier} for range)" : string.Empty;
            var dodge = outcome.Dodged ? $" (+{ThresholdModifierCalculator.ReactionBonus} panic dodge)" : string.Empty;
            var verb = outcome.AsReaction ? "strikes at" : "attacks";
            var line = $"{outcome.Attacker.Name} {verb} {outcome.Target.Name} with {outcome.Attacker.Attack.Name}: " +
                       $"({dice}){sign}{Math.Abs(roll.Modifier)} = {roll.Total} vs DT {roll.Threshold}{range}{dodge}{advantage}";

            if (roll.Success == true)
            {
                var critical = roll.IsAbsoluteSuccess ? " — double twelve!" : roll.IsCritical ? " — critical hit!" : " — hit";
                line += $"{critical} {outcome.RolledDamage} damage";
                line += outcome.Absorbed > 0 ? $", armour absorbs {outcome.Absorbed}, {outcome.Dealt} Body." : " Body.";

                line += outcome.Target.IsOutOfFight
                    ? $" {outcome.Target.Name} is out of the fight."
                    : $" {outcome.Target.Name} has {outcome.Target.Body}/{outcome.Target.BodyMax} Body.";
            }
            else
            {
                line += outcome.Dodged && roll.IsCritical ? " — Riposte! The critical hit becomes a critical failure."
                    : roll.IsAbsoluteFailure ? " — double one!"
                    : roll.IsCritical ? " — critical miss!"
                    : " — miss.";
                if (outcome.AttackerWounded)
                {
                    line += $" {outcome.Attacker.Name} overcommits and takes a wound";
                    line += outcome.Attacker.IsOutOfFight ? ", and is out of the fight." : ".";
                }
            }

            return line;
        }

        /// <summary>
        /// Breaks every creature that has reached its breaking point (GM Reference §8, "Let
        /// non-Solo adversaries break"): it stops being willing to fight, and looks for a way out.
        /// </summary>
        private void CheckMorale()
        {
            foreach (var creature in Fighting(Side.Adversaries).Where(a => !a.IsBroken && a.Morale is not null).ToList())
            {
                var alliesDown = combatants.Count(c => c.Side == Side.Adversaries && c != creature
                    && c.IsOutOfFight && !c.HasFled && !c.HasSurrendered);
                if (creature.Morale!.IsBroken(creature, alliesDown))
                {
                    creature.IsBroken = true;
                    Write($"{creature.Name} has had enough ({creature.Morale.Trait}) — it breaks, and looks for a way out.");
                }
            }
        }

        /// <summary>
        /// Takes off the board every adversary no character has within Long — it has got clear,
        /// and takes no further part. Whatever it was holding, it let go of to run.
        /// </summary>
        private void CheckDepartures()
        {
            foreach (var gone in Fighting(Side.Adversaries).Where(a => !IsWithinLongOfParty(a.Position)).ToList())
            {
                Release(gone, "lets go");
                gone.HasFled = true;
                Write($"{gone.Name} is beyond Long of everyone, and off the board.");
            }
        }

        private void ReportObjectives()
        {
            foreach (var objective in Objectives)
            {
                Write($"Objective — {objective.Name}: {(objective.StatusIn(this) == ObjectiveStatus.Achieved ? "achieved" : "failed")}.");
            }
        }

        private static string DisadvantageReason(Combatant attacker) => (attacker.IsStandingGround, attacker.IsOffGuard) switch
        {
            (true, true) => "standing their ground and being Off-guard",
            (true, false) => "standing their ground",
            _ => "being Off-guard",
        };

        private void Write(string line)
        {
            log.Add(line);
            Logged?.Invoke(line);
        }
    }
}
