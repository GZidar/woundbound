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
        /// A player character in their equipped armour, fighting with whichever loadout is in hand.
        /// The build is kept, not copied, so a swap mid-fight changes what they attack with.
        /// </summary>
        public static Combatant FromCharacter(CharacterBuild build, GridPosition position)
        {
            var loadout = InventoryCalculator.ValidateEquippedLoadout(build);
            if (!loadout.IsValid)
            {
                throw new ArgumentException($"{build.CharacterName} can't fight like that: {string.Join(" ", loadout.Errors)}", nameof(build));
            }

            var stats = CharacterCalculator.Calculate(build);
            var armour = build.Armour.FirstOrDefault(a => a.IsEquipped)?.Weight ?? ArmourWeight.None;

            return new Combatant
            {
                Name = build.CharacterName,
                Side = Side.Party,
                Build = build,
                Position = position,
                BodyMax = stats.BodyWoundPool,
                Body = build.Play.CurrentBodyWounds ?? stats.BodyWoundPool,
                IsStable = build.Play.IsStable,
                BaseDodgeThreshold = stats.DodgeThreshold,
                BaseStabilityThreshold = stats.StabilityThreshold,
                BaseResolveThreshold = stats.ResolveThreshold,
                Armour = armour,
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
            BaseDodgeThreshold = info.DodgeThreshold,
            BaseStabilityThreshold = info.StabilityThreshold,
            BaseResolveThreshold = info.ResolveThreshold,
            StatblockAttack = new AttackProfile(
                Name: info.AttackName,
                AttackModifier: info.AttackModifier,
                DamageDiceCount: info.DamageDiceCount,
                DamageDieSides: DiceNotation.ParseSides(info.DamageDie),
                DamageModifier: info.DamageModifier,
                Reach: info.Reach,
                Weapon: null),
            Armour = info.Armour,
            Speed = info.Speed,
            IsMinion = info.Role == AdversaryRole.Minion,
            StaminaMax = info.Stamina,
            Stamina = info.Stamina,
            TacticalSpendsPerRound = CharacterCalculator.TacticalSpendsPerRound(info.Tier),
        };

        /// <summary>A catalogue weapon in the loadout in hand (Core Rules §15, "Equipped Items").</summary>
        public static WeaponDefinition InHand(StartingWeaponInfo info)
        {
            var weapon = FromCatalogue(info);
            weapon.IsEquipped = true;
            return weapon;
        }

        /// <summary>A catalogue weapon in the loadout at the ready — on the body, a swap away.</summary>
        public static WeaponDefinition AtTheReady(StartingWeaponInfo info)
        {
            var weapon = FromCatalogue(info);
            weapon.IsAtReady = true;
            return weapon;
        }

        private static WeaponDefinition FromCatalogue(StartingWeaponInfo info) => new()
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
        };
    }
}
