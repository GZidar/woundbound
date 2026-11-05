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

        /// <summary>Not a lever of the scene: a Scrying spell's viewing point, held there for the scene.</summary>
        Scrying,
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
            Working? working = null;
            for (var i = workings.Count - 1; i >= 0 && working is null; i--)
            {
                if (workings[i] is { Lever: Lever.Ground, Holds: true } w && w.Squares.Contains(square))
                {
                    working = w;
                }
            }

            if (working is null || Setting.Water.ContainsKey(square))
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
            SpellArea.UtilityKindOf(spell) == UtilityKind.Lever && SpellArea.LeverOf(spell) is null
                ? $"{spell.Name} moves none of the levers a fight turns on — light, weather or ground."
                : shape != SpellShape.Volume
                    ? $"{spell.Name} is put down on a block of ground, as a Volume."
                    : null;

        /// <summary>Puts a lasting Utility spell down where it was placed (Core Rules §9, "What the Levers Touch Elsewhere").</summary>
        private void Work(Combatant caster, SpellCast cast, CastPlan plan)
        {
            var spell = cast.Spell;
            switch (SpellArea.UtilityKindOf(spell))
            {
                case UtilityKind.Sensing:
                    Sense(caster, cast, plan);
                    return;
                case UtilityKind.Scrying:
                    Scry(caster, cast, plan);
                    return;
            }

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

        /// <summary>
        /// A Scrying spell's viewing point, put down on the block (Core Rules §11, "Scrying"): the caster
        /// sees the place as it is now — by Sight alone at T1, with Sound at T2, and Substance at T3. It
        /// has no Threshold, and it lasts the scene, anchored to its caster. A caster has one at a time.
        /// </summary>
        private void Scry(Combatant caster, SpellCast cast, CastPlan plan)
        {
            var spell = cast.Spell;
            workings.RemoveAll(w => w.Caster == caster && w.Lever == Lever.Scrying);
            workings.Add(new Working(caster, spell.Name, Lever.Scrying, cast.Corner ?? plan.Origin, SpellArea.Across(spell.Size), plan.Area.ToHashSet(), (int)spell.SpellTier));
            var senses = spell.SpellTier switch { Tier.T1 => "sight", Tier.T2 => "sight and sound", _ => "sight, sound and touch" };
            var there = combatants.Where(c => c != caster && c.OccupiesSquare && c.Footprint.Any(plan.Area.Contains)).Select(c => c.Name).ToList();
            Write($"{caster.Name}'s {spell.Name} opens a window on {plan.Area.Count} squares, by {senses}, for the rest of the scene — "
                  + (there.Count == 0 ? "nobody is there." : $"{string.Join(", ", there)} {(there.Count == 1 ? "is" : "are")} there."));
        }

        /// <summary>
        /// A Sensing spell over the block (Core Rules §11, "Sensing"). Whatever isn't hiding simply shows
        /// itself, the answer as precise as the Tier: whether a creature carries active magic (T1), its
        /// general character (T2), the exact detail (T3). An Illusion is a lie being told, so seeing
        /// through it is an opposed roll: the cast roll against its Deceit, the higher winning, as a
        /// Search is — and on a win the caster sees it for what it is.
        /// </summary>
        private void Sense(Combatant caster, SpellCast cast, CastPlan plan)
        {
            var spell = cast.Spell;
            var area = plan.Area.ToHashSet();
            foreach (var illusion in illusions.Where(i => i.Caster.Side != caster.Side && !i.SeenThrough.Contains(caster)
                         && (i.Squares.Any(area.Contains) || i.Phantom?.Footprint.Any(area.Contains) == true || i.Subject?.Footprint.Any(area.Contains) == true)).ToList())
            {
                var roll = RollAgainst(plan.CastBonus, illusion.Deceit, advantage: false, disadvantage: false, caster);
                var sees = roll.Total >= illusion.Deceit;
                Write($"{caster.Name}'s {spell.Name} feels for the lie in {illusion.Spell}: {DescribeRoll(roll)} against Deceit {illusion.Deceit} — "
                      + (sees ? "it is seen for what it is." : "the lie holds."));
                if (sees)
                {
                    illusion.SeenThrough.Add(caster);
                }
            }

            var found = new List<string>();
            foreach (var creature in combatants.Where(c => c != caster && c.OccupiesSquare && c.Footprint.Any(area.Contains)))
            {
                var held = spellEffects.Where(e => e.Target == creature).Select(e => e.Spell.Name).Distinct().ToList();
                var magic = creature.Spells.Count > 0 || held.Count > 0;
                if (creature.Spells.Count > 0 && creature.Side != caster.Side)
                {
                    creature.IsSensedAsCaster = true;
                }

                found.Add(spell.SpellTier switch
                {
                    Tier.T1 => $"{creature.Name}: {(magic ? "magic" : "no magic")}",
                    Tier.T2 => $"{creature.Name}: " + (!magic ? "no magic"
                        : string.Join(", ", creature.Spells.Select(s => SpellCalculator.SourceLabel(s.Source)).Distinct()
                            .Concat(held.Count > 0 ? ["under a spell"] : []))),
                    _ => $"{creature.Name}: " + (!magic ? "no magic"
                        : string.Join(", ", creature.Spells.Select(s => s.Name).Concat(held.Select(h => $"under {h}")))),
                });
            }

            Write($"{caster.Name}'s {spell.Name} senses {(found.Count == 0 ? "nobody in the block" : string.Join("; ", found))}.");
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
