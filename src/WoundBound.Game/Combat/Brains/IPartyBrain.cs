using WoundBound.Rules.Enums;

namespace WoundBound.Game.Combat.Brains
{
    /// <summary>
    /// A bot at the player's seat: chooses what a party member does, where a person would click.
    ///
    /// <para>Its choices are carried out by <see cref="BotDriver"/> through exactly the commands the
    /// battle screen's buttons call (<see cref="Encounter.TryMove"/>, <see cref="Encounter.TryAttack"/>
    /// and the rest), so a bot can do nothing a player couldn't. A choice the rules refuse throws:
    /// it's a bug in the brain.</para>
    /// </summary>
    public interface IPartyBrain
    {
        /// <summary>A short name for reports: "Charge", "Bow first".</summary>
        string Name { get; }

        /// <summary>
        /// Which of <paramref name="stillToAct"/> takes the next turn (Core Rules §9, "Players
        /// choose their own order"). Asked only when there are at least two to choose between,
        /// all of them still fighting. Must answer one of them.
        /// </summary>
        Combatant ChooseNextCharacter(BattleView view, IReadOnlyList<Combatant> stillToAct);

        /// <summary>What <paramref name="character"/> does next on their turn. Asked until it answers <see cref="PartyStep.End"/>.</summary>
        PartyStep NextStep(BattleView view, Combatant character);

        /// <summary>
        /// Whether to spend 1 Stamina on a panic dodge against <paramref name="reaction"/>. Only
        /// asked when the dodge would turn the hit and the character could pay for it.
        /// </summary>
        bool PanicDodges(BattleView view, PendingReaction reaction);
    }

    /// <summary>One thing a party member does on their turn — one click.</summary>
    public abstract record PartyStep
    {
        private PartyStep()
        {
        }

        public sealed record Move(GridPosition Square) : PartyStep;

        public sealed record Attack(Combatant Target) : PartyStep;

        /// <summary>Swap the loadout in hand for the one at the ready.</summary>
        public sealed record Swap : PartyStep;

        /// <summary>Spend 1 Stamina on an extra action.</summary>
        public sealed record BuyExtraAction : PartyStep;

        /// <summary>Take Dodge, Grounded or Resolute.</summary>
        public sealed record StandGround(CombatAction Action) : PartyStep;

        public sealed record EndTurn : PartyStep;

        public static PartyStep End { get; } = new EndTurn();
    }
}
