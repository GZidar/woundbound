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

        /// <summary>The GM is owed an adversary turn; <see cref="Encounter.RunAdversaryTurn"/> takes it.</summary>
        AdversaryTurn,

        Victory,
        Defeat
    }

    /// <summary>Everything one attack roll did, for the log and for anything that animates it.</summary>
    public sealed record AttackOutcome(
        Combatant Attacker,
        Combatant Target,
        RollResult Roll,
        int RolledDamage,
        int Absorbed,
        int Dealt,
        bool AttackerWounded,
        bool UsedOpportunity);

    /// <summary>
    /// One combat challenge on a grid (Core Rules §9). Owns the turn structure and asks the Rules
    /// library for every number: what a move costs, whether an attack lands, what armour takes off.
    ///
    /// <para>Round structure follows "Rounds &amp; Turn Order": every party member takes one turn,
    /// and after each one the GM takes one adversary turn, never more adversary turns in a round
    /// than there are characters. The slice plays the party in list order rather than letting the
    /// players choose, which only matters once there is more than one of them.</para>
    /// </summary>
    public sealed class Encounter
    {
        private readonly List<Combatant> combatants;
        private readonly RangeBandTables bands;
        private readonly IDiceRoller dice;
        private readonly List<string> log = new();
        private readonly Queue<Combatant> partyStillToAct = new();
        private int adversaryTurnsRemaining;

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

        /// <summary>Whoever is taking a turn. Null while the GM is owed a turn and once the fight is over.</summary>
        public Combatant? Active { get; private set; }

        /// <summary>
        /// GM Opportunities banked and not yet spent. Every tactical Stamina spend hands the GM one
        /// (Core Rules §6, §9); the Ratkin's statblock spends one for Advantage on an attack.
        /// </summary>
        public int GmOpportunities { get; private set; }

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
        public int? MoveCostTo(Combatant combatant, GridPosition square)
        {
            if (!IsOnBoard(square) || square == combatant.Position || At(square) is not null)
            {
                return null;
            }

            var cost = RangeBandCalculator.MoveCost(bands, combatant.Position.DistanceTo(square), combatant.Speed);
            return cost is { } moves && moves <= MovesAvailable(combatant) ? moves : null;
        }

        /// <summary>Every square the active character could move to now, with what each would cost.</summary>
        public IReadOnlyDictionary<GridPosition, int> ReachableSquares(Combatant combatant)
        {
            var squares = new Dictionary<GridPosition, int>();
            foreach (var square in AllSquares())
            {
                if (MoveCostTo(combatant, square) is { } cost)
                {
                    squares[square] = cost;
                }
            }

            return squares;
        }

        public bool CanAttack(Combatant attacker, Combatant target) =>
            attacker.Side != target.Side
            && !target.IsOutOfFight
            && attacker.ActionsRemaining > 0
            && attacker.CanReach(target.Position);

        /// <summary>
        /// Why the active character can't buy an extra action with Stamina right now, or null if
        /// they can (Core Rules §6, "Stamina Limit").
        /// </summary>
        public string? ExtraActionRefusal(Combatant combatant)
        {
            if (combatant.Stamina < CombatActionCatalog.ExtraActionStaminaCost)
            {
                return "No Stamina left to spend.";
            }

            if (TacticalSpendCalculator.IsAtLimit(combatant.TacticalSpendsThisRound, combatant.TacticalSpendsPerRound))
            {
                return $"Already made {combatant.TacticalSpendsThisRound} tactical spend(s) this round — the limit at this Tier.";
            }

            return null;
        }

        public bool TryMove(GridPosition square)
        {
            if (Phase != EncounterPhase.PartyTurn || Active is not { } mover || MoveCostTo(mover, square) is not { } cost)
            {
                return false;
            }

            Move(mover, square, cost);
            return true;
        }

        public AttackOutcome? TryAttack(Combatant target)
        {
            if (Phase != EncounterPhase.PartyTurn || Active is not { } attacker || !CanAttack(attacker, target))
            {
                return null;
            }

            var outcome = ResolveAttack(attacker, target, spendOpportunity: false);
            CheckForEnd();
            return outcome;
        }

        /// <summary>Spends 1 Stamina for an extra action this turn (Core Rules §6).</summary>
        public bool TryBuyExtraAction()
        {
            if (Phase != EncounterPhase.PartyTurn || Active is not { } buyer || ExtraActionRefusal(buyer) is not null)
            {
                return false;
            }

            buyer.Stamina -= CombatActionCatalog.ExtraActionStaminaCost;
            buyer.TacticalSpendsThisRound++;
            buyer.ActionsRemaining++;
            GmOpportunities++;

            Write($"{buyer.Name} spends 1 Stamina for an extra action ({buyer.Stamina} left). The GM gains an Opportunity.");
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
        /// Takes the adversary turn the GM is owed. A separate step from <see cref="EndTurn"/> so
        /// whatever shows the fight can pause between the two.
        /// </summary>
        public void RunAdversaryTurn()
        {
            if (Phase != EncounterPhase.AdversaryTurn)
            {
                return;
            }

            TakeAdversaryTurn();
            adversaryTurnsRemaining--;

            if (CheckForEnd())
            {
                return;
            }

            if (partyStillToAct.Count > 0)
            {
                BeginNextPartyTurn();
            }
            else if (adversaryTurnsRemaining > 0)
            {
                // A held turn, taken in the gap after the round's last character (Core Rules §9).
                Phase = EncounterPhase.AdversaryTurn;
            }
            else
            {
                StartRound();
            }
        }

        private void StartRound()
        {
            Round++;
            Write($"— Round {Round} —");

            foreach (var combatant in combatants)
            {
                combatant.TacticalSpendsThisRound = 0;
            }

            partyStillToAct.Clear();
            foreach (var member in Fighting(Side.Party))
            {
                partyStillToAct.Enqueue(member);
            }

            adversaryTurnsRemaining = partyStillToAct.Count;
            BeginNextPartyTurn();
        }

        private void BeginNextPartyTurn()
        {
            while (partyStillToAct.TryDequeue(out var next))
            {
                if (next.IsOutOfFight)
                {
                    continue;
                }

                if (next.Stamina <= 0)
                {
                    // "If a character starts their turn with 0 Stamina, they fall unconscious" (Core Rules §6).
                    next.IsUnconscious = true;
                    Write($"{next.Name} starts their turn with no Stamina and falls unconscious.");
                    if (CheckForEnd())
                    {
                        return;
                    }

                    AfterPartyTurn();
                    return;
                }

                Active = next;
                next.HasMove = true;
                next.ActionsRemaining = 1;
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

            if (adversaryTurnsRemaining > 0 && Fighting(Side.Adversaries).Any())
            {
                Phase = EncounterPhase.AdversaryTurn;
            }
            else if (partyStillToAct.Count > 0)
            {
                BeginNextPartyTurn();
            }
            else
            {
                StartRound();
            }
        }

        /// <summary>
        /// The GM's side of the table, played simply: act with an adversary that can already strike
        /// a character, or else the one closest to doing so, and go for the nearest character —
        /// stepping into reach with the move and keeping the action for the blow when it can, and
        /// spending both on closing the distance when it can't.
        /// </summary>
        private void TakeAdversaryTurn()
        {
            var targets = Fighting(Side.Party).ToList();
            var actor = Fighting(Side.Adversaries)
                .OrderBy(a => targets.Any(t => a.CanReach(t.Position)) ? 0 : 1)
                .ThenBy(a => targets.Min(t => a.Position.DistanceTo(t.Position)))
                .First();
            var target = targets.MinBy(t => actor.Position.DistanceTo(t.Position))!;

            Active = actor;
            actor.HasMove = true;
            actor.ActionsRemaining = 1;

            if (!actor.CanReach(target.Position))
            {
                var options = ReachableSquares(actor)
                    .Select(o => (Square: o.Key, Cost: o.Value, Distance: o.Key.DistanceTo(target.Position)))
                    .ToList();

                var strikeFrom = options
                    .Where(o => o.Distance <= actor.Reach && o.Cost < MovesAvailable(actor))
                    .OrderBy(o => o.Cost)
                    .ThenBy(o => o.Square.DistanceTo(actor.Position))
                    .Select(o => ((GridPosition Square, int Cost)?)(o.Square, o.Cost))
                    .FirstOrDefault();

                var closeIn = options.OrderBy(o => o.Distance).ThenBy(o => o.Cost)
                    .Select(o => ((GridPosition Square, int Cost)?)(o.Square, o.Cost))
                    .FirstOrDefault();

                if ((strikeFrom ?? closeIn) is { } move)
                {
                    Move(actor, move.Square, move.Cost);
                }
            }

            if (CanAttack(actor, target))
            {
                ResolveAttack(actor, target, spendOpportunity: GmOpportunities > 0);
            }
            else if (MovesAvailable(actor) == 2)
            {
                Write($"{actor.Name} holds its ground.");
            }

            FinishTurn(actor);
            Active = null;
        }

        private void Move(Combatant mover, GridPosition square, int cost)
        {
            var from = mover.Position;
            mover.Position = square;

            var remaining = cost;
            if (mover.HasMove && remaining > 0)
            {
                mover.HasMove = false;
                remaining--;
            }

            mover.ActionsRemaining -= remaining;

            var band = RangeBandCalculator.BandForSquares(bands, from.DistanceTo(square));
            var price = cost == 1 ? "a move" : $"{cost} moves (move + {cost - 1} action)";
            Write($"{mover.Name} moves {from.DistanceTo(square)} square(s) ({band}) for {price}.");
        }

        /// <summary>
        /// One attack roll: 2d12 + attack modifier against the target's Dodge Threshold (Core Rules
        /// §9, "Attack Roll"), damage on a hit less what armour absorbs (Core Rules §4), and a
        /// critical's consequences either way (Core Rules §10).
        /// </summary>
        private AttackOutcome ResolveAttack(Combatant attacker, Combatant target, bool spendOpportunity)
        {
            var advantage = spendOpportunity;
            if (spendOpportunity)
            {
                GmOpportunities--;
            }

            var rolled = Enumerable.Range(0, RollCalculator.DiceNeeded(advantage, disadvantage: false))
                .Select(_ => dice.Roll(RollCalculator.HighestFace))
                .ToList();
            var roll = RollCalculator.Resolve(rolled, attacker.AttackModifier, target.DodgeThreshold, advantage, disadvantage: false);
            attacker.ActionsRemaining--;

            int rolledDamage = 0, absorbed = 0, dealt = 0;
            var attackerWounded = false;

            if (roll.Success == true)
            {
                rolledDamage = RollDamage(attacker, roll.IsCritical);
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

            var outcome = new AttackOutcome(attacker, target, roll, rolledDamage, absorbed, dealt, attackerWounded, spendOpportunity);
            Write(Describe(outcome));
            return outcome;
        }

        /// <summary>
        /// The damage dice plus modifier, or on a critical "the maximum result for that action plus
        /// an additional dice" (Core Rules §10).
        /// </summary>
        private int RollDamage(Combatant attacker, bool critical)
        {
            var dieTotal = critical
                ? attacker.DamageDiceCount * attacker.DamageDieSides + dice.Roll(attacker.DamageDieSides)
                : Enumerable.Range(0, attacker.DamageDiceCount).Sum(_ => dice.Roll(attacker.DamageDieSides));

            return Math.Max(PlayStateCalculator.DamageFloor, dieTotal + attacker.DamageModifier);
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

        private static void FinishTurn(Combatant combatant)
        {
            combatant.HasMove = false;
            combatant.ActionsRemaining = 0;
        }

        private bool CheckForEnd()
        {
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

        private IEnumerable<GridPosition> AllSquares()
        {
            for (var y = 0; y < Height; y++)
            {
                for (var x = 0; x < Width; x++)
                {
                    yield return new GridPosition(x, y);
                }
            }
        }

        private static string Describe(AttackOutcome outcome)
        {
            var roll = outcome.Roll;
            var dice = string.Join("+", roll.CountedDice);
            var dropped = RollCalculator.DroppedDice(roll);
            var advantage = outcome.UsedOpportunity
                ? $" (GM Opportunity: Advantage, dropped {string.Join(", ", dropped)})"
                : string.Empty;
            var sign = roll.Modifier >= 0 ? "+" : "−";
            var line = $"{outcome.Attacker.Name} attacks {outcome.Target.Name} with {outcome.Attacker.AttackName}: " +
                       $"({dice}){sign}{Math.Abs(roll.Modifier)} = {roll.Total} vs DT {roll.Threshold}{advantage}";

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
                line += roll.IsAbsoluteFailure ? " — double one!" : roll.IsCritical ? " — critical miss!" : " — miss.";
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
