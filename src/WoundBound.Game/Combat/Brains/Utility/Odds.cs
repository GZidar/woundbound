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
        private static readonly Dictionary<(int Margin, bool Advantage, bool Disadvantage), RollOdds> Cache = new();

        public static RollOdds Of(int modifier, int threshold, bool advantage = false, bool disadvantage = false)
        {
            var key = (Math.Clamp(modifier - threshold, -30, 30), advantage, disadvantage);
            lock (Cache)
            {
                if (Cache.TryGetValue(key, out var known))
                {
                    return known;
                }
            }

            var dice = RollCalculator.DiceNeeded(advantage, disadvantage);
            var faces = new int[dice];
            double total = 0, success = 0, critSuccess = 0, critFailure = 0;

            void Enumerate(int at)
            {
                if (at == dice)
                {
                    var roll = RollCalculator.Resolve(faces.ToArray(), key.Item1, 0, advantage, disadvantage);
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
            var odds = new RollOdds(success / total, critSuccess / total, critFailure / total);
            lock (Cache)
            {
                Cache[key] = odds;
            }

            return odds;
        }
    }
}
