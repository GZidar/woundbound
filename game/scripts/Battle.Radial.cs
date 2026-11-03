using System.Collections.Generic;
using System.Linq;
using Godot;
using WoundBound.Game.Combat;
using WoundBound.Game.Combat.Brains.Utility;
using WoundBound.Rules.Calculation;
using WoundBound.Rules.Enums;
using WoundBound.Rules.Models;
using Side = WoundBound.Game.Combat.Side;

namespace WoundBoundGame
{
    /// <summary>
    /// The radial menu's contents: the acting character's actions in groups, around their token.
    /// Every item reads the HUD button it stands for, which <see cref="UpdateHud"/> has already set
    /// by the rules, so the menu never disagrees with the buttons about what can be done.
    /// </summary>
    public partial class Battle
    {
        private RadialMenu radial = null!;

        /// <summary>Opens the actions ring around whoever is acting, if a person is at the controls.</summary>
        private void OpenActions()
        {
            if (bot is not null || encounter.Phase != EncounterPhase.PartyTurn || encounter.Active is not { } acting)
            {
                return;
            }

            radialPurpose = RadialPurpose.Actions;
            var centre = GetGlobalTransformWithCanvas() * SquareCentreOf(acting);
            radial.Open(centre, acting.Name.Split(' ').First(w => w != "The"), () => RootItems(acting));
        }

        /// <summary>
        /// For a look at the menu without a mouse (<c>-- --radial "Mirella/Magic/Eye of Eolis"</c>):
        /// begins the named character's turn, then opens the ring and follows each label in turn.
        /// </summary>
        private void OpenRadialPath(string path)
        {
            var parts = path.Split('/');
            if (encounter.Combatants.FirstOrDefault(c => c.Side == Side.Party && c.Name.Contains(parts[0])) is { } who)
            {
                encounter.TryBeginTurn(who);
                Refresh();
                OpenActions();
                foreach (var label in parts.Skip(1))
                {
                    radial.Follow(label);
                }
            }
        }

        /// <summary>
        /// For checking the menu without a mouse (<c>-- --radial Fighter --radial-click Hands-on</c>):
        /// a real left click, pushed through Godot's input, on the item named — then prints whether a
        /// ring opened and whether the acting character moved.
        /// </summary>
        private async void ClickRadial(string label)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (radial.WhereIs(label) is not { } at || encounter.Active is not { } acting)
            {
                GD.Print($"radial-click: no item '{label}' on the open ring");
                return;
            }

            var (rings, square) = (radial.OpenRings, acting.Position);
            var screen = GetViewport().GetFinalTransform() * at;
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = screen, GlobalPosition = screen });
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = screen, GlobalPosition = screen });
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GD.Print($"radial-click: rings {rings} -> {radial.OpenRings}; {acting.Name} at {square} -> {acting.Position}");
        }

        private IReadOnlyList<RadialItem> RootItems(Combatant acting)
        {
            var items = new List<RadialItem>
            {
                Group("Attack", "Strike a foe, with a manoeuvre if your weapon allows one.", AttackItems(acting)) with { Icon = AttackIcon(acting) },
            };

            if (acting.Spells.Count > 0)
            {
                items.Add(Group("Magic", "Cast a spell. Its choices come next, then click where it goes.", MagicItems(acting)));
            }

            items.Add(Group("Hands-on", "Grab, Shove, Trip, Drag — rolled unarmed against Stability.", HandsOnItems()));
            items.Add(Group("Aid", "Help, Harry, or tend a wound.", aidButtons.Keys.Select(AidItem).ToList()));
            items.Add(Group("Defend", "Stand your ground, or raise a shield.",
                standGroundButtons.Values.Append(raiseShieldButton).Select(b => Mirror(b)).ToList()));

            if (acting.IsNimble)
            {
                items.Add(Group("Exploits", "A Nimble character's held edges: 1 Stamina each.", exploitButtons.Values.Select(b => Mirror(b)).ToList()));
            }

            if (acting.Steed is { Body: > 0 } steed)
            {
                items.Add(Group("Mount", $"{steed.Name}: into or out of the saddle, and its hooves to borrow.", MountItems(acting, steed)));
            }

            items.Add(Group("Gear", "Weapons and lights: the swap, the pack, and the ground.", new[] { swapButton, lightButton, dropLightButton, takeUpButton }
                .Where(b => b.Visible).Select(b => Mirror(b)).Concat(PackItems(acting)).ToList()));

            // What only applies now and then is offered only when it does.
            foreach (var button in new[] { standUpButton, breakFreeButton, regainFootingButton, duckBackButton })
            {
                if (!button.Disabled)
                {
                    items.Add(Mirror(button));
                }
            }

            items.Add(Mirror(extraActionButton, "+1 action"));
            items.Add(Mirror(endTurnButton, hint: "Finish this character's turn."));
            return items;
        }

        private List<RadialItem> AttackItems(Combatant acting)
        {
            var noAction = acting.ActionsRemaining < 1 ? "No action left this turn." : null;
            var items = new List<RadialItem>();

            // The off-hand strike the Attack action just allowed comes first, with its own manoeuvres.
            if (acting.OffHandAttack is { } offHand && acting.OffHandOwed)
            {
                var why = encounter.OffHandRefusal(acting);
                items.Add(new RadialItem($"Off-hand ({offHand.Name})", why,
                    $"Strike with the {offHand.Name} too: 1 Stamina, levied by the equipment — no action, no tactical spend, and no Opportunity for the GM. Rolled on its own.")
                {
                    Children = why is null ? () => Targets(acting, null, offHand: true) : null,
                    Icon = "off-hand",
                });

                var offPermitted = Encounter.ManoeuvresOf(acting, offHand: true);
                items.AddRange(manoeuvreButtons
                    .Where(m => offPermitted.Contains(m.Key))
                    .Select(m => new RadialItem($"{m.Key} (off-hand {offHand.Name})", why,
                        m.Value.TooltipText.Replace(" Then click the foe.", " Then choose who.") + $"\nMade with the {offHand.Name}, as the off-hand strike.")
                    {
                        Children = why is null ? () => Targets(acting, m.Key, offHand: true) : null,
                        Icon = m.Key.ToString().ToLowerInvariant(),
                    }));
            }

            items.AddRange(new RadialItem[]
            {
                new($"Strike ({acting.Attack.Name})", noAction, "Choose who to strike: everyone your weapon can reach from here, allies included.")
                {
                    Children = noAction is null ? () => Targets(acting, null) : null,
                    Icon = AttackIcon(acting),
                },
            });

            // Only what the weapon in hand permits — read from the rules, not the HUD buttons, which
            // may not have caught up with whoever's turn just began.
            var permitted = Encounter.ManoeuvresOf(acting);
            items.AddRange(manoeuvreButtons
                .Where(m => permitted.Contains(m.Key))
                .Select(m => new RadialItem(m.Key.ToString(), noAction, m.Value.TooltipText.Replace(" Then click the foe.", " Then choose who."))
                {
                    Children = noAction is null ? () => Targets(acting, m.Key) : null,
                    Icon = m.Key.ToString().ToLowerInvariant(),
                }));
            // A weapon that can be thrown, past Close: the Attack action, and the weapon goes with it.
            if (Encounter.ThrowOf(acting) is { } thrown)
            {
                items.Add(new RadialItem($"Throw ({thrown.Name})", noAction,
                    $"Throw the {thrown.Name}: a shot by its Weight past Close, no manoeuvre — and it lies where it lands until somebody picks it up.")
                {
                    Children = noAction is null ? () => ThrowTargets(acting) : null,
                    Icon = "shoot",
                });
            }

            if (sacrificeBox.Visible)
            {
                items.Add(Toggle(sacrificeBox));
            }

            return items;
        }

        /// <summary>Everyone the throwable weapon in hand could be thrown at from here.</summary>
        private IReadOnlyList<RadialItem> ThrowTargets(Combatant thrower)
        {
            var targets = encounter.Combatants
                .Where(c => c.OccupiesSquare && encounter.ThrowRefusal(thrower, c) is null)
                .OrderBy(c => c.Side == thrower.Side)
                .ThenBy(c => thrower.DistanceTo(c))
                .ToList();
            if (targets.Count == 0)
            {
                return [new RadialItem("Nobody", "Nobody in sight past Close to throw it at.")];
            }

            var thrown = Encounter.ThrowOf(thrower)!;
            return targets.Select(target =>
            {
                var plan = encounter.PlanThrow(thrower, target);
                var hit = Odds.Of(thrown.AttackModifier, plan.DodgeThreshold).Success;
                return new RadialItem(target.Side == thrower.Side ? $"{target.Name} (ally)" : target.Name, null,
                    $"{plan.Note}\n{thrown.Name}: +{thrown.AttackModifier} against DT {plan.DodgeThreshold} — about {hit:P0} to hit.")
                {
                    Pick = () =>
                    {
                        Disarm();
                        encounter.TryThrow(target, sacrificeBox.ButtonPressed);
                        Refresh();
                    },
                };
            }).ToList();
        }

        /// <summary>
        /// The mount's own ring (Core Rules §9, "Mounts"): getting up or down — a move, not movement —
        /// and, in the saddle, the horse's attack in place of the rider's own.
        /// </summary>
        private List<RadialItem> MountItems(Combatant acting, Combatant steed)
        {
            var items = new List<RadialItem>
            {
                acting.IsRiding
                    ? new RadialItem("Dismount", Encounter.DismountRefusal(acting), $"Get down from {steed.Name}: a move, but not movement — nobody's reach is left.")
                    {
                        Icon = "dismount",
                        Pick = () =>
                        {
                            encounter.TryDismount();
                            Refresh();
                        },
                    }
                    : new RadialItem("Mount", encounter.MountRefusal(acting), $"Get up onto {steed.Name}, standing beside it: a move, but not movement.")
                    {
                        Icon = "mount",
                        Pick = () =>
                        {
                            encounter.TryMount();
                            Refresh();
                        },
                    },
            };

            if (acting.SteedAttack is { } hooves)
            {
                var noAction = acting.ActionsRemaining < 1 ? "No action left this turn." : null;
                items.Add(new RadialItem($"Strike ({hooves.Name})", noAction,
                    $"Give up your own attack to use the horse's: +{hooves.AttackModifier}, {hooves.DamageDiceCount}d{hooves.DamageDieSides}{hooves.DamageModifier:+0;-0;+0}.")
                {
                    Children = noAction is null ? () => HoovesTargets(acting, hooves) : null,
                    Icon = "hooves",
                });
            }

            return items;
        }

        /// <summary>Everyone the horse's hooves could reach from here.</summary>
        private IReadOnlyList<RadialItem> HoovesTargets(Combatant rider, AttackProfile hooves)
        {
            var targets = encounter.Combatants.Where(c => c.OccupiesSquare && c != rider && encounter.CanAttack(rider, c))
                .Where(c => encounter.PlanAttack(rider, c) is { CanAttack: true } && rider.DistanceTo(c) <= hooves.Reach)
                .OrderBy(c => c.Side == rider.Side).ThenBy(c => rider.DistanceTo(c)).ToList();
            if (targets.Count == 0)
            {
                return [new RadialItem("Nobody", "Nobody within the horse's reach.")];
            }

            return targets.Select(target => new RadialItem(target.Side == rider.Side ? $"{target.Name} (ally)" : target.Name, null,
                $"{hooves.Name}: +{hooves.AttackModifier} against DT {target.DodgeThreshold}.")
            {
                Pick = () =>
                {
                    Disarm();
                    encounter.TryAttack(target, sacrificeBox.ButtonPressed, with: hooves);
                    Refresh();
                },
            }).ToList();
        }

        /// <summary>The pack and the ground: drawing a weapon, stowing or dropping the one in hand, picking one up.</summary>
        private IEnumerable<RadialItem> PackItems(Combatant acting)
        {
            foreach (var weapon in Encounter.PackWeapons(acting))
            {
                var why = Encounter.DrawRefusal(acting, weapon);
                yield return new RadialItem($"Draw {weapon.Name}", why,
                    $"Take the {weapon.Name} from the pack into hand: an action" + (weapon.Weight == WoundBound.Rules.Enums.WeaponWeight.Heavy ? ", and 1 Stamina — it's Heavy." : "."))
                {
                    Pick = () =>
                    {
                        encounter.TryDrawWeapon(weapon);
                        Refresh();
                    },
                };
            }

            if (acting.Build is { } build)
            {
                foreach (var held in WoundBound.Rules.Calculation.InventoryCalculator.InHandWeapons(build))
                {
                    yield return new RadialItem($"Stow {held.Name}", Encounter.StowRefusal(acting, held), $"Put the {held.Name} away into the pack: an action.")
                    {
                        Pick = () =>
                        {
                            encounter.TryStowWeapon(held);
                            Refresh();
                        },
                    };
                    yield return new RadialItem($"Drop {held.Name}", null, $"Let the {held.Name} fall at your feet: free. Anyone may pick it up — an action.")
                    {
                        Pick = () =>
                        {
                            encounter.TryDropWeapon(held);
                            Refresh();
                        },
                    };
                }
            }

            foreach (var lying in encounter.GroundWeapons.Where(g => acting.Covers(g.At)))
            {
                yield return new RadialItem($"Pick up {lying.Weapon.Name}", encounter.PickUpRefusal(acting, lying),
                    $"Pick the {lying.Weapon.Name} up off the ground: an action. Into hand if there's room, the pack if not.")
                {
                    Pick = () =>
                    {
                        encounter.TryPickUpWeapon(lying);
                        Refresh();
                    },
                };
            }
        }

        /// <summary>A sword for a blade (or fists) in hand, a bow for a bow, crossbow or sling.</summary>
        private static string AttackIcon(Combatant acting) => acting.Attack.IsRanged ? "shoot" : "strike";

        /// <summary>
        /// Everyone <paramref name="attacker"/> could strike from where they stand, as the rules have
        /// it — a blade whoever is in its Reach, a bow or a sling whoever is beyond Close and in sight,
        /// allies too — foes first, nearest first. Choosing one makes the attack.
        /// </summary>
        /// <param name="offHand">The off-hand strike of a Two Weapons loadout, rather than the Attack action.</param>
        private IReadOnlyList<RadialItem> Targets(Combatant attacker, WeaponManoeuvre? kind, bool offHand = false)
        {
            // A rider's mount may be struck instead of the rider (Core Rules §9, "Mounts").
            var targets = encounter.Combatants.Where(c => c.OccupiesSquare).Concat(encounter.Steeds.Where(s => s.Side != attacker.Side))
                .Where(c => offHand ? encounter.OffHandRefusal(attacker, c) is null : encounter.CanAttack(attacker, c))
                .Where(c => kind is null || (kind == WeaponManoeuvre.Cleave
                    ? CleaveSeconds(attacker, c, offHand).Count > 0
                    : Encounter.ManoeuvreRefusal(attacker, c, new Manoeuvre(kind.Value), offHand) is null))
                .OrderBy(c => c.Side == attacker.Side)
                .ThenBy(c => attacker.DistanceTo(c))
                .ToList();

            if (targets.Count == 0)
            {
                return [new RadialItem("Nobody", attacker.Attack.IsRanged
                    ? "Nobody in sight beyond Close — a bow or a sling can't shoot at someone beside you."
                    : "Nobody within your weapon's Reach.")];
            }

            return targets.Select(target =>
            {
                var plan = offHand ? encounter.PlanOffHandAttack(attacker, target) : encounter.PlanAttack(attacker, target);
                var attack = plan.Profile ?? attacker.Attack;
                var hit = Odds.Of(attack.AttackModifier, plan.DodgeThreshold).Success;
                var ally = target.Side == attacker.Side;
                var hint = $"{plan.Note}\n{attack.Name}: +{attack.AttackModifier} against DT {plan.DodgeThreshold} — about {hit:P0} to hit before any Advantage or Disadvantage" +
                           (plan.DamageDieSides is { } die ? $", {attack.DamageDiceCount}d{die}{attack.DamageModifier:+0;-0;+0}." : ".") +
                           $"\n{target.Name}: Body {target.Body}/{target.BodyMax}." +
                           (ally ? "\nThis is one of your own." : string.Empty);
                var label = ally ? $"{target.Name} (ally)" : target.Name;
                return kind switch
                {
                    // Cleave asks who the sweep carries on into; Hook, which way the target is hauled.
                    WeaponManoeuvre.Cleave => new RadialItem(label, null, hint + "\nThen choose who the sweep carries on into.")
                    {
                        Children = () => CleaveSeconds(attacker, target, offHand).Select(second => new RadialItem(second.Name, null,
                            $"The same damage roll lands on {second.Name} too, if it beats their DT {second.DodgeThreshold}. Armour absorbs it as normal.")
                        {
                            Pick = () => Strike(target, new Manoeuvre(WeaponManoeuvre.Cleave, second)),
                        }).ToList(),
                    },
                    WeaponManoeuvre.Hook => new RadialItem(label, null, hint + "\nThen choose which way to haul them.")
                    {
                        Children = () => HookDirections(attacker, target)
                            .Select(d => new RadialItem(d.Label, null, d.Hint)
                            {
                                Pick = () => Strike(target, new Manoeuvre(WeaponManoeuvre.Hook, Direction: d.Step)),
                                Arrow = new Godot.Vector2(d.Step.X, d.Step.Y),
                            })
                            .ToList(),
                    },
                    { } other => new RadialItem(label, null, hint) { Pick = () => Strike(target, new Manoeuvre(other)) },
                    null => new RadialItem(label, null, hint) { Pick = () => Strike(target, null) },
                };
            }).ToList();

            void Strike(Combatant target, Manoeuvre? manoeuvre)
            {
                Disarm();
                if (offHand)
                {
                    encounter.TryOffHandAttack(target, manoeuvre);
                }
                else
                {
                    encounter.TryAttack(target, sacrificeBox.ButtonPressed, manoeuvre);
                }

                Refresh();
            }
        }

        /// <summary>Every enemy a Cleave at <paramref name="target"/> could sweep on into, as the rules allow it, weakest first.</summary>
        private List<Combatant> CleaveSeconds(Combatant attacker, Combatant target, bool offHand = false) =>
            encounter.Combatants
                .Where(c => c != target && c.OccupiesSquare
                            && Encounter.ManoeuvreRefusal(attacker, target, new Manoeuvre(WeaponManoeuvre.Cleave, c), offHand) is null)
                .OrderBy(c => c.Body)
                .ToList();

        private static readonly (string Name, int X, int Y)[] Compass =
        [
            ("North", 0, -1), ("North-east", 1, -1), ("East", 1, 0), ("South-east", 1, 1),
            ("South", 0, 1), ("South-west", -1, 1), ("West", -1, 0), ("North-west", -1, -1),
        ];

        /// <summary>
        /// The eight ways, the one a move takes when nobody says otherwise named as such: towards the
        /// attacker for a Hook, straight away from the caster for a Shift — each of which is one of
        /// the eight steps, as the Encounter reckons it.
        /// </summary>
        private static IEnumerable<(string Label, string Hint, (int X, int Y) Step)> EightWays((int X, int Y) usual, string usualName, string hint) =>
            Compass.Select(c => (c.X, c.Y) == usual
                ? ($"{c.Name} — {usualName}", hint + $"\n{usualName[0].ToString().ToUpperInvariant()}{usualName[1..]}: the way it goes if you don't choose.", (c.X, c.Y))
                : (c.Name, hint, (c.X, c.Y)));

        /// <summary>
        /// Which ways a Hook can haul <paramref name="target"/>, towards the attacker the usual one. A
        /// wall or a body stops them short (Core Rules §9, "Forced Movement").
        /// </summary>
        private static IEnumerable<(string Label, string Hint, (int X, int Y) Step)> HookDirections(Combatant attacker, Combatant target)
        {
            var towards = (System.Math.Sign(attacker.Position.X - target.Position.X), System.Math.Sign(attacker.Position.Y - target.Position.Y));
            var squares = System.Math.Max(0, attacker.Reach - 1);
            var reach = $"Up to {squares} square{(squares == 1 ? string.Empty : "s")} — your Reach less one. Something solid in the way stops them short.";
            return EightWays(towards, "towards you", reach);
        }

        private List<RadialItem> HandsOnItems() =>
            controlButtons.Keys.Select(ControlItem)
                .Append(Mirror(dragButton, why: "You aren't holding anyone."))
                .Append(Mirror(letGoButton, why: "You aren't holding anyone."))
                .ToList();

        private List<RadialItem> MagicItems(Combatant caster)
        {
            var noAction = caster.ActionsRemaining < 1 ? "No action left this turn." : null;
            var items = caster.Spells.Select(spell => SpellItem(caster, spell, noAction)).ToList();

            var options = new[] { payInBodyBox, overclockBox, backlashBox }.Where(b => b.Visible).ToList();
            if (options.Count > 0)
            {
                items.Add(new RadialItem("How to pay", null, "Bleeding for Power, Overclock, Backlash: on or off for the casts that follow.")
                {
                    Children = () => options.Select(Toggle).ToList(),
                    Icon = "pay",
                });
            }

            return items;
        }

        /// <summary>
        /// A spell, and the decisions its casting asks for, one ring each: what a Control does, which
        /// Threshold a Buff or Bane moves, the Shape of an area, where Fuel comes from, and whether to
        /// hold it. The last choice arms it; the next click is where it goes.
        /// </summary>
        private RadialItem SpellItem(Combatant caster, SpellDefinition spell, string? noAction)
        {
            var steps = new List<(string Title, OptionButton? Picker, CheckBox? Box, string[] Labels)>();
            if (spell.SpellType == SpellType.Control && WoundBound.Game.Combat.SpellArea.FixedEffectOf(spell) is null)
            {
                steps.Add(("Effect", effectPicker, null, ["Fear", "Confusion", "Compulsion"]));
            }

            if (spell.SpellType is SpellType.Buff or SpellType.Bane)
            {
                steps.Add(("Threshold", thresholdPicker, null, BuffThresholds.Select(t => t.ToString()).ToArray()));
            }

            // A T2 or T3 Bind: its own effect, or a lower Tier's — most binding first.
            if (IsTieredBind(spell))
            {
                steps.Add(("Bind", bindPicker, null, Enumerable.Range(1, (int)spell.SpellTier).Reverse()
                    .Select(t => Encounter.BindEffect((Tier)t).ToString()).ToArray()));
            }

            if (spell.Target == SpellTarget.Many && spell.Range != SpellRange.Touch)
            {
                steps.Add(("Shape", shapePicker, null, ["Volume", "Line", "Cone"]));
            }

            if (spell.Source == MagicSource.ElementalMagic)
            {
                steps.Add(("Fuel", tapPicker, null, ["Pack only", "Light in hand", "Fire beside me"]));
            }

            // A Shift is over the moment it lands: there's nothing to hold.
            if (SpellCalculator.CanBeSustained(spell.SpellType)
                && !(spell.SpellType == SpellType.Motion && WoundBound.Game.Combat.SpellArea.MotionOf(spell) == MotionKind.Shift))
            {
                steps.Add(("Hold it?", null, sustainBox, ["Just cast", "Sustain it"]));
            }

            var hint = $"{spell.SpellType} · {spell.Target} · {(spell.Range == SpellRange.Touch ? "Touch" : spell.Shape?.ToString() ?? "Ray")} · {SpellCalculator.SourceLabel(spell.Source)}\n{spell.Notes}";

            void Arm()
            {
                Disarm();
                armedSpell = spell;
            }

            // A spell at one creature ends in a ring of who it could go to; anything else is armed,
            // and placed on the board.
            var aimed = IsAimedAtOne(spell);

            IReadOnlyList<RadialItem> Aim()
            {
                Arm();
                return SpellTargets(caster, spell);
            }

            IReadOnlyList<RadialItem> Step(int index)
            {
                var (_, picker, box, labels) = steps[index];
                var last = index == steps.Count - 1;
                return labels.Select((label, choice) => new RadialItem(label)
                {
                    // Fear, Confusion and Compulsion have icons; Thresholds, Shapes and Fuel stay as words.
                    Icon = picker == effectPicker ? label.ToLowerInvariant() : null,
                    Pick = last && !aimed
                        ? () =>
                        {
                            Choose(picker, box, choice);
                            Arm();
                        }
                        : null,
                    Children = !last || aimed
                        ? () =>
                        {
                            Choose(picker, box, choice);
                            return last ? Aim() : Step(index + 1);
                        }
                        : null,
                }).ToList();
            }

            if (noAction is not null)
            {
                return new RadialItem(spell.Name, noAction, hint);
            }

            if (steps.Count == 0)
            {
                return aimed
                    ? new RadialItem(spell.Name, null, hint) { Children = Aim }
                    : new RadialItem(spell.Name, null, hint + "\nThen click where it goes.") { Pick = Arm };
            }

            return new RadialItem(spell.Name, null, hint) { Children = () => Step(0) };

            static void Choose(OptionButton? picker, CheckBox? box, int choice)
            {
                if (picker is not null)
                {
                    picker.Selected = choice;
                }

                if (box is not null)
                {
                    box.ButtonPressed = choice == 1;
                }
            }
        }

        /// <summary>A Bind above T1, which may be cast for a lower Tier's effect.</summary>
        private static bool IsTieredBind(SpellDefinition spell) =>
            spell.SpellType == SpellType.Motion && WoundBound.Game.Combat.SpellArea.MotionOf(spell) == MotionKind.Bind && spell.SpellTier > Tier.T1;

        /// <summary>A group of items opening a ring of its own — greyed, though still open to look into, when nothing in it can be done.</summary>
        private static RadialItem Group(string label, string hint, IReadOnlyList<RadialItem> children) =>
            new(label, children.Any(c => c.Why is null || c.IsBranch) ? null : "Nothing here can be done right now.", hint)
            {
                Children = () => children,
                Icon = label.ToLowerInvariant(),
            };

        /// <summary>The icon for one of the HUD's buttons (game/assets/icons), or null to show it as words.</summary>
        private string? IconOf(Button button)
        {
            var named = new (Button Button, string Icon)[]
            {
                (raiseShieldButton, "raise-shield"), (swapButton, "swap"), (lightButton, "light"), (dropLightButton, "drop-light"),
                (takeUpButton, "take-up"), (standUpButton, "stand-up"), (breakFreeButton, "break-free"), (regainFootingButton, "regain-footing"), (duckBackButton, "duck-back"),
                (extraActionButton, "extra-action"), (endTurnButton, "end-turn"), (dragButton, "drag"), (letGoButton, "let-go"),
            };

            if (named.FirstOrDefault(n => n.Button == button).Icon is { } icon)
            {
                return icon;
            }

            foreach (var (action, stand) in standGroundButtons)
            {
                if (stand == button)
                {
                    return action.ToString().ToLowerInvariant();
                }
            }

            foreach (var (exploit, held) in exploitButtons)
            {
                if (held == button)
                {
                    return exploit.ToString().ToLowerInvariant();
                }
            }

            return null;
        }

        /// <summary>
        /// An item standing for a HUD button: its label, whether it's enabled, and its tooltip. A
        /// toggle button arms its action (it isn't un-armed by choosing it again from the menu).
        /// </summary>
        /// <param name="why">The reason to show when it's disabled, where the button's tooltip describes the action rather than the refusal.</param>
        private RadialItem Mirror(Button button, string? label = null, string? why = null, string? hint = null)
        {
            var tip = string.IsNullOrWhiteSpace(button.TooltipText) ? null : button.TooltipText;
            var refusal = button.Disabled ? why ?? tip ?? "Not now." : null;
            return new RadialItem(label ?? button.Text, refusal, hint ?? (refusal == tip ? null : tip))
            {
                Icon = IconOf(button),
                Pick = () =>
                {
                    if (!(button.ToggleMode && button.ButtonPressed))
                    {
                        button.EmitSignal(BaseButton.SignalName.Pressed);
                    }
                },
            };
        }

        /// <summary>A casting or attack option that stays on or off: flipped from the ring, which stays open.</summary>
        private RadialItem Toggle(CheckBox box) =>
            new(box.Text, box.Disabled ? "Not available this round." : null, box.TooltipText)
            {
                Icon = box == sacrificeBox ? "sacrifice" : null,
                Pick = () => box.ButtonPressed = !box.ButtonPressed,
                IsOn = () => box.ButtonPressed,
            };
    }
}
