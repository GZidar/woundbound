using WoundBound.Rules.Calculation;
using WoundBound.Rules.Catalog;
using WoundBound.Rules.Enums;
using WoundBound.Rules.Models;

namespace WoundBound.Game.Combat
{
    /// <summary>
    /// Turns the Rules library's two kinds of sheet into board pieces: a player character's
    /// <see cref="CharacterBuild"/>, whose numbers are derived, and a generic foe's statblock,
    /// whose numbers are written down already (GM Reference §2).
    /// </summary>
    public static class CombatantFactory
    {
        /// <summary>
        /// A player character fighting with their first equipped weapon (unarmed without one) in
        /// their equipped armour.
        /// </summary>
        public static Combatant FromCharacter(CharacterBuild build, GridPosition position)
        {
            var stats = CharacterCalculator.Calculate(build);
            var weapon = build.Weapons.FirstOrDefault(w => w.IsEquipped) ?? WeaponCalculator.Unarmed();
            var armour = build.Armour.FirstOrDefault(a => a.IsEquipped)?.Weight ?? ArmourWeight.None;

            var attackModifier = CharacterCalculator.WeaponAttackBonus(build, weapon)
                ?? throw new ArgumentException("A character needs a Class Principle before it can attack.", nameof(build));

            return new Combatant
            {
                Name = build.CharacterName,
                Side = Side.Party,
                Position = position,
                BodyMax = stats.BodyWoundPool,
                Body = build.Play.CurrentBodyWounds ?? stats.BodyWoundPool,
                IsStable = build.Play.IsStable,
                DodgeThreshold = stats.DodgeThreshold,
                AttackName = weapon.Name,
                AttackModifier = attackModifier,
                DamageDiceCount = 1,
                DamageDieSides = DiceNotation.ParseSides(WeaponCalculator.BaseDamageDie(weapon.Weight)),
                DamageModifier = build.GetModifier(WeaponCalculator.DamageAbilityFor(weapon.Grip)),
                Armour = armour,
                Reach = WeaponCalculator.CharacterReach(weapon),
                Speed = Speed.Normal,
                StaminaMax = stats.Stamina,
                Stamina = build.Play.CurrentStamina ?? stats.Stamina,
                TacticalSpendsPerRound = stats.TacticalSpendsPerRound,
            };
        }

        /// <summary>A generic foe from the starting adversaries catalogue (GM Reference §7).</summary>
        public static Combatant FromAdversary(StartingAdversaryInfo info, string name, GridPosition position) => new()
        {
            Name = name,
            Side = Side.Adversaries,
            Position = position,
            BodyMax = info.BodyMax,
            Body = info.BodyMax,
            DodgeThreshold = info.DodgeThreshold,
            AttackName = info.AttackName,
            AttackModifier = info.AttackModifier,
            DamageDiceCount = info.DamageDiceCount,
            DamageDieSides = DiceNotation.ParseSides(info.DamageDie),
            DamageModifier = info.DamageModifier,
            Armour = info.Armour,
            Reach = info.Reach,
            Speed = info.Speed,
            IsMinion = info.Role == AdversaryRole.Minion,
            StaminaMax = info.Stamina,
            Stamina = info.Stamina,
            TacticalSpendsPerRound = CharacterCalculator.TacticalSpendsPerRound(info.Tier),
        };

        /// <summary>A catalogue weapon, equipped in hand.</summary>
        public static WeaponDefinition EquippedWeapon(StartingWeaponInfo info) => new()
        {
            Name = info.Name,
            IsRanged = info.IsRanged,
            Edge = info.Edge,
            Grip = info.Grip,
            Weight = info.Weight,
            Size = info.Size,
            ReachBonus = info.ReachBonus,
            CanBeThrown = info.CanBeThrown,
            TwoHanded = info.TwoHanded,
            Unwieldy = info.Unwieldy,
            IsEquipped = true,
        };
    }
}
