using WoundBound.Rules.Calculation;
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
        public bool IsRanged => Weapon?.IsRanged == true;

        /// <summary>Can't strike an adjacent target at all (Core Rules §9, "Weapons").</summary>
        public bool IsUnwieldy => Weapon?.Unwieldy == true;

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
