using WoundBound.Rules.Enums;

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


        /// <summary>What the fight is about besides dropping everybody.</summary>
        public IReadOnlyList<Objective> Objectives => encounter.Objectives;

        /// <summary>One of <paramref name="attacker"/>'s attacks from <paramref name="from"/>, at <paramref name="target"/> standing on <paramref name="targetAt"/>.</summary>
        public AttackPlan PlanAttackWith(Combatant attacker, AttackProfile attack, GridPosition from, Combatant target, GridPosition? targetAt = null) =>
            encounter.PlanAttackWith(attacker, attack, from, target, targetAt);

        /// <summary>Where the fight happens.</summary>
        public Setting Setting => encounter.Setting;

        /// <summary>The Visibility <paramref name="target"/> is seen at, standing where they are or on <paramref name="at"/>.</summary>
        public Visibility VisibilityOf(Combatant target, GridPosition? at = null) => encounter.VisibilityOf(target, at);

        /// <summary>What the dark adds to <paramref name="target"/>'s Dodge Threshold, standing where they are or on <paramref name="at"/>.</summary>
        public int VisibilityDodgeModifier(Combatant target, GridPosition? at = null) => encounter.VisibilityDodgeModifier(target, at);

        /// <summary>The most squares <paramref name="speed"/> covers for <paramref name="moves"/> moves.</summary>
        public int SquaresFor(Speed speed, int moves) => encounter.SquaresFor(speed, moves);

        /// <summary>Squares from <paramref name="square"/> to the nearest character still fighting.</summary>
        public int DistanceToNearestCharacter(GridPosition square) =>
            Characters.Select(c => c.Position.DistanceTo(square)).DefaultIfEmpty(int.MaxValue).Min();

        /// <summary>Whether a character still fighting has <paramref name="square"/> within Long. An adversary nobody does has left the board.</summary>
        public bool IsWithinLongOfParty(GridPosition square) => encounter.IsWithinLongOfParty(square);

        /// <summary>Whether a fight objective is about keeping <paramref name="combatant"/> from getting away.</summary>
        public bool MustNotEscape(Combatant combatant) =>
            Objectives.Any(o => o is Objective.NoneEscape || (o is Objective.DontLetEscape d && d.Who == combatant));

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

        /// <summary>Why <paramref name="actor"/> can't Grab, Shove or Trip <paramref name="target"/> now, or null if they can.</summary>
        public static string? ControlRefusal(Combatant actor, ControlAction action, Combatant target) =>
            Encounter.ControlRefusal(actor, action, target);

        /// <summary>Why <paramref name="actor"/> can't Help or Harry <paramref name="target"/> now, or null if they can.</summary>
        public static string? AidRefusal(Combatant actor, Rules.Enums.CombatAction action, Combatant target) => Encounter.AidRefusal(actor, action, target);

        /// <summary>Why <paramref name="actor"/> can't Heal, Meditate or Pray over <paramref name="target"/> now, or null if they can.</summary>
        public static string? TendRefusal(Combatant actor, Rules.Enums.CombatAction action, Combatant target) => Encounter.TendRefusal(actor, action, target);

        /// <summary>Why <paramref name="bearer"/> can't raise their shield — over <paramref name="ally"/>, if named — now, or null if they can.</summary>
        public static string? ShieldRefusal(Combatant bearer, Combatant? ally = null) => Encounter.ShieldRefusal(bearer, ally);

        /// <summary>The party, the dying and the stable among them included — everyone a Heal could still reach.</summary>
        public IEnumerable<Combatant> PartyOnBoard => encounter.Combatants.Where(c => c.Side == Side.Party && c.OccupiesSquare);

        /// <summary>Why <paramref name="combatant"/> can't regain the footing a hazard took, or null if they can.</summary>
        public static string? RegainFootingRefusal(Combatant combatant) => Encounter.RegainFootingRefusal(combatant);

        /// <summary>How a move there would go — its cost, its route, and whether any of it must be swum.</summary>
        public MovePlan? PlanMove(Combatant mover, GridPosition square) => encounter.PlanMove(mover, square);

        /// <summary>Why <paramref name="combatant"/> can't stand up from Prone now, or null if they can.</summary>
        public static string? StandUpRefusal(Combatant combatant) => Encounter.StandUpRefusal(combatant);

        /// <summary>Why <paramref name="combatant"/> can't try to break free now, or null if they can.</summary>
        public static string? BreakFreeRefusal(Combatant combatant) => Encounter.BreakFreeRefusal(combatant);

        /// <summary>What casting <paramref name="cast"/> now would come to, turn and all, or why it can't be cast.</summary>
        public CastPlan PlanCast(Combatant caster, SpellCast cast) => encounter.PlanCast(caster, cast);

        /// <summary>What casting <paramref name="cast"/> from <paramref name="from"/> would come to, leaving the turn aside.</summary>
        public CastPlan PlanCastFrom(Combatant caster, GridPosition from, SpellCast cast) => encounter.PlanCastFrom(caster, from, cast, checkTurn: false);

        /// <summary>Why <paramref name="combatant"/> can't Sacrifice now, or null if they can.</summary>
        public static string? SacrificeRefusal(Combatant combatant) => Encounter.SacrificeRefusal(combatant);

        /// <summary>Lights burning where they were dropped.</summary>
        public IReadOnlyList<GridPosition> LightsOnTheGround => encounter.LightsOnTheGround;

        /// <summary>The built fires still burning.</summary>
        public IEnumerable<GridPosition> Fires => encounter.Fires;

        /// <summary>The lights the party has: carried, dropped, and built fires.</summary>
        public int PartyLights => Characters.Sum(c => c.CarriedLights) + encounter.LightsOnTheGround.Count + encounter.Fires.Count();

        /// <summary>Spells still at work on the board.</summary>
        public IReadOnlyList<SpellInPlay> SpellsInPlay => encounter.SpellsInPlay;

        /// <summary>Why <paramref name="combatant"/> can't make a tactical Stamina spend now, or null if they can.</summary>
        public static string? TacticalSpendRefusal(Combatant combatant) => Encounter.TacticalSpendRefusal(combatant);
    }
}
