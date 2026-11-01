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
