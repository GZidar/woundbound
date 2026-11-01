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
                MindMax = stats.MindWoundPool,
                Mind = build.Play.CurrentMindWounds ?? stats.MindWoundPool,
                SoulMax = stats.SoulWoundPool,
                Soul = build.Play.CurrentSoulWounds ?? stats.SoulWoundPool,
                BaseDodgeThreshold = stats.DodgeThreshold,
                BaseStabilityThreshold = stats.StabilityThreshold,
                BaseResolveThreshold = stats.ResolveThreshold,
                Armour = armour,
                InnateSpeed = Speed.Normal,
                StaminaMax = stats.Stamina,
                Stamina = build.Play.CurrentStamina ?? stats.Stamina,
                TacticalSpendsPerRound = stats.TacticalSpendsPerRound,
                SkillBonusFor = CharacterCalculator.TryGetSkillGroups(build, out var groups, out _) && groups is not null
                    ? ability => groups.FirstOrDefault(g => g.Ability == ability)?.Bonus ?? 0
                    : _ => 0,
            };
        }

        /// <summary>A generic foe from the starting adversaries catalogue (GM Reference §7).</summary>
        /// <param name="spellCatalogue">The Starting Spells, for a Caster's SPELLS rows, which name catalogue spells.</param>
        public static Combatant FromAdversary(StartingAdversaryInfo info, string name, GridPosition position, IReadOnlyList<StartingSpellInfo>? spellCatalogue = null)
        {
            var casting = info.Traits?.Contains(AdversaryTrait.Caster) == true && info.Casting is { } c ? c : null;
            var foe = Plain(info, name, position);
            if (casting is null)
            {
                return foe;
            }

            var attackSpell = AttackLineSpell(info, casting);
            var spells = new List<SpellDefinition>();
            if (attackSpell is not null)
            {
                spells.Add(attackSpell);
            }

            foreach (var line in info.Spells ?? [])
            {
                if (spellCatalogue?.FirstOrDefault(s => s.Name == line.Name) is { } known)
                {
                    spells.Add(SpellCalculator.FromReference(known));
                }
            }

            return new Combatant
            {
                Name = foe.Name, Side = foe.Side, Position = foe.Position,
                BodyMax = foe.BodyMax, Body = foe.Body, MindMax = foe.MindMax, Mind = foe.Mind, SoulMax = foe.SoulMax, Soul = foe.Soul,
                BaseDodgeThreshold = foe.BaseDodgeThreshold, BaseStabilityThreshold = foe.BaseStabilityThreshold, BaseResolveThreshold = foe.BaseResolveThreshold,
                // The Attack line was the spell; what is left in the hands is a punch.
                StatblockAttacks = attackSpell is null && !string.IsNullOrWhiteSpace(info.AttackName)
                    ? foe.StatblockAttacks
                    : [StatblockLine("Unarmed", AdversarySkillCalculator.Baseline(info.Tier), 1, "d4", 0, info.Reach)],
                Armour = foe.Armour, InnateSpeed = foe.InnateSpeed, IsSwarm = foe.IsSwarm, Role = foe.Role, Morale = foe.Morale,
                SkillBonusFor = foe.SkillBonusFor, IsMinion = foe.IsMinion, StaminaMax = foe.StaminaMax, Stamina = foe.Stamina,
                TacticalSpendsPerRound = foe.TacticalSpendsPerRound,
                Tier = info.Tier,
                Casting = casting,
                StatblockSpells = spells,
                StatblockCastBonus = AdversaryTraitCatalog.CastBonus(info.ToTemplate()),
                StatblockBleedBonus = AdversarySkillCalculator.Baseline(info.Tier),
            };
        }

        /// <summary>
        /// A Caster's Attack line, where it is a spell: "a spell that is the creature's attack sits here
        /// with its cost" (GM Reference §9). Harm against Dodge, Control against Resolve, Motion against
        /// Stability, at the creature's own Tier, one target, at Touch where the line says so.
        /// </summary>
        private static SpellDefinition? AttackLineSpell(StartingAdversaryInfo info, AdversaryCasting casting)
        {
            if (string.IsNullOrWhiteSpace(info.AttackName) || info.AttackName.Equals("None", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var touch = info.AttackName.Contains("Touch", StringComparison.OrdinalIgnoreCase);
            var type = info.AttackVsThreshold switch
            {
                ThresholdType.Resolve => SpellType.Control,
                ThresholdType.Stability => SpellType.Motion,
                _ => SpellType.Harm,
            };
            return new SpellDefinition
            {
                Name = info.AttackName.Split(" (")[0],
                Source = casting.Source,
                SpellTier = info.Tier,
                SpellType = type,
                Target = SpellTarget.One,
                Shape = touch ? SpellShape.None : SpellShape.Ray,
                Size = SpellSize.Small,
                Range = touch ? SpellRange.Touch : SpellRange.Short,
                Threshold = info.AttackVsThreshold,
                DamageType = type == SpellType.Harm ? info.DamageType : null,
                Notes = $"{info.AttackName}. {info.AttackEffect}".Trim(),
            };
        }

        private static Combatant Plain(StartingAdversaryInfo info, string name, GridPosition position) => new()
        {
            Name = name,
            Side = Side.Adversaries,
            Position = position,
            BodyMax = info.BodyMax,
            Body = info.BodyMax,
            MindMax = info.MindMax ?? 0,
            Mind = info.MindMax ?? 0,
            SoulMax = info.SoulMax ?? 0,
            Soul = info.SoulMax ?? 0,
            BaseDodgeThreshold = info.DodgeThreshold,
            BaseStabilityThreshold = info.StabilityThreshold,
            BaseResolveThreshold = info.ResolveThreshold,
            StatblockAttacks = new[] { StatblockLine(info.AttackName, info.AttackModifier, info.DamageDiceCount, info.DamageDie, info.DamageModifier, info.Reach) }
                .Concat((info.MoreAttacks ?? []).Select(a => StatblockLine(a.Name, a.Modifier, a.DamageDiceCount, a.DamageDie, a.DamageModifier, info.Reach)))
                .ToList(),
            Armour = info.Armour,
            InnateSpeed = info.Speed,
            IsSwarm = info.Role == AdversaryRole.Swarm,
            Role = info.Role,
            Morale = Morale.FromStatblock(info),
            SkillBonusFor = info.Kind == AdversaryKind.Combat ? SkillsOf(info) : _ => 0,
            IsMinion = info.Role == AdversaryRole.Minion,
            StaminaMax = info.Stamina,
            Stamina = info.Stamina,
            TacticalSpendsPerRound = CharacterCalculator.TacticalSpendsPerRound(info.Tier),
            Tier = info.Tier,
        };

        /// <summary>
        /// One attack line of a statblock. A line made with a bow, crossbow or sling is a shot, and
        /// follows the weapon range rules like anybody's (GM Reference §9: "at Middle a Medium weapon
        /// rolls d4 rather than d6 and the target's Dodge Threshold is 2 higher"); the to-hit and the
        /// die at Close and Short are the statblock's own.
        /// </summary>
        public static AttackProfile StatblockLine(string name, int modifier, int diceCount, string die, int damageModifier, int reach) => new(
            Name: name,
            AttackModifier: modifier,
            DamageDiceCount: diceCount,
            DamageDieSides: DiceNotation.ParseSides(die),
            DamageModifier: damageModifier,
            Reach: reach,
            Weapon: ShotFor(name))
        {
            OwnReach = reach,
        };

        /// <summary>The ranged weapon a statblock attack's name says it's made with, or null for a blow, a bite or a claw.</summary>
        public static WeaponDefinition? ShotFor(string attackName)
        {
            var name = attackName.ToLowerInvariant();
            var (weight, grip) = name switch
            {
                _ when name.Contains("crossbow") => (name.Contains("heavy") ? WeaponWeight.Heavy : WeaponWeight.Medium, WeaponGrip.Power),
                _ when name.Contains("longbow") => (WeaponWeight.Heavy, WeaponGrip.Power),
                _ when name.Contains("bow") => (WeaponWeight.Medium, WeaponGrip.Finesse),
                _ when name.Contains("sling") => (WeaponWeight.Light, WeaponGrip.Finesse),
                _ => ((WeaponWeight?)null, WeaponGrip.Finesse),
            };

            return weight is { } w
                ? new WeaponDefinition { Name = attackName, IsRanged = true, Weight = w, Grip = grip, Edge = WeaponEdge.Cutting, TwoHanded = true }
                : null;
        }

        private static Func<Ability, int> SkillsOf(StartingAdversaryInfo info)
        {
            var template = info.ToTemplate();
            return ability => AdversarySkillCalculator.Bonus(template, ability);
        }

        /// <summary>A shield in the loadout in hand (Core Rules §15, "Equipped Items").</summary>
        public static ShieldDefinition ShieldInHand(string name, ShieldWeight weight) => new()
        {
            Name = name,
            Weight = weight,
            IsEquipped = true,
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
