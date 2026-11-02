using WoundBound.Rules.Calculation;
using WoundBound.Rules.Enums;
using WoundBound.Rules.Models;

namespace WoundBound.Game.Combat
{
    /// <summary>
    /// What a creature attacks with right now. A character's is worked out afresh from whatever is
    /// in hand, so swapping loadouts changes it; a generic foe's is its statblock's attack line.
    /// </summary>
    /// <param name="DamageDieSides">The weapon's base die — what a hit rolls at Close or in melee, and what a critical's bonus die always is.</param>
    /// <param name="Reach">How far it can put a blade into somebody (Core Rules §9, "Reach"), in squares. Shooting is measured in bands instead.</param>
    /// <param name="Weapon">The weapon in hand, or null for a statblock attack.</param>
    /// <summary>
    /// A poison on a blade, a bite or a dart: on a hit that got past armour at all, the attack's own
    /// total against the target's Fortitude, and if it holds, this much Direct damage on top of the
    /// weapon's (Core Rules §10, "Fortitude Threshold").
    /// </summary>
    public sealed record Poison(int DiceCount, int DieSides, int Modifier, WoundType Track)
    {
        public override string ToString() => $"{DiceCount}d{DieSides}{(Modifier == 0 ? string.Empty : Modifier.ToString("+0;-0"))} {Track} (Direct)";
    }

    public sealed record AttackProfile(
        string Name,
        int AttackModifier,
        int DamageDiceCount,
        int DamageDieSides,
        int DamageModifier,
        int Reach,
        WeaponDefinition? Weapon)
    {
        /// <summary>
        /// The reach of the body behind the attack — a statblock's Reach line, whatever the line
        /// is made with. Set for a foe's attack lines, where a shot's own <see cref="Reach"/> is the
        /// body's too; a character's is always 1 (Core Rules §9, "Reach").
        /// </summary>
        public int OwnReach { get; init; } = Rules.Calculation.WeaponCalculator.DefaultOwnReach;
        public bool IsRanged => Weapon?.IsRanged == true || IsThrown;

        /// <summary>A melee weapon that "can be thrown", thrown: a shot by its own Weight on the range table (Core Rules §9, "Weapon Range").</summary>
        public bool IsThrown { get; init; }

        /// <summary>
        /// An attack "against every creature within N squares" — one roll each, as one attack (GM
        /// Reference §3, "An area attack is one attack"). Null for a blow at one target.
        /// </summary>
        public int? AreaSquares { get; init; }

        /// <summary>
        /// The Threshold the roll is made against: Dodge for a blow, Fortitude for something breathed
        /// or swallowed with no attack behind it (Core Rules §10). Nothing turns a Fortitude roll aside —
        /// no panic dodge, no shield.
        /// </summary>
        public ThresholdType Against { get; init; } = ThresholdType.Dodge;

        /// <summary>
        /// A condition a hit lays on the target until the start of the attacker's next turn — an
        /// innate Bind such as the Weeping Sentinel's gaze, "Motion - Bind, Stuck", rolled against
        /// Stability; uncosted, so never held past its turn (GM Reference §9, "On Casters").
        /// </summary>
        public CharacterCondition? Imposes { get; init; }

        /// <summary>A line that deals no damage — its effect is the whole of it.</summary>
        public bool DealsNoDamage { get; init; }

        /// <summary>The track it wounds: Body for any weapon (Core Rules §9, "Damage Types"); Mind or Soul for what a statblock says strikes there.</summary>
        public WoundType Track { get; init; } = WoundType.Body;

        /// <summary>Damage armour doesn't absorb — a gas, a swallowed poison (Core Rules §4, "What Armour Absorbs").</summary>
        public bool IsDirect { get; init; }

        /// <summary>A poison the blow carries, rolled against Fortitude with the same total (Core Rules §10). Null for a clean blade.</summary>
        public Poison? Poison { get; init; }

        /// <summary>Rationed to every other round, as a statblock may write an area attack.</summary>
        public bool EveryOtherRound { get; init; }

        /// <summary>Can't strike an adjacent target at all (Core Rules §9, "Weapons").</summary>
        public bool IsUnwieldy => Weapon?.Unwieldy == true;

        /// <summary>
        /// Cutting or Breaking (Core Rules §9, "Weapons"): the weapon's own, or what a statblock line
        /// names in its brackets — "Iron Cudgel (Heavy, Breaking)". Null for a bite or claw that says
        /// neither. It matters against the Undead (GM Reference §9, "On Type").
        /// </summary>
        public WeaponEdge? Edge => Weapon?.Edge ?? NamedEdge;

        /// <summary>The edge a statblock line's name gives it, where it has no weapon of its own.</summary>
        public WeaponEdge? NamedEdge { get; init; }

        /// <summary>The edge an attack line's name says it has: "Breaking" or "Cutting" written in it, or an unarmed strike's Breaking.</summary>
        public static WeaponEdge? EdgeNamedIn(string name) =>
            name.Contains("Breaking", StringComparison.OrdinalIgnoreCase) || name.Equals("Unarmed", StringComparison.OrdinalIgnoreCase) ? WeaponEdge.Breaking
            : name.Contains("Cutting", StringComparison.OrdinalIgnoreCase) ? WeaponEdge.Cutting
            : null;

        /// <summary>
        /// A character's attack with <paramref name="weapon"/>, or an unarmed strike without one:
        /// Tier + attack stat to hit (Core Rules §9, "Attack Roll") and the Grip's stat to damage.
        /// </summary>
        public static AttackProfile ForWeapon(CharacterBuild build, WeaponDefinition? weapon)
        {
            var swung = weapon ?? WeaponCalculator.Unarmed();

            return new AttackProfile(
                Name: swung.Name,
                AttackModifier: CharacterCalculator.WeaponAttackBonus(build, swung)
                    ?? throw new ArgumentException("A character needs a Class Principle before it can attack.", nameof(build)),
                DamageDiceCount: 1,
                DamageDieSides: DiceNotation.ParseSides(WeaponCalculator.BaseDamageDie(swung.Weight)),
                DamageModifier: build.GetModifier(WeaponCalculator.DamageAbilityFor(swung.Grip)),
                Reach: WeaponCalculator.CharacterReach(swung),
                Weapon: swung);
        }
    }

    /// <summary>
    /// Whether one attack can be made at one target from where the two stand, and if so what it is
    /// rolled against and what a hit rolls (Core Rules §9, "Reach" and "Weapon Range").
    /// </summary>
    /// <param name="Refusal">Why it can't be made; null when it can.</param>
    /// <param name="DodgeThreshold">The target's Dodge Threshold with the range modifier added.</param>
    /// <param name="DamageDieSides">The die a hit rolls at this distance; null where range has taken it away entirely.</param>
    /// <param name="Note">How the distance shaped the attack, for the player — the range band, or the melee reach.</param>
    /// <param name="Profile">Which of the attacker's attacks this is.</param>
    public sealed record AttackPlan(
        string? Refusal,
        int DodgeThreshold,
        int RangeDodgeModifier,
        int? DamageDieSides,
        string Note,
        AttackProfile? Profile = null)
    {
        public bool CanAttack => Refusal is null;
    }
}
