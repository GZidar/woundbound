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

        /// <summary>
        /// A phantom figure's body — an Illusion's, never in the fight's roster: it can't act, be
        /// hurt or hold ground, and only those taken in by it ever see it (Core Rules §11).
        /// </summary>
        public bool IsPhantom { get; internal set; }

        public required Side Side { get; init; }

        /// <summary>Where it stands — for a creature bigger than one square, the top-left corner of its footprint.</summary>
        public GridPosition Position
        {
            get => position;
            internal set
            {
                position = value;

                // A mount goes where it's ridden (Core Rules §9, "Mounts").
                if (IsRiding && Steed is { } steed)
                {
                    steed.position = value;
                }
            }
        }

        private GridPosition position;

        /// <summary>
        /// The mount this creature owns — ridden or standing riderless (Core Rules §9, "Mounts"). It
        /// takes no turns and counts toward nobody's cap; it has its own Body, Dodge and Stability
        /// Thresholds, Speed and attack. Null for anyone without one.
        /// </summary>
        public Combatant? Steed { get; internal set; }

        /// <summary>In the saddle: its moves use the mount's Speed, and its feet stay out of it.</summary>
        public bool IsRiding { get; internal set; }

        /// <summary>A mount, not a creature that fights on its own: never on the board's list, never given a turn.</summary>
        public bool IsSteed { get; init; }

        /// <summary>For a mount, whose it is.</summary>
        public Combatant? Rider { get; internal set; }

        /// <summary>
        /// A Nimble character's footwork — the free step and the cheaper Long move — which belongs to
        /// their legs, not the horse's (Core Rules §9, "Mounts").
        /// </summary>
        public bool HasFootwork => IsNimble && !IsRiding;

        /// <summary>
        /// How many squares across it stands: 1 for Tiny, Small and Medium, 2 for Large (4
        /// squares), 3 for Huge (9) — GM Reference §9, "Role - Size Type".
        /// </summary>
        public int Across { get; init; } = 1;

        /// <summary>Every square it stands on.</summary>
        public IEnumerable<GridPosition> Footprint => Footprints.At(Position, Across);

        /// <summary>Whether <paramref name="square"/> is one it stands on.</summary>
        public bool Covers(GridPosition square) => Footprints.Contains(Position, Across, square);

        /// <summary>
        /// Holds the ground it stands on, closing it to enemies. A Swarm occupies none of its
        /// squares, and a Tiny creature not its one: anyone may walk through or stop there (Core
        /// Rules §9, "Movement") — and either may come down on top of somebody, which is the only way
        /// a Reach of 0 bites anyone (GM Reference §3, "Where a Swarm Stands").
        /// </summary>
        public bool HoldsGround => !IsSwarm && !IsTiny;

        /// <summary>Tiny (GM Reference §9): stands on a square without occupying it, and reaches 0 unless its block says otherwise.</summary>
        public bool IsTiny { get; init; }

        /// <summary>Sees clearly even in complete darkness: the Visibility rules for darkness don't touch its attacks (a statblock's Nightvision).</summary>
        public bool SeesInDark { get; init; }

        /// <summary>Squares from the nearest square of its footprint to <paramref name="square"/>.</summary>
        public int DistanceTo(GridPosition square) => Footprints.Distance(Position, Across, square, 1);

        /// <summary>Squares between the nearest squares of the two footprints — 0 where they share one.</summary>
        public int DistanceTo(Combatant other) => Footprints.Distance(Position, Across, other.Position, other.Across);

        /// <summary>Squares to <paramref name="other"/> were this creature standing at <paramref name="at"/>.</summary>
        public int DistanceFrom(GridPosition at, Combatant other) => Footprints.Distance(at, Across, other.Position, other.Across);

        public required int BodyMax { get; init; }

        public int Body { get; internal set; }

        /// <summary>The Mind track (Core Rules §4): Aether spells deal it, and at 0 the creature is Stunned.</summary>
        public int MindMax { get; init; }

        public int Mind { get; internal set; }

        /// <summary>The Soul track: Divine spells deal it, and at 0 the creature is Numbed.</summary>
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
            && (!IsOutOfFight || HasSurrendered || (IsDazed && Body > 0) || (Side == Side.Party && BodyStatus is BodyStatus.Dying or BodyStatus.Stable));

        /// <summary>
        /// Defenceless — attacks on them have Advantage: a character on 0 Body or below and not yet
        /// dead (Core Rules §4, "Dying"), or anybody Stunned or Numbed and still breathing ("Like a
        /// dying or stable character, any attack against them rolls with Advantage").
        /// </summary>
        public bool IsDefenceless => !HasFled
            && ((Side == Side.Party && BodyStatus is BodyStatus.Dying or BodyStatus.Stable) || (IsDazed && Body > 0));

        /// <summary>At 0 Mind (Core Rules §4, "Stunned and Numbed"): no actions, no moves, no tactical spends, until someone restores a point.</summary>
        public bool IsStunned => MindMax > 0 && Mind <= 0;

        /// <summary>At 0 Soul: as Stunned, by the other track.</summary>
        public bool IsNumbed => SoulMax > 0 && Soul <= 0;

        /// <summary>Stunned or Numbed — lying out of the fight until Meditation, Prayer or magic brings them back.</summary>
        public bool IsDazed => IsStunned || IsNumbed;

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
        internal int SceneResolve => SceneResolveNow?.Invoke() ?? 0;

        /// <summary>Reads the weather where the creature stands, which a Utility spell may have eased (Core Rules §9).</summary>
        internal Func<int>? SceneResolveNow { get; set; }

        /// <summary>
        /// The lit lights this creature is holding — a character's from their pack, each taking a
        /// hand; a foe's as the encounter set it, like the lantern-bearer's lantern.
        /// </summary>
        public int CarriedLights => Build is { } build ? InventoryCalculator.LitLightsHeld(build) : StatblockLights;

        /// <summary>Lights a generic foe carries, set by whoever stages the encounter.</summary>
        public int StatblockLights { get; internal set; }

        public int? ResolveThreshold => BaseResolveThreshold + ThresholdModifierCalculator.NetModifier(ThresholdModifiers, ThresholdType.Resolve) + SceneResolve;

        /// <summary>
        /// Fortitude before any spell moves it: 11 + Tier + Vitality for a character, the statblock's
        /// FT for a foe (Core Rules §10). Null where nothing rolled against Fortitude can reach it —
        /// No Living Body, or Bound, Not Alive (GM Reference §9).
        /// </summary>
        public int? BaseFortitudeThreshold { get; init; }

        /// <summary>
        /// Fortitude as it stands: no action or Stamina moves it, only a Buff or a Bane (Core Rules
        /// §10, "Fortitude Threshold"). Null for a creature immune to anything rolled against it.
        /// </summary>
        public int? FortitudeThreshold => BaseFortitudeThreshold + ThresholdModifierCalculator.NetModifier(ThresholdModifiers, ThresholdType.Fortitude);

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
            : (StatblockLinesNow.Count > 0 ? StatblockLinesNow[0] : null) ?? throw new InvalidOperationException($"{Name} has neither a character sheet nor a statblock attack.");

        /// <summary>Every attack it could make: a character's weapon in hand, or each of a foe's attack lines.</summary>
        public IReadOnlyList<AttackProfile> Attacks => (Build is not null ? [Attack] : StatblockLinesNow) is var own && SteedAttack is { } hooves
            ? [.. own, hooves]
            : own;

        /// <summary>
        /// The mount's attack, which a rider may make in place of their own, "at the mount's bonus and
        /// damage" (Core Rules §9, "Mounts"). Null on foot, or with no mount.
        /// </summary>
        public AttackProfile? SteedAttack => IsRiding && Steed is { Body: > 0 } steed && steed.StatblockAttacks.Count > 0
            ? steed.StatblockAttacks[0] with { Name = $"{steed.StatblockAttacks[0].Name} ({steed.Name})" }
            : null;

        /// <summary>A foe's attack lines as it stands: a Disarmed foe has lost its weapon lines, and punches with its own Attack bonus.</summary>
        private IReadOnlyList<AttackProfile> StatblockLinesNow
        {
            get
            {
                if (!IsDisarmed || StatblockAttack is not { } first)
                {
                    return StatblockAttacks;
                }

                var kept = StatblockAttacks.Where(a => !IsWieldedLine(a.Name)).ToList();
                return kept.Count > 0
                    ? kept
                    : [first with { Name = "Unarmed", DamageDiceCount = 1, DamageDieSides = 4, Weapon = null, Reach = first.OwnReach }];
            }
        }

        /// <summary>The attack it threatens its Reach with: the first melee line, or the first line of any kind if it has no melee one.</summary>
        public AttackProfile MeleeAttack => Attacks.FirstOrDefault(a => !a.IsRanged) ?? Attack;

        /// <summary>Whether any of its attacks is a shot — a creature that can fight from a distance.</summary>
        public bool HasRangedAttack => Attacks.Any(a => a.IsRanged);

        /// <summary>
        /// The weapon in hand — the main one, where two are: whichever isn't the Light off-hand
        /// weapon. Null when fighting unarmed or for a generic foe.
        /// </summary>
        public WeaponDefinition? InHandWeapon => Build is { } build
            ? InventoryCalculator.InHandWeapons(build) is var inHand && inHand.Count == 2 && OffHandOf(inHand) is { } off
                ? inHand.First(w => w != off)
                : inHand.FirstOrDefault()
            : null;

        /// <summary>
        /// The second weapon of a Two Weapons loadout, which must be Light (Core Rules §9, "Two
        /// Weapons"); null for anyone holding one weapon, or none. Where both are Light, the second.
        /// </summary>
        public WeaponDefinition? OffHandWeapon => Build is { } build ? OffHandOf(InventoryCalculator.InHandWeapons(build)) : null;

        private static WeaponDefinition? OffHandOf(IReadOnlyList<WeaponDefinition> inHand) =>
            inHand.Count == 2 ? inHand.LastOrDefault(w => w.Weight == WeaponWeight.Light) : null;

        /// <summary>The off-hand weapon's attack, rolled with its own Grip (Core Rules §9, "Two Weapons").</summary>
        public AttackProfile? OffHandAttack => Build is { } build && OffHandWeapon is { } off ? AttackProfile.ForWeapon(build, off) : StatblockOffHand;

        /// <summary>A statblock creature's off-hand blow, where its Opportunity bonus is an off-hand attack: its attack bonus, a Light weapon's die.</summary>
        public AttackProfile? StatblockOffHand { get; init; }

        /// <summary>
        /// Set once its Attack action has been made with a Two Weapons loadout: the off-hand strike
        /// that action allows is still to be taken. Gone with its next move or action, or at the end of the turn.
        /// </summary>
        public bool OffHandOwed { get; internal set; }

        /// <summary>The weapon at the ready (Core Rules §15, "Equipped Items"), a swap away.</summary>
        public WeaponDefinition? ReadyWeapon => Build is { } build ? InventoryCalculator.ReadyWeapons(build).FirstOrDefault() : null;

        /// <summary>The armour worn — lowered a type, for the rest of the combat, by a Sunder (Core Rules §9, "The Manoeuvres").</summary>
        public required ArmourWeight Armour { get; set; }

        /// <summary>Has been Sundered this combat — armour dented or a shield split over it: "only once per combat".</summary>
        public bool HasBeenSundered { get; internal set; }

        /// <summary>
        /// The manoeuvres a generic foe's Attack line gives it — "may attempt Topple (ST) as a
        /// manoeuvre" — which it may use without being Martial (GM Reference §2, "Borrowing").
        /// </summary>
        public IReadOnlyList<WeaponManoeuvre> StatblockManoeuvres { get; init; } = [];

        /// <summary>The weapon a Disarm knocked from this creature's hand, lying where it fell; null if none.</summary>
        public WeaponDefinition? DroppedWeapon { get; internal set; }

        /// <summary>The square <see cref="DroppedWeapon"/> lies on — at the creature's feet when it fell.</summary>
        public GridPosition? DroppedAt { get; internal set; }

        /// <summary>A generic foe disarmed of its wielded weapon: its weapon lines are gone until it takes the weapon up.</summary>
        public bool IsDisarmed => conditions.Contains(CharacterCondition.Disarmed);

        /// <summary>
        /// Has a weapon in hand a Disarm could take: a character's weapon in hand, or a foe's melee
        /// line made with a blade, a club or a bow — not a bite, a claw or a fist.
        /// </summary>
        public bool WieldsWeapon => Build is not null
            ? InHandWeapon is not null
            : !IsDisarmed && StatblockAttacks.Any(a => IsWieldedLine(a.Name));

        /// <summary>Whether a statblock attack line is made with something held, which can be knocked away.</summary>
        public static bool IsWieldedLine(string name)
        {
            var n = name.ToLowerInvariant();
            return new[] { "sword", "maul", "cudgel", "shiv", "axe", "spear", "club", "dagger", "knife", "hammer", "mace", "staff", "flail", "halberd", "bow", "sling", "blade", "rapier", "pick", "whip" }
                .Any(n.Contains);
        }

        /// <summary>How far it threatens with a blade — the ground a reaction attack covers (Core Rules §9, "Reach"). A bow threatens what the body does.</summary>
        public int Reach => Build is not null ? Attack.Reach : MeleeAttack.IsRanged ? OwnReach : MeleeAttack.Reach;

        /// <summary>The creature's own Speed — Normal for every player character, the statblock's for a foe.</summary>
        public Speed InnateSpeed { get; init; } = Speed.Normal;

        /// <summary>What a move is priced at right now: Crawling while Prone (Core Rules §9, "Prone and crawling").</summary>
        public Speed Speed => IsRiding && Steed is { } steed ? steed.Speed : ConditionCatalog.Speed(conditions, InnateSpeed);

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

        /// <summary>Has been at 0 Body or below at some point this fight — dropped, if not kept down.</summary>
        public bool HasBeenDropped { get; internal set; }

        /// <summary>A Solo: one creature run against the whole party (GM Reference §3).</summary>
        public bool IsSolo => Role == Rules.Enums.AdversaryRole.Solo;

        /// <summary>
        /// Turns it takes in a round by nature: a Solo, its Tier plus one — 2, 3 or 4 (GM Reference §3,
        /// "Solo Actions"); anything else, one. Separate from any an Opportunity buys.
        /// </summary>
        public int TurnsPerRound => IsSolo ? Tier switch { Tier.T1 => 2, Tier.T2 => 3, _ => 4 } : 1;

        /// <summary>Of its own turns this round, how many it has taken.</summary>
        public int TurnsTakenThisRound { get; internal set; }

        /// <summary>Has a turn of its own still to take this round.</summary>
        public bool HasTurnLeftThisRound => TurnsTakenThisRound < TurnsPerRound;

        /// <summary>Attacks made on its turns this round — a Solo's are limited to one, unless it pays.</summary>
        public int AttacksThisRound { get; internal set; }

        /// <summary>What a GM Opportunity buys this creature on its next action, in place of an extra turn (GM Reference §9, "On Opportunity").</summary>
        public OpportunityBonus OpportunityBonus { get; init; } = OpportunityBonus.Default;

        /// <summary>Has had the extra attack its Opportunity bonus buys this round — a Solo's, once a round.</summary>
        public bool HasTakenBonusAttackThisRound { get; internal set; }

        /// <summary>The round it last made an area attack rationed to every other round.</summary>
        public int? LastAreaAttackRound { get; internal set; }

        /// <summary>
        /// Unshakable (GM Reference §9): Advantage on Stability Threshold checks made against being
        /// shoved or knocked prone — read here as Disadvantage on the roll of whoever is shoving,
        /// tripping, Toppling or Shifting it, since that roll is the check.
        /// </summary>
        public bool IsUnshakable { get; init; }

        /// <summary>
        /// What it is: the statblock it was made from — "Bramble Ratkin" — whatever this one is
        /// called. Pack Tactics counts a second of the same kind. Null for a character.
        /// </summary>
        public string? Kind { get; init; }

        /// <summary>The TRAITS a statblock gives it (GM Reference §9); empty for a character.</summary>
        public IReadOnlySet<AdversaryTrait> Traits { get; init; } = new HashSet<AdversaryTrait>();

        /// <summary>Pack Tactics: Advantage on its attack whenever a second of its kind is within reach of the same target.</summary>
        public bool HasPackTactics => Traits.Contains(AdversaryTrait.PackTactics);

        /// <summary>Resistant to Cutting: Body damage from a Cutting weapon halved, rounding down (Undead).</summary>
        public bool IsResistantToCutting => Traits.Contains(AdversaryTrait.ResistantToCutting);

        /// <summary>Vulnerable to Breaking: a Breaking weapon deals +2 Body damage (Undead).</summary>
        public bool IsVulnerableToBreaking => Traits.Contains(AdversaryTrait.VulnerableToBreaking);

        /// <summary>Bound, Not Alive: no Mind or Soul at all, and destroyed outright at 0 Body (Constructs).</summary>
        public bool IsBoundNotAlive => Traits.Contains(AdversaryTrait.BoundNotAlive);

        /// <summary>
        /// Immune to Mind damage: nothing Bound, Not Alive has a mind, and a Revenant's Deathless
        /// Purpose leaves none in it for that to mean anything (GM Reference §9).
        /// </summary>
        public bool IsMindImmune { get; init; }

        /// <summary>A statblock's Fitness: regains this much Stamina at the start of each round, up to its pool.</summary>
        public int StaminaEachRound { get; init; }

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

        /// <summary>The turn's one move, still unspent. Spending it lets an owed off-hand strike go.</summary>
        public bool HasMove
        {
            get => hasMove;
            internal set
            {
                OffHandOwed &= value || !hasMove;
                hasMove = value;
            }
        }

        private bool hasMove;

        /// <summary>
        /// Actions still to take this turn: the baseline one plus any bought with Stamina. Spending
        /// one lets an owed off-hand strike go — it belonged to the Attack action before it.
        /// </summary>
        public int ActionsRemaining
        {
            get => actionsRemaining;
            internal set
            {
                OffHandOwed &= value >= actionsRemaining;
                actionsRemaining = value;
            }
        }

        private int actionsRemaining;

        public BodyStatus BodyStatus => PlayStateCalculator.GetBodyStatus(Body, IsStable);

        /// <summary>
        /// Whether this creature is still fighting. A generic foe is out at 0 Body with no dying
        /// state (GM Reference §2); a character at 0 is Dying, which the slice treats as out of the
        /// fight since nobody is left to stabilise them.
        /// </summary>
        public bool IsOutOfFight => IsUnconscious || HasFled || HasSurrendered || IsDazed
            || (Side == Side.Party ? BodyStatus != BodyStatus.Healthy : Body <= 0);

        /// <summary>
        /// Has cast a spell in this fight — which is how a caster gives themselves away, and why
        /// the other side goes for them first from then on.
        /// </summary>
        public bool HasCastSpell { get; internal set; }

        /// <summary>The statblock a foe was built from — what its phantom double is made in the likeness of. Null for a character.</summary>
        public Rules.Catalog.StartingAdversaryInfo? Statblock { get; init; }

        /// <summary>
        /// Found out as a caster by a Sensing spell before casting anything: the other side treats them
        /// as one from then on, as though they had cast (Core Rules §11, "Sensing").
        /// </summary>
        public bool IsSensedAsCaster { get; internal set; }

        /// <summary>Whether the other side knows this is a caster: they have cast, or a Sensing spell found the magic in them.</summary>
        public bool ShowsAsCaster => HasCastSpell || IsSensedAsCaster;

        /// <summary>The spells on a character's sheet, or on a foe's statblock under its Caster trait (Core Rules §11; GM Reference §9).</summary>
        public IReadOnlyList<SpellDefinition> Spells => Build?.Spells ?? StatblockSpells;

        /// <summary>A generic foe's spells — its Attack line, where that is a spell, and its SPELLS rows.</summary>
        internal IReadOnlyList<SpellDefinition> StatblockSpells { get; init; } = [];

        /// <summary>What a foe's Caster trait says it casts, and with what — null for a foe that casts nothing.</summary>
        public AdversaryCasting? Casting { get; init; }

        /// <summary>A foe's Tier, which caps the spells it casts ("of up to its own Tier").</summary>
        public Tier Tier { get; init; } = Tier.T1;

        /// <summary>What a foe rolls where a spell has to land: the Caster trait's bonus, its Attack bonus by default (GM Reference §9).</summary>
        internal int StatblockCastBonus { get; init; }

        /// <summary>What a foe rolls when it pays a Divine or Aether spell from Body instead: its Tier's baseline, +0 / +1 / +2.</summary>
        internal int StatblockBleedBonus { get; init; }

        /// <summary>Which Magic sources this creature can cast from, and how (Core Rules §11; GM Reference §9, "On Casters").</summary>
        public IReadOnlyList<MagicAccessEntry> MagicAccess => magicAccess ??= Build is { } build ? SpellCalculator.GetMagicAccess(build) : StatblockAccess();

        /// <summary>
        /// A Caster trait's access: its own tradition, up to its Tier, paid in that tradition's track —
        /// and, for Aether or Divine, Body instead at its Tier's baseline, which is also how such a
        /// creature reaches a Blood Magic Heal.
        /// </summary>
        /// <summary>
        /// The Fuel a Caster - Elemental creature carries — the N its trait names (GM Reference §9,
        /// "On Casters") — spent as it casts, after whatever the place and a tapped source cover.
        /// </summary>
        public int StatblockFuel { get; internal set; }

        private IReadOnlyList<MagicAccessEntry> StatblockAccess()
        {
            if (Casting is not { } casting)
            {
                return [];
            }

            if (casting.Source == MagicSource.ElementalMagic)
            {
                // Fuel by the standard formula — or, made of what it casts, its own Body, exactly as
                // Blood Magic is paid (GM Reference §9, "Caster - Elemental, of its element").
                return [new MagicAccessEntry(MagicSource.ElementalMagic, Ability.Intellect, casting.OfItsElement ? WoundType.Body : null,
                    CanBleedForPower: false, MaxSpellTier: Tier, Notes: "Caster trait",
                    Elements: casting.Element is { } element ? [element] : null)];
            }

            var bleeds = casting.Source is MagicSource.AetherMagic or MagicSource.DivineMagic;
            WoundType? track = casting.Source switch
            {
                MagicSource.AetherMagic => WoundType.Mind,
                MagicSource.DivineMagic => WoundType.Soul,
                MagicSource.BloodMagic => WoundType.Body,
                _ => null,
            };
            var native = new MagicAccessEntry(casting.Source, Ability.Intellect, track,
                CanBleedForPower: bleeds, MaxSpellTier: Tier, Notes: "Caster trait", BleedCastAbility: bleeds ? Ability.Vitality : null);
            return bleeds
                ? [native, new MagicAccessEntry(MagicSource.BloodMagic, Ability.Vitality, WoundType.Body, false, Tier, "Caster trait, bleeding", ViaBleedingForPower: true)]
                : [native];
        }

        private IReadOnlyList<MagicAccessEntry>? magicAccess;

        /// <summary>A Cerebral character: once a round, 1 Stamina may pay 1 wound of a spell's cost (Core Rules §3, "Overclock").</summary>
        public bool HasOverclock => Build?.ClassPrinciple == ClassPrinciple.Cerebral;

        /// <summary>Body as the Secondary Wound: once a round, 1 Body before an attack or a cast buys Advantage on it (Core Rules §3, "Sacrifice").</summary>
        public bool HasSacrifice => Build?.SecondaryWound == WoundType.Body;

        /// <summary>Mind as the Secondary Wound: once a round, for 1 Mind, take their turn inside the GM's, when an adversary declares an action (Core Rules §3, "Interrupt").</summary>
        public bool HasInterrupt => Build?.SecondaryWound == WoundType.Mind;

        /// <summary>Trained as Sentry (Core Rules §7): an Aether Mage who may Interrupt even after acting this round — one action, for 1 Stamina more.</summary>
        public bool IsSentry => Build is { } build && CharacterCalculator.HasTrainedAsSentry(build);

        /// <summary>Trained as Field Medic (Core Rules §7): a Priest whose Shelter reaches any ally they can see.</summary>
        public bool IsFieldMedic => Build is { } build && CharacterCalculator.HasTrainedAsFieldMedic(build);

        /// <summary>Has Interrupted this round: a character's one Interrupt of the round, whether before acting or, as a Sentry, after.</summary>
        public bool HasInterruptedThisRound { get; internal set; }

        /// <summary>Soul as the Secondary Wound: once a round, take an ally's damage as Soul (Core Rules §3, "Shelter").</summary>
        public bool HasShelter => Build?.SecondaryWound == WoundType.Soul;

        /// <summary>Has Sheltered somebody this round.</summary>
        public bool HasShelteredThisRound { get; internal set; }

        /// <summary>Has taken an extra turn bought with a GM Opportunity this round — one a round, for anything but a Solo.</summary>
        public bool HasTakenExtraTurnThisRound { get; internal set; }

        /// <summary>Whether a GM Opportunity may buy this adversary another turn now: one a round, though a Solo may take more (GM Reference §3).</summary>
        public bool CanTakeExtraTurn => Side == Side.Adversaries && !IsOutOfFight && (Role == Rules.Enums.AdversaryRole.Solo || !HasTakenExtraTurnThisRound);

        /// <summary>A Nimble character: free footwork, and the Exploits (Core Rules §3, "Exploit").</summary>
        public bool IsNimble => Build?.ClassPrinciple == ClassPrinciple.Nimble;

        /// <summary>A Martial character: manoeuvres with a weapon that permits them (Core Rules §3, "Prowess").</summary>
        public bool IsMartial => Build?.ClassPrinciple == ClassPrinciple.Martial;

        /// <summary>Has taken the free step this round — "once per round" (Core Rules §9, "Footwork").</summary>
        public bool HasUsedFreeStep { get; internal set; }

        /// <summary>Has moved — by its own movement — since its last attack: what Precise asks.</summary>
        public bool HasMovedSinceLastAttack { get; internal set; }

        /// <summary>Has been attacked since the start of its last turn: what Patient forbids.</summary>
        public bool HasBeenAttackedSinceTurnStart { get; internal set; }

        /// <summary>Bought Slip this round: leaving an enemy's reach draws no reaction attack, until the round ends.</summary>
        public bool IsSlipping { get; internal set; }

        /// <summary>Holds Precise: the next attack is Direct and adds Vitality to its damage.</summary>
        public bool HoldsPrecise { get; internal set; }

        /// <summary>Holds Patient: the next attack is made with Advantage.</summary>
        public bool HoldsPatient { get; internal set; }

        /// <summary>Ringed (Core Rules §9, "Manoeuvres"): no tactical Stamina expenditure until the start of its next turn.</summary>
        public bool IsRinged { get; internal set; }

        /// <summary>Has Overclocked this round.</summary>
        public bool HasOverclockedThisRound { get; internal set; }

        /// <summary>Has used Sacrifice this round.</summary>
        public bool HasSacrificedThisRound { get; internal set; }

        /// <summary>
        /// Has given ground this turn, Frightened — and can't come back: closing again would spend a
        /// move it must still finish further out for, which it can't do twice (Core Rules §9).
        /// </summary>
        public bool HasWithdrawnThisTurn { get; internal set; }

        /// <summary>Lanterns tapped for a spell: cold and intact, needing a flask of Oil before they'll light again (Core Rules §14).</summary>
        internal HashSet<InventoryItemDefinition> DrainedLanterns { get; } = new();

        /// <summary>The one spell this caster is Sustaining, if any (Core Rules §11, "One at a time").</summary>
        public string? Sustaining { get; internal set; }

        /// <summary>The statblock's Role, for a foe; null for a character.</summary>
        public Rules.Enums.AdversaryRole? Role { get; init; }

        /// <summary>
        /// A skill group's bonus for this creature — Tier + ability + Affinity for a character, the
        /// statblock's Skills row for a foe (Core Rules §8; GM Reference §3).
        /// </summary>
        internal Func<Ability, int>? SkillBonusFor { get; init; }

        /// <summary>The bonus this creature rolls <paramref name="skill"/> at.</summary>
        public int SkillBonus(string skill) =>
            SkillCatalog.AllSkills.FirstOrDefault(s => s.Name == skill) is { } info && SkillBonusFor is { } bonus ? bonus(info.Ability) : 0;

        /// <summary>
        /// Pulled off their footing by a hazard — the Undertow's "must spend an action regaining
        /// their footing before acting normally again" — and so able to do nothing else with an
        /// action until they have.
        /// </summary>
        public bool NeedsFooting { get; internal set; }

        /// <summary>When this creature stops being willing to fight; null for one that never will.</summary>
        public Morale? Morale { get; init; }

        /// <summary>Has reached its breaking point, and is looking for a way out rather than a fight (GM Reference §8).</summary>
        public bool IsBroken { get; internal set; }

        /// <summary>Was left beyond Long by the party getting clear of it, not by its own going.</summary>
        public bool WasOutdistanced { get; internal set; }

        /// <summary>Has left the field, and takes no further part in the Challenge.</summary>
        public bool HasFled { get; internal set; }

        /// <summary>Gave up when the fight ended — caught in a hold as the rest of its side broke and ran.</summary>
        public bool HasSurrendered { get; internal set; }

        /// <summary>Whether a melee blow could land on <paramref name="square"/> — within Reach, and not inside an Unwieldy haft.</summary>
        public bool CanReach(GridPosition square) => CanReachFrom(Position, square);

        /// <summary>Whether a melee blow from <paramref name="from"/>, were this creature standing there, could land on <paramref name="square"/>.</summary>
        public bool CanReachFrom(GridPosition from, GridPosition square) => Reaches(Footprints.Distance(from, Across, square, 1));

        /// <summary>Whether a melee blow could land on any square <paramref name="target"/> stands on.</summary>
        public bool CanReach(Combatant target) => CanReachFrom(Position, target);

        /// <summary>Whether a melee blow from <paramref name="from"/> could land on any square <paramref name="target"/> stands on.</summary>
        public bool CanReachFrom(GridPosition from, Combatant target) => Reaches(DistanceFrom(from, target));

        /// <summary>Whether a melee blow could land on a creature <paramref name="across"/> squares across standing with its corner on <paramref name="corner"/>.</summary>
        public bool CanReachBlock(GridPosition corner, int across) => Reaches(Footprints.Distance(Position, Across, corner, across));

        private bool Reaches(int distance) => distance <= Reach && !(MeleeAttack.IsUnwieldy && distance <= 1 && Reach > 0);
    }
}
