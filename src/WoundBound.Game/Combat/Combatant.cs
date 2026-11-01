using WoundBound.Rules.Calculation;
using WoundBound.Rules.Catalog;
using WoundBound.Rules.Enums;
using WoundBound.Rules.Models;

namespace WoundBound.Game.Combat
{
    public enum Side
    {
        Party,
        Adversaries
    }

    /// <summary>
    /// One creature on the board, with the handful of numbers a fight reads. Every one of them is
    /// derived by <see cref="CombatantFactory"/> from the Rules library — a character's from its
    /// <see cref="Rules.Models.CharacterBuild"/>, a generic foe's from its statblock — so nothing
    /// here recomputes a formula.
    /// </summary>
    public sealed class Combatant
    {
        public required string Name { get; init; }

        public required Side Side { get; init; }

        public GridPosition Position { get; internal set; }

        public required int BodyMax { get; init; }

        public int Body { get; internal set; }

        /// <summary>Stabilized at 0 Body (Core Rules §4). Only ever true of a player character.</summary>
        public bool IsStable { get; internal set; }

        /// <summary>Dodge Threshold before anything in play moves it: 11 + Tier + Agility for a character, the statblock's for a foe.</summary>
        public required int BaseDodgeThreshold { get; init; }

        /// <summary>Stability Threshold before anything in play moves it (Core Rules §10).</summary>
        public required int BaseStabilityThreshold { get; init; }

        /// <summary>Resolve Threshold before anything in play moves it; null for a foe with no mind to sway.</summary>
        public int? BaseResolveThreshold { get; init; }

        /// <summary>
        /// Everything currently moving a Threshold — for now the Dodge, Grounded and Resolute
        /// actions — held and summed the way the Rules library's Play Mode holds them.
        /// </summary>
        internal List<ThresholdModifier> ThresholdModifiers { get; set; } = new();

        /// <summary>What an attack is rolled against right now: the base, plus every modifier held (Core Rules §10).</summary>
        public int DodgeThreshold => BaseDodgeThreshold + ThresholdModifierCalculator.NetModifier(ThresholdModifiers, ThresholdType.Dodge);

        public int StabilityThreshold => BaseStabilityThreshold + ThresholdModifierCalculator.NetModifier(ThresholdModifiers, ThresholdType.Stability);

        public int? ResolveThreshold => BaseResolveThreshold + ThresholdModifierCalculator.NetModifier(ThresholdModifiers, ThresholdType.Resolve);

        /// <summary>
        /// The conditions this creature is under (Core Rules §9, "Conditions"), as the Rules
        /// library keeps them — a set, in the order they went on. Change them through
        /// <see cref="SetCondition"/>, which keeps Off-guard's −2 DT in step.
        /// </summary>
        public IReadOnlyList<CharacterCondition> Conditions => conditions;

        private List<CharacterCondition> conditions = new();

        public bool IsProne => conditions.Contains(CharacterCondition.Prone);

        /// <summary>Off-guard (Core Rules §9): −2 Dodge Threshold, and Disadvantage on attack rolls. Prone makes a creature so.</summary>
        public bool IsOffGuard => ConditionCatalog.IsOffGuard(conditions);

        /// <summary>Whoever has hold of this creature, if anybody (Core Rules §9, "Held").</summary>
        public Combatant? HeldBy { get; internal set; }

        /// <summary>The creature this one has hold of, if any — one at a time.</summary>
        public Combatant? Holding { get; internal set; }

        /// <summary>Puts <paramref name="condition"/> on or takes it off, and re-reads Off-guard's −2 DT from the result.</summary>
        internal void SetCondition(CharacterCondition condition, bool on)
        {
            conditions = ConditionCatalog.Set(conditions, condition, on);
            ThresholdModifiers = ConditionCatalog.WithOffGuard(ThresholdModifiers, conditions);
        }

        /// <summary>Which bracing actions — Dodge, Grounded, Resolute — this creature has standing, until the start of its next turn.</summary>
        public IEnumerable<CombatAction> StandingGround => ThresholdModifiers
            .Where(m => m.StackKey == ThresholdModifierCalculator.BracingActionKey)
            .Select(m => CombatActionCatalog.BracingAction(m.Threshold))
            .OfType<CombatAction>();

        /// <summary>
        /// Standing their ground (Core Rules §10): no move of any kind until their next turn, and
        /// Disadvantage on every attack roll they make while it lasts.
        /// </summary>
        public bool IsStandingGround => StandingGround.Any();

        /// <summary>Has spent movement this turn — and so can no longer stand their ground. Swapping a loadout isn't movement.</summary>
        public bool HasMovedThisTurn { get; internal set; }

        /// <summary>
        /// The character sheet behind a party member — the source of their loadouts, which change
        /// mid-fight. Null for a generic foe, which has a statblock instead.
        /// </summary>
        public CharacterBuild? Build { get; init; }

        /// <summary>A generic foe's attack line. Unused where there is a <see cref="Build"/>.</summary>
        internal AttackProfile? StatblockAttack { get; init; }

        /// <summary>What this creature attacks with right now — for a character, whatever is in hand.</summary>
        public AttackProfile Attack => Build is { } build
            ? AttackProfile.ForWeapon(build, InHandWeapon)
            : StatblockAttack ?? throw new InvalidOperationException($"{Name} has neither a character sheet nor a statblock attack.");

        /// <summary>The weapon in hand; null when fighting unarmed or for a generic foe.</summary>
        public WeaponDefinition? InHandWeapon => Build is { } build ? InventoryCalculator.InHandWeapons(build).FirstOrDefault() : null;

        /// <summary>The weapon at the ready (Core Rules §15, "Equipped Items"), a swap away.</summary>
        public WeaponDefinition? ReadyWeapon => Build is { } build ? InventoryCalculator.ReadyWeapons(build).FirstOrDefault() : null;

        public required ArmourWeight Armour { get; init; }

        public int Reach => Attack.Reach;

        /// <summary>The creature's own Speed — Normal for every player character, the statblock's for a foe.</summary>
        public Speed InnateSpeed { get; init; } = Speed.Normal;

        /// <summary>What a move is priced at right now: Crawling while Prone (Core Rules §9, "Prone and crawling").</summary>
        public Speed Speed => ConditionCatalog.Speed(conditions, InnateSpeed);

        /// <summary>
        /// A Swarm: no body in it to take hold of, and no ground it holds to be moved off, so it
        /// can be neither grabbed, shoved nor tripped — and grabs nobody in return (GM Reference §3).
        /// </summary>
        public bool IsSwarm { get; init; }

        /// <summary>
        /// The body's reach, not a weapon's — what a Grab, Shove or Trip reaches, and how far a
        /// Shove pushes (Core Rules §9, "Grab, Shove, Trip and Drag"). 1 for a character; a foe's
        /// statblock Reach, which is its own.
        /// </summary>
        public int OwnReach => Build is not null ? WeaponCalculator.DefaultOwnReach : StatblockAttack?.Reach ?? WeaponCalculator.DefaultOwnReach;

        /// <summary>
        /// What a Grab, Shove, Trip or break-free adds to its 2d12: an unarmed attack's Tier + stat
        /// for a character, and a foe's Attack bonus, "since that number already is its 2d12 + Tier
        /// + stat" (GM Reference §2).
        /// </summary>
        public int UnarmedAttackModifier => Build is { } build
            ? CharacterCalculator.WeaponAttackBonus(build, WeaponCalculator.Unarmed())
                ?? throw new InvalidOperationException($"{Name} needs a Class Principle before they can grab anybody.")
            : StatblockAttack?.AttackModifier ?? 0;

        /// <summary>
        /// A hand holding no weapon, shield or light (Core Rules §9, "Grab"). A character counts
        /// what's in hand; a foe is taken to have one unless it's a Swarm.
        /// </summary>
        public bool HasFreeHand => Build is { } build
            ? InventoryCalculator.InHandWeapons(build).Sum(w => InventoryCalculator.FillsBothHands(w) ? 2 : 1)
              + InventoryCalculator.InHandShields(build).Count
              + InventoryCalculator.LitLightsHeld(build) < 2
            : !IsSwarm;

        /// <summary>
        /// GM Reference §3, "Minion Rule": any Body hit that gets past its armour removes it from
        /// the fight outright, whatever was rolled.
        /// </summary>
        public bool IsMinion { get; init; }

        public required int StaminaMax { get; init; }

        public int Stamina { get; internal set; }

        public required int TacticalSpendsPerRound { get; init; }

        public int TacticalSpendsThisRound { get; internal set; }

        /// <summary>Started a turn on 0 Stamina (Core Rules §6).</summary>
        public bool IsUnconscious { get; internal set; }

        /// <summary>
        /// Has had a turn this round. An Opportunity's bonus can only go to an adversary that
        /// hasn't (Core Rules §9, "GM Opportunities"); an extra turn can go to either.
        /// </summary>
        public bool HasActedThisRound { get; internal set; }

        /// <summary>The turn's one move, still unspent.</summary>
        public bool HasMove { get; internal set; }

        /// <summary>Actions still to take this turn: the baseline one plus any bought with Stamina.</summary>
        public int ActionsRemaining { get; internal set; }

        public BodyStatus BodyStatus => PlayStateCalculator.GetBodyStatus(Body, IsStable);

        /// <summary>
        /// Whether this creature is still fighting. A generic foe is out at 0 Body with no dying
        /// state (GM Reference §2); a character at 0 is Dying, which the slice treats as out of the
        /// fight since nobody is left to stabilise them.
        /// </summary>
        public bool IsOutOfFight => IsUnconscious || (Side == Side.Party ? BodyStatus != BodyStatus.Healthy : Body <= 0);

        /// <summary>Whether a melee blow could land on <paramref name="square"/> — within Reach, and not inside an Unwieldy haft.</summary>
        public bool CanReach(GridPosition square) => CanReachFrom(Position, square);

        /// <summary>Whether a melee blow from <paramref name="from"/>, were this creature standing there, could land on <paramref name="square"/>.</summary>
        public bool CanReachFrom(GridPosition from, GridPosition square)
        {
            var distance = from.DistanceTo(square);
            return distance <= Reach && !(Attack.IsUnwieldy && distance <= 1);
        }
    }
}
