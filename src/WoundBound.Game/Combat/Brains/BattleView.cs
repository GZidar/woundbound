namespace WoundBound.Game.Combat.Brains
{
    /// <summary>
    /// The fight as a GM brain sees it: everything a GM at the table could see or work out, and
    /// no way to change any of it. Every rule question is answered by the <see cref="Encounter"/>,
    /// so a brain asking "could I hit them from there?" gets the same answer the rules would.
    /// </summary>
    public sealed class BattleView
    {
        private readonly Encounter encounter;

        internal BattleView(Encounter encounter)
        {
            this.encounter = encounter;
        }

        public int Width => encounter.Width;

        public int Height => encounter.Height;

        public int Round => encounter.Round;

        public IReadOnlyList<Combatant> Combatants => encounter.Combatants;

        /// <summary>The GM's creatures still fighting.</summary>
        public IEnumerable<Combatant> Adversaries => encounter.Fighting(Side.Adversaries);

        /// <summary>The party still fighting.</summary>
        public IEnumerable<Combatant> Characters => encounter.Fighting(Side.Party);

        /// <summary>The party members yet to take a turn this round.</summary>
        public IReadOnlyList<Combatant> PartyStillToAct => encounter.PartyStillToAct;

        public int BankedOpportunities => encounter.BankedOpportunities;

        public int ImmediateOpportunities => encounter.ImmediateOpportunities;

        public int Opportunities => encounter.GmOpportunities;

        public int MaxBankedOpportunities => Encounter.MaxBankedOpportunities;

        /// <summary>Adversary turns a round, as capped by the party's size when the fight began.</summary>
        public int AdversaryTurnsPerRound => encounter.AdversaryTurnsPerRound;

        public bool IsOnBoard(GridPosition square) => encounter.IsOnBoard(square);

        /// <summary>Whoever is standing on <paramref name="square"/> and still fighting, if anybody.</summary>
        public Combatant? At(GridPosition square) => encounter.At(square);

        /// <summary>Moves <paramref name="combatant"/> has left this turn: the move, plus each action.</summary>
        public int MovesAvailable(Combatant combatant) => Encounter.MovesAvailable(combatant);

        /// <summary>What moving there now would cost, or null if it can't be done with what's left.</summary>
        public int? MoveCostTo(Combatant combatant, GridPosition square) => encounter.MoveCostTo(combatant, square);

        /// <summary>Every free square <paramref name="combatant"/> could reach for at most <paramref name="moves"/> moves, with each one's cost.</summary>
        public IReadOnlyDictionary<GridPosition, int> SquaresWithin(Combatant combatant, int moves) =>
            encounter.SquaresWithin(combatant, moves);

        /// <summary>Whether <paramref name="attacker"/> could attack <paramref name="target"/> right now, action and all.</summary>
        public bool CanAttack(Combatant attacker, Combatant target) => encounter.CanAttack(attacker, target);

        /// <summary>What an attack at <paramref name="target"/> would face from where <paramref name="attacker"/> stands.</summary>
        public AttackPlan PlanAttack(Combatant attacker, Combatant target) => encounter.PlanAttack(attacker, target);

        /// <summary>What an attack at <paramref name="target"/> would face if <paramref name="attacker"/> stood on <paramref name="from"/> instead.</summary>
        public AttackPlan PlanAttackFrom(Combatant attacker, GridPosition from, Combatant target) =>
            encounter.PlanAttackFrom(attacker, from, target);

        /// <summary>Whether a melee blow from <paramref name="from"/> could land on <paramref name="square"/>.</summary>
        public static bool CanReachFrom(Combatant attacker, GridPosition from, GridPosition square) =>
            attacker.CanReachFrom(from, square);

        /// <summary>Everybody still fighting on the other side from <paramref name="combatant"/>.</summary>
        public IEnumerable<Combatant> EnemiesOf(Combatant combatant) =>
            encounter.Fighting(combatant.Side == Side.Party ? Side.Adversaries : Side.Party);

        /// <summary>The closest enemy still fighting, or null if there's nobody left.</summary>
        public Combatant? NearestEnemy(Combatant combatant) =>
            EnemiesOf(combatant).MinBy(e => combatant.Position.DistanceTo(e.Position));

        /// <summary>
        /// The nearest free square at most <paramref name="moves"/> moves away from which
        /// <paramref name="actor"/>'s melee reach takes in <paramref name="target"/>, or null.
        /// </summary>
        public GridPosition? StrikeSquare(Combatant actor, Combatant target, int moves = 1)
        {
            var origin = actor.Position;
            return SquaresWithin(actor, moves).Keys
                .Where(square => actor.CanReachFrom(square, target.Position))
                .OrderBy(square => square.DistanceTo(origin))
                .Select(square => (GridPosition?)square)
                .FirstOrDefault();
        }

        /// <summary>Why <paramref name="combatant"/> can't swap loadouts now, or null if they can.</summary>
        public static string? SwapRefusal(Combatant combatant) => Encounter.SwapRefusal(combatant);

        /// <summary>Why <paramref name="combatant"/> can't take Dodge, Grounded or Resolute now, or null if they can.</summary>
        public static string? StandGroundRefusal(Combatant combatant, Rules.Enums.CombatAction action) =>
            Encounter.StandGroundRefusal(combatant, action);

        /// <summary>Why <paramref name="combatant"/> can't make a tactical Stamina spend now, or null if they can.</summary>
        public static string? TacticalSpendRefusal(Combatant combatant) => Encounter.TacticalSpendRefusal(combatant);
    }
}
