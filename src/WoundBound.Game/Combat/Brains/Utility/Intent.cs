namespace WoundBound.Game.Combat.Brains.Utility
{
    /// <summary>
    /// What a GM wants a fight to feel like: the result it steers its intent towards. The dice are
    /// never touched, and no creature is played below its best. Only how hard the GM plays for the
    /// kill changes (<see cref="Assessor.Ruthlessness"/>; the user's ruling, 2026-11-08).
    /// </summary>
    public enum Challenge
    {
        /// <summary>The party should come through comfortably: a fight to enjoy, not to fear.</summary>
        Easy,

        /// <summary>A real fight the party should still win most of the time, a little scary.</summary>
        Average,

        /// <summary>Push the party as hard as the GM can: ruthless from the first blow.</summary>
        Push,
    }

    /// <summary>
    /// How a GM sets its ruthlessness: fixed for the fight, or steered round by round by a director
    /// that reads how the fight is going and eases off or presses on to land near a target
    /// (<see cref="Steer"/>). The director only lowers the GM below its best. A fight that is too
    /// much for the party even at full mercy is a fight that costs too much, not a brain to tune.
    /// </summary>
    public sealed record GmIntent(double Ruthlessness, Challenge? Steer = null, bool FinishesTheDowned = false)
    {
        /// <summary>Plays to win throughout, but leaves the dying all but alone: the default.</summary>
        public static GmIntent Ruthless { get; } = new(1);

        /// <summary>
        /// The most extreme: ruthless, and it finishes the dying. For story reasons only (the user's
        /// ruling, 2026-11-08). Players grow frustrated if it happens often.
        /// </summary>
        public static GmIntent Merciless { get; } = new(1, FinishesTheDowned: true);

        /// <summary>A fixed ruthlessness, 0 to 1, held all fight.</summary>
        public static GmIntent Fixed(double ruthlessness) => new(Math.Clamp(ruthlessness, 0, 1));

        /// <summary>A director steering towards <paramref name="challenge"/>, starting where that challenge usually sits.</summary>
        public static GmIntent Directed(Challenge challenge) => new(StartFor(challenge), challenge);

        /// <summary>Where a director starts each challenge, before the fight has shown anything.</summary>
        public static double StartFor(Challenge challenge) => challenge switch
        {
            Challenge.Easy => 0.1,
            Challenge.Average => 0.7,
            _ => 1,
        };

        /// <summary>
        /// The lead the party should hold for each challenge: the share of the foes' strength gone,
        /// less the share of the party's (<see cref="Lead"/>). Easy wants the party well ahead,
        /// Average a little ahead, and Push wants it behind, so a Push director never eases off.
        /// </summary>
        public static double TargetLead(Challenge challenge) => challenge switch
        {
            Challenge.Easy => 0.35,
            Challenge.Average => 0.1,
            _ => -1,
        };

        /// <summary>How far the director moves the ruthlessness for each point the lead is off target, once a round.</summary>
        public const double Gain = 1.0;

        /// <summary>
        /// The ruthlessness for the coming round: up when the party is further ahead than the
        /// challenge wants, down when it is behind.
        /// </summary>
        public static double Steered(double ruthlessness, Challenge challenge, double lead) =>
            Math.Clamp(ruthlessness + Gain * (lead - TargetLead(challenge)), 0, 1);

        /// <summary>
        /// How far ahead the party stands, from −1 to 1: the share of the foes worn down, less the
        /// share of the party. A creature out of the fight, fled or broken counts whole, and a
        /// character dying counts whole. Otherwise each counts the share of its Body gone. Steeds and phantoms don't count.
        /// </summary>
        public static double Lead(BattleView view)
        {
            static double Worn(Combatant c) =>
                c.IsOutOfFight || c.HasFled || c.IsBroken || c.HasSurrendered ? 1
                : c.BodyMax <= 0 ? 0
                : Math.Clamp(1 - (double)c.Body / c.BodyMax, 0, 1);

            var all = view.Combatants.Where(c => !c.IsSteed && !c.IsPhantom).ToList();
            var foes = all.Where(c => c.Side == Side.Adversaries).ToList();
            var party = all.Where(c => c.Side == Side.Party).ToList();
            if (foes.Count == 0 || party.Count == 0)
            {
                return 0;
            }

            return foes.Average(Worn) - party.Average(Worn);
        }
    }
}
