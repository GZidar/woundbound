using System.Globalization;
using Godot;
using WoundBound.Game.Combat;
using WoundBound.Game.Combat.Brains.Utility;

namespace WoundBoundGame
{
    /// <summary>
    /// How the GM brain reads at the table (the user, 2026-11-08): the scene brief for everyone,
    /// with how the GM side's goal stands; pauses for a human GM to narrate each GM action before it
    /// takes effect and again once it lands; and the director's state, for the GM's eyes only,
    /// behind a toggle that starts off.
    /// </summary>
    public partial class Battle
    {
        private Label briefLabel = null!;
        private CheckButton narrateToggle = null!;
        private CheckButton directorToggle = null!;
        private Label directorLabel = null!;
        private PanelContainer narrateBox = null!;
        private Label narrateLabel = null!;

        /// <summary>The brief, under the round and phase: why the fight is happening and how the GM side's goal stands.</summary>
        private void BuildBrief(Container column)
        {
            briefLabel = WrappingLabel(column);
            briefLabel.AddThemeColorOverride("font_color", new Color(0.86f, 0.80f, 0.66f));
            briefLabel.Visible = false;
        }

        /// <summary>The table's controls, under the GM pickers: narration pauses, and the director's readout.</summary>
        private void BuildTableControls(Container column)
        {
            narrateToggle = new CheckButton
            {
                Text = "Narrate: pause on each GM action",
                TooltipText = "For a human GM at the table: the fight stops when a GM creature declares its action, so you can read it out, and again once it lands, so you can narrate what happened.",
            };
            narrateToggle.Toggled += on =>
            {
                encounter.NarrationPauses = on;
                if (!on && encounter.AwaitingNarration)
                {
                    encounter.ContinueNarration();
                }

                Refresh();
            };
            column.AddChild(narrateToggle);

            directorToggle = new CheckButton
            {
                Text = "Show the director (GM only)",
                TooltipText = "How hard the GM is playing for the kill, what it is steering towards, and how far ahead the party is. Keep it hidden from the players.",
            };
            directorToggle.Toggled += _ => Refresh();
            column.AddChild(directorToggle);
            directorLabel = WrappingLabel(column);
            directorLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.75f, 0.85f));
            directorLabel.Visible = false;

            narrateBox = new PanelContainer { Visible = false };
            narrateBox.AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                BgColor = new Color(0.16f, 0.18f, 0.26f),
                ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 8, ContentMarginBottom = 8,
            });
            column.AddChild(narrateBox);
            var inner = new VBoxContainer();
            inner.AddThemeConstantOverride("separation", 8);
            narrateBox.AddChild(inner);
            narrateLabel = WrappingLabel(inner);
            var go = new Button { Text = "Continue" };
            go.Pressed += () =>
            {
                encounter.ContinueNarration();
                Refresh();
            };
            inner.AddChild(go);
        }

        /// <summary>A new fight takes the table's setting: pauses only for a human GM, never in a recorded demo.</summary>
        private void ApplyTableSettings() => encounter.NarrationPauses = narrateToggle.ButtonPressed && !demo;

        private void UpdateTable()
        {
            if (encounter.Brief is { } brief)
            {
                briefLabel.Visible = true;
                briefLabel.Text = $"{brief.Stakes} · {brief.Commitment}" + (brief.Motive.Length > 0 ? $"\n{brief.Motive}" : string.Empty)
                                  + (encounter.GoalProgress is { } progress ? $"\n{progress}" : string.Empty);
            }
            else
            {
                briefLabel.Visible = false;
            }

            narrateBox.Visible = encounter.AwaitingNarration;
            if (encounter.AwaitingNarration)
            {
                narrateLabel.Text = encounter.Pending!.Prompt;
            }

            directorLabel.Visible = directorToggle.ButtonPressed;
            if (directorToggle.ButtonPressed)
            {
                directorLabel.Text = encounter.Gm is UtilityGmBrain gm
                    ? $"Director: {(gm.Intent.Steer is { } steer ? $"steering for {steer}" : gm.Intent.FinishesTheDowned ? "Merciless" : "fixed")}"
                      + $" · ruthlessness {gm.Ruthlessness.ToString("0.00", CultureInfo.InvariantCulture)}"
                      + $" · party lead {gm.Lead.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture)}"
                      + (gm.Naive ? " · novice habits" : string.Empty)
                    : "Director: none (this GM plays a fixed script).";
            }
        }
    }
}
