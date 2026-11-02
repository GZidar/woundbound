using System.Collections.Generic;
using System.Linq;
using Godot;
using WoundBound.Game.Combat;
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
                Group("Attack", "Strike a foe, with a manoeuvre if your weapon allows one.", AttackItems(acting)),
            };

            if (acting.Spells.Count > 0)
            {
                items.Add(Group("Magic", "Cast a spell. Its choices come next, then click where it goes.", MagicItems(acting)));
            }

            items.Add(Group("Hands-on", "Grab, Shove, Trip, Drag — rolled unarmed against Stability.", HandsOnItems()));
            items.Add(Group("Aid", "Help, Harry, or tend a wound.", aidButtons.Values.Select(b => Mirror(b, b.Text, "Nobody you could do it for is beside you.")).ToList()));
            items.Add(Group("Defend", "Stand your ground, or raise a shield.",
                standGroundButtons.Values.Append(raiseShieldButton).Select(b => Mirror(b)).ToList()));

            if (acting.IsNimble)
            {
                items.Add(Group("Exploits", "A Nimble character's held edges: 1 Stamina each.", exploitButtons.Values.Select(b => Mirror(b)).ToList()));
            }

            items.Add(Group("Gear", "Weapons and lights.", new[] { swapButton, lightButton, dropLightButton, takeUpButton }
                .Where(b => b.Visible).Select(b => Mirror(b)).ToList()));

            // What only applies now and then is offered only when it does.
            foreach (var button in new[] { standUpButton, breakFreeButton, regainFootingButton })
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
            var items = new List<RadialItem>
            {
                new($"Strike ({acting.Attack.Name})", noAction, "Then click a foe. (Clicking a foe without the menu does the same.)")
                {
                    Pick = Disarm,
                },
            };

            items.AddRange(manoeuvreButtons.Values.Where(b => b.Visible).Select(b => Mirror(b, why: noAction)));
            if (sacrificeBox.Visible)
            {
                items.Add(Toggle(sacrificeBox));
            }

            return items;
        }

        private List<RadialItem> HandsOnItems() =>
            controlButtons.Values.Select(b => Mirror(b, why: "No foe beside you it would work on."))
                .Append(Mirror(dragButton, why: "You aren't holding anyone."))
                .Append(Mirror(letGoButton, why: "You aren't holding anyone."))
                .ToList();

        private List<RadialItem> MagicItems(Combatant caster)
        {
            var noAction = caster.ActionsRemaining < 1 ? "No action left this turn." : null;
            var items = caster.Spells.Select(spell => SpellItem(spell, noAction)).ToList();

            var options = new[] { payInBodyBox, overclockBox, backlashBox }.Where(b => b.Visible).ToList();
            if (options.Count > 0)
            {
                items.Add(new RadialItem("How to pay", null, "Bleeding for Power, Overclock, Backlash: on or off for the casts that follow.")
                {
                    Children = () => options.Select(Toggle).ToList(),
                });
            }

            return items;
        }

        /// <summary>
        /// A spell, and the decisions its casting asks for, one ring each: what a Control does, which
        /// Threshold a Buff or Bane moves, the Shape of an area, where Fuel comes from, and whether to
        /// hold it. The last choice arms it; the next click is where it goes.
        /// </summary>
        private RadialItem SpellItem(SpellDefinition spell, string? noAction)
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

            if (spell.Target == SpellTarget.Many && spell.Range != SpellRange.Touch)
            {
                steps.Add(("Shape", shapePicker, null, ["Volume", "Line", "Cone"]));
            }

            if (spell.Source == MagicSource.ElementalMagic)
            {
                steps.Add(("Fuel", tapPicker, null, ["Pack only", "Light in hand", "Fire beside me"]));
            }

            if (SpellCalculator.CanBeSustained(spell.SpellType))
            {
                steps.Add(("Hold it?", null, sustainBox, ["Just cast", "Sustain it"]));
            }

            var hint = $"{spell.SpellType} · {spell.Target} · {(spell.Range == SpellRange.Touch ? "Touch" : spell.Shape?.ToString() ?? "Ray")} · {SpellCalculator.SourceLabel(spell.Source)}\n{spell.Notes}";

            void Arm()
            {
                Disarm();
                armedSpell = spell;
            }

            IReadOnlyList<RadialItem> Step(int index)
            {
                var (_, picker, box, labels) = steps[index];
                return labels.Select((label, choice) => new RadialItem(label)
                {
                    Pick = index == steps.Count - 1
                        ? () =>
                        {
                            Choose(picker, box, choice);
                            Arm();
                        }
                        : null,
                    Children = index < steps.Count - 1
                        ? () =>
                        {
                            Choose(picker, box, choice);
                            return Step(index + 1);
                        }
                        : null,
                }).ToList();
            }

            if (steps.Count == 0 || noAction is not null)
            {
                return new RadialItem(spell.Name, noAction, hint) { Pick = Arm };
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

        /// <summary>A group of items opening a ring of its own — greyed, though still open to look into, when nothing in it can be done.</summary>
        private static RadialItem Group(string label, string hint, IReadOnlyList<RadialItem> children) =>
            new(label, children.Any(c => c.Why is null || c.IsBranch) ? null : "Nothing here can be done right now.", hint)
            {
                Children = () => children,
            };

        /// <summary>
        /// An item standing for a HUD button: its label, whether it's enabled, and its tooltip. A
        /// toggle button arms its action (it isn't un-armed by choosing it again from the menu).
        /// </summary>
        /// <param name="why">The reason to show when it's disabled, where the button's tooltip describes the action rather than the refusal.</param>
        private static RadialItem Mirror(Button button, string? label = null, string? why = null, string? hint = null)
        {
            var tip = string.IsNullOrWhiteSpace(button.TooltipText) ? null : button.TooltipText;
            var refusal = button.Disabled ? why ?? tip ?? "Not now." : null;
            return new RadialItem(label ?? button.Text, refusal, hint ?? (refusal == tip ? null : tip))
            {
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
        private static RadialItem Toggle(CheckBox box) =>
            new(box.Text, box.Disabled ? "Not available this round." : null, box.TooltipText)
            {
                Pick = () => box.ButtonPressed = !box.ButtonPressed,
                IsOn = () => box.ButtonPressed,
            };
    }
}
