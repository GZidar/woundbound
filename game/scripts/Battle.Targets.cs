using System.Collections.Generic;
using System.Linq;
using WoundBound.Game.Combat;
using WoundBound.Game.Combat.Brains.Utility;
using WoundBound.Rules.Enums;
using WoundBound.Rules.Models;

namespace WoundBoundGame
{
    /// <summary>
    /// The radial menu's target rings: who an action at one creature could be aimed at, as the rules
    /// decide it, and any choice still owed once the target is known — which way a Shift drives them,
    /// what a Compulsion orders, which track a Heal mends.
    /// </summary>
    public partial class Battle
    {
        /// <summary>A spell aimed at one creature rather than placed on the board: anything but an area or an Illusion.</summary>
        private static bool IsAimedAtOne(SpellDefinition spell) =>
            spell.SpellType != SpellType.Illusion && !(spell.Target == SpellTarget.Many && spell.Range != SpellRange.Touch);

        /// <summary>
        /// Everyone the armed spell could be cast on from where the caster stands, with the choices
        /// already made — each one planned by the rules, so only a cast that would go is offered.
        /// Foes first, then allies, nearest first.
        /// </summary>
        private IReadOnlyList<RadialItem> SpellTargets(Combatant caster, SpellDefinition spell)
        {
            var offered = encounter.Combatants
                .Where(c => c.OccupiesSquare)
                .Select(c => (Target: c, Cast: CastAt(caster, c.Position)))
                .Where(t => t.Cast is { } cast && cast.Target == t.Target && encounter.PlanCast(caster, cast).CanCast)
                .OrderBy(t => t.Target.Side == caster.Side)
                .ThenBy(t => caster.DistanceTo(t.Target))
                .ToList();

            if (offered.Count == 0)
            {
                return [new RadialItem("Nobody", spell.Range == SpellRange.Touch
                    ? "Nobody you could lay a hand on — Touch needs them beside you."
                    : "Nobody this spell could reach from here, or that it could work on.")];
            }

            return offered.Select(t =>
            {
                var (target, cast) = (t.Target, t.Cast!);
                var label = target == caster ? $"{target.Name} (you)" : target.Side == caster.Side ? $"{target.Name} (ally)" : target.Name;
                var hint = SpellHint(caster, spell, target, encounter.PlanCast(caster, cast));

                // What the spell still needs to know, now that it knows who.
                if (spell.SpellType == SpellType.Motion && WoundBound.Game.Combat.SpellArea.MotionOf(spell) == MotionKind.Shift)
                {
                    return new RadialItem(label, null, hint + "\nThen choose which way to drive them.")
                    {
                        Children = () => ShiftDirections(caster, target)
                            .Select(d => new RadialItem(d.Label, null, d.Hint)
                            {
                                Pick = () => Cast(cast with { Direction = d.Step }),
                                Arrow = new Godot.Vector2(d.Step.X, d.Step.Y),
                            })
                            .ToList(),
                    };
                }

                if (spell.SpellType == SpellType.Control && cast.Effect == ControlEffect.Compulsion)
                {
                    return new RadialItem(label, null, hint + "\nThen choose what they're compelled to do.")
                    {
                        Children = () => CompulsionOrders(caster, target, cast),
                    };
                }

                if (spell.SpellType == SpellType.Heal)
                {
                    return new RadialItem(label, null, hint + "\nThen choose which track it mends.")
                    {
                        Children = () => HealTracks(target, cast),
                    };
                }

                return new RadialItem(label, null, hint) { Pick = () => Cast(cast) };
            }).ToList();
        }

        private void Cast(SpellCast cast)
        {
            Disarm();
            encounter.TryCast(cast);
            Refresh();
        }

        /// <summary>The roll the cast makes on <paramref name="target"/>, if it makes one, and what it costs.</summary>
        private static string SpellHint(Combatant caster, SpellDefinition spell, Combatant target, CastPlan plan)
        {
            var lines = new List<string>();
            var against = spell.Threshold switch
            {
                ThresholdType.Dodge => (int?)target.DodgeThreshold,
                ThresholdType.Stability => target.StabilityThreshold,
                ThresholdType.Resolve => target.ResolveThreshold,
                ThresholdType.Fortitude => target.FortitudeThreshold,
                _ => null,
            };
            if (spell.Threshold is { } threshold && target.Side != caster.Side)
            {
                lines.Add(against is { } value
                    ? $"{plan.CastBonus:+0;-0;+0} against their {threshold} Threshold {value} — about {Odds.Of(plan.CastBonus, value).Success:P0} to land before any Advantage or Disadvantage."
                    : $"Rolled against their {threshold} Threshold.");
            }

            lines.Add($"Costs {plan.WoundsPaid} {plan.Track?.ToString() ?? "Fuel"}" + (plan.WoundsPaid == plan.Cost ? "." : $" (of {plan.Cost}).") + $" {plan.Range} range.");
            lines.Add($"{target.Name}: Body {target.Body}/{target.BodyMax}" +
                      (target.MindMax > 0 ? $", Mind {target.Mind}/{target.MindMax}" : string.Empty) +
                      (target.SoulMax > 0 ? $", Soul {target.Soul}/{target.SoulMax}" : string.Empty) + ".");
            return string.Join("\n", lines);
        }

        /// <summary>
        /// Which ways a Shift can drive <paramref name="target"/>, as far as its Tier carries them —
        /// straight away from the caster the usual one, a step that is (1, 0) on the caster's own square.
        /// </summary>
        private static IEnumerable<(string Label, string Hint, (int X, int Y) Step)> ShiftDirections(Combatant caster, Combatant target)
        {
            var away = (System.Math.Sign(target.Position.X - caster.Position.X), System.Math.Sign(target.Position.Y - caster.Position.Y));
            return EightWays(away == (0, 0) ? (1, 0) : away, "away from you",
                "As far as the spell's Tier carries them, in a straight line. Something solid in the way stops them short.");
        }

        /// <summary>
        /// What a Compulsion can order: drop the weapon and kneel, or attack someone it can see — each
        /// order planned by the rules before it's offered.
        /// </summary>
        private IReadOnlyList<RadialItem> CompulsionOrders(Combatant caster, Combatant target, SpellCast cast)
        {
            var orders = new List<RadialItem>
            {
                new("Kneel", null, "\"Drop your weapon and kneel\": no action and no move for as long as it holds.") { Pick = () => Cast(cast with { Directed = null }), Icon = "kneel" },
            };

            orders.AddRange(encounter.Combatants
                .Where(c => c != target && c.OccupiesSquare && !c.IsOutOfFight)
                .Where(c => encounter.PlanCast(caster, cast with { Directed = c }).CanCast)
                .OrderBy(c => c.Side == caster.Side)
                .ThenBy(c => target.DistanceTo(c))
                .Select(c => new RadialItem($"Attack {c.Name}", null, $"\"Attack {c.Name}\": {target.Name} goes for them on its turns, for as long as it holds.")
                {
                    Pick = () => Cast(cast with { Directed = c }),
                }));
            return orders;
        }

        /// <summary>The tracks a Heal could mend on <paramref name="target"/> — "chosen freely" (Core Rules §11) — greyed where there's nothing to mend.</summary>
        private IReadOnlyList<RadialItem> HealTracks(Combatant target, SpellCast cast) =>
            new[] { WoundType.Body, WoundType.Mind, WoundType.Soul }
                .Where(track => target.Track(track).Max > 0)
                .Select(track =>
                {
                    var (now, max) = target.Track(track);
                    return new RadialItem(track.ToString(), now >= max ? $"{target.Name}'s {track} is already full." : null, $"{track} {now}/{max}.")
                    {
                        Pick = () => Cast(cast with { Restores = track }),
                    };
                })
                .ToList();

        /// <summary>Grab, Shove or Trip: a ring of the foes it would work on, each a click away.</summary>
        private RadialItem ControlItem(ControlAction action)
        {
            var button = controlButtons[action];
            if (encounter.Active is not { } actor || button.Disabled)
            {
                return Mirror(button, why: "No foe beside you it would work on.") with { Icon = action.ToString().ToLowerInvariant() };
            }

            return new RadialItem(action.ToString(), null, button.TooltipText.Replace(" Then click the foe.", " Then choose who."))
            {
                Icon = action.ToString().ToLowerInvariant(),
                Children = () => encounter.Fighting(actor.Side == Side.Party ? Side.Adversaries : Side.Party)
                    .Where(foe => Encounter.ControlRefusal(actor, action, foe) is null)
                    .OrderBy(foe => actor.DistanceTo(foe))
                    .Select(foe => new RadialItem(foe.Name, null,
                        $"Your unarmed roll, 2d12{actor.UnarmedAttackModifier:+0;-0;+0}, against their ST {foe.StabilityThreshold} — about {Odds.Of(actor.UnarmedAttackModifier, foe.StabilityThreshold).Success:P0} before any Advantage or Disadvantage.")
                    {
                        Pick = () =>
                        {
                            Disarm();
                            encounter.TryControl(action, foe);
                            Refresh();
                        },
                    })
                    .ToList(),
            };
        }

        private static string AidIcon(CombatAction action) => action == CombatAction.Shield ? "shield-ally" : action.ToString().ToLowerInvariant();

        /// <summary>Help, Harry, Heal, Meditate, Pray or Shield an ally: a ring of who it could be for.</summary>
        private RadialItem AidItem(CombatAction action)
        {
            var button = aidButtons[action];
            if (encounter.Active is not { } actor || button.Disabled)
            {
                return Mirror(button, button.Text, "Nobody you could do it for is beside you.") with { Icon = AidIcon(action) };
            }

            return new RadialItem(button.Text, null, button.TooltipText.Replace(" Then click who it's for.", " Then choose who."))
            {
                Icon = AidIcon(action),
                Children = () => encounter.Combatants
                    .Where(c => c.OccupiesSquare && ArmedRefusal(actor, action, c) is null)
                    .OrderBy(c => c != actor)
                    .ThenBy(c => actor.DistanceTo(c))
                    .Select(c => new RadialItem(c == actor ? $"{c.Name} (you)" : c.Name, null, $"{c.Name}: Body {c.Body}/{c.BodyMax}, Mind {c.Mind}/{c.MindMax}, Soul {c.Soul}/{c.SoulMax}.")
                    {
                        Pick = () =>
                        {
                            Disarm();
                            _ = action switch
                            {
                                CombatAction.Help or CombatAction.Harry => encounter.TryAid(action, c),
                                CombatAction.Shield => encounter.TryRaiseShield(c),
                                _ => encounter.TryTend(action, c),
                            };
                            Refresh();
                        },
                    })
                    .ToList(),
            };
        }
    }
}
