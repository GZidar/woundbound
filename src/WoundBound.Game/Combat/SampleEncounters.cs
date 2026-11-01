using WoundBound.Game.Data;
using WoundBound.Rules.Catalog;
using WoundBound.Rules.Enums;
using WoundBound.Rules.Models;

namespace WoundBound.Game.Combat
{
    /// <summary>The fights the slice can stage, until encounters come from somewhere better.</summary>
    public static class SampleEncounters
    {
        public const int BoardWidth = 10;
        public const int BoardHeight = 8;

        /// <summary>
        /// The field the party fights are fought on: wide enough that a creature running from the
        /// party can get beyond Long — 16 squares — of all of them, which is what leaving the board
        /// means. The party starts at its western end.
        /// </summary>
        public const int FieldWidth = 28;
        public const int FieldHeight = 18;

        /// <summary>
        /// The Dumb Fighter of Core Rules §12 — the same build the Rules tests check against the
        /// book's worked numbers — in light armour, longsword in hand and a short bow slung at the
        /// ready for whatever he can't walk up to.
        /// </summary>
        /// <param name="torch">Carrying a lit torch in his free hand — the bundle of three every pack has.</param>
        public static CharacterBuild DumbFighter(GameData data, bool torch = false) => new()
        {
            CharacterName = "The Dumb Fighter",
            ClassPrinciple = ClassPrinciple.Martial,
            SecondaryWound = WoundType.Body,
            Tier = Tier.T1,
            AbilityModifiers = new Dictionary<Ability, int>
            {
                [Ability.Strength] = 3,
                [Ability.Agility] = 2,
                [Ability.Intellect] = -1,
                [Ability.Vitality] = 1,
                [Ability.Focus] = 0,
                [Ability.Presence] = 1,
            },
            Weapons =
            {
                CombatantFactory.InHand(data.Weapon("Longsword")),
                CombatantFactory.AtTheReady(data.Weapon("Short Bow")),
            },
            Armour = { new ArmourDefinition { Name = "Leather jerkin", Weight = ArmourWeight.Light, IsEquipped = true } },
            InventoryItems = { new InventoryItemDefinition { Name = "Torches (bundle of 3)", Quantity = 3, IsLit = torch } },
        };

        /// <summary>
        /// Salla Rinehart, Nimble Duelist, from the Quickstart Adventure: rapier in hand, and a
        /// dagger beside it standing in for the stiletto the catalogue doesn't have. She fights
        /// with the rapier — two-weapon fighting isn't in yet.
        /// </summary>
        public static CharacterBuild SallaRinehart(GameData data) => new()
        {
            CharacterName = "Salla Rinehart",
            ClassPrinciple = ClassPrinciple.Nimble,
            SecondaryWound = WoundType.Body,
            Tier = Tier.T1,
            AbilityModifiers = new Dictionary<Ability, int>
            {
                [Ability.Strength] = 1,
                [Ability.Agility] = 3,
                [Ability.Intellect] = 1,
                [Ability.Vitality] = 2,
                [Ability.Focus] = -1,
                [Ability.Presence] = 0,
            },
            Weapons =
            {
                CombatantFactory.InHand(data.Weapon("Rapier")),
                CombatantFactory.InHand(data.Weapon("Dagger")),
            },
            Armour = { new ArmourDefinition { Name = "Leather armour", Weight = ArmourWeight.Light, IsEquipped = true } },
        };

        /// <summary>
        /// Joren Blacke, Martial Templar, from the Quickstart Adventure: battle axe and a buckler in
        /// hand, in chainmail. The buckler is a Light shield — his own cover, and nobody else's.
        /// </summary>
        public static CharacterBuild JorenBlacke(GameData data) => new()
        {
            CharacterName = "Joren Blacke",
            ClassPrinciple = ClassPrinciple.Martial,
            SecondaryWound = WoundType.Soul,
            Tier = Tier.T1,
            AbilityModifiers = new Dictionary<Ability, int>
            {
                [Ability.Strength] = 3,
                [Ability.Agility] = 1,
                [Ability.Intellect] = 0,
                [Ability.Vitality] = 2,
                [Ability.Focus] = -1,
                [Ability.Presence] = 1,
            },
            Weapons = { CombatantFactory.InHand(data.Weapon("Battleaxe")) },
            Shields = { CombatantFactory.ShieldInHand("Buckler", ShieldWeight.Light) },
            Armour = { new ArmourDefinition { Name = "Chainmail", Weight = ArmourWeight.Medium, IsEquipped = true } },
        };

        /// <summary>
        /// Drovan Thale, Nimble Ranger, from the Quickstart Adventure: short bow in hand and a
        /// dagger at the ready for the hunting knife.
        /// </summary>
        public static CharacterBuild DrovanThale(GameData data) => new()
        {
            CharacterName = "Drovan Thale",
            ClassPrinciple = ClassPrinciple.Nimble,
            SecondaryWound = WoundType.Mind,
            Tier = Tier.T1,
            AbilityModifiers = new Dictionary<Ability, int>
            {
                [Ability.Strength] = -1,
                [Ability.Agility] = 3,
                [Ability.Intellect] = 1,
                [Ability.Vitality] = 1,
                [Ability.Focus] = 2,
                [Ability.Presence] = 0,
            },
            Weapons =
            {
                CombatantFactory.InHand(data.Weapon("Short Bow")),
                CombatantFactory.AtTheReady(data.Weapon("Dagger")),
            },
            Armour = { new ArmourDefinition { Name = "Leather", Weight = ArmourWeight.Light, IsEquipped = true } },
        };

        /// <summary>
        /// Mirella Vask, Spiritual Warden, from the Quickstart Adventure: a sword in hand and her
        /// lantern in the other, and the four spells on her sheet — Smite, Scourging Light, Vital
        /// Transfer (Blood Magic, reached by Bleeding for Power) and Blessing. She casts with
        /// Presence, keeps it when she bleeds, and Grace adds her Intellect to her spells' dice.
        /// </summary>
        /// <param name="lantern">Her lantern lit, in her free hand.</param>
        public static CharacterBuild MirellaVask(GameData data, bool lantern = false) => new()
        {
            CharacterName = "Mirella Vask",
            ClassPrinciple = ClassPrinciple.Spiritual,
            SecondaryWound = WoundType.Body,
            Tier = Tier.T1,
            AbilityModifiers = new Dictionary<Ability, int>
            {
                [Ability.Strength] = -1,
                [Ability.Agility] = 0,
                [Ability.Intellect] = 1,
                [Ability.Vitality] = 2,
                [Ability.Focus] = 1,
                [Ability.Presence] = 3,
            },
            Experiences = { "Has ministered in the many villages that dot the land, and travelled the region extensively", "Cross Trained (Agility)" },
            Weapons = { CombatantFactory.InHand(data.Weapon("Short Sword")) },
            Armour = { new ArmourDefinition { Name = "Leather", Weight = ArmourWeight.Light, IsEquipped = true } },
            InventoryItems =
            {
                new InventoryItemDefinition { Name = "Lantern", Quantity = 1, IsLit = lantern },
                new InventoryItemDefinition { Name = "Oil (flask)", Quantity = 1 },
            },
            Spells = { data.Spell("Smite"), data.Spell("Scourging Light"), data.Spell("Vital Transfer"), data.Spell("Blessing") },
        };

        /// <summary>
        /// Elthric Dane, Cerebral Blood Mage, from the Quickstart Adventure: a dagger, a torch, and
        /// the four spells on his sheet — Mind Spike, Foresight, Telekinetic Shove and Blood Mist. He
        /// casts with Focus whichever pool pays, and Overclocks once a round. Trained in Elemental
        /// Magic (Fire), he also builds Flame Burst and Conflagration — cast with Intellect, paid in
        /// Fuel, and his torches are the fire he carries (Core Rules §11, "Example of Play: Elemental Magic").
        /// </summary>
        /// <param name="torch">A lit torch in his free hand.</param>
        public static CharacterBuild ElthricDane(GameData data, bool torch = false) => new()
        {
            CharacterName = "Elthric Dane",
            ClassPrinciple = ClassPrinciple.Cerebral,
            SecondaryWound = WoundType.Body,
            Tier = Tier.T1,
            AbilityModifiers = new Dictionary<Ability, int>
            {
                [Ability.Strength] = -1,
                [Ability.Agility] = 1,
                [Ability.Intellect] = 1,
                [Ability.Vitality] = 2,
                [Ability.Focus] = 3,
                [Ability.Presence] = 0,
            },
            Experiences = { "Studied ancient languages and cyphers", "Trained in Elemental Magic (Fire)" },
            Weapons = { CombatantFactory.InHand(data.Weapon("Dagger")) },
            InventoryItems = { new InventoryItemDefinition { Name = "Torches (bundle of 3)", Quantity = 6, IsLit = torch } },
            Spells =
            {
                data.Spell("Mind Spike"), data.Spell("Foresight"), data.Spell("Telekinetic Shove"), data.Spell("Blood Mist"),
                data.Spell("Flame Burst"), data.Spell("Conflagration"),
            },
        };

        /// <summary>
        /// Three characters — the Dumb Fighter, Salla and Drovan — ambushed on the road: a Tollroad
        /// Bandit, his Timber Wolf, and two Bramble Ratkin. 1.5 + 1 + 0.25 × 2 is a Threat Value
        /// of 3, a Standard fight's budget for a party of three (GM Reference §5), and the GM has
        /// three adversary turns a round for four creatures.
        /// </summary>
        public static Encounter PartyVersusAmbush(GameData data, IDiceRoller dice, Brains.IGmBrain? gm = null)
        {
            var ratkin = data.Adversary("Bramble Ratkin");

            var combatants = new List<Combatant>
            {
                CombatantFactory.FromCharacter(DumbFighter(data), new GridPosition(1, 4)),
                CombatantFactory.FromCharacter(SallaRinehart(data), new GridPosition(1, 2)),
                CombatantFactory.FromCharacter(DrovanThale(data), new GridPosition(0, 6)),
                CombatantFactory.FromAdversary(data.Adversary("Tollroad Bandit"), "Tollroad Bandit", new GridPosition(8, 4)),
                CombatantFactory.FromAdversary(data.Adversary("Timber Wolf"), "Timber Wolf", new GridPosition(8, 2)),
                CombatantFactory.FromAdversary(ratkin, "Ratkin Biter", new GridPosition(7, 6)),
                CombatantFactory.FromAdversary(ratkin, "Ratkin Shiv", new GridPosition(9, 7)),
            };

            return new Encounter(FieldWidth, FieldHeight, combatants, data.RangeBands, dice, gm);
        }

        /// <summary>
        /// The Quickstart Adventure's Roadside Bandit (Blood and Vintage, "Roadside Ambush"): the
        /// lantern-bearer at the signpost, and his like. Cowardice — he breaks below half his Body,
        /// or on seeing one of his side go down.
        /// </summary>
        public static StartingAdversaryInfo RoadsideBandit { get; } = QuickstartBandit("Roadside Bandit", "Rusty Sword", die: "d6", damageModifier: 0);

        /// <summary>
        /// The Quickstart Adventure's Bandit Ambusher: a Worn Crossbow (+2, 1d6), "or Rusty Knife"
        /// (+2, 1d4+2) for when somebody gets close.
        /// </summary>
        public static StartingAdversaryInfo BanditAmbusher { get; } = QuickstartBandit("Bandit Ambusher", "Worn Crossbow", die: "d6", damageModifier: 0) with
        {
            MoreAttacks = [new StartingAttackInfo("Rusty Knife", Modifier: 2, DamageDiceCount: 1, DamageDie: "d4", DamageModifier: 2)],
        };

        private static StartingAdversaryInfo QuickstartBandit(string name, string attack, string die, int damageModifier) => new(
            ContentSource: "quickstart",
            Name: name,
            Role: AdversaryRole.Skirmisher,
            Tier: Tier.T1,
            BodyMax: 6,
            MindMax: 4,
            SoulMax: 4,
            Stamina: 3,
            DodgeThreshold: 15,
            ResolveThreshold: 12,
            StabilityThreshold: 11,
            AttackName: attack,
            AttackModifier: 2,
            AttackVsThreshold: ThresholdType.Dodge,
            DamageDiceCount: 1,
            DamageDie: die,
            DamageModifier: damageModifier,
            DamageType: WoundType.Body,
            SpecialText: string.Empty,
            Notes: "Blood and Vintage, Roadside Ambush.",
            Armour: ArmourWeight.Light,
            FortitudeThreshold: 12,
            Features:
            [
                new StartingFeatureInfo("Cowardice",
                    "reduced below half its Body pool, or watching another of its side go down, it looks for a chance to break and run rather than fight to the last wound."),
            ]);

        /// <summary>
        /// The Quickstart's signpost ambush for a table of three (Blood and Vintage, "Roadside
        /// Ambush" — two Bandit Ambushers and a Roadside Bandit, one Skirmisher per player): the
        /// lantern-bearer under the signpost, an ambusher on the ridge and another in the trees.
        /// "This combat is not intended to be deadly and these adversaries will run away" — and
        /// the lantern-bearer is the one the party wants to keep: a captured bandit can be made to
        /// talk (GM Reference §2). It's night, and Unlit: the lantern-bearer's lantern lights him,
        /// the Dumb Fighter carries the party's one torch, and everybody else stands in the dark.
        /// The ridge and the trees are broken ground. The opening volley isn't in yet.
        /// </summary>
        /// <param name="fourPlayers">
        /// The book's table of four: Joren Blacke joins the party, and the fourth bandit — a second
        /// Roadside Bandit, around the corner — joins the ambush ("If you have four players, then
        /// have two of each").
        /// </param>
        /// <param name="setting">Where to stage it: the book's night by default; <see cref="Setting.Daylight"/> stages it with no
        /// lights needed, and the Dumb Fighter's torch stays in his pack.</param>
        public static Encounter SignpostAmbush(GameData data, IDiceRoller dice, Brains.IGmBrain? gm = null, bool fourPlayers = false, Setting? setting = null)
        {
            setting ??= SignpostNight;
            var dark = setting.IsUnlit;
            var lanternBearer = CombatantFactory.FromAdversary(RoadsideBandit, "Lantern-bearer", new GridPosition(6, 4));
            lanternBearer.StatblockLights = dark ? 1 : 0;

            var combatants = new List<Combatant>
            {
                CombatantFactory.FromCharacter(DumbFighter(data, torch: dark), new GridPosition(1, 4)),
                CombatantFactory.FromCharacter(SallaRinehart(data), new GridPosition(1, 3)),
                CombatantFactory.FromCharacter(DrovanThale(data), new GridPosition(1, 5)),
                lanternBearer,
                CombatantFactory.FromAdversary(BanditAmbusher, "Ambusher on the ridge", new GridPosition(7, 1)),
                CombatantFactory.FromAdversary(BanditAmbusher, "Ambusher in the trees", new GridPosition(7, 6)),
            };

            if (fourPlayers)
            {
                combatants.Insert(3, CombatantFactory.FromCharacter(JorenBlacke(data), new GridPosition(2, 4)));
                combatants.Add(CombatantFactory.FromAdversary(RoadsideBandit, "Bandit round the corner", new GridPosition(11, 3)));
            }

            return new Encounter(FieldWidth, FieldHeight, combatants, data.RangeBands, dice, gm,
                objectives: [new Objective.DontLetEscape("Take the lantern-bearer — he can be made to talk", lanternBearer)],
                setting: setting, environment: data.Environment);
        }

        /// <summary>
        /// The signpost with the Quickstart's own party of five — Salla, Drovan, Joren, Mirella and
        /// Elthric, the two casters among them — against one Skirmisher each: the lantern-bearer, the
        /// bandit round the corner, and three Bandit Ambushers. At night Mirella's lantern and
        /// Elthric's torch are the party's lights.
        /// </summary>
        public static Encounter SignpostWithCasters(GameData data, IDiceRoller dice, Brains.IGmBrain? gm = null, Setting? setting = null)
        {
            setting ??= SignpostNight;
            var dark = setting.IsUnlit;
            var lanternBearer = CombatantFactory.FromAdversary(RoadsideBandit, "Lantern-bearer", new GridPosition(6, 4));
            lanternBearer.StatblockLights = dark ? 1 : 0;

            var combatants = new List<Combatant>
            {
                CombatantFactory.FromCharacter(SallaRinehart(data), new GridPosition(1, 3)),
                CombatantFactory.FromCharacter(DrovanThale(data), new GridPosition(1, 5)),
                CombatantFactory.FromCharacter(JorenBlacke(data), new GridPosition(2, 4)),
                CombatantFactory.FromCharacter(MirellaVask(data, lantern: dark), new GridPosition(0, 4)),
                CombatantFactory.FromCharacter(ElthricDane(data, torch: dark), new GridPosition(0, 3)),
                lanternBearer,
                CombatantFactory.FromAdversary(BanditAmbusher, "Ambusher on the ridge", new GridPosition(7, 1)),
                CombatantFactory.FromAdversary(BanditAmbusher, "Ambusher in the trees", new GridPosition(7, 6)),
                CombatantFactory.FromAdversary(BanditAmbusher, "Ambusher by the stones", new GridPosition(9, 5)),
                CombatantFactory.FromAdversary(RoadsideBandit, "Bandit round the corner", new GridPosition(11, 3)),
            };

            return new Encounter(FieldWidth, FieldHeight, combatants, data.RangeBands, dice, gm,
                objectives: [new Objective.DontLetEscape("Take the lantern-bearer — he can be made to talk", lanternBearer)],
                setting: setting, environment: data.Environment);
        }

        /// <summary>
        /// The signpost at night (Blood and Vintage, "Roadside Ambush"): "Visibility Unlit — night
        /// has fallen, so what can be seen depends on who is carrying a light (the lantern-bearer
        /// included). The rain soaks everyone, but it isn't heavy enough to count as Obscured — yet.
        /// Terrain Firm" — the road. The ridge on the right and the trees on the left are rough going.
        /// </summary>
        public static Setting SignpostNight { get; } = new()
        {
            Name = "The signpost at night",
            Visibility = Visibility.None,
            Cause = VisibilityCause.Unlit,
            Ground = Setting.Patches(Terrain.Broken, (6, 0, 9, 1), (6, 7, 9, 8)),
        };

        /// <summary>
        /// The Ferry Shrine at Longreach Crossing (Lands of the Inner Sea, §10): a river running
        /// north to south through the field, forded on a line of loose river stone — "Broken in the
        /// ford itself … and Firm on both banks" — with deep water above and below the ford that has
        /// to be swum. The Undertow waits in the ford: "Avoid: Normal (18), Athletics … or Endurance.
        /// Effect: A character who fails is swept a Short distance downstream and must spend an
        /// action regaining their footing before acting normally again." On the east bank stand the
        /// shrine's low stone arch and the ferryman's hut, both solid.
        /// </summary>
        public static Setting LongreachCrossing { get; } = BuildLongreachCrossing();

        private static Setting BuildLongreachCrossing()
        {
            var water = new Dictionary<GridPosition, WaterDepth>();
            for (var x = 11; x <= 15; x++)
            {
                for (var y = -6; y < FieldHeight + 6; y++)
                {
                    water[new GridPosition(x, y)] = y is >= 7 and <= 9 ? WaterDepth.Shallow : WaterDepth.Deep;
                }
            }

            return new Setting
            {
                Name = "Longreach Crossing",
                Water = water,
                Walls = Setting.Squares((18, 6, 18, 6), (18, 10, 18, 10), (20, 2, 22, 4)),
                Hazards =
                [
                    new Hazard("the Undertow",
                        "The ford looks calmer than it is — a slow, cold pull beneath the surface that's claimed more than one cart.",
                        Setting.Squares((11, 7, 15, 9)),
                        [new HazardCheck("Athletics", 18), new HazardCheck("Endurance", 18)],
                        new HazardEffect.Swept(Squares: 4, Dx: 0, Dy: 1, LosesFooting: true)),
                ],
            };
        }

        /// <summary>
        /// The Loom's Challenge hook at the Ferry Shrine: Skirmishers "use the shrine's blind
        /// approach to stage an ambush" — here caught with the party mid-ford, where they stand at
        /// −2 Stability and the Undertow underneath. Two Reedmarsh Skulkers and a Bandit Ambusher,
        /// a Standard fight for three (GM Reference §5).
        /// </summary>
        public static Encounter FerryShrineAmbush(GameData data, IDiceRoller dice, Brains.IGmBrain? gm = null)
        {
            var skulker = data.Adversary("Reedmarsh Skulker");
            var combatants = new List<Combatant>
            {
                CombatantFactory.FromCharacter(DumbFighter(data), new GridPosition(12, 8)),
                CombatantFactory.FromCharacter(SallaRinehart(data), new GridPosition(13, 7)),
                CombatantFactory.FromCharacter(DrovanThale(data), new GridPosition(11, 9)),
                CombatantFactory.FromAdversary(skulker, "Skulker by the arch", new GridPosition(19, 5)),
                CombatantFactory.FromAdversary(skulker, "Skulker in the reeds", new GridPosition(20, 11)),
                CombatantFactory.FromAdversary(BanditAmbusher, "Ambusher by the hut", new GridPosition(23, 6)),
            };

            return new Encounter(FieldWidth, FieldHeight, combatants, data.RangeBands, dice, gm,
                setting: LongreachCrossing, environment: data.Environment);
        }

        /// <summary>
        /// The Ferry Shrine ambush with the Quickstart's own five caught mid-ford — Salla, Drovan,
        /// Joren, Mirella and Elthric — against one Skirmisher each: three Reedmarsh Skulkers and two
        /// Bandit Ambushers. The Skulkers have no Cowardice, so this one is fought out.
        /// </summary>
        public static Encounter FerryShrineWithCasters(GameData data, IDiceRoller dice, Brains.IGmBrain? gm = null)
        {
            var skulker = data.Adversary("Reedmarsh Skulker");
            var combatants = new List<Combatant>
            {
                CombatantFactory.FromCharacter(SallaRinehart(data), new GridPosition(13, 7)),
                CombatantFactory.FromCharacter(JorenBlacke(data), new GridPosition(14, 8)),
                CombatantFactory.FromCharacter(DrovanThale(data), new GridPosition(12, 9)),
                CombatantFactory.FromCharacter(MirellaVask(data), new GridPosition(12, 8)),
                CombatantFactory.FromCharacter(ElthricDane(data), new GridPosition(11, 7)),
                CombatantFactory.FromAdversary(skulker, "Skulker by the arch", new GridPosition(19, 5)),
                CombatantFactory.FromAdversary(skulker, "Skulker in the reeds", new GridPosition(20, 11)),
                CombatantFactory.FromAdversary(skulker, "Skulker on the bank", new GridPosition(17, 13)),
                CombatantFactory.FromAdversary(BanditAmbusher, "Ambusher by the hut", new GridPosition(23, 6)),
                CombatantFactory.FromAdversary(BanditAmbusher, "Ambusher behind the arch", new GridPosition(21, 9)),
            };

            return new Encounter(FieldWidth, FieldHeight, combatants, data.RangeBands, dice, gm,
                setting: LongreachCrossing, environment: data.Environment);
        }

        /// <summary>One fighter against a pack of three Bramble Ratkin: Minions, so every hit that gets past their (lack of) armour drops one.</summary>
        public static Encounter FighterVersusRatkin(GameData data, IDiceRoller dice, Brains.IGmBrain? gm = null)
        {
            var ratkin = data.Adversary("Bramble Ratkin");

            var combatants = new List<Combatant>
            {
                CombatantFactory.FromCharacter(DumbFighter(data), new GridPosition(1, 4)),
                CombatantFactory.FromAdversary(ratkin, "Ratkin Biter", new GridPosition(7, 2)),
                CombatantFactory.FromAdversary(ratkin, "Ratkin Sneak", new GridPosition(8, 4)),
                CombatantFactory.FromAdversary(ratkin, "Ratkin Shiv", new GridPosition(7, 6)),
            };

            return new Encounter(BoardWidth, BoardHeight, combatants, data.RangeBands, dice, gm);
        }
    }
}
