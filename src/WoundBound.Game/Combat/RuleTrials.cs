using WoundBound.Rules.Enums;

namespace WoundBound.Game.Combat
{
    /// <summary>
    /// Rules the game is trying out ahead of the rules documents — each a change the user means to
    /// write into the rules if the simulator shows it earns its place. Once the rules repo carries
    /// it, the trial goes and the game reads the rules library again.
    /// </summary>
    public static class RuleTrials
    {
        /// <summary>
        /// The Stamina a Minion gets: 1 as its statblock writes it, or 2 if 1 proves to leave it
        /// nothing to spend (the user, 2026-11-02: "if that makes minions too useless then we give
        /// them 2 as well"). Settable so the simulator can compare the two.
        /// </summary>
        public static int MinionStamina { get; set; } = 1;

        /// <summary>
        /// For measuring, never for play: every creature fights to the last wound, whatever its
        /// statblock's breaking point says — what Morale is worth to a fight's difficulty.
        /// </summary>
        public static bool IgnoreMorale { get; set; }

        /// <summary>
        /// For measuring, never for play: creatures added to every fight as it is set up — what a
        /// bigger budget does to a fight, for calibrating Threat Value (GM Reference §5). Handed the
        /// fight's combatants and its setting; returns the newcomers.
        /// </summary>
        public static Func<IReadOnlyList<Combatant>, Setting, IEnumerable<Combatant>>? Reinforce { get; set; }

        /// <summary>
        /// A statblock's Stamina under the trial floor: only a Minion stands on so little; everything
        /// else has at least Tier + 1 (the user, 2026-11-02, to go into the GM Reference). A creature
        /// that starts a turn on 0 falls unconscious, so 1 is a creature that can never spend.
        /// </summary>
        public static int StaminaFor(AdversaryRole role, Tier tier, int written) =>
            role == AdversaryRole.Minion ? MinionStamina : Math.Max(written, (int)tier + 1);
    }
}
