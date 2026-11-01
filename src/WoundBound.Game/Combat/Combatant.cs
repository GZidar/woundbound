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

        /// <summary>The Mind track (Core Rules §4). Nothing in the slice deals Mind damage yet.</summary>
        public int MindMax { get; init; }

        public int Mind { get; internal set; }

        /// <summary>The Soul track. Nothing in the slice deals Soul damage yet.</summary>
        public int SoulMax { get; init; }

        public int Soul { get; internal set; }

        /// <summary>
        /// Whether Heal, Meditate or Pray could tend each track (Core Rules §4, "Recovery"): set by
        /// a fresh wound of that type, cleared once the matching action has helped it. A character
        /// never tended is eligible, so long as they carry a wound.
        /// </summary>
        internal Dictionary<WoundType, bool> TendEligible { get; } = new()
        {
            [WoundType.Body] = true,
            [WoundType.Mind] = true,
            [WoundType.Soul] = true,
        };

        /// <summary>Where a track stands, for the recovery actions: (current, max).</summary>
        public (int Current, int Max) Track(WoundType type) => type switch
        {
            WoundType.Body => (Body, BodyMax),
            WoundType.Mind => (Mind, MindMax),
            _ => (Soul, SoulMax),
        };

        /// <summary>Whether Heal, Meditate or Pray would do anything for this track right now (Core Rules §4).</summary>
        public bool CanBeTended(WoundType type) =>
            Track(type) is var (current, max) && PlayStateCalculator.CanTend(current, max, TendEligible[type]);

        /// <summary>
        /// Allies whose Help this creature carries — Advantage on its next roll — and enemies whose
        /// Harrying it carries — Disadvantage on it (Core Rules §9, "Actions in Combat"). Each
        /// lapses at the start of the giver's next turn if no roll has used it.
        /// </summary>
        internal List<Combatant> HelpedBy { get; } = new();

        internal List<Combatant> HarriedBy { get; } = new();

        /// <summary>Somebody's Help is waiting on this creature's next roll.</summary>
        public bool IsHelped => HelpedBy.Count > 0;

        /// <summary>Somebody's Harrying is waiting on this creature's next roll.</summary>
        public bool IsHarried => HarriedBy.Count > 0;

        /// <summary>
        /// Shields raised over this creature, by whom, until the start of each bearer's next turn
        /// (Core Rules §9, "Shields"). Only the largest counts: "only one shield ever covers a
        /// character at a time".
        /// </summary>
        internal List<(Combatant Bearer, ShieldWeight Weight)> CoveredBy { get; } = new();

        /// <summary>The Dodge Threshold the shield covering this creature adds, if any.</summary>
        public int ShieldBonus => CoveredBy.Count == 0 ? 0 : CoveredBy.Max(c => InventoryCalculator.ShieldDodgeThresholdBonus(c.Weight));

        /// <summary>The shield in this character's hand, if any — the heaviest, should there be two.</summary>
        public ShieldDefinition? InHandShield => Build is { } build
            ? InventoryCalculator.InHandShields(build).OrderByDescending(s => s.Weight).FirstOrDefault()
            : null;

        /// <summary>A Heavy shield held with no weapon in hand: one raise covers the bearer and an ally (Core Rules §9, "Bracing").</summary>
        public bool IsShieldBraced => InHandShield is { } shield
            && InventoryCalculator.IsBraced(shield.Weight, hasWeaponEquipped: InHandWeapon is not null, holdingLitLight: Build is { } b && InventoryCalculator.LitLightsHeld(b) > 0);

        /// <summary>
        /// Lying on the board, taking up a square: anybody still fighting, a character dying or
        /// stable (who can still be reached, healed — and stabbed), or a foe who gave up.
        /// </summary>
        public bool OccupiesSquare => !HasFled
            && (!IsOutOfFight || HasSurrendered || (Side == Side.Party && BodyStatus is BodyStatus.Dying or BodyStatus.Stable));

        /// <summary>A character on 0 Body or below and not yet dead — defenceless: attacks on them have Advantage (Core Rules §4, "Dying").</summary>
        public bool IsDefenceless => Side == Side.Party && BodyStatus is BodyStatus.Dying or BodyStatus.Stable && !HasFled;

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
        public int DodgeThreshold => BaseDodgeThreshold + ThresholdModifierCalculator.NetModifier(ThresholdModifiers, ThresholdType.Dodge) + ShieldBonus;

        /// <summary>Stability as it stands right now: the ground underfoot moves it too — Sure +2, Broken −2 (Core Rules §9, "Terrain").</summary>
        public int StabilityThreshold => BaseStabilityThreshold + ThresholdModifierCalculator.NetModifier(ThresholdModifiers, ThresholdType.Stability)
            + (GroundStability?.Invoke(Position) ?? 0);

        /// <summary>What the ground on a square does to Stability, as the encounter's setting says. Null outside an encounter.</summary>
        internal Func<GridPosition, int>? GroundStability { get; set; }

        /// <summary>What the scene's Exposure does to Resolve — the same for everybody in it (Core Rules §9, "Exposure").</summary>
        internal int SceneResolve { get; set; }

        /// <summary>
        /// The lit lights this creature is holding — a character's from their pack, each taking a
        /// hand; a foe's as the encounter set it, like the lantern-bearer's lantern.
        /// </summary>
        public int CarriedLights => Build is { } build ? InventoryCalculator.LitLightsHeld(build) : StatblockLights;

        /// <summary>Lights a generic foe carries, set by whoever stages the encounter.</summary>
        public int StatblockLights { get; internal set; }

        public int? ResolveThreshold => BaseResolveThreshold + ThresholdModifierCalculator.NetModifier(ThresholdModifiers, ThresholdType.Resolve) + SceneResolve;

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

        /// <summary>
        /// A generic foe's attack lines, the first first — "a creature with more than one attack
        /// takes a row for each, the second beginning <i>or</i>" (GM Reference §9). Unused where
        /// there is a <see cref="Build"/>.
        /// </summary>
        internal IReadOnlyList<AttackProfile> StatblockAttacks { get; init; } = [];

        private AttackProfile? StatblockAttack => StatblockAttacks.Count > 0 ? StatblockAttacks[0] : null;

        /// <summary>What this creature attacks with first — for a character, whatever is in hand; for a foe, its first attack line.</summary>
        public AttackProfile Attack => Build is { } build
            ? AttackProfile.ForWeapon(build, InHandWeapon)
            : StatblockAttack ?? throw new InvalidOperationException($"{Name} has neither a character sheet nor a statblock attack.");

        /// <summary>Every attack it could make: a character's weapon in hand, or each of a foe's attack lines.</summary>
        public IReadOnlyList<AttackProfile> Attacks => Build is not null ? [Attack] : StatblockAttacks;

        /// <summary>The attack it threatens its Reach with: the first melee line, or the first line of any kind if it has no melee one.</summary>
        public AttackProfile MeleeAttack => Attacks.FirstOrDefault(a => !a.IsRanged) ?? Attack;

        /// <summary>Whether any of its attacks is a shot — a creature that can fight from a distance.</summary>
        public bool HasRangedAttack => Attacks.Any(a => a.IsRanged);

        /// <summary>The weapon in hand; null when fighting unarmed or for a generic foe.</summary>
        public WeaponDefinition? InHandWeapon => Build is { } build ? InventoryCalculator.InHandWeapons(build).FirstOrDefault() : null;

        /// <summary>The weapon at the ready (Core Rules §15, "Equipped Items"), a swap away.</summary>
        public WeaponDefinition? ReadyWeapon => Build is { } build ? InventoryCalculator.ReadyWeapons(build).FirstOrDefault() : null;

        public required ArmourWeight Armour { get; init; }

        /// <summary>How far it threatens with a blade — the ground a reaction attack covers (Core Rules §9, "Reach"). A bow threatens what the body does.</summary>
        public int Reach => Build is not null ? Attack.Reach : MeleeAttack.IsRanged ? OwnReach : MeleeAttack.Reach;

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
        public int OwnReach => Build is not null ? WeaponCalculator.DefaultOwnReach : StatblockAttack?.OwnReach ?? WeaponCalculator.DefaultOwnReach;

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
        public bool IsOutOfFight => IsUnconscious || HasFled || HasSurrendered
            || (Side == Side.Party ? BodyStatus != BodyStatus.Healthy : Body <= 0);

        /// <summary>
        /// Has cast a spell in this fight — which is how a caster gives themselves away, and why
        /// the other side goes for them first from then on. Nothing casts yet.
        /// </summary>
        public bool HasCastSpell { get; internal set; }

        /// <summary>The statblock's Role, for a foe; null for a character.</summary>
        public Rules.Enums.AdversaryRole? Role { get; init; }

        /// <summary>When this creature stops being willing to fight; null for one that never will.</summary>
        public Morale? Morale { get; init; }

        /// <summary>Has reached its breaking point, and is looking for a way out rather than a fight (GM Reference §8).</summary>
        public bool IsBroken { get; internal set; }

        /// <summary>Has left the field, and takes no further part in the Challenge.</summary>
        public bool HasFled { get; internal set; }

        /// <summary>Gave up when the fight ended — caught in a hold as the rest of its side broke and ran.</summary>
        public bool HasSurrendered { get; internal set; }

        /// <summary>Whether a melee blow could land on <paramref name="square"/> — within Reach, and not inside an Unwieldy haft.</summary>
        public bool CanReach(GridPosition square) => CanReachFrom(Position, square);

        /// <summary>Whether a melee blow from <paramref name="from"/>, were this creature standing there, could land on <paramref name="square"/>.</summary>
        public bool CanReachFrom(GridPosition from, GridPosition square)
        {
            var distance = from.DistanceTo(square);
            return distance <= Reach && !(MeleeAttack.IsUnwieldy && distance <= 1);
        }
    }
}
