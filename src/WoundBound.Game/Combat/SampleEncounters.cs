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

        /// <summary>One fighter against a pack of three Bramble Ratkin: Minions, so every hit that gets past their (lack of) armour drops one.</summary>
        public static Encounter FighterVersusRatkin(GameData data, IDiceRoller dice)
        {
            var ratkin = data.Adversary("Bramble Ratkin");

            var combatants = new List<Combatant>
            {
                CombatantFactory.FromCharacter(DumbFighter(data), new GridPosition(1, 4)),
                CombatantFactory.FromAdversary(ratkin, "Ratkin Biter", new GridPosition(7, 2)),
                CombatantFactory.FromAdversary(ratkin, "Ratkin Sneak", new GridPosition(8, 4)),
                CombatantFactory.FromAdversary(ratkin, "Ratkin Shiv", new GridPosition(7, 6)),
            };

            return new Encounter(BoardWidth, BoardHeight, combatants, data.RangeBands, dice);
        }
    }
}
