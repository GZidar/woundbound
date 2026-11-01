using WoundBound.Game.Data;
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
        /// The Dumb Fighter of Core Rules §12 — the same build the Rules tests check against the
        /// book's worked numbers — in light armour, longsword in hand and a short bow slung at the
        /// ready for whatever he can't walk up to.
        /// </summary>
        public static CharacterBuild DumbFighter(GameData data) => new()
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

            return new Encounter(BoardWidth, BoardHeight, combatants, data.RangeBands, dice, gm);
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
