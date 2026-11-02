using WoundBound.Rules.Calculation;
using WoundBound.Rules.Enums;
using WoundBound.Rules.Models;

namespace WoundBound.Game.Combat
{
    /// <summary>
    /// The environmental lever a lasting Utility spell moves one grade (Core Rules §9, "What the
    /// Levers Touch Elsewhere"): conjured light, held warmth or chill, or the ground made firmer or fouled.
    /// </summary>
    public enum Lever
    {
        Light,
        Exposure,
        Ground,
    }

    /// <summary>
    /// A lasting Utility spell at work on the ground it was put down on — it stays there, and doesn't
    /// follow anybody. It lasts the scene, and ends the moment its caster is knocked unconscious or
    /// dies (Core Rules §11, "Utility Spells", "Duration and the Caster").
    /// </summary>
    /// <param name="Corner">The block's corner, least X and Y.</param>
    /// <param name="Across">How many squares across the block is.</param>
    /// <param name="Grade">Light: how many carried lights it counts as. Ground: +1 firmer, −1 fouled. Exposure: 1.</param>
    public sealed record Working(Combatant Caster, string Spell, Lever Lever, GridPosition Corner, int Across, IReadOnlySet<GridPosition> Squares, int Grade)
    {
        /// <summary>Still held: its caster is conscious, alive and on the field.</summary>
        public bool Holds => !Caster.IsUnconscious && !Caster.HasFled && Caster.Body > 0;
    }

    public sealed partial class Encounter
    {
        private readonly List<Working> workings = new();

        /// <summary>Every lasting Utility spell still at work.</summary>
        public IReadOnlyList<Working> Workings => workings.Where(w => w.Holds).ToList();

        /// <summary>
        /// The ground underfoot as it is now: the map's grade, moved one grade by a Utility spell over
        /// it. Water stays Broken, whatever is done to the bed under it. Only one such spell counts on
        /// a square — two castings don't stack — so the latest is the one that holds.
        /// </summary>
        public Terrain GroundAt(GridPosition square)
        {
            var grade = Setting.GroundAt(square);
            if (Setting.Water.ContainsKey(square)
                || workings.LastOrDefault(w => w.Lever == Lever.Ground && w.Holds && w.Squares.Contains(square)) is not { } working)
            {
                return grade;
            }

            return working.Grade > 0
                ? grade == Terrain.Broken ? Terrain.Firm : Terrain.Sure
                : grade == Terrain.Sure ? Terrain.Firm : Terrain.Broken;
        }

        /// <summary>The weather where <paramref name="combatant"/> stands: the scene's, a grade milder inside held warmth or chill.</summary>
        public Exposure ExposureFor(Combatant combatant) =>
            Setting.Exposure != Exposure.Normal
            && workings.Any(w => w.Lever == Lever.Exposure && w.Holds && combatant.Footprint.Any(w.Squares.Contains))
                ? Setting.Exposure - 1
                : Setting.Exposure;

        /// <summary>
        /// What <paramref name="target"/>'s Dodge Threshold would get from the dark were
        /// <paramref name="extra"/> conjured as well — for a brain weighing where a light would go.
        /// </summary>
        internal int DodgeModifierWith(Combatant target, Working extra)
        {
            if (environment is not { } tables || !Setting.IsUnlit)
            {
                return VisibilityDodgeModifier(target);
            }

            workings.Add(extra);
            try
            {
                return EnvironmentCalculator.DodgeThresholdModifier(tables, WorkOutVisibility(tables, target, target.Position));
            }
            finally
            {
                workings.Remove(extra);
            }
        }

        /// <summary>The block a Utility spell is put down on — or why it can't be.</summary>
        private static string? UtilityRefusal(SpellDefinition spell, SpellShape shape) =>
            SpellArea.LeverOf(spell) is null
                ? $"{spell.Name} moves none of the levers a fight turns on — light, weather or ground."
                : shape != SpellShape.Volume
                    ? $"{spell.Name} is put down on a block of ground, as a Volume."
                    : null;

        /// <summary>Puts a lasting Utility spell down where it was placed (Core Rules §9, "What the Levers Touch Elsewhere").</summary>
        private void Work(Combatant caster, SpellCast cast, CastPlan plan)
        {
            var spell = cast.Spell;
            var lever = SpellArea.LeverOf(spell)!.Value;
            var grade = lever switch
            {
                Lever.Light => spell.Size == SpellSize.Small ? 1 : 2,
                Lever.Ground => cast.GroundShift >= 0 ? 1 : -1,
                _ => 1,
            };

            var corner = cast.Corner ?? plan.Origin;
            var working = new Working(caster, spell.Name, lever, corner, SpellArea.Across(spell.Size), plan.Area.ToHashSet(), grade);

            // Only one of a kind counts for any square: a fresh one of the caster's own replaces it.
            workings.RemoveAll(w => w.Caster == caster && w.Lever == lever);
            workings.Add(working);
            visibilityCache.Clear();

            Write(lever switch
            {
                Lever.Light => $"Light rises over {plan.Area.Count} squares, as {grade} carried light{(grade > 1 ? "s" : string.Empty)} — it stays where it was put.",
                Lever.Exposure => Setting.Exposure == Exposure.Normal
                    ? "The air inside it turns, but the weather was never against anybody here."
                    : $"Inside it the {Setting.Exposure.ToString().ToLowerInvariant()} weather eases to {(Setting.Exposure - 1).ToString().ToLowerInvariant()}.",
                _ => grade > 0 ? $"The ground over {plan.Area.Count} squares firms underfoot." : $"The ground over {plan.Area.Count} squares turns to mire.",
            });
        }

        /// <summary>Lets go of what a fallen caster was holding (Core Rules §11, "Duration and the Caster").</summary>
        private void EndWorkingsOfFallenCasters()
        {
            foreach (var working in workings.Where(w => !w.Holds).ToList())
            {
                workings.Remove(working);
                visibilityCache.Clear();
                Write($"{working.Spell} fails with {working.Caster.Name}.");
            }
        }
    }
}
