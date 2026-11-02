using WoundBound.Rules.Calculation;
using WoundBound.Rules.Enums;
using WoundBound.Rules.Models;

namespace WoundBound.Game.Combat
{
    /// <summary>
    /// The Fortitude Threshold (Core Rules §10): whether something that has got inside a creature
    /// takes hold. No action or Stamina raises it, so a Fortitude check offers no reaction and hands
    /// the GM nothing; only a Buff or a Bane moves it. A creature with no living body is immune.
    /// </summary>
    public sealed partial class Encounter
    {
        /// <summary>
        /// A poisoned blow, once its damage has landed: "resolved the way a manoeuvre is" — the same
        /// total against the target's Fortitude — and it takes hold "only on a hit that got past armour
        /// at all", dealing its own damage, Direct, on top of the weapon's (Core Rules §10).
        /// </summary>
        /// <param name="gotThrough">The weapon's wounds left after armour, before anyone Sheltered them.</param>
        private void Envenom(AttackProfile attack, Combatant target, RollResult roll, int gotThrough, Action then)
        {
            if (attack.Poison is not { } poison || roll.Success != true)
            {
                then();
                return;
            }

            if (gotThrough <= 0)
            {
                Write($"The {attack.Name}'s poison never broke the skin — nothing delivered.");
                then();
                return;
            }

            if (target.FortitudeThreshold is not { } fortitude)
            {
                Write($"{target.Name} has no living body for the {attack.Name}'s poison to take hold in.");
                then();
                return;
            }

            if (target.BodyStatus == BodyStatus.Dead)
            {
                then();
                return;
            }

            if (roll.Total < fortitude)
            {
                Write($"The poison: {roll.Total} against {target.Name}'s FT {fortitude} — their body throws it off.");
                then();
                return;
            }

            var amount = Math.Max(PlayStateCalculator.DamageFloor,
                Enumerable.Range(0, poison.DiceCount).Sum(_ => dice.Roll(poison.DieSides)) + poison.Modifier);
            Write($"The poison: {roll.Total} against {target.Name}'s FT {fortitude} — it takes hold, {amount} {poison.Track} (Direct).");
            Wound(target, poison.Track, amount, direct: true, then);
        }
    }
}
