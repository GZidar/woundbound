using WoundBound.Rules.Calculation;
using WoundBound.Rules.Catalog;
using WoundBound.Rules.Enums;
using WoundBound.Rules.Models;

namespace WoundBound.Game.Combat
{
    public enum EncounterPhase
    {
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
    /// A hit the defender can still turn into a miss: the roll is known, it lands, and +2 Dodge
    /// Threshold would put it out of reach (Core Rules §10, "Dodge Threshold"). Against a critical
    /// hit that is a Riposte, and the attacker is wounded instead.
    /// </summary>
    public sealed record PendingReaction(Combatant Defender, Combatant Attacker, RollResult Roll, int DodgeThreshold)
    {
        public int RaisedDodgeThreshold => DodgeThreshold + ThresholdModifierCalculator.ReactionBonus;

        public bool WouldRiposte => Roll.IsCritical;
    }

    /// <summary>
    /// One combat challenge on a grid (Core Rules §9). Owns the turn structure and asks the Rules
    /// library for every number: what a move costs, whether an attack lands, what armour takes off.
    ///
    /// <para>Round structure follows "Rounds &amp; Turn Order": every party member takes one turn,
    /// and in the gap after each one the GM is owed one adversary turn, never more owed turns in a
    /// round than there are characters. That cap is counted when the fight begins and never falls,
    /// however many characters go down. Opportunities buy turns past it ("GM Opportunities"). At
    /// the round's end every adversary that took no turn closes in with a single move. The slice plays the party in list order rather than letting the players choose, which only
    /// matters once there is more than one of them.</para>
    /// </summary>
    public sealed class Encounter
    {
        /// <summary>"The GM may hold a maximum of 3 banked Opportunities at any time" (Core Rules §9).</summary>
        public const int MaxBankedOpportunities = 3;

        private readonly List<Combatant> combatants;
        private readonly RangeBandTables bands;
        private readonly IDiceRoller dice;
        private readonly List<string> log = new();
        private readonly Queue<Combatant> partyStillToAct = new();

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

        public Encounter(int width, int height, IEnumerable<Combatant> combatants, RangeBandTables bands, IDiceRoller dice)
        {
            Width = width;
            Height = height;
            this.combatants = combatants.ToList();
            this.bands = bands;
            this.dice = dice;

            StartRound();
        }

        public int Width { get; }

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
            PriceOfMove(combatant, square) is { } cost && cost <= MovesAvailable(combatant) ? cost : null;

        /// <summary>Every square the active character could move to now, with what each would cost.</summary>
        public IReadOnlyDictionary<GridPosition, int> ReachableSquares(Combatant combatant) =>
            SquaresWithin(combatant, MovesAvailable(combatant));

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
        public AttackPlan PlanAttack(Combatant attacker, Combatant target)
        {
            var attack = attacker.Attack;
            var distance = attacker.Position.DistanceTo(target.Position);

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

            var from = mover.Position;
            var paidWith = PayMoves(mover, cost);

            // "Leaving an enemy's reach is a trigger" (Core Rules §9, "Movement"): from a square
            // within that enemy's Reach to one outside it. The slice moves in a straight hop, so the
            // squares it starts and ends on are the whole of the question.
            var leftBehind = new Queue<Combatant>(Fighting(Side.Adversaries)
                .Where(a => a.CanReach(from) && !a.CanReach(square)));

            StrikeAtLeaver(mover, leftBehind, () =>
            {
                if (!mover.IsOutOfFight)
                {
                    mover.Position = square;
                    var band = RangeBandCalculator.BandForSquares(bands, from.DistanceTo(square));
                    Write($"{mover.Name} moves {from.DistanceTo(square)} square(s) ({band}) for {paidWith}.");
                }

                CheckForEnd();
            });

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
        /// Answers <see cref="Pending"/>: <paramref name="dodge"/> spends 1 Stamina for +2 Dodge
        /// Threshold against the roll (a tactical spend, handing the GM an Immediate Opportunity);
        /// otherwise the blow lands as rolled. Returns false if there was nothing to answer, or the
        /// dodge can no longer be paid for.
        /// </summary>
        public bool RespondToReaction(bool dodge)
        {
            if (Phase != EncounterPhase.AwaitingReaction || Pending is not { } pending || resumeAfterReaction is not { } resume)
            {
                return false;
            }

            if (dodge)
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
            resume(dodge);
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
                actor = ChooseOwedActor();
            }
            else if (ChooseExtraTurnActor() is { } bought)
            {
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
            foreach (var member in combatants.Where(c => c.Side == Side.Party))
            {
                partyStillToAct.Enqueue(member);
            }

            // "The cap is set when the fight begins, and it never falls" (Core Rules §9): counted
            // once, raised by anyone who joins, never lowered by anyone who drops.
            adversaryTurnCap = ChallengeCalculator.AdversaryTurnCap(adversaryTurnCap, partyStillToAct.Count);
            adversaryTurnsRemaining = adversaryTurnCap.Value;
            BeginNextPartyTurn();
        }

        public int AdversaryTurnsPerRound => adversaryTurnCap ?? 0;

        private void BeginNextPartyTurn()
        {
            while (partyStillToAct.TryDequeue(out var next))
            {
                if (next.IsOutOfFight)
                {
                    Write($"{next.Name} has no turn to take.");
                    AfterPartyTurn();
                    return;
                }

                ExpireImmediateOpportunities(next);

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
                return;
            }

            AfterPartyTurn();
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

            if (owedInGap > 0 || ChooseExtraTurnActor() is not null)
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
                CloseIn();
                if (!CheckForEnd())
                {
                    StartRound();
                }
            }
        }

        /// <summary>
        /// "Adversaries that didn't act close in" (Core Rules §9): after the GM's last turn of the
        /// round, each adversary that took no turn may take one move — no action, nothing riding on
        /// it — and it isn't a turn. The GM walks each toward the nearest character, nearest first,
        /// and leaves where it is any that can already strike.
        /// </summary>
        private void CloseIn()
        {
            var idle = Fighting(Side.Adversaries)
                .Where(a => !a.HasActedThisRound)
                .OrderBy(a => NearestTarget(a).Position.DistanceTo(a.Position))
                .ToList();

            foreach (var adversary in idle)
            {
                var target = NearestTarget(adversary);
                if (adversary.CanReach(target.Position))
                {
                    continue;
                }

                var destination = StrikeSquare(adversary, target)
                    ?? SquaresWithin(adversary, 1).Keys
                        .Where(square => square.DistanceTo(target.Position) < adversary.Position.DistanceTo(target.Position))
                        .OrderBy(square => square.DistanceTo(target.Position))
                        .ThenBy(square => square.DistanceTo(adversary.Position))
                        .Select(square => (GridPosition?)square)
                        .FirstOrDefault();

                if (destination is not { } square)
                {
                    continue;
                }

                // A move is a straight hop in the slice; heading for the nearest character never
                // leaves a character's reach, so no reaction is drawn (PC reaction attacks aren't in yet).
                var from = adversary.Position;
                adversary.Position = square;
                var band = RangeBandCalculator.BandForSquares(bands, from.DistanceTo(square));
                Write($"{adversary.Name} didn't act this round, and closes in {from.DistanceTo(square)} square(s) ({band}).");
            }
        }

        /// <summary>
        /// The GM's side of the table, played simply: whoever can already strike a character, or
        /// else whoever is closest to doing so, goes for the nearest character — stepping into reach
        /// with the move and keeping the action for the blow when it can, and spending both on
        /// closing the distance when it can't.
        /// </summary>
        private Combatant ChooseOwedActor()
        {
            var targets = Fighting(Side.Party).ToList();
            return Fighting(Side.Adversaries)
                .OrderBy(a => CanStrikeThisTurn(a) ? 0 : 1)
                .ThenBy(a => targets.Min(t => a.Position.DistanceTo(t.Position)))
                .First();
        }

        /// <summary>
        /// Who the GM would buy an extra turn for, if anybody. GM Reference §7: spend on
        /// overextension, not on openings — so only on an adversary that can strike a character on
        /// that turn, preferring one that has already acted, since chaining two turns back to back
        /// is the strongest use of one. Immediate Opportunities are spent first, since they lapse.
        /// </summary>
        private Combatant? ChooseExtraTurnActor()
        {
            if (GmOpportunities == 0)
            {
                return null;
            }

            var targets = Fighting(Side.Party).ToList();
            return Fighting(Side.Adversaries)
                .Where(CanStrikeThisTurn)
                .OrderBy(a => a.HasActedThisRound ? 0 : 1)
                .ThenBy(a => targets.Min(t => a.Position.DistanceTo(t.Position)))
                .FirstOrDefault();
        }

        private void TakeAdversaryTurn(Combatant actor, bool allowOpportunityBonus, Action done)
        {
            var target = NearestTarget(actor);

            Active = actor;
            actor.HasMove = true;
            actor.ActionsRemaining = 1;

            if (!actor.CanReach(target.Position))
            {
                var squares = SquaresWithin(actor, MovesAvailable(actor))
                    .Select(o => (Square: o.Key, Cost: o.Value))
                    .ToList();

                (GridPosition Square, int Cost)? strikeFrom = StrikeSquare(actor, target) is { } strike ? (strike, 1) : null;
                (GridPosition Square, int Cost)? closeIn = squares.Count > 0
                    ? squares.OrderBy(o => o.Square.DistanceTo(target.Position)).ThenBy(o => o.Cost).First()
                    : null;

                if ((strikeFrom ?? closeIn) is { } move)
                {
                    Move(actor, move.Square, move.Cost);
                }
            }

            if (CanAttack(actor, target))
            {
                // The Opportunity bonus goes to "an adversary yet to act this round" (Core Rules §9).
                // An extra turn is usually worth more, so the GM only reaches for the bonus when the
                // bank is full and another banked Opportunity would be wasted.
                var bonus = allowOpportunityBonus && !actor.HasActedThisRound && BankedOpportunities >= MaxBankedOpportunities;
                ResolveAttack(actor, target, asReaction: false, bonus, _ =>
                {
                    FinishTurn(actor);
                    done();
                });
                return;
            }

            if (actor.HasMove)
            {
                Write($"{actor.Name} holds its ground.");
            }

            FinishTurn(actor);
            done();
        }

        /// <summary>
        /// Lets each enemy whose reach <paramref name="mover"/> is leaving strike at them as they
        /// go — for 1 Stamina, a tactical spend like any reaction (Core Rules §9, "Movement"). An
        /// adversary's spend hands the GM nothing; Opportunities come only from the players' own
        /// choices (GM Reference §1).
        /// </summary>
        private void StrikeAtLeaver(Combatant mover, Queue<Combatant> leftBehind, Action then)
        {
            while (leftBehind.TryDequeue(out var enemy))
            {
                if (mover.IsOutOfFight)
                {
                    break;
                }

                if (TacticalSpendRefusal(enemy) is not null || !PlanAttack(enemy, mover).CanAttack)
                {
                    continue;
                }

                PayTacticalSpend(enemy, CombatActionCatalog.ReactionStaminaCost);
                Write($"{mover.Name} leaves {enemy.Name}'s reach, and it strikes as they go (1 Stamina).");
                ResolveAttack(enemy, mover, asReaction: true, opportunityBonus: false, _ => StrikeAtLeaver(mover, leftBehind, then));
                return;
            }

            then();
        }

        private void Move(Combatant mover, GridPosition square, int cost)
        {
            var from = mover.Position;
            mover.Position = square;
            var paidWith = PayMoves(mover, cost);

            var band = RangeBandCalculator.BandForSquares(bands, from.DistanceTo(square));
            Write($"{mover.Name} moves {from.DistanceTo(square)} square(s) ({band}) for {paidWith}.");
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

            var rolled = Enumerable.Range(0, RollCalculator.DiceNeeded(opportunityBonus, disadvantage: false))
                .Select(_ => dice.Roll(RollCalculator.HighestFace))
                .ToList();
            var roll = RollCalculator.Resolve(rolled, attacker.Attack.AttackModifier, plan.DodgeThreshold, opportunityBonus, disadvantage: false);

            if (!asReaction)
            {
                attacker.ActionsRemaining--;
            }

            if (!DodgeWouldTurn(roll, plan.DodgeThreshold) || TacticalSpendRefusal(target) is not null)
            {
                then(FinishAttack(attacker, target, roll, plan, opportunityBonus, asReaction, dodged: false));
                return;
            }

            if (target.Side == Side.Adversaries)
            {
                // The GM spends its creature's Stamina to turn any hit it can. A Minion dropped by any
                // blow that lands has every reason to; and its spend hands nobody an Opportunity.
                PayTacticalSpend(target, CombatActionCatalog.ReactionStaminaCost);
                Write($"{target.Name} panic dodges (1 Stamina).");
                then(FinishAttack(attacker, target, roll, plan, opportunityBonus, asReaction, dodged: true));
                return;
            }

            Pending = new PendingReaction(target, attacker, roll, plan.DodgeThreshold);
            phaseBeforeReaction = Phase;
            Phase = EncounterPhase.AwaitingReaction;
            resumeAfterReaction = dodged => then(FinishAttack(attacker, target, roll, plan, opportunityBonus, asReaction, dodged));

            var riposte = roll.IsCritical ? " — a critical, so a dodge would Riposte it" : string.Empty;
            Write($"{attacker.Name}'s {attacker.Attack.Name} rolls {roll.Total} against {target.Name}'s DT {plan.DodgeThreshold}{riposte}. " +
                  $"A panic dodge (1 Stamina, +2) would make it miss.");
        }

        /// <summary>
        /// Whether +2 Dodge Threshold, decided after seeing the roll, would turn this hit into a miss.
        /// Nothing answers a double twelve — "there is no Threshold to reach" (Core Rules §10).
        /// </summary>
        private static bool DodgeWouldTurn(RollResult roll, int dodgeThreshold) =>
            roll.Success == true
            && !roll.IsAbsoluteSuccess
            && roll.Total < dodgeThreshold + ThresholdModifierCalculator.ReactionBonus;

        private AttackOutcome FinishAttack(
            Combatant attacker, Combatant target, RollResult rolled, AttackPlan plan, bool usedOpportunity, bool asReaction, bool dodged)
        {
            // Re-read the same dice against the raised Threshold. A dodged critical comes back a
            // critical failure, which is exactly what a Riposte is (Core Rules §10).
            var roll = dodged
                ? RollCalculator.Resolve(rolled.AllRolledDice, rolled.Modifier, plan.DodgeThreshold + ThresholdModifierCalculator.ReactionBonus,
                    rolled.AppliedAdvantage, rolled.AppliedDisadvantage)
                : rolled;

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

            if (!Fighting(Side.Party).Any())
            {
                Phase = EncounterPhase.Defeat;
                Active = null;
                Write("The party has fallen.");
                return true;
            }

            if (!Fighting(Side.Adversaries).Any())
            {
                Phase = EncounterPhase.Victory;
                Active = null;
                Write("The last foe falls. Victory!");
                return true;
            }

            return false;
        }

        private Combatant NearestTarget(Combatant actor) =>
            Fighting(actor.Side == Side.Party ? Side.Adversaries : Side.Party).MinBy(t => actor.Position.DistanceTo(t.Position))!;

        /// <summary>Whether <paramref name="adversary"/> could hit a character on a fresh turn: from where it stands, or after a single move.</summary>
        private bool CanStrikeThisTurn(Combatant adversary)
        {
            var target = NearestTarget(adversary);
            return adversary.CanReach(target.Position) || StrikeSquare(adversary, target) is not null;
        }

        /// <summary>The nearest square one move away from which <paramref name="actor"/> can strike <paramref name="target"/>, leaving the action for the blow.</summary>
        private GridPosition? StrikeSquare(Combatant actor, Combatant target)
        {
            var origin = actor.Position;
            return SquaresWithin(actor, 1).Keys
                .Where(square => square.DistanceTo(target.Position) <= actor.Reach
                                 && !(actor.Attack.IsUnwieldy && square.DistanceTo(target.Position) <= 1))
                .OrderBy(square => square.DistanceTo(origin))
                .Select(square => (GridPosition?)square)
                .FirstOrDefault();
        }

        /// <summary>Every free square <paramref name="mover"/> could reach for at most <paramref name="moves"/> moves, with the price of each.</summary>
        private Dictionary<GridPosition, int> SquaresWithin(Combatant mover, int moves)
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
            var advantage = outcome.UsedOpportunity
                ? $" (Advantage, dropped {string.Join(", ", dropped)})"
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

        private void Write(string line)
        {
            log.Add(line);
            Logged?.Invoke(line);
        }
    }
}
