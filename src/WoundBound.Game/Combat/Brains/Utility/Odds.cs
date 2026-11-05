using WoundBound.Rules.Calculation;

namespace WoundBound.Game.Combat.Brains.Utility
{
    /// <summary>The chance of each way a roll can come out.</summary>
    /// <param name="Success">Meets the Threshold — doubles twelve always, double one never.</param>
    /// <param name="CriticalSuccess">A success on a double.</param>
    /// <param name="CriticalFailure">A failure on a double.</param>
    public readonly record struct RollOdds(double Success, double CriticalSuccess, double CriticalFailure);

    /// <summary>
    /// Exact odds for the one roll the game makes: 2d12 + modifier against a Threshold, with a
    /// third die for Advantage or Disadvantage (Core Rules §10). Worked out by putting every
    /// combination of dice through <see cref="RollCalculator.Resolve"/>, so the odds are the
    /// rules' own — absolute twelves and ones included — and cached, since only the margin
    /// between modifier and Threshold matters.
    /// </summary>
    public static class Odds
    {
        private const int Widest = 30;

        // One slot per margin (-30 to +30) and Advantage/Disadvantage pair, filled the first time it's
        // asked for. No lock: the brains ask from every thread at once, and a slot is a whole object,
        // so a reader sees it filled or empty, never half-written; two threads that both find it empty
        // work out the same odds.
        private static readonly System.Runtime.CompilerServices.StrongBox<RollOdds>?[] Known = new System.Runtime.CompilerServices.StrongBox<RollOdds>?[(2 * Widest + 1) * 4];

        public static RollOdds Of(int modifier, int threshold, bool advantage = false, bool disadvantage = false)
        {
            var margin = Math.Clamp(modifier - threshold, -Widest, Widest);
            var slot = ((margin + Widest) * 4) + (advantage ? 2 : 0) + (disadvantage ? 1 : 0);
            if (Volatile.Read(ref Known[slot]) is { } known)
            {
                return known.Value;
            }

            var odds = WorkOut(margin, advantage, disadvantage);
            Volatile.Write(ref Known[slot], new System.Runtime.CompilerServices.StrongBox<RollOdds>(odds));
            return odds;
        }

        private static RollOdds WorkOut(int margin, bool advantage, bool disadvantage)
        {
            var dice = RollCalculator.DiceNeeded(advantage, disadvantage);
            var faces = new int[dice];
            double total = 0, success = 0, critSuccess = 0, critFailure = 0;

            void Enumerate(int at)
            {
                if (at == dice)
                {
                    var roll = RollCalculator.Resolve(faces.ToArray(), margin, 0, advantage, disadvantage);
                    total++;
                    if (roll.Success == true)
                    {
                        success++;
                        critSuccess += roll.IsCritical ? 1 : 0;
                    }
                    else
                    {
                        critFailure += roll.IsCritical ? 1 : 0;
                    }

                    return;
                }

                for (var face = 1; face <= RollCalculator.HighestFace; face++)
                {
                    faces[at] = face;
                    Enumerate(at + 1);
                }
            }

            Enumerate(0);
            return new RollOdds(success / total, critSuccess / total, critFailure / total);
        }
    }
}
