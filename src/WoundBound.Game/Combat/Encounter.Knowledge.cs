using WoundBound.Rules.Enums;
using WoundBound.Rules.Models;

namespace WoundBound.Game.Combat
{
    /// <summary>
    /// What the table has seen of each creature's Thresholds — the record a GM brain that hasn't
    /// read the character sheets learns from (the user's direction, 2026-11-02: the GM may cheat on
    /// knowledge, never on dice). Every roll made against a creature is said aloud with its total
    /// and whether it landed, and every modifier on top of the sheet — range, cover, the dark, a
    /// raised shield, a Buff or a Bane, standing firm — is declared at the table too. So a hit
    /// says the creature's sheet Threshold is at most the total less those modifiers, and a miss
    /// says it is more. Criticals land or fail whatever the Threshold, so they say nothing.
    /// </summary>
    public sealed partial class Encounter
    {
        private readonly Dictionary<(Combatant, ThresholdType), (int Min, int Max)> seenThresholds = new();

        /// <summary>
        /// The bounds the rolls so far put on <paramref name="creature"/>'s sheet value for
        /// <paramref name="threshold"/>, or null where nothing has been rolled against it yet.
        /// </summary>
        public (int Min, int Max)? SeenBounds(Combatant creature, ThresholdType threshold) =>
            seenThresholds.TryGetValue((creature, threshold), out var bounds) ? bounds : null;

        /// <summary>The sheet value of <paramref name="creature"/>'s <paramref name="threshold"/>, before anything on top of it; null where it has none.</summary>
        public static int? SheetThreshold(Combatant creature, ThresholdType threshold) => threshold switch
        {
            ThresholdType.Dodge => creature.BaseDodgeThreshold,
            ThresholdType.Stability => creature.BaseStabilityThreshold,
            ThresholdType.Resolve => creature.BaseResolveThreshold,
            _ => creature.BaseFortitudeThreshold,
        };

        /// <summary>Notes what <paramref name="roll"/>, made against <paramref name="target"/>'s <paramref name="threshold"/>, showed of its sheet value.</summary>
        private void Witness(Combatant target, ThresholdType threshold, RollResult roll)
        {
            if (roll.IsCritical || roll.Success is not { } landed || roll.Threshold is not { } faced || SheetThreshold(target, threshold) is not { } sheet)
            {
                return;
            }

            // Everything the roll faced on top of the sheet was declared, so it comes off.
            var declared = faced - sheet;
            var total = roll.Total;
            var (min, max) = SeenBounds(target, threshold) ?? (int.MinValue, int.MaxValue);
            if (landed)
            {
                max = Math.Min(max, total - declared);
            }
            else
            {
                min = Math.Max(min, total + 1 - declared);
            }

            seenThresholds[(target, threshold)] = (min, max);
        }
    }
}
