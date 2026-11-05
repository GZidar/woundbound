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
            // A Light Crossbow at the ready (the user, 2026-11-06: missing from the sheet): "can I reach
            // them with the axe? If not, the crossbow" — a Power weapon, so it shoots on his Strength.
            Weapons = { CombatantFactory.InHand(data.Weapon("Battleaxe")), CombatantFactory.AtTheReady(data.Weapon("Light Crossbow")) },
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
        /// A Sensing spell built for the game from Core Rules §11, "Sensing": Aether, a Small Volume at
        /// Short. At T1 a bare yes or no — does anybody in the block carry active magic — and the cast
        /// roll against any Illusion there, to see it for the lie it is.
        /// </summary>
        public static SpellDefinition Witchsight => new()
        {
            Name = "Witchsight",
            Source = MagicSource.AetherMagic,
            SpellTier = Tier.T1,
            SpellType = SpellType.Utility,
            Target = SpellTarget.Many,
            Shape = SpellShape.Volume,
            Size = SpellSize.Small,
            Range = SpellRange.Short,
            Notes = "Sensing: the threads of active magic in the block show themselves to him, and a lie woven there frays under his look.",
        };

        /// <summary>
        /// A Scrying spell built for the game from Core Rules §11, "Scrying": Aether, a Small Volume as far
        /// as Long. Sight only at T1; it lasts the scene, anchored to its caster, and rolls against nothing.
        /// </summary>
        public static SpellDefinition FarEye => new()
        {
            Name = "Far Eye",
            Source = MagicSource.AetherMagic,
            SpellTier = Tier.T1,
            SpellType = SpellType.Utility,
            Target = SpellTarget.Many,
            Shape = SpellShape.Volume,
            Size = SpellSize.Small,
            Range = SpellRange.Long,
            Notes = "Scrying: he sees the place as it is now, as though standing in it - by sight alone.",
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
                data.Spell("Flame Burst"), data.Spell("Conflagration"), SettleTheGround, Witchsight, FarEye,
                data.Spell("Phantom Double"), data.Spell("Phantom Wound"), PhantomWall, data.Spell("Fog of Confusion"),
            },
        };

        /// <summary>A creature lying in wait for an ambush (Core Rules §9, "Ambush"): it may strike first, from hiding, if the party misses it.</summary>
        /// <param name="ambush">Whether the fight is staged as an ambush, rolled before round 1; off, it opens with both sides in the open.</param>
        private static Combatant Lurking(Combatant creature, bool ambush)
        {
            creature.LiesInWait = ambush;
            return creature;
        }

        /// <summary>
        /// Three characters — the Dumb Fighter, Salla and Drovan — ambushed on the road: a Tollroad
        /// Bandit, his Timber Wolf, and two Bramble Ratkin. 1.5 + 1 + 0.25 × 2 is a Threat Value
        /// of 3, a Standard fight's budget for a party of three (GM Reference §5), and the GM has
        /// three adversary turns a round for four creatures.
        /// </summary>
        public static Encounter PartyVersusAmbush(GameData data, IDiceRoller dice, Brains.IGmBrain? gm = null, bool ambush = false)
        {
            var ratkin = data.Adversary("Bramble Ratkin");

            var combatants = new List<Combatant>
            {
                CombatantFactory.FromCharacter(DumbFighter(data), new GridPosition(1, 4)),
                CombatantFactory.FromCharacter(SallaRinehart(data), new GridPosition(1, 2)),
                CombatantFactory.FromCharacter(DrovanThale(data), new GridPosition(0, 6)),
                CombatantFactory.FromAdversary(data.Adversary("Tollroad Bandit"), "Tollroad Bandit", new GridPosition(8, 4)),
                CombatantFactory.FromAdversary(data.Adversary("Timber Wolf"), "Timber Wolf", new GridPosition(8, 2)),
                Lurking(CombatantFactory.FromAdversary(ratkin, "Ratkin Biter", new GridPosition(7, 6)), ambush),
                Lurking(CombatantFactory.FromAdversary(ratkin, "Ratkin Shiv", new GridPosition(9, 7)), ambush),
            };

            return new Encounter(FieldWidth, FieldHeight, combatants, data.RangeBands, dice, gm);
        }

        /// <summary>
        /// The Quickstart Adventure's Roadside Bandit (Blood and Vintage, "Roadside Ambush"): the
        /// lantern-bearer at the signpost, and his like. Without the book's Cowardice here (see
        /// <see cref="CowardlyBandit"/>).
        /// </summary>
        public static StartingAdversaryInfo RoadsideBandit { get; } = QuickstartBandit("Roadside Bandit", "Rusty Sword", die: "d4", damageModifier: 2) with
        {
            // A Skirmisher with no ranged attack (GM Reference §3).
            Traits = [AdversaryTrait.HitAndFade],
        };

        /// <summary>
        /// The Quickstart Adventure's Bandit Ambusher: a Worn Crossbow (+2, 1d4+2), "or Rusty Knife"
        /// (+2, 1d4) for when somebody gets close — a secondary attack, without the damage bonus.
        /// </summary>
        public static StartingAdversaryInfo BanditAmbusher { get; } = QuickstartBandit("Bandit Ambusher", "Worn Crossbow", die: "d4", damageModifier: 2) with
        {
            MoreAttacks = [new StartingAttackInfo("Rusty Knife", Modifier: 2, DamageDiceCount: 1, DamageDie: "d4", DamageModifier: 0)],
        };

        private static StartingAdversaryInfo QuickstartBandit(string name, string attack, string die, int damageModifier) => new(
            ContentSource: "quickstart",
            Name: name,
            Role: AdversaryRole.Skirmisher,
            Tier: Tier.T1,
            BodyMax: 4,
            MindMax: 2,
            SoulMax: 2,
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
            // The book gives these bandits Cowardice so the first fight of the Quickstart can't end
            // in a wipe; the user's call (2026-11-04) is that it's too much of a weakness for a fight
            // the game uses as its test bed, so here they fight on. CowardlyBandit puts it back.
            Features: []);

        /// <summary>A Quickstart bandit as the book has him: Cowardice — below half his Body, or seeing one of his side go down, he looks to run.</summary>
        public static StartingAdversaryInfo CowardlyBandit(StartingAdversaryInfo bandit) => bandit with
        {
            Features =
            [
                new StartingFeatureInfo("Cowardice",
                    "reduced below half its Body pool, or watching another of its side go down, it looks for a chance to break and run rather than fight to the last wound."),
            ],
        };

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
        /// <remarks>
        /// The book now fields two bandits per player (each 0.5 Threat, 2026-11-06). This one keeps
        /// the old one-per-player count: it is the scripted-dice test bed for a small table, and a
        /// fight for the sim is <see cref="SignpostWithCasters"/>, which has the book's numbers.
        /// </remarks>
        /// <param name="cowardly">The bandits as the book has them, with Cowardice — for testing how foes break and run.</param>
        /// <param name="fourPlayers">
        /// The book's table of four: Joren Blacke joins the party, and the fourth bandit — a second
        /// Roadside Bandit, around the corner — joins the ambush ("If you have four players, then
        /// have two of each").
        /// </param>
        /// <param name="setting">Where to stage it: the book's night by default; <see cref="Setting.Daylight"/> stages it with no
        /// lights needed, and the Dumb Fighter's torch stays in his pack.</param>
        public static Encounter SignpostAmbush(GameData data, IDiceRoller dice, Brains.IGmBrain? gm = null, bool fourPlayers = false, Setting? setting = null, bool ambush = false, bool cowardly = false)
        {
            var roadside = cowardly ? CowardlyBandit(RoadsideBandit) : RoadsideBandit;
            var ambusher = cowardly ? CowardlyBandit(BanditAmbusher) : BanditAmbusher;
            setting ??= ambush ? SignpostAmbushNight : SignpostNight;
            var dy = ambush ? 6 : 0;
            var dark = setting.IsUnlit;
            var lanternBearer = CombatantFactory.FromAdversary(roadside, "Lantern-bearer", new GridPosition(6, 4 + dy));
            lanternBearer.StatblockLights = dark ? 1 : 0;

            var combatants = new List<Combatant>
            {
                CombatantFactory.FromCharacter(DumbFighter(data, torch: dark), new GridPosition(1, 4 + dy)),
                CombatantFactory.FromCharacter(SallaRinehart(data), new GridPosition(1, 3 + dy)),
                CombatantFactory.FromCharacter(DrovanThale(data), new GridPosition(1, 5 + dy)),
                lanternBearer,
                Lurking(CombatantFactory.FromAdversary(ambusher, "Ambusher on the ridge", ambush ? new GridPosition(3, 3) : new GridPosition(7, 1)), ambush),
                Lurking(CombatantFactory.FromAdversary(ambusher, "Ambusher in the trees", ambush ? new GridPosition(3, 16) : new GridPosition(7, 6)), ambush),
            };

            if (fourPlayers)
            {
                combatants.Insert(3, CombatantFactory.FromCharacter(JorenBlacke(data), new GridPosition(2, 4 + dy)));
                combatants.Add(CombatantFactory.FromAdversary(roadside, "Bandit round the corner", new GridPosition(11, 3 + dy)));
            }

            return new Encounter(FieldWidth, FieldHeight, combatants, data.RangeBands, dice, gm,
                objectives: [new Objective.DontLetEscape("Take the lantern-bearer — he can be made to talk", lanternBearer)],
                setting: setting, environment: data.Environment);
        }

        /// <summary>
        /// The signpost with the Quickstart's own party of five — Salla, Drovan, Joren, Mirella and
        /// Elthric, the two casters among them — against two Skirmishers each, as the book now has it
        /// (0.5 Threat apiece, 2026-11-06): the lantern-bearer and a second bandit at the signpost, two
        /// round the corner, and six Bandit Ambushers, two each on the ridge, in the trees and by the
        /// stones ("with multiple spellcasters two extra Bandit Ambushers"). At night Mirella's lantern
        /// and Elthric's torch are the party's lights.
        /// </summary>
        /// <param name="cowardly">The bandits as the book has them, with Cowardice.</param>
        public static Encounter SignpostWithCasters(GameData data, IDiceRoller dice, Brains.IGmBrain? gm = null, Setting? setting = null, bool ambush = false, bool cowardly = false)
        {
            var roadside = cowardly ? CowardlyBandit(RoadsideBandit) : RoadsideBandit;
            var ambusher = cowardly ? CowardlyBandit(BanditAmbusher) : BanditAmbusher;
            setting ??= ambush ? SignpostAmbushNight : SignpostNight;
            var dy = ambush ? 6 : 0;
            var dark = setting.IsUnlit;
            var lanternBearer = CombatantFactory.FromAdversary(roadside, "Lantern-bearer", new GridPosition(6, 4 + dy));
            lanternBearer.StatblockLights = dark ? 1 : 0;

            var combatants = new List<Combatant>
            {
                CombatantFactory.FromCharacter(SallaRinehart(data), new GridPosition(1, 3 + dy)),
                CombatantFactory.FromCharacter(DrovanThale(data), new GridPosition(1, 5 + dy)),
                CombatantFactory.FromCharacter(JorenBlacke(data), new GridPosition(2, 4 + dy)),
                CombatantFactory.FromCharacter(MirellaVask(data, lantern: dark), new GridPosition(0, 4 + dy)),
                CombatantFactory.FromCharacter(ElthricDane(data, torch: dark), new GridPosition(0, 3 + dy)),
                lanternBearer,
                CombatantFactory.FromAdversary(roadside, "Bandit at the signpost", new GridPosition(7, 4 + dy)),
                Lurking(CombatantFactory.FromAdversary(ambusher, "Ambusher on the ridge", ambush ? new GridPosition(3, 3) : new GridPosition(7, 1)), ambush),
                Lurking(CombatantFactory.FromAdversary(ambusher, "Second ambusher on the ridge", ambush ? new GridPosition(5, 3) : new GridPosition(8, 1)), ambush),
                Lurking(CombatantFactory.FromAdversary(ambusher, "Ambusher in the trees", ambush ? new GridPosition(3, 16) : new GridPosition(7, 6)), ambush),
                Lurking(CombatantFactory.FromAdversary(ambusher, "Second ambusher in the trees", ambush ? new GridPosition(5, 16) : new GridPosition(8, 6)), ambush),
                Lurking(CombatantFactory.FromAdversary(ambusher, "Ambusher by the stones", new GridPosition(9, 5 + dy)), ambush),
                Lurking(CombatantFactory.FromAdversary(ambusher, "Second ambusher by the stones", new GridPosition(10, 5 + dy)), ambush),
                CombatantFactory.FromAdversary(roadside, "Bandit round the corner", new GridPosition(11, 3 + dy)),
                CombatantFactory.FromAdversary(roadside, "Second bandit round the corner", new GridPosition(12, 3 + dy)),
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
        /// The signpost as the ambush is laid (the user's staging, 2026-11-03): the same night, the road
        /// six rows further south, with bushes on the ridge to the north and in the trees to the south,
        /// each between a crossbowman and the road at Middle range — partial cover, in the dark — and
        /// a stone east down the road for the third.
        /// </summary>
        public static Setting SignpostAmbushNight { get; } = SignpostNight with
        {
            Name = "The signpost at night, the ambush laid",
            Ground = Setting.Patches(Terrain.Broken, (6, 6, 9, 7), (6, 13, 9, 14)),
            CoverPieces = Setting.CoverOf(Cover.Partial, (2, 4, 4, 4), (2, 15, 4, 15), (8, 11, 8, 11)),
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
        public static Encounter FerryShrineAmbush(GameData data, IDiceRoller dice, Brains.IGmBrain? gm = null, bool ambush = false)
        {
            var skulker = data.Adversary("Reedmarsh Skulker");
            var combatants = new List<Combatant>
            {
                CombatantFactory.FromCharacter(DumbFighter(data), new GridPosition(12, 8)),
                CombatantFactory.FromCharacter(SallaRinehart(data), new GridPosition(13, 7)),
                CombatantFactory.FromCharacter(DrovanThale(data), new GridPosition(11, 9)),
                Lurking(CombatantFactory.FromAdversary(skulker, "Skulker by the arch", new GridPosition(19, 5)), ambush),
                Lurking(CombatantFactory.FromAdversary(skulker, "Skulker in the reeds", new GridPosition(20, 11)), ambush),
                Lurking(CombatantFactory.FromAdversary(BanditAmbusher, "Ambusher by the hut", new GridPosition(23, 6)), ambush),
            };

            return new Encounter(FieldWidth, FieldHeight, combatants, data.RangeBands, dice, gm,
                setting: LongreachCrossing, environment: data.Environment);
        }

        /// <summary>
        /// The Ferry Shrine ambush with the Quickstart's own five caught mid-ford — Salla, Drovan,
        /// Joren, Mirella and Elthric — against two Skirmishers each: six Reedmarsh Skulkers and four
        /// Bandit Ambushers, a Standard Challenge (GM Reference §5). The Skulkers have no Cowardice, so this one is fought out.
        /// </summary>
        public static Encounter FerryShrineWithCasters(GameData data, IDiceRoller dice, Brains.IGmBrain? gm = null, bool ambush = false)
        {
            var skulker = data.Adversary("Reedmarsh Skulker");
            var combatants = new List<Combatant>
            {
                CombatantFactory.FromCharacter(SallaRinehart(data), new GridPosition(13, 7)),
                CombatantFactory.FromCharacter(JorenBlacke(data), new GridPosition(14, 8)),
                CombatantFactory.FromCharacter(DrovanThale(data), new GridPosition(12, 9)),
                CombatantFactory.FromCharacter(MirellaVask(data), new GridPosition(12, 8)),
                CombatantFactory.FromCharacter(ElthricDane(data), new GridPosition(11, 7)),
                Lurking(CombatantFactory.FromAdversary(skulker, "Skulker by the arch", new GridPosition(19, 5)), ambush),
                Lurking(CombatantFactory.FromAdversary(skulker, "Skulker in the reeds", new GridPosition(20, 11)), ambush),
                Lurking(CombatantFactory.FromAdversary(skulker, "Skulker on the bank", new GridPosition(17, 13)), ambush),
                Lurking(CombatantFactory.FromAdversary(BanditAmbusher, "Ambusher by the hut", new GridPosition(23, 6)), ambush),
                Lurking(CombatantFactory.FromAdversary(BanditAmbusher, "Ambusher behind the arch", new GridPosition(21, 9)), ambush),
                Lurking(CombatantFactory.FromAdversary(skulker, "Skulker under the bank", new GridPosition(18, 3)), ambush),
                Lurking(CombatantFactory.FromAdversary(skulker, "Skulker in the shallows", new GridPosition(19, 14)), ambush),
                Lurking(CombatantFactory.FromAdversary(skulker, "Skulker by the post", new GridPosition(21, 12)), ambush),
                Lurking(CombatantFactory.FromAdversary(BanditAmbusher, "Ambusher on the hut roof", new GridPosition(24, 8)), ambush),
                Lurking(CombatantFactory.FromAdversary(BanditAmbusher, "Ambusher by the boats", new GridPosition(22, 4)), ambush),
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
        /// his brother, their three Timber Wolves, and three Bandit Ambushers on the rough ground to
        /// either side — a Standard fight's budget for five (GM Reference §5). Elthric sits by the fire.
        /// </summary>
        public static Encounter CampfireAmbush(GameData data, IDiceRoller dice, Brains.IGmBrain? gm = null, bool ambush = false)
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
                Lurking(CombatantFactory.FromAdversary(BanditAmbusher, "Ambusher in the rocks", new GridPosition(17, 3)), ambush),
                Lurking(CombatantFactory.FromAdversary(BanditAmbusher, "Ambusher in the gully", new GridPosition(17, 15)), ambush),
                CombatantFactory.FromAdversary(data.Adversary("Tollroad Bandit"), "Second Tollroad Bandit", new GridPosition(21, 11)),
                CombatantFactory.FromAdversary(wolf, "Wolf from the east", new GridPosition(19, 9)),
                Lurking(CombatantFactory.FromAdversary(BanditAmbusher, "Ambusher by the trees", new GridPosition(18, 1)), ambush),
            };

            return new Encounter(FieldWidth, FieldHeight, combatants, data.RangeBands, dice, gm,
                setting: CampAtNight, environment: data.Environment);
        }

        /// <summary>
        /// The hedge-witches' toll — staged for the game, not taken from a book: the Quickstart's five
        /// stopped on a daylight road by a Tollroad Bandit and his three Timber Wolves, with three casters
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
                Foe("Timber Wolf", "Second wolf", 12, 6),
                Foe("Timber Wolf", "Third wolf", 12, 12),
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
        /// <paramref name="count"/> fields only the first so many of them, for pricing a rival built
        /// from a character sheet (GM Reference §5).
        /// </summary>
        public static Encounter RivalBand(GameData data, IDiceRoller dice, Brains.IGmBrain? gm = null, int count = 5)
        {
            Combatant Rival(CharacterBuild build, int x, int y) => CombatantFactory.FromCharacter(build, new GridPosition(x, y), Side.Adversaries);

            var combatants = new List<Combatant>
            {
                CombatantFactory.FromCharacter(SallaRinehart(data), new GridPosition(3, 7)),
                CombatantFactory.FromCharacter(JorenBlacke(data), new GridPosition(4, 9)),
                CombatantFactory.FromCharacter(DrovanThale(data), new GridPosition(2, 11)),
                CombatantFactory.FromCharacter(MirellaVask(data), new GridPosition(2, 9)),
                CombatantFactory.FromCharacter(ElthricDane(data), new GridPosition(1, 8)),
            };
            combatants.AddRange(new[]
            {
                Rival(BrannocVey(data), 13, 8),
                Rival(HeskMarrow(data), 13, 10),
                Rival(IlseVarrow(data), 14, 6),
                Rival(AldousCrane(data), 16, 9),
                Rival(SedgeOfTheFen(data), 16, 11),
            }.Take(count));

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
            // Rapier and dagger, as Salla fights: the off-hand strike is the GM Opportunity's for a rival.
            Weapons = { CombatantFactory.InHand(data.Weapon("Rapier")), CombatantFactory.InHand(data.Weapon("Dagger")) },
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
        /// The Quickstart's five met by two Fenmoor Ratswarms and the eight Bramble Ratkin who herd them —
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
                CombatantFactory.FromAdversary(data.Adversary("Fenmoor Ratswarm"), "Second ratswarm", new GridPosition(16, 10)),
                CombatantFactory.FromAdversary(ratkin, "Ratkin Nipper", new GridPosition(20, 7)),
                CombatantFactory.FromAdversary(ratkin, "Ratkin Gnawer", new GridPosition(20, 10)),
                CombatantFactory.FromAdversary(ratkin, "Ratkin Scab", new GridPosition(21, 8)),
                CombatantFactory.FromAdversary(ratkin, "Ratkin Tail", new GridPosition(21, 9)),
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
        /// is for. His Opportunity bonus is the game's own: Cleave, for a great sword swung through two
        /// of the party who crowd him (GM Reference §9: "Cleave when two characters stand within reach
        /// of the same blade"). The retainer's crossbow is aimed: Precise.
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
                CombatantFactory.FromAdversary(data.Adversary("Noble Champion") with { OpportunityText = "Cleave" }, "Noble Champion", new GridPosition(15, 9)),
                CombatantFactory.FromAdversary(data.Adversary("Tollroad Bandit"), "Retainer with a cudgel", new GridPosition(16, 6)),
                CombatantFactory.FromAdversary(BanditAmbusher with { OpportunityText = "Precise" }, "Retainer with a crossbow", new GridPosition(19, 11)),
            };

            return new Encounter(FieldWidth, FieldHeight, combatants, data.RangeBands, dice, gm,
                setting: Setting.Daylight with { Name = "The village green" }, environment: data.Environment);
        }

        /// <summary>
        /// A glamourer of the fen, built for the game on the Bog Hex-Caller's frame (GM Reference §9, "On
        /// Casters"): a T1 Adept, Caster - Aether (+2), who puts up a Phantom Double of itself and fogs
        /// minds with Fog of Confusion. The statblock Illusion caster.
        /// </summary>
        public static StartingAdversaryInfo FenGlamourer(GameData data) => data.Adversary("Bog Hex-Caller") with
        {
            Name = "Fen Glamourer",
            Role = AdversaryRole.Adept,
            Tier = Tier.T1,
            AttackName = "None",
            Notes = "A hedge-glamourer: what it shows you is never quite where it stands.",
            Casting = new AdversaryCasting { Source = MagicSource.AetherMagic, Bonus = 2 },
            Spells = [new StartingSpellLineInfo("Phantom Double", "Illusion (Sight): its own likeness", "1 Mind"),
                new StartingSpellLineInfo("Fog of Confusion", "Control - Confusion over a block", "1 Mind")],
        };

        /// <summary>
        /// The glamoured mill — staged for the game: the Quickstart's five at an old mill against a
        /// Tollroad Bandit, the miller's man (another), two Timber Wolves, two Vampire Bats out of the mill's dark (their Opportunity
        /// bonus is Direct damage), and a Fen Glamourer behind them putting up doubles of itself.
        /// Daylight, open ground.
        /// </summary>
        public static Encounter GlamouredMill(GameData data, IDiceRoller dice, Brains.IGmBrain? gm = null)
        {
            Combatant Foe(StartingAdversaryInfo info, string name, int x, int y) =>
                CombatantFactory.FromAdversary(info, name, new GridPosition(x, y), data.Spells);

            var combatants = new List<Combatant>
            {
                CombatantFactory.FromCharacter(SallaRinehart(data), new GridPosition(6, 8)),
                CombatantFactory.FromCharacter(JorenBlacke(data), new GridPosition(6, 10)),
                CombatantFactory.FromCharacter(DrovanThale(data), new GridPosition(4, 12)),
                CombatantFactory.FromCharacter(MirellaVask(data), new GridPosition(4, 9)),
                CombatantFactory.FromCharacter(ElthricDane(data), new GridPosition(3, 7)),
                Foe(data.Adversary("Tollroad Bandit"), "Tollroad Bandit", 14, 8),
                Foe(data.Adversary("Timber Wolf"), "Timber Wolf", 14, 10),
                Foe(data.Adversary("Vampire Bat"), "First bat", 13, 6),
                Foe(data.Adversary("Vampire Bat"), "Second bat", 13, 12),
                Foe(data.Adversary("Timber Wolf"), "Second wolf", 15, 12),
                Foe(data.Adversary("Tollroad Bandit"), "Miller's man", 16, 9),
                Foe(FenGlamourer(data), "Fen Glamourer", 18, 9),
            };

            return new Encounter(FieldWidth, FieldHeight, combatants, data.RangeBands, dice, gm,
                setting: Setting.Daylight with { Name = "The old mill" }, environment: data.Environment);
        }

        /// <summary>
        /// A clearing in the woods at dusk — a random encounter, nobody's set piece: trees to stand
        /// behind (full cover), lines of bushes (partial), a scree of broken rock, and the party's own
        /// lights against the gloom. The band met there — four Reedmarsh Skulkers, four Timber Wolves and
        /// four Bramble Ratkin, none of them quick to break, a Standard Challenge for five (GM Reference §5) — is placed by the GM's
        /// setup (<see cref="Brains.Placement"/>), anywhere from Short to Long of the party, lying in
        /// wait when <paramref name="prepared"/>.
        /// </summary>
        public static Encounter WoodlandClearing(GameData data, IDiceRoller dice, Brains.IGmBrain? gm = null,
            Brains.SetupSkill setup = Brains.SetupSkill.Smart, bool prepared = true, int seed = 7)
        {
            var party = new List<Combatant>
            {
                CombatantFactory.FromCharacter(SallaRinehart(data), new GridPosition(12, 8)),
                CombatantFactory.FromCharacter(JorenBlacke(data), new GridPosition(12, 10)),
                CombatantFactory.FromCharacter(DrovanThale(data), new GridPosition(11, 9)),
                CombatantFactory.FromCharacter(MirellaVask(data, lantern: true), new GridPosition(10, 8)),
                CombatantFactory.FromCharacter(ElthricDane(data, torch: true), new GridPosition(10, 10)),
            };
            var band = new List<Combatant>
            {
                CombatantFactory.FromAdversary(data.Adversary("Reedmarsh Skulker"), "First skulker", new GridPosition(0, 0)),
                CombatantFactory.FromAdversary(data.Adversary("Reedmarsh Skulker"), "Second skulker", new GridPosition(0, 1)),
                CombatantFactory.FromAdversary(data.Adversary("Timber Wolf"), "First wolf", new GridPosition(0, 2)),
                CombatantFactory.FromAdversary(data.Adversary("Timber Wolf"), "Second wolf", new GridPosition(0, 3)),
                CombatantFactory.FromAdversary(data.Adversary("Bramble Ratkin"), "Ratkin Biter", new GridPosition(0, 4)),
                CombatantFactory.FromAdversary(data.Adversary("Bramble Ratkin"), "Ratkin Shiv", new GridPosition(0, 5)),
                CombatantFactory.FromAdversary(data.Adversary("Reedmarsh Skulker"), "Third skulker", new GridPosition(0, 6)),
                CombatantFactory.FromAdversary(data.Adversary("Reedmarsh Skulker"), "Fourth skulker", new GridPosition(0, 7)),
                CombatantFactory.FromAdversary(data.Adversary("Timber Wolf"), "Third wolf", new GridPosition(0, 8)),
                CombatantFactory.FromAdversary(data.Adversary("Timber Wolf"), "Fourth wolf", new GridPosition(0, 9)),
                CombatantFactory.FromAdversary(data.Adversary("Bramble Ratkin"), "Ratkin Sneak", new GridPosition(0, 10)),
                CombatantFactory.FromAdversary(data.Adversary("Bramble Ratkin"), "Ratkin Nipper", new GridPosition(0, 11)),
            };

            Brains.Placement.Place(band, party, WoodlandAtDusk, data.Environment, data.RangeBands, FieldWidth, FieldHeight,
                Brains.Placement.ZoneAround(party, FieldWidth, FieldHeight, minimum: 3, maximum: 12), setup, prepared, new Random(seed));
            return new Encounter(FieldWidth, FieldHeight, [.. party, .. band], data.RangeBands, dice, gm,
                setting: WoodlandAtDusk, environment: data.Environment);
        }

        /// <summary>The clearing: dusk under the trees, so a light is what anybody is seen by.</summary>
        public static Setting WoodlandAtDusk { get; } = new()
        {
            Name = "A clearing at dusk",
            Visibility = Visibility.Poor,
            Cause = VisibilityCause.Unlit,
            Ground = Setting.Patches(Terrain.Broken, (18, 13, 21, 15)),
            CoverPieces = Merge(
                Setting.CoverOf(Cover.Full, (4, 3, 4, 3), (7, 14, 7, 14), (16, 4, 16, 4), (19, 9, 19, 9), (22, 3, 22, 3), (15, 15, 15, 15), (6, 8, 6, 8), (24, 12, 24, 12)),
                Setting.CoverOf(Cover.Partial, (15, 6, 17, 6), (15, 12, 17, 12), (6, 4, 8, 4), (6, 13, 8, 13), (21, 8, 21, 10))),
        };

        private static Dictionary<GridPosition, Cover> Merge(params Dictionary<GridPosition, Cover>[] maps) =>
            maps.SelectMany(m => m).GroupBy(kv => kv.Key).ToDictionary(g => g.Key, g => g.Max(kv => kv.Value));

        /// <summary>
        /// A gnoll following a rout — staged for the game: a Duskfang Gnoll (T2 Brute, GM Reference
        /// §11, Rampage) lying in wait in the brush five squares off the road, behind a Tollroad Bandit
        /// and two Roadside Bandits, in daylight. Rampage costs the party nothing until somebody drops, and then a great deal.
        /// </summary>
        public static Encounter GnollRaid(GameData data, IDiceRoller dice, Brains.IGmBrain? gm = null, bool ambush = false)
        {
            var combatants = new List<Combatant>
            {
                CombatantFactory.FromCharacter(SallaRinehart(data), new GridPosition(6, 8)),
                CombatantFactory.FromCharacter(JorenBlacke(data), new GridPosition(6, 10)),
                CombatantFactory.FromCharacter(DrovanThale(data), new GridPosition(4, 12)),
                CombatantFactory.FromCharacter(MirellaVask(data), new GridPosition(4, 9)),
                CombatantFactory.FromCharacter(ElthricDane(data), new GridPosition(3, 7)),
                Lurking(CombatantFactory.FromAdversary(data.Adversary("Duskfang Gnoll"), "Duskfang Gnoll", new GridPosition(11, 9)), ambush),
                CombatantFactory.FromAdversary(data.Adversary("Tollroad Bandit"), "Tollroad Bandit", new GridPosition(15, 7)),
                CombatantFactory.FromAdversary(RoadsideBandit, "First bandit", new GridPosition(15, 11)),
                CombatantFactory.FromAdversary(RoadsideBandit, "Second bandit", new GridPosition(16, 13)),
            };

            return new Encounter(FieldWidth, FieldHeight, combatants, data.RangeBands, dice, gm,
                setting: Setting.Daylight with { Name = "The rout's tail" }, environment: data.Environment);
        }

        /// <summary>
        /// A roadblock — staged for the game to show cover (Core Rules §9, "Cover"): two Ambushers
        /// with crossbows crouched behind stacked crates (full cover; they lean out to shoot, then
        /// pay to duck back), two Roadside Bandits behind overturned carts (partial), and the Tollroad
        /// Bandit at the back. The party comes up the road behind a broken field wall of its own
        /// (partial), with a gap in it. Daylight. Four of each behind crates and carts (GM Reference §5)
        /// (GM Reference §5), a Standard Challenge for five.
        /// </summary>
        public static Encounter Roadblock(GameData data, IDiceRoller dice, Brains.IGmBrain? gm = null, bool ambush = false)
        {
            var combatants = new List<Combatant>
            {
                CombatantFactory.FromCharacter(SallaRinehart(data), new GridPosition(6, 8)),
                CombatantFactory.FromCharacter(JorenBlacke(data), new GridPosition(6, 10)),
                CombatantFactory.FromCharacter(DrovanThale(data), new GridPosition(4, 12)),
                CombatantFactory.FromCharacter(MirellaVask(data), new GridPosition(4, 9)),
                CombatantFactory.FromCharacter(ElthricDane(data), new GridPosition(3, 7)),
                Lurking(CombatantFactory.FromAdversary(BanditAmbusher, "Ambusher behind the crates", new GridPosition(18, 8)), ambush),
                Lurking(CombatantFactory.FromAdversary(BanditAmbusher, "Second ambusher", new GridPosition(18, 11)), ambush),
                CombatantFactory.FromAdversary(RoadsideBandit, "Bandit at the north cart", new GridPosition(18, 5)),
                CombatantFactory.FromAdversary(RoadsideBandit, "Bandit at the south cart", new GridPosition(18, 14)),
                CombatantFactory.FromAdversary(data.Adversary("Tollroad Bandit"), "Tollroad Bandit", new GridPosition(21, 9)),
                Lurking(CombatantFactory.FromAdversary(BanditAmbusher, "Third ambusher", new GridPosition(18, 9)), ambush),
                Lurking(CombatantFactory.FromAdversary(BanditAmbusher, "Fourth ambusher", new GridPosition(18, 10)), ambush),
                CombatantFactory.FromAdversary(RoadsideBandit, "Second bandit at the north cart", new GridPosition(18, 4)),
                CombatantFactory.FromAdversary(RoadsideBandit, "Second bandit at the south cart", new GridPosition(18, 15)),
            };

            var cover = Setting.CoverOf(Cover.Full, (17, 7, 17, 9), (17, 10, 17, 12));
            foreach (var (square, grade) in Setting.CoverOf(Cover.Partial, (17, 4, 17, 6), (17, 13, 17, 15), (9, 6, 9, 8), (9, 10, 9, 12)))
            {
                cover[square] = grade;
            }

            return new Encounter(FieldWidth, FieldHeight, combatants, data.RangeBands, dice, gm,
                setting: Setting.Daylight with { Name = "The roadblock", CoverPieces = cover }, environment: data.Environment);
        }

        /// <summary>
        /// Outriders on the road — staged for the game: four Tollroad Bandits on
        /// riding horses (Core Rules §9, "Mounts": Fast, a horse's hooves to borrow, a horse to strike
        /// instead), with two Ambushers and their crossbows on foot behind. Daylight, open ground. The
        /// outriders' Opportunity bonus is the game's own: extra movement, a horse's burst of speed.
        /// </summary>
        public static Encounter Outriders(GameData data, IDiceRoller dice, Brains.IGmBrain? gm = null)
        {
            var rider = data.Adversary("Tollroad Bandit") with { OpportunityText = "Extra movement" };
            var combatants = new List<Combatant>
            {
                CombatantFactory.FromCharacter(SallaRinehart(data), new GridPosition(6, 8)),
                CombatantFactory.FromCharacter(JorenBlacke(data), new GridPosition(6, 10)),
                CombatantFactory.FromCharacter(DrovanThale(data), new GridPosition(4, 12)),
                CombatantFactory.FromCharacter(MirellaVask(data), new GridPosition(4, 9)),
                CombatantFactory.FromCharacter(ElthricDane(data), new GridPosition(3, 7)),
                CombatantFactory.Mounted(CombatantFactory.FromAdversary(rider, "Mounted Tollroad Bandit", new GridPosition(15, 9))),
                CombatantFactory.Mounted(CombatantFactory.FromAdversary(rider, "Outrider", new GridPosition(16, 5))),
                CombatantFactory.Mounted(CombatantFactory.FromAdversary(rider, "Second outrider", new GridPosition(16, 13))),
                CombatantFactory.FromAdversary(BanditAmbusher, "Ambusher on foot", new GridPosition(18, 9)),
                CombatantFactory.Mounted(CombatantFactory.FromAdversary(rider, "Third outrider", new GridPosition(18, 2))),
                CombatantFactory.FromAdversary(BanditAmbusher, "Second ambusher on foot", new GridPosition(19, 12)),
            };

            return new Encounter(FieldWidth, FieldHeight, combatants, data.RangeBands, dice, gm,
                setting: Setting.Daylight with { Name = "The open road" }, environment: data.Environment);
        }

        /// <summary>
        /// A bench for pricing one statblock: the Quickstart's five on open ground in daylight against
        /// <paramref name="count"/> of <paramref name="foe"/>, drawn up in ranks twelve squares off —
        /// nothing in the place to favour either side, so what the fight costs the party is what the
        /// creatures are worth (GM Reference §5, Threat Value).
        /// </summary>
        public static Encounter Bench(GameData data, IDiceRoller dice, Brains.IGmBrain? gm, StartingAdversaryInfo foe, int count,
            StartingAdversaryInfo? second = null, int secondCount = 0, bool cover = false)
        {
            // With cover: the clearing's trees, bushes and scree, in daylight — the same field with
            // somewhere to hide, to tell what open ground does to a price from what the creature does.
            var setting = cover
                ? WoodlandAtDusk with { Name = "Open ground with cover", Visibility = Setting.Daylight.Visibility, Cause = Setting.Daylight.Cause }
                : Setting.Daylight with { Name = "Open ground" };
            var combatants = new List<Combatant>
            {
                CombatantFactory.FromCharacter(SallaRinehart(data), new GridPosition(6, 8)),
                CombatantFactory.FromCharacter(JorenBlacke(data), new GridPosition(6, 10)),
                CombatantFactory.FromCharacter(DrovanThale(data), new GridPosition(4, 12)),
                CombatantFactory.FromCharacter(MirellaVask(data), new GridPosition(4, 9)),
                CombatantFactory.FromCharacter(ElthricDane(data), new GridPosition(3, 7)),
            };

            // A second statblock, where there is one, drawn up in front of the first.
            var bands = second is null ? [(foe, count)] : new[] { (second, secondCount), (foe, count) };
            var x = 18;
            foreach (var (kind, many) in bands)
            {
                var step = Footprints.AcrossFor(kind.Size) + 1;
                var perRank = Math.Max(1, (FieldHeight - 2) / step);
                var placed = 0;
                for (var i = 0; placed < many; i++)
                {
                    var (rank, file) = (i / perRank, i % perRank);
                    var at = new GridPosition(x + (rank * step), 1 + (file * step));
                    if (setting.CoverPieces.ContainsKey(at))
                    {
                        continue;  // nobody starts in a tree
                    }

                    combatants.Add(CombatantFactory.FromAdversary(kind, $"{kind.Name} {placed + 1}", at, data.Spells));
                    placed++;
                }

                x += ((many + perRank - 1) / perRank) * step;
            }

            return new Encounter(FieldWidth, FieldHeight, combatants, data.RangeBands, dice, gm,
                setting: setting, environment: data.Environment);
        }

        /// <summary>
        /// A shrine's guardians — staged for the game, not taken from a book: a Stoneheart Golem (T2
        /// Brute, Large, Heavy armour, Reach 2) and two Animated Suits of Mail (DT 14), in a ruined
        /// cloister's open yard. Constructs have no Mind or Soul for a Smite, a Mind Spike or a Control
        /// to find, and nothing in them breaks and runs: the blades have to grind them down, which is
        /// where a held Curse on the one they're all hitting earns its Stamina. The Golem's Opportunity
        /// bonus is the game's own: a Threshold boost, stone settling against the blows.
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
                CombatantFactory.FromAdversary(data.Adversary("Stoneheart Golem") with { OpportunityText = "Threshold boost" }, "Stoneheart Golem", new GridPosition(16, 8)),
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
        /// <param name="asMinions">
        /// False plays the ratkin as Brutes: Minions take one turn between them as a mob (Core
        /// Rules §9), so a fight that wants one ratkin a turn, a crowd under the turn cap, can't use
        /// them as written.
        /// </param>
        public static Encounter FighterVersusRatkin(GameData data, IDiceRoller dice, Brains.IGmBrain? gm = null, bool asMinions = true)
        {
            var ratkin = data.Adversary("Bramble Ratkin");
            if (!asMinions)
            {
                ratkin = ratkin with { Role = AdversaryRole.Brute };
            }

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
