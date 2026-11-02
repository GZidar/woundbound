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
            Spells = { data.Spell("Smite"), data.Spell("Scourging Light"), data.Spell("Vital Transfer"), data.Spell("Blessing"), EyeOfEolis, ShrineLight, data.Spell("False Portent"), data.Spell("Curse") },
        };

        /// <summary>
        /// The Control spell Mirella builds in Core Rules §11's Example of Play: "the Eye of Eolis
        /// opening in the dark above him, and it knows his name" — Divine, one target, a Ray, Small,
        /// against Resolve. Which of Fear, Confusion or Compulsion is chosen when it lands.
        /// </summary>
        public static SpellDefinition EyeOfEolis => new()
        {
            Name = "Eye of Eolis",
            Source = MagicSource.DivineMagic,
            SpellTier = Tier.T1,
            SpellType = SpellType.Control,
            Target = SpellTarget.One,
            Shape = SpellShape.Ray,
            Size = SpellSize.Small,
            Range = SpellRange.Short,
            Threshold = ThresholdType.Resolve,
            Notes = "For a moment the target sees what Mirella sees: the Eye of Eolis opening in the dark above them, and it knows their name.",
        };

        /// <summary>
        /// The light Mirella raises in Core Rules §11's Example of Play: Utility — "the carved letters
        /// start to shine, the way the shrine lamps do at home." Divine, a Small Volume at Close: one
        /// carried light where it is put, for the scene, and out if she goes down.
        /// </summary>
        public static SpellDefinition ShrineLight => new()
        {
            Name = "Shrine Light",
            Source = MagicSource.DivineMagic,
            SpellTier = Tier.T1,
            SpellType = SpellType.Utility,
            Target = SpellTarget.Many,
            Shape = SpellShape.Volume,
            Size = SpellSize.Small,
            Range = SpellRange.Close,
            Notes = "Whatever she lays a hand on begins to shine, the way the shrine lamps do at home - light that stays where it was put.",
        };

        /// <summary>
        /// A Sight Illusion built for the game from Core Rules §11: a block of fitted stone standing on
        /// open ground — seen, not felt, so walking into it or shooting through it gives it away.
        /// </summary>
        public static SpellDefinition PhantomWall => new()
        {
            Name = "Phantom Wall",
            Source = MagicSource.AetherMagic,
            SpellTier = Tier.T1,
            SpellType = SpellType.Illusion,
            Target = SpellTarget.Many,
            Shape = SpellShape.Volume,
            Size = SpellSize.Small,
            Range = SpellRange.Short,
            Notes = "A wall of fitted stone stands where there was open ground - seen, never touched.",
        };

        /// <summary>
        /// An Aether working on the ground, built for the game from Core Rules §9's "drying, damping,
        /// firming or fouling ground": a Small Volume pressed flat and dry underfoot, or churned to mire —
        /// one grade either way, for the scene.
        /// </summary>
        public static SpellDefinition SettleTheGround => new()
        {
            Name = "Settle the Ground",
            Source = MagicSource.AetherMagic,
            SpellTier = Tier.T1,
            SpellType = SpellType.Utility,
            Target = SpellTarget.Many,
            Shape = SpellShape.Volume,
            Size = SpellSize.Small,
            Range = SpellRange.Short,
            Notes = "Thought presses the ground flat and dry for a sure footing - or churns it to mire under somebody else's.",
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
                data.Spell("Flame Burst"), data.Spell("Conflagration"), SettleTheGround,
                data.Spell("Phantom Double"), data.Spell("Phantom Wound"), PhantomWall, data.Spell("Fog of Confusion"),
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

        /// <summary>
        /// A camp on the road at night — staged for the game, not taken from a book. Unlit, lit by
        /// the party's campfire — a built fire, which holds its grade out to Long, so the camp can be
        /// seen from the dark and the dark can't be seen from the camp — and by Mirella's lantern;
        /// the torches are packed away. The fire is also 2 Fuel for a Fire caster in reach of it,
        /// spent whole and its light with it, leaving the lantern (Core Rules §14).
        /// </summary>
        public static Setting CampAtNight { get; } = new()
        {
            Name = "The camp at night",
            Visibility = Visibility.None,
            Cause = VisibilityCause.Unlit,
            Fires = new HashSet<GridPosition> { CampfireSquare },
            Ground = Setting.Patches(Terrain.Broken, (16, 0, 19, 4), (16, 13, 19, 17)),
        };

        /// <summary>Where the campfire burns.</summary>
        public static GridPosition CampfireSquare => new(10, 9);

        /// <summary>
        /// The Quickstart's five around their campfire, set on in the night by a Tollroad Bandit and
        /// his two Timber Wolves, with two Bandit Ambushers on the rough ground to either side — about
        /// a Standard fight's budget for five (GM Reference §5). Elthric sits by the fire.
        /// </summary>
        public static Encounter CampfireAmbush(GameData data, IDiceRoller dice, Brains.IGmBrain? gm = null)
        {
            var wolf = data.Adversary("Timber Wolf");
            var combatants = new List<Combatant>
            {
                CombatantFactory.FromCharacter(SallaRinehart(data), new GridPosition(9, 8)),
                CombatantFactory.FromCharacter(JorenBlacke(data), new GridPosition(11, 10)),
                CombatantFactory.FromCharacter(DrovanThale(data), new GridPosition(10, 10)),
                CombatantFactory.FromCharacter(MirellaVask(data, lantern: true), new GridPosition(9, 10)),
                CombatantFactory.FromCharacter(ElthricDane(data), new GridPosition(11, 8)),
                CombatantFactory.FromAdversary(data.Adversary("Tollroad Bandit"), "Tollroad Bandit", new GridPosition(20, 9)),
                CombatantFactory.FromAdversary(wolf, "Wolf from the north", new GridPosition(17, 6)),
                CombatantFactory.FromAdversary(wolf, "Wolf from the south", new GridPosition(17, 12)),
                CombatantFactory.FromAdversary(BanditAmbusher, "Ambusher in the rocks", new GridPosition(17, 3)),
                CombatantFactory.FromAdversary(BanditAmbusher, "Ambusher in the gully", new GridPosition(17, 15)),
            };

            return new Encounter(FieldWidth, FieldHeight, combatants, data.RangeBands, dice, gm,
                setting: CampAtNight, environment: data.Environment);
        }

        /// <summary>
        /// The hedge-witches' toll — staged for the game, not taken from a book: the Quickstart's five
        /// stopped on a daylight road by a Tollroad Bandit and his Timber Wolf, with three casters
        /// behind them — a Bog Hex-Caller, who breaks a line with fear; a Hollow Bloodwright, who bleeds
        /// for its Blood Lance; and a Wyrding Chanter, who keeps the ones in front blessed and mended
        /// (GM Reference §7). The hedges either side of the road are rough going.
        /// </summary>
        public static Encounter HedgeWitchesToll(GameData data, IDiceRoller dice, Brains.IGmBrain? gm = null)
        {
            Combatant Foe(string statblock, string name, int x, int y) =>
                CombatantFactory.FromAdversary(data.Adversary(statblock), name, new GridPosition(x, y), data.Spells);

            var combatants = new List<Combatant>
            {
                CombatantFactory.FromCharacter(SallaRinehart(data), new GridPosition(3, 7)),
                CombatantFactory.FromCharacter(JorenBlacke(data), new GridPosition(4, 9)),
                CombatantFactory.FromCharacter(DrovanThale(data), new GridPosition(2, 11)),
                CombatantFactory.FromCharacter(MirellaVask(data), new GridPosition(2, 9)),
                CombatantFactory.FromCharacter(ElthricDane(data), new GridPosition(1, 8)),
                Foe("Tollroad Bandit", "Tollroad Bandit", 11, 8),
                Foe("Timber Wolf", "Timber Wolf", 11, 10),
                Foe("Wyrding Chanter", "Wyrding Chanter", 14, 9),
                Foe("Hollow Bloodwright", "Hollow Bloodwright", 15, 7),
                Foe("Bog Hex-Caller", "Bog Hex-Caller", 15, 11),
            };

            return new Encounter(FieldWidth, FieldHeight, combatants, data.RangeBands, dice, gm,
                setting: HedgeRoad, environment: data.Environment);
        }

        /// <summary>
        /// The Grey Lanterns — a rival company of five, built exactly as the party is, from the same
        /// character rules, and fighting for the GM: "build a creature from any rule in this system"
        /// (GM Reference §2). Every rule but Interrupt travels, so they manoeuvre, Slip, Shelter, heal
        /// and cast back — a fight with the back-and-forth T1 statblocks are built not to give.
        /// </summary>
        public static Encounter RivalBand(GameData data, IDiceRoller dice, Brains.IGmBrain? gm = null)
        {
            Combatant Rival(CharacterBuild build, int x, int y) => CombatantFactory.FromCharacter(build, new GridPosition(x, y), Side.Adversaries);

            var combatants = new List<Combatant>
            {
                CombatantFactory.FromCharacter(SallaRinehart(data), new GridPosition(3, 7)),
                CombatantFactory.FromCharacter(JorenBlacke(data), new GridPosition(4, 9)),
                CombatantFactory.FromCharacter(DrovanThale(data), new GridPosition(2, 11)),
                CombatantFactory.FromCharacter(MirellaVask(data), new GridPosition(2, 9)),
                CombatantFactory.FromCharacter(ElthricDane(data), new GridPosition(1, 8)),
                Rival(BrannocVey(data), 13, 8),
                Rival(HeskMarrow(data), 13, 10),
                Rival(IlseVarrow(data), 14, 6),
                Rival(AldousCrane(data), 16, 9),
                Rival(SedgeOfTheFen(data), 16, 11),
            };

            return new Encounter(FieldWidth, FieldHeight, combatants, data.RangeBands, dice, gm,
                setting: HedgeRoad, environment: data.Environment);
        }

        private static Dictionary<Ability, int> Array(int str, int agi, int intl, int vit, int foc, int pre) => new()
        {
            [Ability.Strength] = str, [Ability.Agility] = agi, [Ability.Intellect] = intl,
            [Ability.Vitality] = vit, [Ability.Focus] = foc, [Ability.Presence] = pre,
        };

        /// <summary>Brannoc Vey, Martial Warrior: a war flail — Ring, Disarm and Sunder — and chainmail.</summary>
        public static CharacterBuild BrannocVey(GameData data) => new()
        {
            CharacterName = "Brannoc Vey",
            ClassPrinciple = ClassPrinciple.Martial,
            SecondaryWound = WoundType.Body,
            Tier = Tier.T1,
            AbilityModifiers = Array(str: 3, agi: 2, intl: 0, vit: 1, foc: -1, pre: 1),
            Weapons = { CombatantFactory.InHand(data.Weapon("War Flail")) },
            Armour = { new ArmourDefinition { Name = "Chainmail", Weight = ArmourWeight.Medium, IsEquipped = true } },
        };

        /// <summary>Hesk Marrow, Martial Templar: a mace — Topple and Ring — a round shield, and chainmail; Shelters the company.</summary>
        public static CharacterBuild HeskMarrow(GameData data) => new()
        {
            CharacterName = "Hesk Marrow",
            ClassPrinciple = ClassPrinciple.Martial,
            SecondaryWound = WoundType.Soul,
            Tier = Tier.T1,
            AbilityModifiers = Array(str: 3, agi: 1, intl: 0, vit: 2, foc: -1, pre: 1),
            Weapons = { CombatantFactory.InHand(data.Weapon("Mace")) },
            Shields = { CombatantFactory.ShieldInHand("Round shield", ShieldWeight.Medium) },
            Armour = { new ArmourDefinition { Name = "Chainmail", Weight = ArmourWeight.Medium, IsEquipped = true } },
        };

        /// <summary>Ilse Varrow, Nimble Rogue: rapier and leather, Slip and Precise — and Shelter.</summary>
        public static CharacterBuild IlseVarrow(GameData data) => new()
        {
            CharacterName = "Ilse Varrow",
            ClassPrinciple = ClassPrinciple.Nimble,
            SecondaryWound = WoundType.Soul,
            Tier = Tier.T1,
            AbilityModifiers = Array(str: 1, agi: 3, intl: 1, vit: 2, foc: -1, pre: 0),
            Weapons = { CombatantFactory.InHand(data.Weapon("Rapier")) },
            Armour = { new ArmourDefinition { Name = "Leather", Weight = ArmourWeight.Light, IsEquipped = true } },
        };

        /// <summary>Father Aldous Crane, Spiritual Priest: Mending Light to keep the company standing, Smite, Scourging Light and Blessing.</summary>
        public static CharacterBuild AldousCrane(GameData data) => new()
        {
            CharacterName = "Aldous Crane",
            ClassPrinciple = ClassPrinciple.Spiritual,
            SecondaryWound = WoundType.Soul,
            Tier = Tier.T1,
            AbilityModifiers = Array(str: -1, agi: 0, intl: 1, vit: 2, foc: 1, pre: 3),
            Weapons = { CombatantFactory.InHand(data.Weapon("Club")) },
            Armour = { new ArmourDefinition { Name = "Leather", Weight = ArmourWeight.Light, IsEquipped = true } },
            Spells = { MendingLight, data.Spell("Smite"), data.Spell("Scourging Light"), data.Spell("Blessing") },
        };

        /// <summary>A Divine Heal by touch — one ally's wounds closed over in light.</summary>
        public static SpellDefinition MendingLight => new()
        {
            Name = "Mending Light",
            Source = MagicSource.DivineMagic,
            SpellTier = Tier.T1,
            SpellType = SpellType.Heal,
            Target = SpellTarget.One,
            Shape = SpellShape.None,
            Size = SpellSize.Small,
            Range = SpellRange.Touch,
            Notes = "A hand laid on a wound, and light where the blood was.",
        };

        /// <summary>Sedge of the Fen, Cerebral Blood Mage: Mind Spike, Mind Storm over a crowd, Charm, and Blood Mist when Mind runs dry.</summary>
        public static CharacterBuild SedgeOfTheFen(GameData data) => new()
        {
            CharacterName = "Sedge of the Fen",
            ClassPrinciple = ClassPrinciple.Cerebral,
            SecondaryWound = WoundType.Body,
            Tier = Tier.T1,
            AbilityModifiers = Array(str: -1, agi: 1, intl: 1, vit: 2, foc: 3, pre: 0),
            Weapons = { CombatantFactory.InHand(data.Weapon("Dagger")) },
            Spells = { data.Spell("Mind Spike"), data.Spell("Mind Storm"), data.Spell("Charm"), data.Spell("Blood Mist") },
        };

        /// <summary>A road in daylight between two hedgerows of rough going.</summary>
        public static Setting HedgeRoad { get; } = Setting.Daylight with
        {
            Name = "The hedge road",
            Ground = Setting.Patches(Terrain.Broken, (0, 3, 27, 4), (0, 14, 27, 15)),
        };

        /// <summary>
        /// A causeway through the fen — staged for the game, not taken from a book: four squares of
        /// firm ground running east to west, a square of shallow reed-water either side, and deep
        /// marsh beyond that goes on past the map's edges. A corridor, so a Swarm on it is a fight
        /// the party must stand in (GM Reference §3, "The counter is the ground").
        /// </summary>
        public static Setting FenCauseway { get; } = BuildFenCauseway();

        private static Setting BuildFenCauseway()
        {
            var water = new Dictionary<GridPosition, WaterDepth>();
            for (var x = -6; x < FieldWidth + 6; x++)
            {
                for (var y = -6; y < FieldHeight + 6; y++)
                {
                    if (y is >= 7 and <= 10)
                    {
                        continue;
                    }

                    water[new GridPosition(x, y)] = y is 6 or 11 ? WaterDepth.Shallow : WaterDepth.Deep;
                }
            }

            return Setting.Daylight with { Name = "The fen causeway", Water = water };
        }

        /// <summary>
        /// The Quickstart's five met by a Fenmoor Ratswarm and the four Bramble Ratkin who herd it —
        /// on the fen causeway, or, with <paramref name="openGround"/>, on dry open ground where the
        /// party can back off and chip it (GM Reference §3). The rats come first; the ratkin behind.
        /// </summary>
        public static Encounter RatswarmOnTheCauseway(GameData data, IDiceRoller dice, Brains.IGmBrain? gm = null, bool openGround = false)
        {
            var ratkin = data.Adversary("Bramble Ratkin");
            var combatants = new List<Combatant>
            {
                CombatantFactory.FromCharacter(SallaRinehart(data), new GridPosition(5, 8)),
                CombatantFactory.FromCharacter(JorenBlacke(data), new GridPosition(5, 9)),
                CombatantFactory.FromCharacter(DrovanThale(data), new GridPosition(3, 7)),
                CombatantFactory.FromCharacter(MirellaVask(data), new GridPosition(3, 9)),
                CombatantFactory.FromCharacter(ElthricDane(data), new GridPosition(2, 8)),
                CombatantFactory.FromAdversary(data.Adversary("Fenmoor Ratswarm"), "Ratswarm", new GridPosition(14, 8)),
                CombatantFactory.FromAdversary(ratkin, "Ratkin Herder", new GridPosition(18, 7)),
                CombatantFactory.FromAdversary(ratkin, "Ratkin Biter", new GridPosition(18, 10)),
                CombatantFactory.FromAdversary(ratkin, "Ratkin Sneak", new GridPosition(19, 8)),
                CombatantFactory.FromAdversary(ratkin, "Ratkin Shiv", new GridPosition(19, 9)),
            };

            return new Encounter(FieldWidth, FieldHeight, combatants, data.RangeBands, dice, gm,
                setting: openGround ? Setting.Daylight with { Name = "Open ground" } : FenCauseway, environment: data.Environment);
        }

        /// <summary>
        /// A lord's champion — staged for the game, not taken from a book: the Quickstart's five on a
        /// village green, in daylight, against a Noble Champion (T3 Brute: DT 15, Medium armour, a
        /// great sword that can Topple, Unshakable, and Fitness to keep his Stamina coming) and the two
        /// retainers riding with him — a Tollroad Bandit and a Bandit Ambusher with a crossbow. Open
        /// ground, so the party can ring him; hard to hit, so it takes rounds — the fight a held Bane
        /// is for.
        /// </summary>
        public static Encounter ChampionOnTheGreen(GameData data, IDiceRoller dice, Brains.IGmBrain? gm = null)
        {
            var combatants = new List<Combatant>
            {
                CombatantFactory.FromCharacter(SallaRinehart(data), new GridPosition(6, 8)),
                CombatantFactory.FromCharacter(JorenBlacke(data), new GridPosition(6, 10)),
                CombatantFactory.FromCharacter(DrovanThale(data), new GridPosition(4, 12)),
                CombatantFactory.FromCharacter(MirellaVask(data), new GridPosition(4, 9)),
                CombatantFactory.FromCharacter(ElthricDane(data), new GridPosition(3, 7)),
                CombatantFactory.FromAdversary(data.Adversary("Noble Champion"), "Noble Champion", new GridPosition(15, 9)),
                CombatantFactory.FromAdversary(data.Adversary("Tollroad Bandit"), "Retainer with a cudgel", new GridPosition(16, 6)),
                CombatantFactory.FromAdversary(BanditAmbusher, "Retainer with a crossbow", new GridPosition(19, 11)),
            };

            return new Encounter(FieldWidth, FieldHeight, combatants, data.RangeBands, dice, gm,
                setting: Setting.Daylight with { Name = "The village green" }, environment: data.Environment);
        }

        /// <summary>
        /// A shrine's guardians — staged for the game, not taken from a book: a Stoneheart Golem (T2
        /// Brute, Large, Heavy armour, Reach 2) and two Animated Suits of Mail (DT 14), in a ruined
        /// cloister's open yard. Constructs have no Mind or Soul for a Smite, a Mind Spike or a Control
        /// to find, and nothing in them breaks and runs: the blades have to grind them down, which is
        /// where a held Curse on the one they're all hitting earns its Stamina.
        /// </summary>
        public static Encounter ShrineGuardians(GameData data, IDiceRoller dice, Brains.IGmBrain? gm = null)
        {
            var mail = data.Adversary("Animated Suit of Mail");
            var combatants = new List<Combatant>
            {
                CombatantFactory.FromCharacter(SallaRinehart(data), new GridPosition(6, 8)),
                CombatantFactory.FromCharacter(JorenBlacke(data), new GridPosition(6, 10)),
                CombatantFactory.FromCharacter(DrovanThale(data), new GridPosition(4, 12)),
                CombatantFactory.FromCharacter(MirellaVask(data), new GridPosition(4, 9)),
                CombatantFactory.FromCharacter(ElthricDane(data), new GridPosition(3, 7)),
                CombatantFactory.FromAdversary(data.Adversary("Stoneheart Golem"), "Stoneheart Golem", new GridPosition(16, 8)),
                CombatantFactory.FromAdversary(mail, "Suit of Mail by the gate", new GridPosition(14, 5)),
                CombatantFactory.FromAdversary(mail, "Suit of Mail by the well", new GridPosition(14, 13)),
            };

            return new Encounter(FieldWidth, FieldHeight, combatants, data.RangeBands, dice, gm,
                setting: Setting.Daylight with { Name = "The cloister yard" }, environment: data.Environment);
        }

        /// <summary>
        /// A T1 Solo, built for the game by GM Reference §3, "Sizing a Solo": Body 60, Mind and Soul
        /// 20, one Brute's attack (d8+1), Stamina 3 for a hard but fair capstone. Large, so it stands
        /// on four squares. Its area attack is rationed to every other round, at about a third of the
        /// single blow per head (§3, "An area attack is one attack").
        /// </summary>
        public static StartingAdversaryInfo HollowStag(GameData data) => data.Adversary("Cave Bear") with
        {
            Name = "The Hollow Stag",
            Role = AdversaryRole.Solo,
            Tier = Tier.T1,
            BodyMax = 60,
            MindMax = 20,
            SoulMax = 20,
            Stamina = 3,
            DodgeThreshold = 14,
            ResolveThreshold = 13,
            StabilityThreshold = 16,
            AttackName = "Antlers",
            AttackModifier = 3,
            DamageDiceCount = 1,
            DamageDie = "d8",
            DamageModifier = 1,
            Reach = 2,
            Size = AdversarySize.Large,
            Armour = ArmourWeight.Light,
            AttackEffect = ". One attack a round, whichever of its turns it is taken on",
            MoreAttacks = [new StartingAttackInfo("Stamping Fury", 3, 1, "d4",
                Effect: ". Every other round, and it is that round's attack, against every creature within 1 square")],
            Traits = [AdversaryTrait.SoloActions, AdversaryTrait.SoloResilience],
            Features = [],
            CreatureType = CreatureType.Beast,
            Identity = "A great stag gone hollow in the barrows, moss where its heart was.",
            Notes = "A T1 Solo staged for the game.",
        };

        /// <summary>
        /// The Hollow Stag in the birchwood — staged for the game: the Quickstart's five, in daylight
        /// among the birches, against one T1 Solo that takes two turns a round, one attack among them.
        /// </summary>
        public static Encounter HollowStagInTheBirchwood(GameData data, IDiceRoller dice, Brains.IGmBrain? gm = null)
        {
            var combatants = new List<Combatant>
            {
                CombatantFactory.FromCharacter(SallaRinehart(data), new GridPosition(6, 8)),
                CombatantFactory.FromCharacter(JorenBlacke(data), new GridPosition(6, 10)),
                CombatantFactory.FromCharacter(DrovanThale(data), new GridPosition(4, 12)),
                CombatantFactory.FromCharacter(MirellaVask(data), new GridPosition(4, 9)),
                CombatantFactory.FromCharacter(ElthricDane(data), new GridPosition(3, 7)),
                CombatantFactory.FromAdversary(HollowStag(data), "The Hollow Stag", new GridPosition(16, 8)),
            };

            return new Encounter(FieldWidth, FieldHeight, combatants, data.RangeBands, dice, gm,
                setting: Setting.Daylight with { Name = "The birchwood", Ground = Setting.Patches(Terrain.Broken, (10, 2, 13, 4), (10, 13, 13, 15)) },
                environment: data.Environment);
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
