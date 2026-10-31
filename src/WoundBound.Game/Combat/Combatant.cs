using WoundBound.Rules.Calculation;
using WoundBound.Rules.Enums;

namespace WoundBound.Game.Combat
{
    public enum Side
    {
        Party,
        Adversaries
    }

    /// <summary>
    /// One creature on the board, with the handful of numbers a fight reads. Every one of them is
    /// derived by <see cref="CombatantFactory"/> from the Rules library — a character's from its
    /// <see cref="Rules.Models.CharacterBuild"/>, a generic foe's from its statblock — so nothing
    /// here recomputes a formula.
    /// </summary>
    public sealed class Combatant
    {
        public required string Name { get; init; }

        public required Side Side { get; init; }

        public GridPosition Position { get; internal set; }

        public required int BodyMax { get; init; }

        public int Body { get; internal set; }

        /// <summary>Stabilized at 0 Body (Core Rules §4). Only ever true of a player character.</summary>
        public bool IsStable { get; internal set; }

        public required int DodgeThreshold { get; init; }

        public required string AttackName { get; init; }

        public required int AttackModifier { get; init; }

        public required int DamageDiceCount { get; init; }

        public required int DamageDieSides { get; init; }

        public required int DamageModifier { get; init; }

        public required ArmourWeight Armour { get; init; }

        public required int Reach { get; init; }

        public Speed Speed { get; init; } = Speed.Normal;

        /// <summary>
        /// GM Reference §3, "Minion Rule": any Body hit that gets past its armour removes it from
        /// the fight outright, whatever was rolled.
        /// </summary>
        public bool IsMinion { get; init; }

        public required int StaminaMax { get; init; }

        public int Stamina { get; internal set; }

        public required int TacticalSpendsPerRound { get; init; }

        public int TacticalSpendsThisRound { get; internal set; }

        /// <summary>Started a turn on 0 Stamina (Core Rules §6).</summary>
        public bool IsUnconscious { get; internal set; }

        /// <summary>The turn's one move, still unspent.</summary>
        public bool HasMove { get; internal set; }

        /// <summary>Actions still to take this turn: the baseline one plus any bought with Stamina.</summary>
        public int ActionsRemaining { get; internal set; }

        public BodyStatus BodyStatus => PlayStateCalculator.GetBodyStatus(Body, IsStable);

        /// <summary>
        /// Whether this creature is still fighting. A generic foe is out at 0 Body with no dying
        /// state (GM Reference §2); a character at 0 is Dying, which the slice treats as out of the
        /// fight since nobody is left to stabilise them.
        /// </summary>
        public bool IsOutOfFight => IsUnconscious || (Side == Side.Party ? BodyStatus != BodyStatus.Healthy : Body <= 0);

        public bool CanReach(GridPosition square) => Position.DistanceTo(square) <= Reach;
    }
}
