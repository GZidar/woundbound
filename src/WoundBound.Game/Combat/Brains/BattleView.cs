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

        /// <summary>Whole rounds gone by with nobody hurt or healed.</summary>
        public int QuietRounds => encounter.QuietRounds;

        public IReadOnlyList<Combatant> Combatants => encounter.Combatants;

        /// <summary>The GM's creatures still fighting.</summary>
        public IEnumerable<Combatant> Adversaries => encounter.Fighting(Side.Adversaries);

        /// <summary>The adversaries still fighting that haven't had their turn this round — the ones an owed turn can go to.</summary>
        /// <remarks>A Solo stays on the list until it has taken all its turns.</remarks>
        public IEnumerable<Combatant> YetToAct => Adversaries.Where(a => a.HasTurnLeftThisRound);

        /// <summary>The party still fighting.</summary>
        public IEnumerable<Combatant> Characters => encounter.Fighting(Side.Party);

        /// <summary>The party members yet to take a turn this round.</summary>
        public IReadOnlyList<Combatant> PartyStillToAct => encounter.PartyStillToAct;

        /// <summary>The declared action the turn under way Interrupted, if it's an Interrupt's turn.</summary>
        public Interruption? Interrupting => encounter.Interrupting;

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
        /// <summary>A second of the attacker's kind within reach of the target, for Pack Tactics; null if none.</summary>
        public Combatant? PackMateOf(Combatant attacker, Combatant target) => encounter.PackMateOf(attacker, target);

        public AttackPlan PlanAttackWith(Combatant attacker, AttackProfile attack, GridPosition from, Combatant target, GridPosition? targetAt = null) =>
            Believed(attacker, from, targetAt ?? target.Position, encounter.PlanAttackWith(attacker, attack, from, target, targetAt));

        /// <summary>Where the fight happens.</summary>
        public Setting Setting => encounter.Setting;

        /// <summary>The Visibility <paramref name="target"/> is seen at, standing where they are or on <paramref name="at"/>.</summary>
        public Visibility VisibilityOf(Combatant target, GridPosition? at = null) => encounter.VisibilityOf(target, at);

        /// <summary>What the dark adds to <paramref name="target"/>'s Dodge Threshold, standing where they are or on <paramref name="at"/>.</summary>
        public int VisibilityDodgeModifier(Combatant target, GridPosition? at = null, Combatant? seer = null) => encounter.VisibilityDodgeModifier(target, at, seer);

        /// <summary>What the dark would add to <paramref name="target"/>'s Dodge Threshold were <paramref name="light"/> conjured too.</summary>
        public int DodgeModifierWith(Combatant target, Working light) => encounter.DodgeModifierWith(target, light);

        /// <summary>The lasting Utility spells at work.</summary>
        public IReadOnlyList<Working> Workings => encounter.Workings;

        /// <summary>Whether the scene is dark for want of light, which light answers.</summary>
        public bool IsUnlit => encounter.Setting.IsUnlit;

        /// <summary>The most squares <paramref name="speed"/> covers for <paramref name="moves"/> moves.</summary>
        public int SquaresFor(Speed speed, int moves) => encounter.SquaresFor(speed, moves);

        /// <summary>Squares from <paramref name="square"/> to the nearest character still fighting.</summary>
        public int DistanceToNearestCharacter(GridPosition square) =>
            Characters.Select(c => c.DistanceTo(square)).DefaultIfEmpty(int.MaxValue).Min();

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
        /// <remarks>None at all for a creature that can't move itself just now — standing its ground, Held, or Frightened and already withdrawn.</remarks>
        public IReadOnlyDictionary<GridPosition, int> SquaresWithin(Combatant combatant, int moves)
        {
            if (!Encounter.CanMoveItself(combatant))
            {
                return new Dictionary<GridPosition, int>();
            }

            var squares = encounter.SquaresWithin(combatant, moves);
            if (!encounter.Illusions.Any(i => i.Form is IllusionForm.Wall or IllusionForm.Figure && encounter.Fools(i, combatant)))
            {
                return squares;
            }

            // A creature taken in by a phantom wall or figure won't walk into it, or through it — it
            // believes the ground there is held, or solid (the user's ruling: creatures act on belief).
            var phantoms = encounter.PhantomsSeenBy(combatant).Select(p => p.Position).ToHashSet();
            bool Blocked(GridPosition sq) => phantoms.Contains(sq) || encounter.BelievesWall(combatant, sq);
            return squares
                .Where(o => !Blocked(o.Key) && (encounter.PlanMove(combatant, o.Key) is not { } plan || !plan.Path.Skip(1).Any(Blocked)))
                .ToDictionary(o => o.Key, o => o.Value);
        }

        /// <summary>Where <paramref name="mover"/> could get to for <paramref name="moves"/> moves were <paramref name="absent"/> not on the board.</summary>
        public IReadOnlyDictionary<GridPosition, int> SquaresWithinIfAbsent(Combatant mover, Combatant absent, int moves) =>
            Encounter.CanMoveItself(mover) ? encounter.SquaresWithinIfAbsent(mover, absent, moves) : new Dictionary<GridPosition, int>();

        /// <summary>Whether <paramref name="attacker"/> could attack <paramref name="target"/> right now, action and all.</summary>
        /// <remarks>A Solo that has made its attack this round can attack again only if it can buy it.</remarks>
        public bool CanAttack(Combatant attacker, Combatant target) =>
            encounter.CanAttack(attacker, target) && Encounter.SoloAttackRefusal(attacker) is null
            && Believed(attacker, attacker.Position, target.Position, encounter.PlanAttack(attacker, target)).CanAttack;

        /// <summary>What an attack at <paramref name="target"/> would face from where <paramref name="attacker"/> stands.</summary>
        public AttackPlan PlanAttack(Combatant attacker, Combatant target) =>
            Believed(attacker, attacker.Position, target.Position, encounter.PlanAttack(attacker, target));

        /// <summary>What an attack at <paramref name="target"/> would face if <paramref name="attacker"/> stood on <paramref name="from"/> instead.</summary>
        public AttackPlan PlanAttackFrom(Combatant attacker, GridPosition from, Combatant target) =>
            Believed(attacker, from, target.Position, encounter.PlanAttackFrom(attacker, from, target));

        /// <summary>One particular attack line, as though <paramref name="attacker"/> stood on <paramref name="from"/>.</summary>
        public AttackPlan PlanAttackFrom(Combatant attacker, AttackProfile with, GridPosition from, Combatant target) =>
            Believed(attacker, from, target.Position, encounter.PlanAttackWith(attacker, with, from, target));

        /// <summary>
        /// An attack as <paramref name="attacker"/> believes it would go: one with a phantom wall it
        /// is taken in by standing between can't be made — as far as it knows, there's stone in the way.
        /// </summary>
        private AttackPlan Believed(Combatant attacker, GridPosition from, GridPosition to, AttackPlan plan) =>
            plan.CanAttack && from.DistanceTo(to) > 1 && !Sight.Clear(from, to, sq => encounter.BelievesWall(attacker, sq))
                ? plan with { Refusal = "As far as it knows, there's a wall in the way." }
                : plan;

        /// <summary>The Illusions standing, for whoever is weighing whether to test one.</summary>
        public IReadOnlyList<Illusion> Illusions => encounter.Illusions;

        /// <summary>Whether <paramref name="creature"/> is taken in by <paramref name="illusion"/>.</summary>
        public bool Fools(Illusion illusion, Combatant creature) => encounter.Fools(illusion, creature);

        /// <summary>The Body <paramref name="creature"/> believes it has, a felt wound counted.</summary>
        public int BelievedBody(Combatant creature) => encounter.BelievedBody(creature);

        /// <summary>How many of <paramref name="creature"/>'s own side are down — what its Morale counts.</summary>
        public int AlliesDown(Combatant creature) =>
            encounter.Combatants.Count(c => c.Side == creature.Side && c != creature && c.IsOutOfFight && !c.HasFled && !c.HasSurrendered);

        /// <summary>One step from breaking: help seen coming for the other side would do it.</summary>
        public bool IsWavering(Combatant creature) =>
            creature.Morale is { } morale && !creature.IsBroken && morale.IsWavering(creature, AlliesDown(creature), BelievedBody(creature));

        /// <summary>Why <paramref name="searcher"/> can't test <paramref name="illusion"/> now, or null.</summary>
        public string? SearchRefusal(Combatant searcher, Illusion illusion) => encounter.SearchRefusal(searcher, illusion);

        /// <summary>Whether a melee blow from <paramref name="from"/> could land on <paramref name="square"/>.</summary>
        public static bool CanReachFrom(Combatant attacker, GridPosition from, GridPosition square) =>
            attacker.CanReachFrom(from, square);

        /// <summary>Whether a melee blow from <paramref name="from"/> could land on any square <paramref name="target"/> stands on.</summary>
        public static bool CanReachFrom(Combatant attacker, GridPosition from, Combatant target) =>
            attacker.CanReachFrom(from, target);

        /// <summary>Everybody still fighting on the other side from <paramref name="combatant"/>.</summary>
        /// <remarks>As <paramref name="combatant"/> believes it: a phantom figure it is taken in by is one of them.</remarks>
        public IEnumerable<Combatant> EnemiesOf(Combatant combatant) =>
            encounter.Fighting(combatant.Side == Side.Party ? Side.Adversaries : Side.Party).Concat(encounter.PhantomsSeenBy(combatant));

        /// <summary>Everybody still fighting on <paramref name="combatant"/>'s own side, themselves included.</summary>
        public IEnumerable<Combatant> AlliesOf(Combatant combatant) => encounter.Fighting(combatant.Side);

        /// <summary>The closest enemy still fighting, or null if there's nobody left.</summary>
        public Combatant? NearestEnemy(Combatant combatant) =>
            EnemiesOf(combatant).MinBy(e => combatant.DistanceTo(e));

        /// <summary>
        /// The nearest free square at most <paramref name="moves"/> moves away from which
        /// <paramref name="actor"/>'s melee reach takes in <paramref name="target"/>, or null.
        /// </summary>
        public GridPosition? StrikeSquare(Combatant actor, Combatant target, int moves = 1)
        {
            var origin = actor.Position;
            return SquaresWithin(actor, moves).Keys
                .Where(square => actor.CanReachFrom(square, target))
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

        public static string? TakeUpRefusal(Combatant combatant) => Encounter.TakeUpRefusal(combatant);

        /// <summary>What casting <paramref name="cast"/> now would come to, turn and all, or why it can't be cast.</summary>
        public CastPlan PlanCast(Combatant caster, SpellCast cast) => encounter.PlanCast(caster, cast);

        /// <summary>What casting <paramref name="cast"/> from <paramref name="from"/> would come to, leaving the turn aside.</summary>
        public CastPlan PlanCastFrom(Combatant caster, GridPosition from, SpellCast cast) => encounter.PlanCastFrom(caster, from, cast, checkTurn: false);

        /// <summary>Why <paramref name="character"/> can't buy <paramref name="exploit"/> now, or null if they can.</summary>
        public static string? ExploitRefusal(Combatant character, Rules.Enums.NimbleExploit exploit) => Encounter.ExploitRefusal(character, exploit);

        /// <summary>Why <paramref name="attacker"/> can't declare <paramref name="manoeuvre"/> at <paramref name="target"/>, or null if they can.</summary>
        public static string? ManoeuvreRefusal(Combatant attacker, Combatant target, Manoeuvre manoeuvre) => Encounter.ManoeuvreRefusal(attacker, target, manoeuvre);

        /// <summary>Why <paramref name="combatant"/> can't Sacrifice now, or null if they can.</summary>
        public static string? SacrificeRefusal(Combatant combatant) => Encounter.SacrificeRefusal(combatant);

        /// <summary>Lights burning where they were dropped.</summary>
        public IReadOnlyList<GridPosition> LightsOnTheGround => encounter.LightsOnTheGround;

        /// <summary>The built fires still burning.</summary>
        public IEnumerable<GridPosition> Fires => encounter.Fires;

        /// <summary>The lights the party has: carried, dropped, and built fires.</summary>
        public int PartyLights => Characters.Sum(c => c.CarriedLights) + encounter.LightsOnTheGround.Count + encounter.Fires.Count();

        /// <summary>Grades of Visibility the caster's enemies, and their own side, would lose if <paramref name="cast"/>'s source were tapped.</summary>
        public (int Theirs, int Ours) GradesLostIfTapped(Combatant caster, SpellCast cast) => encounter.GradesLostIfTapped(caster, cast);

        /// <summary>Whether a Control spell is holding <paramref name="combatant"/>.</summary>
        public bool IsControlled(Combatant combatant) => encounter.IsControlled(combatant);

        /// <summary>Spells still at work on the board.</summary>
        public IReadOnlyList<SpellInPlay> SpellsInPlay => encounter.SpellsInPlay;

        /// <summary>Why <paramref name="combatant"/> can't make a tactical Stamina spend now, or null if they can.</summary>
        public static string? TacticalSpendRefusal(Combatant combatant) => Encounter.TacticalSpendRefusal(combatant);
    }
}
