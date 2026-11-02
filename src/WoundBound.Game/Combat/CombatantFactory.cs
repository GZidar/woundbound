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
        /// <param name="side">Whose side they fight on: a rival band built like characters fights for the GM (GM Reference §2).</param>
        public static Combatant FromCharacter(CharacterBuild build, GridPosition position, Side side = Side.Party)
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
                Side = side,
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
                BaseFortitudeThreshold = stats.FortitudeThreshold,
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
                BaseFortitudeThreshold = foe.BaseFortitudeThreshold,
                // The Attack line was the spell; what is left in the hands is a punch.
                StatblockAttacks = attackSpell is null && !string.IsNullOrWhiteSpace(info.AttackName)
                    ? foe.StatblockAttacks
                    : [StatblockLine("Unarmed", AdversarySkillCalculator.Baseline(info.Tier), 1, "d4", 0, info.Reach)],
                Armour = foe.Armour, InnateSpeed = foe.InnateSpeed, IsSwarm = foe.IsSwarm, Across = foe.Across, Role = foe.Role, Morale = foe.Morale,
                SkillBonusFor = foe.SkillBonusFor, IsMinion = foe.IsMinion, IsUnshakable = foe.IsUnshakable, StaminaEachRound = foe.StaminaEachRound, StaminaMax = foe.StaminaMax, Stamina = foe.Stamina,
                TacticalSpendsPerRound = foe.TacticalSpendsPerRound,
                StatblockManoeuvres = foe.StatblockManoeuvres,
                Kind = foe.Kind, Traits = foe.Traits, IsMindImmune = foe.IsMindImmune,
                Tier = info.Tier,
                Casting = casting,
                StatblockFuel = casting.Source == MagicSource.ElementalMagic && !casting.OfItsElement ? casting.Fuel : 0,
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
            BaseFortitudeThreshold = FortitudeOf(info),
            StatblockAttacks = new[] { Area(Rolled(StatblockLine(info.AttackName, info.AttackModifier, info.DamageDiceCount, info.DamageDie, info.DamageModifier, info.Reach),
                    info.AttackVsThreshold, info.DamageType, info.DamageIsDirect, info.AttackDealsDamage), info.AttackEffect) }
                .Concat((info.MoreAttacks ?? []).Select(a => Area(Rolled(StatblockLine(a.Name, a.Modifier, a.DamageDiceCount, a.DamageDie, a.DamageModifier, info.Reach),
                    a.VsThreshold, a.DamageType, a.DamageIsDirect, a.DealsDamage), a.Effect)))
                .ToList(),
            Armour = info.Armour,
            InnateSpeed = info.Speed,
            IsSwarm = info.Role == AdversaryRole.Swarm,
            IsTiny = info.Size == AdversarySize.Tiny,
            SeesInDark = info.Features?.Any(f => f.Name.Contains("Nightvision", StringComparison.OrdinalIgnoreCase)) == true,
            Across = Footprints.AcrossFor(info.Size),
            Role = info.Role,
            Morale = Morale.FromStatblock(info),
            SkillBonusFor = info.Kind == AdversaryKind.Combat ? SkillsOf(info) : _ => 0,
            IsMinion = info.Role == AdversaryRole.Minion,
            IsUnshakable = info.Traits?.Contains(AdversaryTrait.Unshakable) == true,
            StaminaEachRound = info.Features?.Any(f => f.Name == "Fitness") == true ? 1 : 0,
            StaminaMax = RuleTrials.StaminaFor(info.Role, info.Tier, info.Stamina),
            Stamina = RuleTrials.StaminaFor(info.Role, info.Tier, info.Stamina),
            TacticalSpendsPerRound = CharacterCalculator.TacticalSpendsPerRound(info.Tier),
            Tier = info.Tier,
            StatblockManoeuvres = ManoeuvresIn(info.AttackEffect),
            Kind = info.Name,
            Traits = (info.Traits ?? []).ToHashSet(),
            IsMindImmune = info.Traits?.Contains(AdversaryTrait.BoundNotAlive) == true || info.Features?.Any(f => f.Name == "Deathless Purpose") == true,
        };

        /// <summary>
        /// The manoeuvres an Attack line's effect gives the creature — "may attempt Topple (ST) as a
        /// manoeuvre" — each named one it can declare with that blow (GM Reference §2).
        /// </summary>
        public static IReadOnlyList<WeaponManoeuvre> ManoeuvresIn(string? effect)
        {
            if (string.IsNullOrWhiteSpace(effect) || !effect.Contains("manoeuvre", StringComparison.OrdinalIgnoreCase))
            {
                return [];
            }

            return Enum.GetValues<WeaponManoeuvre>()
                .Where(m => System.Text.RegularExpressions.Regex.IsMatch(effect, $@"\b{m}\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                .ToList();
        }

        /// <summary>
        /// One attack line of a statblock. A line made with a bow, crossbow or sling is a shot, and
        /// follows the weapon range rules like anybody's (GM Reference §9: "at Middle a Medium weapon
        /// rolls d4 rather than d6 and the target's Dodge Threshold is 2 higher"); the to-hit and the
        /// die at Close and Short are the statblock's own.
        /// </summary>
        /// <summary>
        /// An attack line written "against every creature within N squares" becomes an area attack
        /// reaching that far; "every other round" rations it (GM Reference §3, §11).
        /// </summary>
        /// <summary>
        /// A foe's Fortitude Threshold: its statblock's FT, or null where it is immune — written so,
        /// or carrying No Living Body or Bound, Not Alive (GM Reference §9, "On Type"). A block that
        /// leaves FT out gets 11 + Tier, a Role's middling Vitality.
        /// </summary>
        private static int? FortitudeOf(StartingAdversaryInfo info) =>
            info.FortitudeImmune || info.Traits?.Contains(AdversaryTrait.NoLivingBody) == true || info.Traits?.Contains(AdversaryTrait.BoundNotAlive) == true
                ? null
                : info.FortitudeThreshold ?? 11 + (int)info.Tier;

        /// <summary>
        /// What an Attack line's effect adds to the line: a poison carried on the blow — "poison vs FT,
        /// 1d4 Body (Direct)" (Core Rules §10) — and an area — "against every creature within N squares".
        /// </summary>
        public static AttackProfile Area(AttackProfile line, string? effect)
        {
            if (PoisonIn(effect) is { } poison)
            {
                line = line with { Poison = poison };
            }

            if (string.IsNullOrWhiteSpace(effect)
                || System.Text.RegularExpressions.Regex.Match(effect, @"every creature within (\d+) squares?", System.Text.RegularExpressions.RegexOptions.IgnoreCase) is not { Success: true } within)
            {
                return line;
            }

            var squares = int.Parse(within.Groups[1].Value);
            return line with
            {
                Reach = squares,
                AreaSquares = squares,
                EveryOtherRound = effect.Contains("every other round", StringComparison.OrdinalIgnoreCase),
            };
        }

        /// <summary>
        /// What an Attack line is rolled against and what it wounds: Dodge and Body unless it says
        /// otherwise — "+2 v FT / 1d6 Body (Direct)" for a gas or a spore cloud, with no attack behind
        /// it (Core Rules §10, "Fortitude Threshold"); Mind or Soul for what strikes at those. A line
        /// against Resolve or Stability that isn't a spell is read against Dodge, as before.
        /// </summary>
        private static AttackProfile Rolled(AttackProfile line, ThresholdType against, WoundType track, bool direct, bool dealsDamage = true)
        {
            // An innate Bind: "Petrifying Gaze (Motion - Bind, Stuck) - +3 v ST" — against Stability,
            // laying the Bind's effect on a hit (Core Rules §11, "Motion Spells").
            var bind = against == ThresholdType.Stability && line.Name.Contains("Bind", StringComparison.OrdinalIgnoreCase);
            return line with
            {
                Against = against == ThresholdType.Fortitude || bind ? against : ThresholdType.Dodge,
                Track = track,
                IsDirect = direct,
                Imposes = !bind ? null
                    : line.Name.Contains("Frozen", StringComparison.OrdinalIgnoreCase) ? CharacterCondition.Frozen
                    : line.Name.Contains("Stuck", StringComparison.OrdinalIgnoreCase) ? CharacterCondition.Stuck
                    : CharacterCondition.Slowed,
                DealsNoDamage = !dealsDamage,
            };
        }

        /// <summary>The poison an Attack line's effect carries — "poison vs FT, 1d4 Body (Direct)" — or null.</summary>
        public static Poison? PoisonIn(string? effect)
        {
            if (string.IsNullOrWhiteSpace(effect)
                || System.Text.RegularExpressions.Regex.Match(effect, @"poison\s+vs?\.?\s+FT\s*,?\s*(?<count>\d+)?d(?<die>\d+)\s*(?<mod>[+\-−–]\s*\d+)?\s+(?<track>Body|Mind|Soul)",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase) is not { Success: true } m)
            {
                return null;
            }

            return new Poison(
                m.Groups["count"].Success ? int.Parse(m.Groups["count"].Value) : 1,
                int.Parse(m.Groups["die"].Value),
                m.Groups["mod"].Success ? int.Parse(m.Groups["mod"].Value.Replace(" ", "").Replace('−', '-').Replace('–', '-').Replace("+", "")) : 0,
                Enum.Parse<WoundType>(m.Groups["track"].Value, ignoreCase: true));
        }

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
            NamedEdge = AttackProfile.EdgeNamedIn(name),
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
        /// <summary>
        /// The riding horse the rules assume (Core Rules §9, "Mounts"): Body 8, DT 12, ST 14, Fast,
        /// Hooves +2 v DT / 1d6 Body — in the saddle, with <paramref name="rider"/> on it.
        /// </summary>
        public static Combatant Mounted(Combatant rider, string? name = null)
        {
            var horse = new Combatant
            {
                Name = name ?? (rider.Side == Side.Party ? $"{rider.Name.Split(' ').First(w => w != "The")}'s horse" : $"{rider.Name}'s horse"),
                Side = rider.Side,
                Position = rider.Position,
                BodyMax = 8,
                Body = 8,
                BaseDodgeThreshold = 12,
                BaseStabilityThreshold = 14,
                Armour = ArmourWeight.None,
                InnateSpeed = Speed.Fast,
                StatblockAttacks = [StatblockLine("Hooves", 2, 1, "d6", 0, 1)],
                StaminaMax = 0,
                Stamina = 0,
                TacticalSpendsPerRound = 0,
                Tier = Tier.T1,
                IsSteed = true,
                Kind = "Riding horse",
            };
            horse.Rider = rider;
            rider.Steed = horse;
            rider.IsRiding = true;
            return rider;
        }

        /// <summary>A weapon carried in the pack: neither in hand nor at the ready, an action to take out (Core Rules §15).</summary>
        public static WeaponDefinition InPack(StartingWeaponInfo info) => FromCatalogue(info);

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
