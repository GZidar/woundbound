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
        /// A Bane's penalty, by Tier: −2 / −3 / −4. Core Rules §11 says −1 / −2 / −3; the sims showed
        /// a −1 for one round is seldom worth a wound, and the user is moving the rules to −2/−3/−4
        /// (2026-11-02).
        /// </summary>
        public static int BaneAmount(Tier tier) => tier switch
        {
            Tier.T1 => -2,
            Tier.T2 => -3,
            _ => -4,
        };
    }
}
