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

        /// <summary>
        /// The fight is over because the party got clear: foes still willing to fight were left
        /// beyond Long of everybody. It ends the combat — nobody is on the board — but nobody was beaten.
        /// </summary>
        Withdrew,
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
        bool Dodged,
        int Boost = 0,
        string? BoostedBy = null);

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
        IReadOnlyList<LeaverResponse>? Options = null,
        int Bonus = ThresholdModifierCalculator.ReactionBonus)
    {
        /// <summary>The Threshold the spend would make it: +2, or for a shield raised, what that shield adds.</summary>
        public int RaisedThreshold => Threshold + Bonus;

        /// <summary>Only a Dodge Threshold boost can Riposte (Core Rules §9, "Criticals") — a panic dodge, or a shield raised in time.</summary>
        public bool WouldRiposte => Kind is ReactionKind.PanicDodge or ReactionKind.RaiseShield && Roll?.IsCritical == true;
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
    public sealed partial class Encounter
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
        /// <param name="setting">Where it happens — Visibility, Exposure and the ground; daylight on firm ground by default.</param>
        /// <param name="environment">The levers' grades, needed for any setting but daylight.</param>
        public Encounter(int width, int height, IEnumerable<Combatant> combatants, RangeBandTables bands, IDiceRoller dice, IGmBrain? gm = null,
            IEnumerable<Objective>? objectives = null, Setting? setting = null, EnvironmentTables? environment = null)
        {
            Setting = setting ?? Setting.Daylight;
            this.environment = environment;
            if (this.environment is null && Setting != Setting.Daylight)
            {
                throw new ArgumentException("A setting other than daylight needs the environment tables to read it by.", nameof(environment));
            }

            Width = width;
            Height = height;
            this.combatants = combatants.ToList();
            this.bands = bands;
            this.dice = dice;
            this.gm = gm ?? new SimpleGmBrain();
            Objectives = objectives?.ToList() ?? new List<Objective>();
            ApplySettingToCombatants();
            view = new BattleView(this);

            StartRound();
        }

        /// <summary>The fight as a brain sees it — for bots at either seat.</summary>
        internal BattleView View => view;

        public int Width { get; }

        private readonly EnvironmentTables? environment;

        /// <summary>Lights dropped and still burning where they fell, each counting as one carried light.</summary>
        private readonly List<GridPosition> lightsOnTheGround = new();

        /// <summary>Where the fight happens (Core Rules §9, "Environment").</summary>
        public Setting Setting { get; }

        public IReadOnlyList<GridPosition> LightsOnTheGround => lightsOnTheGround;

        /// <summary>
        /// Hands every combatant the scene: the ground's pull on Stability wherever they stand, the
        /// weather's on Resolve — and the Stamina Exposure levies at the start of the challenge,
        /// capped at 0, granting nobody an Opportunity (Core Rules §9, "Exposure").
        /// </summary>
        private void ApplySettingToCombatants()
        {
            if (environment is not { } tables)
            {
                return;
            }

            var levy = EnvironmentCalculator.ExposureStaminaModifier(tables, Setting.Exposure);
            foreach (var combatant in combatants)
            {
                combatant.GroundStability = square => EnvironmentCalculator.StabilityThresholdModifier(tables, Setting.GroundAt(square));
                combatant.SceneResolve = EnvironmentCalculator.ResolveThresholdModifier(tables, Setting.Exposure);
                combatant.Stamina = EnvironmentCalculator.StartOfChallengeStamina(tables, combatant.Stamina, Setting.Exposure);
            }

            if (levy < 0)
            {
                Write($"The {Setting.Exposure.ToString().ToLowerInvariant()} weather takes {-levy} Stamina from everyone before the first blow.");
            }
        }

        /// <summary>
        /// The Visibility <paramref name="target"/> is seen at standing on <paramref name="at"/> (Core
        /// Rules §9, "Visibility"). In an Unlit scene it's read "from the light to whichever creature
        /// is being looked at": every light within a band of them — their own, others' carried, any
        /// dropped, any built fire — lifts them to that band's grade, and the best of those counts.
        /// Obscured, light lifts nothing, except that whoever carries one is seen a grade better.
        /// </summary>
        public Visibility VisibilityOf(Combatant target, GridPosition? at = null)
        {
            var square = at ?? target.Position;
            if (environment is not { } tables || Setting.Visibility == Visibility.Good)
            {
                return Visibility.Good;
            }

            // Brains ask this thousands of times a decision; the answer only changes when a light moves.
            var stamp = LightStamp();
            if (stamp != visibilityStamp)
            {
                visibilityCache.Clear();
                visibilityStamp = stamp;
            }

            if (visibilityCache.TryGetValue((target, square), out var known))
            {
                return known;
            }

            var seen = WorkOutVisibility(tables, target, square);
            visibilityCache[(target, square)] = seen;
            return seen;
        }

        private readonly Dictionary<(Combatant, GridPosition), Visibility> visibilityCache = new();
        private int visibilityStamp;

        private int LightStamp()
        {
            var hash = new HashCode();
            foreach (var carrier in combatants.Where(c => c.CarriedLights > 0 && c.OccupiesSquare))
            {
                hash.Add(carrier);
                hash.Add(carrier.Position);
                hash.Add(carrier.CarriedLights);
            }

            hash.Add(lightsOnTheGround.Count);
            hash.Add(spentFires.Count);
            return hash.ToHashCode();
        }

        /// <summary>
        /// How well a square itself can be seen — what somebody carrying no light would be seen at,
        /// standing there. For drawing the dark, not for any roll.
        /// </summary>
        public Visibility VisibilityAtSquare(GridPosition square) =>
            environment is not { } tables || Setting.Visibility == Visibility.Good ? Visibility.Good
            : !Setting.IsUnlit ? Setting.Visibility
            : WorkOutVisibility(tables, null, square);

        private Visibility WorkOutVisibility(EnvironmentTables tables, Combatant? target, GridPosition square)
        {

            if (!Setting.IsUnlit)
            {
                return EnvironmentCalculator.VisibilityOfTarget(tables, Setting.Visibility, Setting.Cause, target?.CarriedLights ?? 0);
            }

            var sources = combatants
                .Where(c => c != target && c.CarriedLights > 0 && c.OccupiesSquare)
                .SelectMany(c => Enumerable.Repeat(c.Position, c.CarriedLights))
                .Concat(Enumerable.Repeat(square, target?.CarriedLights ?? 0))
                .Concat(lightsOnTheGround)
                .ToList();

            // Each light lights the square by its own group — the lights carried together with it,
            // within a square of one another — at the band it is from the square. Two torches across
            // the field are two single lights, not a pair. A built fire holds its grade further.
            var best = Setting.Visibility;
            foreach (var source in sources)
            {
                if (RangeBandCalculator.BandForSquares(bands, source.DistanceTo(square)) is not { } band
                    || (Setting.Walls.Count > 0 && !Sight.Clear(source, square, Setting.IsWall)))
                {
                    continue;
                }

                var together = sources.Count(other => other.DistanceTo(source) <= 1);
                var grade = EnvironmentCalculator.VisibilityFromLight(tables, together, builtFire: false, band);
                best = grade < best ? grade : best;
            }

            foreach (var fire in Fires)
            {
                if (RangeBandCalculator.BandForSquares(bands, fire.DistanceTo(square)) is { } band
                    && (Setting.Walls.Count == 0 || Sight.Clear(fire, square, Setting.IsWall)))
                {
                    var grade = EnvironmentCalculator.VisibilityFromLight(tables, 0, builtFire: true, band);
                    best = grade < best ? grade : best;
                }
            }

            return best;
        }

        /// <summary>What the Visibility <paramref name="target"/> is seen at adds to their Dodge Threshold: +2 at Poor, +4 at None.</summary>
        public int VisibilityDodgeModifier(Combatant target, GridPosition? at = null) =>
            environment is { } tables ? EnvironmentCalculator.DodgeThresholdModifier(tables, VisibilityOf(target, at)) : 0;

        /// <summary>Whether <paramref name="target"/>, on <paramref name="at"/>, can be targeted at all from <paramref name="distance"/> squares — nothing beyond Close at None.</summary>
        private bool CanBeTargeted(Combatant target, GridPosition at, int distance) =>
            environment is not { } tables
            || RangeBandCalculator.BandForSquares(bands, distance) is not { } band
            || EnvironmentCalculator.AllowsTargeting(tables, VisibilityOf(target, at), band);

        /// <summary>
        /// A critical failure on Broken ground puts the roller on it, Prone — "the whole of the
        /// slipping rule" (Core Rules §9, "Terrain").
        /// </summary>
        private void Slip(Combatant roller, RollResult roll)
        {
            if (environment is { } tables && roll.IsCritical && roll.Success != true && !roller.IsProne && !roller.IsOutOfFight
                && EnvironmentCalculator.KnocksProneOnCriticalFailure(tables, Setting.GroundAt(roller.Position)))
            {
                roller.SetCondition(CharacterCondition.Prone, true);
                Write($"{roller.Name} loses their footing on the broken ground, and goes down Prone.");
            }
        }

        /// <summary>Why the active character can't drop the light they hold, or null if they can. Dropping costs nothing.</summary>
        public static string? DropLightRefusal(Combatant combatant) =>
            combatant.Build is not { } build || !build.InventoryItems.Any(InventoryCalculator.IsLitLight)
                ? $"{combatant.Name} isn't holding a lit light."
                : null;

        /// <summary>Why the active character can't light a torch or lantern from their pack now, or null if they can: a light to light, a free hand, and the action.</summary>
        public static string? LightRefusal(Combatant combatant)
        {
            if (combatant.Build is not { } build || !build.InventoryItems.Any(i => Lightable(combatant, i)))
            {
                return combatant.DrainedLanterns.Count > 0
                    ? $"{combatant.Name}'s lantern has no oil in it, and there's no flask to fill it from."
                    : $"{combatant.Name} has nothing to light.";
            }

            if (!combatant.HasFreeHand)
            {
                return "No free hand to hold a light in.";
            }

            return combatant.ActionsRemaining < 1 ? "Lighting one takes an action." : null;
        }

        /// <summary>Lets go of the light in hand: it costs nothing, and it goes on burning where it falls.</summary>
        public bool TryDropLight()
        {
            if (Phase != EncounterPhase.PartyTurn || Active is not { Build: { } build } dropper || DropLightRefusal(dropper) is not null)
            {
                return false;
            }

            var light = build.InventoryItems.First(InventoryCalculator.IsLitLight);
            light.IsLit = false;
            light.Quantity--;
            if (light.Quantity <= 0)
            {
                light.IsStored = true;
            }

            lightsOnTheGround.Add(dropper.Position);
            Write($"{dropper.Name} drops their {light.Name.Split(' ')[0].TrimEnd('e', 's').ToLowerInvariant()} — it burns on where it fell.");
            return true;
        }

        /// <summary>Lights a torch or lantern from the pack, into a free hand, with the action.</summary>
        public bool TryLight()
        {
            if (Phase != EncounterPhase.PartyTurn || Active is not { Build: { } build } lighter || LightRefusal(lighter) is not null)
            {
                return false;
            }

            var light = build.InventoryItems.First(i => Lightable(lighter, i));
            var refilled = lighter.DrainedLanterns.Remove(light);
            if (refilled)
            {
                var oil = build.InventoryItems.First(IsOil);
                oil.Quantity--;
            }

            light.IsLit = true;
            lighter.ActionsRemaining--;
            Write(refilled
                ? $"{lighter.Name} fills their lantern from a flask of Oil and relights it."
                : $"{lighter.Name} lights a {light.Name.Split(' ')[0].TrimEnd('e', 's').ToLowerInvariant()}.");
            return true;
        }

        /// <summary>
        /// A light in the pack that can be lit: unlit, not put away — and a lantern tapped dry only
        /// with a flask of Oil to fill it from (Core Rules §14, "The lantern is the one carried
        /// source the tap does not destroy").
        /// </summary>
        private static bool Lightable(Combatant combatant, InventoryItemDefinition item) =>
            !item.IsLit && !item.IsStored && item.Quantity > 0 && InventoryCalculator.LooksLikeALightSource(item)
            && (!combatant.DrainedLanterns.Contains(item) || combatant.Build!.InventoryItems.Any(IsOil));

        private static bool IsOil(InventoryItemDefinition item) =>
            item.Quantity > 0 && item.Name.Contains("oil", StringComparison.OrdinalIgnoreCase) && !InventoryCalculator.LooksLikeALightSource(item);

        /// <summary>The fight's goals besides dropping everybody (Core Rules §9, "Scenes and Challenges").</summary>
        public IReadOnlyList<Objective> Objectives { get; }

        /// <summary>
        /// Whether <paramref name="square"/> is within Long — 16 squares — of a character still
        /// fighting. An adversary no character has within Long has left the board: the fight goes
        /// on until every one of them is down, gone that far, or has given up.
        /// </summary>
        public bool IsWithinLongOfParty(GridPosition square) =>
            Fighting(Side.Party).Any(c => RangeBandCalculator.BandForSquares(bands, c.Position.DistanceTo(square)) is not null);

        /// <summary>The most squares <paramref name="speed"/> covers for <paramref name="moves"/> moves, by the band prices (Core Rules §9, "Speed").</summary>
        public int SquaresFor(Speed speed, int moves)
        {
            var best = 0;
            for (var squares = 1; squares <= LongRangeSquares; squares++)
            {
                if (RangeBandCalculator.MoveCost(bands, squares, speed) is { } cost && cost <= moves)
                {
                    best = squares;
                }
            }

            return best;
        }

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

        public bool IsOver => Phase is EncounterPhase.Victory or EncounterPhase.Defeat or EncounterPhase.Withdrew;

        public IEnumerable<Combatant> Fighting(Side side) => combatants.Where(c => c.Side == side && !c.IsOutOfFight);

        /// <summary>Whoever takes up <paramref name="square"/>: anybody still fighting, a character lying dying or stable there, or a foe who gave up.</summary>
        public Combatant? At(GridPosition square) => combatants.FirstOrDefault(c => c.Position == square && c.OccupiesSquare);

        /// <summary>
        /// Whether <paramref name="square"/> is on the mapped ground — the grid the fight is drawn
        /// on. The edges are open: the world goes on past them, and anybody may walk off the map and
        /// still be in the fight, until no character has them within Long.
        /// </summary>
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
            !combatant.IsStandingGround && !combatant.HasWithdrawnThisTurn && ConditionCatalog.CanMove(combatant.Conditions);

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

            if (!asReaction && actor.NeedsFooting)
            {
                return FootingFirst;
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

        /// <summary>
        /// Why <paramref name="actor"/> can't Help or Harry <paramref name="target"/> now, or null if
        /// they can (Core Rules §9, "Actions in Combat"): the action, and the target <b>adjacent</b> —
        /// an ally to Help, an enemy to Harry, either still in the fight.
        /// </summary>
        public static string? AidRefusal(Combatant actor, CombatAction action, Combatant target)
        {
            if (action is not (CombatAction.Help or CombatAction.Harry))
            {
                return $"{action} isn't Help or Harry.";
            }

            if (actor.IsOutOfFight)
            {
                return $"{actor.Name} is in no state to.";
            }

            if (action == CombatAction.Help ? target.Side != actor.Side || target == actor : target.Side == actor.Side)
            {
                return action == CombatAction.Help ? "Help is for an ally." : "Harry is for an enemy.";
            }

            if (target.IsOutOfFight)
            {
                return $"{target.Name} isn't in the fight.";
            }

            if (actor.Position.DistanceTo(target.Position) > 1)
            {
                return $"{target.Name} has to be adjacent.";
            }

            return actor.ActionsRemaining < 1 ? "No action left to do it with." : actor.NeedsFooting ? FootingFirst : null;
        }

        /// <summary>
        /// Why <paramref name="actor"/> can't Heal, Meditate or Pray over <paramref name="target"/>
        /// now, or null if they can (Core Rules §4, "Recovery"): the action, the target themselves
        /// or an ally beside them — dying ones included — and a track carrying a fresh wound that
        /// action hasn't yet tended.
        /// </summary>
        public static string? TendRefusal(Combatant actor, CombatAction action, Combatant target)
        {
            if (CombatActionCatalog.For(action).Restores is not { } track)
            {
                return $"{action} doesn't tend a wound.";
            }

            if (actor.IsOutOfFight)
            {
                return $"{actor.Name} is in no state to.";
            }

            if (target.Side != actor.Side || target.HasFled || target.BodyStatus == BodyStatus.Dead || (target.IsOutOfFight && !target.IsDefenceless && target != actor))
            {
                return $"{target.Name} is beyond help.";
            }

            if (target != actor && actor.Position.DistanceTo(target.Position) > 1)
            {
                return $"{target.Name} has to be beside them.";
            }

            if (!target.CanBeTended(track))
            {
                return target.Track(track) is var (current, max) && current >= max
                    ? $"{target.Name} carries no {track} wound."
                    : $"{action} has already tended {target.Name}'s {track} — only a fresh wound makes it worth doing again.";
            }

            return actor.ActionsRemaining < 1 ? "No action left to do it with." : actor.NeedsFooting ? FootingFirst : null;
        }

        private const string FootingFirst = "Off their footing — the action has to go on regaining it first.";

        /// <summary>Why <paramref name="combatant"/> can't spend the action regaining their footing, or null if they can.</summary>
        public static string? RegainFootingRefusal(Combatant combatant) =>
            !combatant.NeedsFooting ? $"{combatant.Name} has their footing."
            : combatant.ActionsRemaining < 1 ? "Regaining footing takes an action." : null;

        /// <summary>Spends the action regaining the footing a hazard took (GM Reference §13).</summary>
        public bool TryRegainFooting()
        {
            if (Phase != EncounterPhase.PartyTurn || Active is not { } c || RegainFootingRefusal(c) is not null)
            {
                return false;
            }

            RegainFooting(c);
            return true;
        }

        private void RegainFooting(Combatant c)
        {
            c.ActionsRemaining--;
            c.NeedsFooting = false;
            Write($"{c.Name} gets their footing back.");
        }

        /// <summary>
        /// Why <paramref name="bearer"/> can't raise their shield now — over themselves, or over
        /// <paramref name="ally"/> — or null if they can (Core Rules §9, "Shields"). It takes the
        /// action. Only a Medium or Heavy shield covers somebody else, an ally within the bearer's
        /// Reach; a braced Heavy one covers the bearer and that ally both.
        /// </summary>
        public static string? ShieldRefusal(Combatant bearer, Combatant? ally = null)
        {
            if (bearer.IsOutOfFight)
            {
                return $"{bearer.Name} is in no state to.";
            }

            if (bearer.InHandShield is not { } shield)
            {
                return "No shield in hand.";
            }

            if (ally is not null)
            {
                if (ally == bearer)
                {
                    return null;
                }

                if (ally.Side != bearer.Side || ally.IsOutOfFight && !ally.IsDefenceless)
                {
                    return $"{ally.Name} isn't an ally to cover.";
                }

                if (!InventoryCalculator.CanCoverAnAlly(shield.Weight))
                {
                    return $"A {shield.Weight} shield covers nobody but its bearer.";
                }

                if (bearer.Position.DistanceTo(ally.Position) > bearer.Reach)
                {
                    return $"{ally.Name} is beyond {bearer.Name}'s Reach.";
                }
            }

            return bearer.ActionsRemaining < 1 ? "No action left to raise it with." : bearer.NeedsFooting ? FootingFirst : null;
        }

        /// <summary>
        /// What dragging the creature <paramref name="holder"/> holds to <paramref name="square"/>
        /// would cost — the move's own price and one more (Core Rules §9, "Drag") — or null if it
        /// can't be done.
        /// </summary>
        public int? DragCostTo(Combatant holder, GridPosition square) =>
            holder.Holding is not null && CanMoveItself(holder) && PriceOfMove(holder, square) is { } cost
            && cost + 1 <= MovesAvailable(holder)
                ? cost + 1
                : null;

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
            !attacker.NeedsFooting
            && attacker.Side != target.Side
            && (!target.IsOutOfFight || target.IsDefenceless)
            && attacker.ActionsRemaining > 0
            && PlanAttack(attacker, target).CanAttack;

        /// <summary>
        /// Whether <paramref name="attacker"/>'s attack, as they're armed right now, could be made
        /// at <paramref name="target"/> from where they stand — leaving aside whether they have an
        /// action left to make it with. A blade asks about Reach, in squares; a bow asks about the
        /// band, which moves the Dodge Threshold and the damage die (Core Rules §9, "Weapon Range").
        /// </summary>
        public AttackPlan PlanAttack(Combatant attacker, Combatant target) => PlanAttackFrom(attacker, attacker.Position, target);

        /// <summary>
        /// As <see cref="PlanAttack"/>, as though <paramref name="attacker"/> stood on
        /// <paramref name="from"/> — with whichever of its attacks would do the most from there: a
        /// foe with a bow and a knife shoots at range and stabs up close (GM Reference §9).
        /// </summary>
        internal AttackPlan PlanAttackFrom(Combatant attacker, GridPosition from, Combatant target)
        {
            var plans = attacker.Attacks.Select(a => PlanAttackWith(attacker, a, from, target)).ToList();
            return plans.Where(p => p.CanAttack).MaxBy(p => ExpectedPerHit(p)) ?? plans[0];

            static double ExpectedPerHit(AttackPlan p) =>
                (p.DamageDieSides is { } sides ? p.Profile!.DamageDiceCount * (sides + 1) / 2.0 : 0) + p.Profile!.DamageModifier
                + p.Profile.AttackModifier - p.DodgeThreshold;
        }

        /// <summary>
        /// One particular attack of <paramref name="attacker"/>'s, made from <paramref name="from"/>
        /// at <paramref name="target"/> — standing where it is, or on <paramref name="targetAt"/>,
        /// for a brain weighing where somebody might be shot from next turn.
        /// </summary>
        internal AttackPlan PlanAttackWith(Combatant attacker, AttackProfile attack, GridPosition from, Combatant target, GridPosition? targetAt = null)
        {
            var distance = from.DistanceTo(targetAt ?? target.Position);
            if (distance > 1 && Setting.Walls.Count > 0 && !Sight.Clear(from, targetAt ?? target.Position, Setting.IsWall))
            {
                return new AttackPlan("Something solid is in the way.", 0, 0, null, "Something solid is in the way.", attack);
            }

            if (!attack.IsRanged)
            {
                if (attack.IsUnwieldy && distance <= 1)
                {
                    return Refused($"The {attack.Name} is Unwieldy — it can't strike somebody standing this close.");
                }

                if (distance > attack.Reach)
                {
                    return Refused($"Out of reach — the {attack.Name} reaches {attack.Reach} square(s) and they're {distance} away.");
                }

                if (!CanBeTargeted(target, targetAt ?? target.Position, distance))
                {
                    return Refused("Too dark to see them past arm's length.");
                }

                var dark = VisibilityDodgeModifier(target, targetAt);
                return new AttackPlan(null, target.DodgeThreshold + dark, 0, attack.DamageDieSides,
                    $"Melee, within Reach {attack.Reach}" + (dark > 0 ? $"; +{dark} DT for the dark." : "."), attack);
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
            if (shot.CanAttack && !CanBeTargeted(target, targetAt ?? target.Position, distance))
            {
                return Refused("Too dark to see them — nothing can be targeted beyond Close at Visibility None.");
            }

            var gloom = VisibilityDodgeModifier(target, targetAt);

            return shot.CanAttack
                ? new AttackPlan(
                    Refusal: null,
                    DodgeThreshold: target.DodgeThreshold + shot.DodgeThresholdModifier + gloom,
                    RangeDodgeModifier: shot.DodgeThresholdModifier + gloom,
                    // A statblock's own die holds at Close and Short; past that the weapon's falls off.
                    DamageDieSides: shot.DamageDie is { } die
                        ? attacker.Build is null && shot.DodgeThresholdModifier == 0 ? attack.DamageDieSides : DiceNotation.ParseSides(die)
                        : null,
                    Note: shot.Summary,
                    Profile: attack)
                : Refused(shot.Summary);

            AttackPlan Refused(string why) => new(why, 0, 0, null, why, attack);
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

        /// <param name="sacrifice">Pay 1 Body first for Advantage on the roll (Core Rules §3, "Sacrifice").</param>
        public AttackOutcome? TryAttack(Combatant target, bool sacrifice = false)
        {
            if (Phase != EncounterPhase.PartyTurn || Active is not { } attacker || !CanAttack(attacker, target)
                || (sacrifice && SacrificeRefusal(attacker) is not null))
            {
                return null;
            }

            AttackOutcome? result = null;
            ResolveAttack(attacker, target, asReaction: false, opportunityBonus: false, outcome =>
            {
                result = outcome;
                CheckForEnd();
            }, sacrifice: sacrifice);

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

        /// <summary>Helps an adjacent ally or Harries an adjacent enemy, with the action (Core Rules §9).</summary>
        public bool TryAid(CombatAction action, Combatant target)
        {
            if (Phase != EncounterPhase.PartyTurn || Active is not { } actor || AidRefusal(actor, action, target) is not null)
            {
                return false;
            }

            Aid(actor, action, target);
            return true;
        }

        /// <summary>Heals, Meditates or Prays over the target — themselves or an ally beside them — with the action (Core Rules §4).</summary>
        public bool TryTend(CombatAction action, Combatant target)
        {
            if (Phase != EncounterPhase.PartyTurn || Active is not { } actor || TendRefusal(actor, action, target) is not null)
            {
                return false;
            }

            Tend(actor, action, target);
            CheckForEnd();
            return true;
        }

        /// <summary>
        /// Raises the active character's shield with the action: over themselves, or over
        /// <paramref name="ally"/> — and, braced, over both (Core Rules §9, "Shields").
        /// </summary>
        public bool TryRaiseShield(Combatant? ally = null)
        {
            if (Phase != EncounterPhase.PartyTurn || Active is not { } bearer || ShieldRefusal(bearer, ally) is not null)
            {
                return false;
            }

            bearer.ActionsRemaining--;
            RaiseShield(bearer, ally ?? bearer, reactive: false);
            return true;
        }

        /// <summary>Moves to <paramref name="square"/> hauling the creature the active character holds (Core Rules §9, "Drag").</summary>
        public bool TryDrag(GridPosition square)
        {
            if (Phase != EncounterPhase.PartyTurn || Active is not { } holder || DragCostTo(holder, square) is not { } cost)
            {
                return false;
            }

            Drag(holder, square, cost, () => CheckForEnd());
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

            EndPartyTurn(ending);
        }

        /// <summary>A character's turn over: their Control spells rechecked, then the gap the GM is owed.</summary>
        private void EndPartyTurn(Combatant ending)
        {
            FinishTurn(ending);
            RecheckControls(ending, AfterPartyTurn);
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

            if (spend && pending.Kind != ReactionKind.Backlash)
            {
                if (TacticalSpendRefusal(pending.Defender) is not null)
                {
                    return false;
                }

                PayTacticalSpend(pending.Defender, CombatActionCatalog.ReactionStaminaCost);

                // Holding a spell hands the GM the kind of Opportunity it can bank, since the spell
                // works through the whole round (Core Rules §11, "Sustaining a Spell").
                if (pending.Kind == ReactionKind.Sustain)
                {
                    GrantBankedOpportunity();
                }
                else
                {
                    GrantImmediateOpportunity(pending.Defender);
                }
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
                combatant.HasOverclockedThisRound = false;
                combatant.HasSacrificedThisRound = false;
            }

            EndSpellsOfFallenCasters(roundEnd: true);
            AgeControls();

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
                StartTurnFor(passedOver);
                if (passedOver.BodyStatus == BodyStatus.Dying && !passedOver.HasFled)
                {
                    RollToStabilise(passedOver);
                }
                else
                {
                    Write($"{passedOver.Name} has no turn to take.");
                }

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
            OfferSustain(next, () => TakeControlledTurn(next, resume: () => { }, endTurn: () => EndPartyTurn(next)));
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
            // A creature a Control spell holds does what the spell says, not what the GM would.
            idle.RemoveAll(a => a.IsOutOfFight || IsControlled(a));
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
            var route = PlanMove(adversary, closeIn.Square)!;
            if (!Swims(adversary, route, "its move"))
            {
                Next();
                return;
            }

            var setOff = adversary.Position;
            Relocate(adversary, route.Path, arrivedAt =>
            {
                var crossed = setOff.DistanceTo(arrivedAt);
                if (!adversary.IsOutOfFight)
                {
                    var from = setOff;
                    adversary.Position = arrivedAt;
                    lastMoved = adversary;
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
            // Help and Harry lapse at the start of the giver's next turn, unused; a raised shield
            // comes down at the start of its bearer's (Core Rules §9).
            foreach (var other in combatants)
            {
                if (other.HelpedBy.Remove(combatant))
                {
                    Write($"{combatant.Name}'s Help for {other.Name} lapses unused.");
                }

                if (other.HarriedBy.Remove(combatant))
                {
                    Write($"{combatant.Name}'s Harrying of {other.Name} lapses unused.");
                }

                if (other.CoveredBy.RemoveAll(c => c.Bearer == combatant) > 0)
                {
                    Write(other == combatant
                        ? $"{combatant.Name} lowers their shield."
                        : $"{combatant.Name} lowers their shield from over {other.Name}.");
                }
            }

            var wasStanding = combatant.StandingGround.ToList();
            combatant.ThresholdModifiers = ThresholdModifierCalculator.AtTurnStart(combatant.ThresholdModifiers);
            combatant.HasMovedThisTurn = false;
            combatant.HasWithdrawnThisTurn = false;

            if (wasStanding.Count > 0)
            {
                Write($"{combatant.Name} stops standing their ground ({string.Join(", ", wasStanding)}).");
            }

            ExpireSpellsAtTurnStart(combatant);
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

            OfferSustain(actor, () => TakeControlledTurn(actor, () => TakeStep(turn, done), () =>
            {
                FinishTurn(actor);
                RecheckControls(actor, done);
            }));
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
                    if (!CanAttack(actor, attack.Target)
                        || (attack.With is { } line && (!actor.Attacks.Contains(line) || !PlanAttackWith(actor, line, actor.Position, attack.Target).CanAttack)))
                    {
                        throw new InvalidOperationException($"The GM brain had {actor.Name} attack {attack.Target.Name}, which it can't do now.");
                    }

                    if (attack.WithAdvantage && !turn.BonusAvailable)
                    {
                        throw new InvalidOperationException($"The GM brain spent an Opportunity on {actor.Name}'s bonus, which this turn can't take.");
                    }

                    turn.HasAttacked = true;
                    turn.BonusAvailable &= !attack.WithAdvantage;
                    ResolveAttack(actor, attack.Target, asReaction: false, attack.WithAdvantage, _ => TakeStep(turn, done), attack.With);
                    return;

                case GmStep.Cast cast:
                    if (PlanCast(actor, cast.Spell) is not { CanCast: true } castPlan)
                    {
                        throw new InvalidOperationException($"The GM brain had {actor.Name} cast {cast.Spell.Spell.Name}: {PlanCast(actor, cast.Spell).Refusal}");
                    }

                    turn.HasAttacked = true;
                    Cast(actor, cast.Spell, castPlan, () => TakeStep(turn, done));
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

                case GmStep.Aid aid:
                    if (AidRefusal(actor, aid.Action, aid.Target) is { } cannotAid)
                    {
                        throw new InvalidOperationException($"The GM brain had {actor.Name} {aid.Action} {aid.Target.Name}: {cannotAid}");
                    }

                    turn.HasAttacked = true;
                    Aid(actor, aid.Action, aid.Target);
                    TakeStep(turn, done);
                    return;

                case GmStep.Tend tend:
                    if (TendRefusal(actor, tend.Action, tend.Target) is { } cannotTend)
                    {
                        throw new InvalidOperationException($"The GM brain had {actor.Name} {tend.Action} {tend.Target.Name}: {cannotTend}");
                    }

                    turn.HasAttacked = true;
                    Tend(actor, tend.Action, tend.Target);
                    TakeStep(turn, done);
                    return;

                case GmStep.Drag drag:
                    if (DragCostTo(actor, drag.Square) is not { } dragCost)
                    {
                        throw new InvalidOperationException($"The GM brain had {actor.Name} drag to {drag.Square}, which it can't.");
                    }

                    turn.HasMoved = true;
                    Drag(actor, drag.Square, dragCost, () => TakeStep(turn, done));
                    return;

                case GmStep.RegainFooting:
                    if (RegainFootingRefusal(actor) is { } cannotRegain)
                    {
                        throw new InvalidOperationException($"The GM brain had {actor.Name} regain its footing: {cannotRegain}");
                    }

                    RegainFooting(actor);
                    TakeStep(turn, done);
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
                    RecheckControls(actor, done);
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
        private Queue<Combatant> LeavingReachOf(Combatant mover, IReadOnlyList<GridPosition> path) =>
            new(Fighting(mover.Side == Side.Party ? Side.Adversaries : Side.Party)
                .Select(e => (Enemy: e, At: LeaveIndex(e, path)))
                .Where(x => x.At is not null)
                .OrderBy(x => x.At)
                .Select(x => x.Enemy));

        /// <summary>
        /// Where along <paramref name="path"/> it leaves <paramref name="enemy"/>'s reach — the
        /// last square still within it before the step out — or null if it never does. A route that
        /// passes through somebody's reach and out again leaves it, however briefly it was in.
        /// </summary>
        private static int? LeaveIndex(Combatant enemy, IReadOnlyList<GridPosition> path)
        {
            for (var i = 0; i + 1 < path.Count; i++)
            {
                if (enemy.CanReach(path[i]) && !enemy.CanReach(path[i + 1]))
                {
                    return i;
                }
            }

            return null;
        }

        /// <summary>
        /// Carries <paramref name="mover"/> along <paramref name="path"/>, offering a reaction to
        /// each enemy whose reach it leaves on the way. Says where it ends up: the path's end, or the
        /// square it was caught in — "the rest of that move is lost" (Core Rules §9). Leaves the
        /// position for the caller to set, along with the log line.
        /// </summary>
        private void Relocate(Combatant mover, IReadOnlyList<GridPosition> path, Action<GridPosition> then) =>
            ReactToLeaving(mover, path.ToList(), LeavingReachOf(mover, path), caughtAt => then(caughtAt ?? path[^1]));

        /// <summary>
        /// Swimming deep water on the way (the user's ruling): Athletics against the setting's
        /// Difficulty, once a move. Short of it, the move is spent and nobody goes anywhere.
        /// </summary>
        private bool Swims(Combatant mover, MovePlan plan, string paidWith)
        {
            if (!plan.SwimsDeepWater)
            {
                return true;
            }

            var (helped, harried) = TakeAid(mover);
            var roll = RollAgainst(mover.SkillBonus("Athletics"), Setting.SwimDifficulty, helped, harried, mover);
            if (roll.Success == true)
            {
                Write($"{mover.Name} swims the deep water: Athletics {DescribeRoll(roll)} against {Setting.SwimDifficulty}.");
                return true;
            }

            Write($"{mover.Name} struggles in the deep water and gets nowhere: Athletics {DescribeRoll(roll)} against {Setting.SwimDifficulty} — {paidWith} spent.");
            return false;
        }

        private void Move(Combatant mover, GridPosition square, int cost, Action then)
        {
            var plan = PlanMove(mover, square)!;
            var start = mover.Position;
            var paidWith = PayMoves(mover, cost);
            mover.HasMovedThisTurn = true;
            if (!Swims(mover, plan, paidWith))
            {
                then();
                return;
            }

            Relocate(mover, plan.Path, arrivedAt =>
            {
                if (!mover.IsOutOfFight)
                {
                    var from = start;
                    mover.Position = arrivedAt;
                    lastMoved = mover;
                    var band = RangeBandCalculator.BandForSquares(bands, from.DistanceTo(arrivedAt));
                    Write(arrivedAt == from
                        ? $"{mover.Name} is stopped before it gets anywhere — {paidWith} spent for nothing."
                        : $"{mover.Name} moves {from.DistanceTo(arrivedAt)} square(s) ({band}) for {paidWith}" +
                          (arrivedAt == square ? "." : $", and is stopped at {arrivedAt}."));
                    CheckHolds();
                    MeetHazards(mover, Crossed(plan.Path, arrivedAt), then);
                    return;
                }

                then();
            });
        }

        /// <summary>The squares of <paramref name="path"/> actually crossed, up to and including where the mover stopped.</summary>
        private static List<GridPosition> Crossed(IReadOnlyList<GridPosition> path, GridPosition stoppedAt)
        {
            var end = path.ToList().IndexOf(stoppedAt);
            return path.Take(end < 0 ? path.Count : end + 1).ToList();
        }

        /// <summary>
        /// A hazard met on the way (GM Reference §13): the creature rolls whichever of its Avoid
        /// checks it is best at, and a failure is the hazard's effect. Once a move, at the first
        /// hazard entered.
        /// </summary>
        private void MeetHazards(Combatant creature, IReadOnlyList<GridPosition> crossed, Action then)
        {
            var entered = crossed.Skip(1).Select(sq => (Square: sq, Hazard: Setting.HazardAt(sq))).FirstOrDefault(x => x.Hazard is not null);
            if (entered.Hazard is not { } hazard || creature.IsOutOfFight)
            {
                then();
                return;
            }

            var check = hazard.Avoid.MaxBy(c => creature.SkillBonus(c.Skill) - c.Difficulty)!;
            var (helped, harried) = TakeAid(creature);
            var roll = RollAgainst(creature.SkillBonus(check.Skill), check.Difficulty, helped, harried, creature);
            var line = $"{creature.Name} meets {hazard.Name}: {check.Skill} {DescribeRoll(roll)} against {check.Difficulty}";
            if (roll.Success == true)
            {
                Write(line + " — through it.");
                then();
                return;
            }

            switch (hazard.Effect)
            {
                case HazardEffect.Swept swept:
                    Write(line + $" — swept away.");
                    ForcedMove(creature, (swept.Dx, swept.Dy), swept.Squares, hazard.Name, cause: null, meetHazards: false, () =>
                    {
                        if (swept.LosesFooting && !creature.IsOutOfFight)
                        {
                            creature.NeedsFooting = true;
                            Write($"{creature.Name} must spend an action regaining their footing before acting normally again.");
                        }

                        then();
                    });
                    return;

                default:
                    Write(line + ".");
                    then();
                    return;
            }
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

                // The blow lands where the mover is when it leaves: the last square of the route
                // still within this enemy's reach.
                if (LeaveIndex(enemy, path) is { } leaving && leaving < path.Count)
                {
                    mover.Position = path[leaving];
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

                var leftAt = LeaveIndex(enemy, path) ?? 0;
                var catchAt = path.Take(leftAt + 1).LastOrDefault(square => square.DistanceTo(enemy.Position) <= enemy.OwnReach
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
            var dt = target.DodgeThreshold + VisibilityDodgeModifier(target);
            var st = target.StabilityThreshold;
            var (helped, harried) = TakeAid(actor);
            var roll = RollAgainst(actor.UnarmedAttackModifier, Math.Max(dt, st), helped, AttacksAtDisadvantage(actor) || harried, actor);
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
        /// <param name="with">Which of the attacker's attacks; null for whichever does most from where it stands.</param>
        private void ResolveAttack(Combatant attacker, Combatant target, bool asReaction, bool opportunityBonus, Action<AttackOutcome> then,
            AttackProfile? with = null, bool sacrifice = false)
        {
            var plan = with is null ? PlanAttack(attacker, target) : PlanAttackWith(attacker, with, attacker.Position, target);
            if (sacrifice)
            {
                attacker.Body--;
                attacker.HasSacrificedThisRound = true;
                attacker.TendEligible[WoundType.Body] = true;
                Write($"{attacker.Name} Sacrifices 1 Body for Advantage ({attacker.Body}/{attacker.BodyMax} Body).");
            }

            if (opportunityBonus)
            {
                Write($"The GM spends {SpendOpportunity()} Opportunity on {attacker.Name}'s bonus: Advantage.");
            }

            // Standing your ground costs the blow: Disadvantage on every attack roll while it lasts,
            // reaction attacks included (Core Rules §10) — as does being Off-guard (§9,
            // "Conditions"), or being Harried. Help, a spent Opportunity or a defenceless target
            // give Advantage (§4, "Dying"). The two cancel.
            var (helped, harried) = TakeAid(attacker);
            if (target.IsDefenceless)
            {
                Write($"{target.Name} is defenceless, lying {target.BodyStatus.ToString().ToLowerInvariant()}: Advantage.");
            }

            var advantage = opportunityBonus || helped || target.IsDefenceless || sacrifice;
            var disadvantage = AttacksAtDisadvantage(attacker) || harried;
            var rolled = Enumerable.Range(0, RollCalculator.DiceNeeded(advantage, disadvantage))
                .Select(_ => dice.Roll(RollCalculator.HighestFace))
                .ToList();
            var attack = plan.Profile ?? attacker.Attack;
            var roll = RollCalculator.Resolve(rolled, attack.AttackModifier, plan.DodgeThreshold, advantage, disadvantage);
            Slip(attacker, roll);

            if (!asReaction)
            {
                attacker.ActionsRemaining--;
            }

            // A shield in hand, not yet raised over its bearer, is the better reaction: its whole
            // bonus against this blow, and up until the bearer's next turn (Core Rules §9, "Shields").
            var shieldGain = target.InHandShield is { } shield && target.CoveredBy.All(c => c.Bearer != target)
                ? Math.Max(0, InventoryCalculator.ShieldDodgeThresholdBonus(shield.Weight) - target.ShieldBonus)
                : 0;
            var riposte = roll.IsCritical ? " — a critical, so turning it would Riposte it" : string.Empty;

            if (shieldGain > 0 && BoostWouldTurn(roll, plan.DodgeThreshold, shieldGain))
            {
                OfferReaction(
                    new PendingReaction(ReactionKind.RaiseShield, target, attacker, roll, plan.DodgeThreshold,
                        $"{attacker.Name}'s {attack.Name} rolls {roll.Total} against {target.Name}'s DT {plan.DodgeThreshold}{riposte}. " +
                        $"Raising the shield now (1 Stamina, +{shieldGain}, and it stays up) would make it miss.",
                        Bonus: shieldGain),
                    raised =>
                    {
                        if (raised)
                        {
                            RaiseShield(target, target, reactive: true);
                        }

                        then(FinishAttack(attacker, target, roll, plan, opportunityBonus, asReaction, raised ? shieldGain : 0, raised ? "shield raised" : null));
                    });
                return;
            }

            if (!BoostWouldTurn(roll, plan.DodgeThreshold))
            {
                then(FinishAttack(attacker, target, roll, plan, opportunityBonus, asReaction, boost: 0, boostedBy: null));
                return;
            }

            OfferReaction(
                new PendingReaction(ReactionKind.PanicDodge, target, attacker, roll, plan.DodgeThreshold,
                    $"{attacker.Name}'s {attack.Name} rolls {roll.Total} against {target.Name}'s DT {plan.DodgeThreshold}{riposte}. " +
                    "A panic dodge (1 Stamina, +2) would make it miss."),
                dodged => then(FinishAttack(attacker, target, roll, plan, opportunityBonus, asReaction,
                    dodged ? ThresholdModifierCalculator.ReactionBonus : 0, dodged ? "panic dodge" : null)));
        }

        /// <summary>
        /// Uses up whatever Help and Harrying <paramref name="roller"/> carries on the roll it's
        /// about to make — "Advantage on their next roll", "Disadvantage on their next roll"
        /// (Core Rules §9) — and says so in the log.
        /// </summary>
        private (bool Helped, bool Harried) TakeAid(Combatant roller)
        {
            var helped = roller.HelpedBy.Count > 0;
            var harried = roller.HarriedBy.Count > 0;
            if (helped)
            {
                Write($"{roller.Name} rolls with {string.Join(" and ", roller.HelpedBy.Select(h => h.Name))}'s Help: Advantage.");
            }

            if (harried)
            {
                Write($"{roller.Name} rolls Harried by {string.Join(" and ", roller.HarriedBy.Select(h => h.Name))}: Disadvantage.");
            }

            roller.HelpedBy.Clear();
            roller.HarriedBy.Clear();
            return (helped, harried);
        }

        private void Aid(Combatant actor, CombatAction action, Combatant target)
        {
            actor.ActionsRemaining--;
            (action == CombatAction.Help ? target.HelpedBy : target.HarriedBy).Add(actor);
            Write($"{actor.Name} {CombatActionCatalog.ActionLogLine(action, extra: false, target.Name).TrimEnd('.')}.");
        }

        /// <summary>
        /// +1 to the tended track, and that action is spent on it until a fresh wound (Core Rules §4,
        /// "Recovery"). A dying character healed back above 0 is back in the fight.
        /// </summary>
        private void Tend(Combatant actor, CombatAction action, Combatant target)
        {
            actor.ActionsRemaining--;
            var track = CombatActionCatalog.For(action).Restores!.Value;
            var wasDown = target.BodyStatus != BodyStatus.Healthy;
            switch (track)
            {
                case WoundType.Body:
                    target.Body = PlayStateCalculator.ApplyHeal(target.Body, 1, target.BodyMax);
                    if (target.Body > 0)
                    {
                        target.IsStable = false;
                    }

                    break;
                case WoundType.Mind:
                    target.Mind = PlayStateCalculator.ApplyHeal(target.Mind, 1, target.MindMax);
                    break;
                default:
                    target.Soul = PlayStateCalculator.ApplyHeal(target.Soul, 1, target.SoulMax);
                    break;
            }

            target.TendEligible[track] = false;
            var (now, max) = target.Track(track);
            var who = target == actor ? "themselves" : target.Name;
            Write($"{actor.Name} takes the {action} action on {who}: {track} {now}/{max}." +
                  (wasDown && target.BodyStatus == BodyStatus.Healthy ? $" {target.Name} is back in the fight!" : string.Empty));
        }

        /// <summary>
        /// Raises <paramref name="bearer"/>'s shield over <paramref name="over"/>: its bonus to Dodge
        /// Threshold until the start of the bearer's next turn. A braced Heavy shield raised over an
        /// ally covers the bearer too. Paid for already — the action, or the reaction's Stamina.
        /// </summary>
        private void RaiseShield(Combatant bearer, Combatant over, bool reactive)
        {
            var weight = bearer.InHandShield!.Weight;
            var covered = over == bearer || !bearer.IsShieldBraced ? new[] { over } : new[] { over, bearer };
            foreach (var who in covered)
            {
                who.CoveredBy.RemoveAll(c => c.Bearer == bearer);
                who.CoveredBy.Add((bearer, weight));
            }

            if (!reactive)
            {
                Write(over == bearer
                    ? $"{bearer.Name} raises their {weight} shield: +{InventoryCalculator.ShieldDodgeThresholdBonus(weight)} DT until their next turn (DT {bearer.DodgeThreshold})."
                    : $"{bearer.Name} raises their {weight} shield over {over.Name}" + (covered.Length > 1 ? ", braced, and over themselves" : string.Empty) +
                      $": DT {over.DodgeThreshold} until {bearer.Name}'s next turn.");
            }
        }

        /// <summary>Ends any shield cover whose ally has left the bearer's Reach, or whose bearer is out of the fight.</summary>
        private void CheckCover()
        {
            foreach (var covered in combatants.Where(c => c.CoveredBy.Count > 0))
            {
                var lapsed = covered.CoveredBy.RemoveAll(c => c.Bearer.IsOutOfFight
                    || (c.Bearer != covered && c.Bearer.Position.DistanceTo(covered.Position) > c.Bearer.Reach));
                if (lapsed > 0)
                {
                    Write($"{covered.Name} is no longer behind a shield.");
                }
            }
        }

        /// <summary>
        /// The dying character's roll at the start of their turn (Core Rules §4, "Dying"): 2d12 +
        /// Presence, 10 or more to stabilise. Short of it, they lose another Body wound — and at −3
        /// they're dead.
        /// </summary>
        private void RollToStabilise(Combatant dying)
        {
            const int stabiliseOn = 10;
            var presence = dying.Build?.GetModifier(Ability.Presence) ?? 0;
            var (helped, harried) = TakeAid(dying);
            var roll = RollAgainst(presence, stabiliseOn, helped, harried);
            if (roll.Success == true)
            {
                dying.IsStable = true;
                Write($"{dying.Name}, dying, rolls to hold on: {DescribeRoll(roll)} against 10 — stable, on 0 but no longer dying.");
                return;
            }

            dying.Body = PlayStateCalculator.ApplyDamage(dying.Body, 1, PlayStateCalculator.MinBodyWounds);
            dying.TendEligible[WoundType.Body] = true;
            Write($"{dying.Name}, dying, rolls to hold on: {DescribeRoll(roll)} against 10 — worse: Body {dying.Body}" +
                  (dying.BodyStatus == BodyStatus.Dead ? $". {dying.Name} is dead." : $". At {PlayStateCalculator.MinBodyWounds} they die."));
        }

        /// <summary>
        /// A move hauling the held creature along (Core Rules §9, "Drag"): a roll against its
        /// Stability Threshold first — which it may stand firm against — once per move. On a
        /// success it comes too, ending in a square within the holder's own reach, and the move
        /// costs one more than it would; on a failure the move is spent and nobody goes anywhere.
        /// Drag isn't Forced Movement, so it can't be arrested; but leaving the reach of the
        /// holder's allies on the way gives each of them a reaction.
        /// </summary>
        private void Drag(Combatant holder, GridPosition square, int cost, Action then)
        {
            var held = holder.Holding!;
            var paidWith = PayMoves(holder, cost);
            holder.HasMovedThisTurn = true;

            var (helped, harried) = TakeAid(holder);
            var threshold = held.StabilityThreshold;
            var roll = RollAgainst(holder.UnarmedAttackModifier, threshold, helped, AttacksAtDisadvantage(holder) || harried, holder);

            void Finish(bool stoodFirm)
            {
                var final = stoodFirm ? Reread(roll, threshold + ThresholdModifierCalculator.ReactionBonus) : roll;
                var line = $"{holder.Name} tries to drag {held.Name}: {DescribeRoll(final)} vs ST {final.Threshold}" +
                           (stoodFirm ? " (+2 standing firm)" : string.Empty);

                if (final.Success != true)
                {
                    var overbalanced = final.IsCritical && roll.Success != true;
                    Write(line + (overbalanced
                        ? $" — critical failure! {holder.Name} goes down, Prone, and the move ({paidWith}) is spent."
                        : $" — {held.Name} digs in. The move ({paidWith}) is spent, and nobody goes anywhere."));
                    if (overbalanced)
                    {
                        holder.SetCondition(CharacterCondition.Prone, true);
                    }

                    then();
                    return;
                }

                Write(line + (final.IsCritical ? $" — critical! {held.Name} is hauled off their feet." : $" — {held.Name} comes along."));
                var heldFrom = held.Position;

                // The holder's own leaving draws reactions first — from everyone but the creature it
                // hauls, which comes along — and whoever's reach the held creature is hauled out of,
                // the holder's allies, may then react to it.
                var route = PlanMove(holder, square)?.Path ?? PathBetween(holder.Position, square);
                var setOff = holder.Position;
                var holderReactors = new Queue<Combatant>(LeavingReachOf(holder, route).Where(e => e != held));
                ReactToLeaving(holder, route.ToList(), holderReactors, caughtAt =>
                {
                    var arrivedAt = caughtAt ?? square;
                    if (holder.IsOutOfFight)
                    {
                        then();
                        return;
                    }

                    var from = setOff;
                    holder.Position = arrivedAt;
                    lastMoved = holder;
                    Write($"{holder.Name} moves {from.DistanceTo(arrivedAt)} square(s), hauling {held.Name}, for {paidWith}.");

                    var landing = SquaresAround(arrivedAt, holder.OwnReach)
                        .Where(sq => sq == heldFrom || (At(sq) is null && !Setting.IsWall(sq)))
                        .OrderBy(sq => sq.DistanceTo(heldFrom))
                        .ThenBy(sq => (sq.X - heldFrom.X) * (sq.X - heldFrom.X) + (sq.Y - heldFrom.Y) * (sq.Y - heldFrom.Y))
                        .Select(sq => (GridPosition?)sq)
                        .FirstOrDefault() ?? heldFrom;

                    var reactors = new Queue<Combatant>(LeavingReachOf(held, PathBetween(heldFrom, landing)).Where(a => a != holder));

                    ReactToLeaving(held, PathBetween(heldFrom, landing), reactors, caughtAt =>
                    {
                        if (!held.IsOutOfFight)
                        {
                            held.Position = caughtAt ?? landing;
                            if (final.IsCritical)
                            {
                                held.SetCondition(CharacterCondition.Prone, true);
                            }
                        }

                        CheckHolds();
                        then();
                    });
                });
            }

            if (!BoostWouldTurn(roll, threshold))
            {
                Finish(false);
                return;
            }

            OfferReaction(
                new PendingReaction(ReactionKind.StandFirm, held, holder, roll, threshold,
                    $"{holder.Name} tries to drag {held.Name}: {roll.Total} against ST {threshold}. Standing firm (1 Stamina, +2 ST) would keep them put."),
                Finish);
        }

        private static IEnumerable<GridPosition> SquaresAround(GridPosition centre, int reach) =>
            Enumerable.Range(-reach, reach * 2 + 1)
                .SelectMany(dx => Enumerable.Range(-reach, reach * 2 + 1).Select(dy => new GridPosition(centre.X + dx, centre.Y + dy)))
                .Where(sq => sq != centre);

        /// <summary>Standing their ground or Off-guard: either puts Disadvantage on an attacker's roll (Core Rules §9, §10).</summary>
        private static bool AttacksAtDisadvantage(Combatant attacker) =>
            attacker.IsStandingGround || attacker.IsOffGuard || attacker.Conditions.Contains(CharacterCondition.Frightened);

        /// <summary>
        /// Whether +2 to the Threshold, decided after seeing the roll, would turn this success into
        /// a failure. Nothing answers a double twelve — "there is no Threshold to reach" (Core Rules §10).
        /// </summary>
        private static bool BoostWouldTurn(RollResult roll, int threshold, int bonus = ThresholdModifierCalculator.ReactionBonus) =>
            roll.Success == true
            && !roll.IsAbsoluteSuccess
            && roll.Total < threshold + bonus;

        /// <summary>
        /// Offers <paramref name="offer"/>'s defender their 1-Stamina answer, if they can pay for it,
        /// and carries on with whether they took it — already paid for when they did. The GM brain
        /// answers for its own creatures, whose spends hand nobody an Opportunity; a character's
        /// answer is waited for, through <see cref="Pending"/>.
        /// </summary>
        private void OfferReaction(PendingReaction offer, Action<bool> then)
        {
            var defender = offer.Defender;
            if (offer.Kind != ReactionKind.Backlash && TacticalSpendRefusal(defender) is not null)
            {
                then(false);
                return;
            }

            if (defender.Side == Side.Adversaries)
            {
                var takes = offer.Kind switch
                {
                    ReactionKind.PanicDodge or ReactionKind.RaiseShield => gm.PanicDodges(view, offer),
                    ReactionKind.StandFirm or ReactionKind.BraceResolve => gm.StandsFirm(view, offer),
                    ReactionKind.Backlash => gm.Backlashes(view, offer),
                    ReactionKind.Sustain => gm.Sustains(view, offer),
                    _ => gm.ArrestsShove(view, offer),
                };

                if (takes && offer.Kind != ReactionKind.Backlash)
                {
                    // An adversary's spend hands the GM nothing, a held spell's included (GM Reference §9).
                    PayTacticalSpend(defender, offer.Kind == ReactionKind.Sustain ? SpellCalculator.SustainStaminaCost : CombatActionCatalog.ReactionStaminaCost);
                    Write(offer.Kind switch
                    {
                        ReactionKind.PanicDodge => $"{defender.Name} panic dodges (1 Stamina).",
                        ReactionKind.StandFirm => $"{defender.Name} stands firm (1 Stamina, +2 ST).",
                        ReactionKind.BraceResolve => $"{defender.Name} steels themselves (1 Stamina, +2).",
                        ReactionKind.Sustain => $"{defender.Name} pays 1 Stamina to keep holding the spell.",
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
            var (helped, harried) = TakeAid(actor);
            var roll = RollAgainst(actor.UnarmedAttackModifier, threshold, opportunityBonus || helped, AttacksAtDisadvantage(actor) || harried, actor);
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
        private void Shove(Combatant shover, Combatant target, Action then) =>
            ForcedMove(target, (Math.Sign(target.Position.X - shover.Position.X), Math.Sign(target.Position.Y - shover.Position.Y)),
                shover.OwnReach, $"{shover.Name}'s shove", shover, meetHazards: true, then);

        /// <summary>
        /// Forced Movement (Core Rules §9): <paramref name="distance"/> squares in a straight line
        /// along <paramref name="step"/>, stopping at anything solid — a creature or a wall — where
        /// the distance left uncrossed is Body wounds, armour absorbing as usual. The target may
        /// arrest it for 1 Stamina; being carried out of an enemy's reach draws reactions, and may
        /// see the target caught on the way; ground carried onto may hold a hazard.
        /// </summary>
        /// <param name="cause">Who is doing the moving, if anybody — a hazard isn't anybody.</param>
        private void ForcedMove(Combatant target, (int X, int Y) step, int distance, string what, Combatant? cause, bool meetHazards, Action then)
        {
            var destination = target.Position;
            var crossed = 0;
            string? stoppedBy = null;

            while (crossed < distance)
            {
                var next = new GridPosition(destination.X + step.X, destination.Y + step.Y);
                if (At(next) is { } inTheWay)
                {
                    stoppedBy = inTheWay.Name;
                    break;
                }

                if (Setting.IsWall(next))
                {
                    stoppedBy = "the wall";
                    break;
                }

                destination = next;
                crossed++;
            }

            var impact = distance - crossed;
            var into = stoppedBy is null ? string.Empty : $" into {stoppedBy} — {impact} Body before armour";
            var path = PathBetween(target.Position, destination);

            OfferReaction(
                new PendingReaction(ReactionKind.Arrest, target, cause ?? target, Roll: null, Threshold: 0,
                    $"{what} carries {target.Name} {crossed} square(s){into}. Arresting it (1 Stamina) keeps them where they stand.",
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
                    ReactToLeaving(target, path, LeavingReachOf(target, path), caughtAt =>
                    {
                        // Caught on the way through, the push ends where the catch put them.
                        if (caughtAt is null && !target.IsOutOfFight)
                        {
                            target.Position = destination;
                            lastMoved = target;
                            if (crossed > 0)
                            {
                                Write($"{target.Name} is carried {crossed} square(s).");
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
                        if (meetHazards && caughtAt is null)
                        {
                            MeetHazards(target, path, then);
                            return;
                        }

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
            var (helped, harried) = TakeAid(breaker);
            var roll = RollAgainst(breaker.UnarmedAttackModifier, threshold, helped, AttacksAtDisadvantage(breaker) || harried, breaker);
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
        private RollResult RollAgainst(int modifier, int threshold, bool advantage, bool disadvantage, Combatant? roller = null)
        {
            var rolled = Enumerable.Range(0, RollCalculator.DiceNeeded(advantage, disadvantage))
                .Select(_ => dice.Roll(RollCalculator.HighestFace))
                .ToList();
            var roll = RollCalculator.Resolve(rolled, modifier, threshold, advantage, disadvantage);
            if (roller is not null)
            {
                Slip(roller, roll);
            }

            return roll;
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

        /// <param name="boost">What a panic dodge or a shield raised in time added to DT after the roll; 0 for neither.</param>
        /// <param name="boostedBy">Which of the two it was, for the log.</param>
        private AttackOutcome FinishAttack(
            Combatant attacker, Combatant target, RollResult rolled, AttackPlan plan, bool usedOpportunity, bool asReaction,
            int boost, string? boostedBy)
        {
            // Re-read the same dice against the raised Threshold. A dodged critical comes back a
            // critical failure, which is exactly what a Riposte is (Core Rules §10).
            var dodged = boost > 0;
            var roll = dodged ? Reread(rolled, plan.DodgeThreshold + boost) : rolled;

            int rolledDamage = 0, absorbed = 0, dealt = 0;
            var attackerWounded = false;

            if (roll.Success == true)
            {
                rolledDamage = RollDamage(plan.Profile ?? attacker.Attack, plan, roll.IsCritical);
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
                usedOpportunity, asReaction, dodged, boost, boostedBy);
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

            // A fresh wound is what makes Heal worth doing again (Core Rules §4, "Recovery").
            combatant.TendEligible[WoundType.Body] = true;

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

            // Whoever just went down lets go, or is let go of; a shield's cover ends out of Reach.
            CheckHolds();
            CheckCover();
            EndSpellsOfFallenCasters(roundEnd: false);
            EndControlsOfFallenCasters();
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
                // Left behind while still willing to fight is the party getting away, not winning.
                var leftBehind = combatants.Any(c => c.Side == Side.Adversaries && c.HasFled && !c.IsBroken && c.WasOutdistanced);
                Phase = leftBehind ? EncounterPhase.Withdrew : EncounterPhase.Victory;
                Active = null;
                Write(leftBehind
                    ? "The party breaks contact — foes still willing to fight are left behind, beyond Long."
                    : combatants.Any(c => c.Side == Side.Adversaries && c.HasFled)
                        ? "No foe is left on the board. Victory!"
                        : "The last foe falls. Victory!");
                ReportObjectives();
                return true;
            }

            return false;
        }

        /// <summary>Every free square <paramref name="mover"/> could reach for at most <paramref name="moves"/> moves, with the price of each.</summary>
        internal Dictionary<GridPosition, int> SquaresWithin(Combatant mover, int moves) =>
            MapFor(mover).All.Where(o => o.Value.Cost <= moves).ToDictionary(o => o.Key, o => o.Value.Cost);

        private readonly Dictionary<Combatant, (int Stamp, MovementMap Map)> movementMaps = new();

        /// <summary>
        /// Every route <paramref name="mover"/> could take from where it stands, worked out once for
        /// as long as nobody on the board moves.
        /// </summary>
        private MovementMap MapFor(Combatant mover)
        {
            // As far as the moves it could have would carry it: two for a fresh turn, more with
            // actions bought.
            var reach = Math.Max(1, SquaresFor(mover.Speed, Math.Max(2, MovesAvailable(mover))));
            var stamp = HashCode.Combine(BoardStamp(mover), reach);
            if (movementMaps.TryGetValue(mover, out var known) && known.Stamp == stamp)
            {
                return known.Map;
            }

            var tables = environment;
            var enemySide = mover.Side == Side.Party ? Side.Adversaries : Side.Party;
            var standing = combatants.Where(c => c.OccupiesSquare && c != mover).GroupBy(c => c.Position).ToDictionary(g => g.Key, g => g.First());
            var threats = Fighting(enemySide).ToList();
            var map = new MovementMap(
                mover.Position,
                radius: reach,
                isWall: Setting.IsWall,
                isClosed: square => standing.TryGetValue(square, out var there)
                    && (there.Side != mover.Side || threats.Any(e => e.CanReach(square))),
                canStop: square => !standing.ContainsKey(square),
                isBroken: square => tables is not null && Setting.GroundAt(square) == Terrain.Broken,
                isDeep: Setting.IsDeepWater,
                price: steps => RangeBandCalculator.MoveCost(bands, steps, mover.Speed),
                brokenTax: tables is null ? 0 : EnvironmentCalculator.ExtraMoveCost(tables, Terrain.Broken));
            movementMaps[mover] = (stamp, map);
            return map;
        }

        /// <summary>Changes whenever anything that shapes a route does: who stands where, and who threatens what.</summary>
        private int BoardStamp(Combatant mover)
        {
            var hash = new HashCode();
            hash.Add(mover.Speed);
            foreach (var c in combatants)
            {
                hash.Add(c.Position);
                hash.Add(c.OccupiesSquare);
                hash.Add(c.IsOutOfFight);
                hash.Add(c.Reach);
            }

            return hash.ToHashCode();
        }

        /// <summary>How a move to <paramref name="square"/> would go, or null if it can't be made at all.</summary>
        internal MovePlan? PlanMove(Combatant mover, GridPosition square) =>
            square == mover.Position ? null : MapFor(mover).To(square);

        /// <summary>What a move to <paramref name="square"/> is priced at, whatever is left to pay with; null where nobody could stop there.</summary>
        /// <summary>
        /// What a move to <paramref name="square"/> is priced at, whatever is left to pay with; null
        /// where nobody could stop there. Broken ground anywhere the move begins, ends or passes
        /// through adds a move (Core Rules §9, "Terrain").
        /// </summary>
        private int? PriceOfMove(Combatant mover, GridPosition square) => PlanMove(mover, square)?.Cost;

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
            var dodge = outcome.Dodged ? $" (+{outcome.Boost} {outcome.BoostedBy})" : string.Empty;
            var verb = outcome.AsReaction ? "strikes at" : "attacks";
            var line = $"{outcome.Attacker.Name} {verb} {outcome.Target.Name} with {(outcome.Plan.Profile ?? outcome.Attacker.Attack).Name}: " +
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
        /// <summary>Whoever last changed square, by their own feet or by force — to tell a foe leaving from the party leaving it.</summary>
        private Combatant? lastMoved;

        private void CheckDepartures()
        {
            foreach (var gone in Fighting(Side.Adversaries).Where(a => !IsWithinLongOfParty(a.Position)).ToList())
            {
                Release(gone, "lets go");
                gone.HasFled = true;

                // Whose move put it out of reach: a character walking off is the party getting clear;
                // the creature's own going, or being carried off, is it leaving.
                gone.WasOutdistanced = lastMoved is { Side: Side.Party };
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
