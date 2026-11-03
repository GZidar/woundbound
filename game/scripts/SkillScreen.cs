using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using WoundBound.Game.Combat;
using WoundBound.Game.Data;
using WoundBound.Game.Skills;
using WoundBound.Rules.Calculation;
using WoundBound.Rules.Enums;

namespace WoundBoundGame
{
    /// <summary>
    /// A Skill Challenge at the table (Core Rules §8): the obstacle being worked — its Type,
    /// Behavior and Shape, its Stakes and any Opposition, the hurdle and the tally of successes and
    /// failures — the party with their tracks and Stamina, the question the rules put to the players
    /// right now with its answers, and the log. The challenge plays on a worker thread and stops to
    /// wait at every player decision; or the bot plays the party, at a pace that can be followed.
    /// </summary>
    public partial class SkillScreen : Control
    {
        private static readonly Color Bone = Color.FromHtml("#e8e6e3");
        private static readonly Color Gold = Color.FromHtml("#d8a657");
        private static readonly Color Rust = Color.FromHtml("#c04a10");
        private static readonly Color PartyBlue = Color.FromHtml("#56b4e9");
        private static readonly Color[] PartyColours =
        [
            Color.FromHtml("#56b4e9"), Color.FromHtml("#e69f00"), Color.FromHtml("#009e73"),
            Color.FromHtml("#cc79a7"), Color.FromHtml("#f0e442"), Color.FromHtml("#0072b2"),
        ];

        private static readonly (string Key, string Label)[] Challenges =
        [
            ("wagon", "The wagon: Cross the River, then repair it (Quickstart)"),
            ("stair", "The Collapsing Stair (Active, Ramps)"),
            ("snare", "The Piston Snare (Active, triggered)"),
            ("vent", "The Weeping Vent (Active, against Fortitude)"),
        ];

        private GameData data = null!;
        private OptionButton picker = null!;
        private CheckButton botToggle = null!;
        private Label obstacleTitle = null!;
        private RichTextLabel obstacleBody = null!;
        private RichTextLabel partyBody = null!;
        private Label question = null!;
        private VBoxContainer answers = null!;
        private RichTextLabel log = null!;

        private SkillChallenge? challenge;
        private List<Combatant> party = new();
        private HumanSkillBrain? human;
        private CancellationTokenSource? running;
        private SkillChallengeResult? finished;
        private readonly ConcurrentQueue<string> incoming = new();
        private SkillChoice? shown;

        public override void _Ready()
        {
            data = GameData.Load(file => FileAccess.GetFileAsString($"res://data/rules/{file}"));
            SetAnchorsPreset(LayoutPreset.FullRect);
            AddChild(new ColorRect { Color = new Color(0.09f, 0.09f, 0.11f), MouseFilter = MouseFilterEnum.Ignore, AnchorRight = 1, AnchorBottom = 1 });

            var margin = new MarginContainer { AnchorRight = 1, AnchorBottom = 1 };
            foreach (var side in new[] { "left", "right", "top", "bottom" })
            {
                margin.AddThemeConstantOverride($"margin_{side}", 14);
            }

            AddChild(margin);
            var column = new VBoxContainer();
            column.AddThemeConstantOverride("separation", 10);
            margin.AddChild(column);

            // The bar: which challenge, who plays the party, start again, back to the fights.
            var bar = new HBoxContainer();
            bar.AddThemeConstantOverride("separation", 10);
            column.AddChild(bar);
            bar.AddChild(new Label { Text = "Skill Challenge:" });
            picker = new OptionButton();
            foreach (var (_, label) in Challenges)
            {
                picker.AddItem(label);
            }

            picker.ItemSelected += _ => Begin();
            bar.AddChild(picker);
            botToggle = new CheckButton { Text = "Bot plays the party" };
            botToggle.Toggled += _ => Begin();
            bar.AddChild(botToggle);
            var restart = new Button { Text = "Start again" };
            restart.Pressed += Begin;
            bar.AddChild(restart);
            var back = new Button { Text = "Back to the fights" };
            back.Pressed += () =>
            {
                running?.Cancel();
                GetTree().ChangeSceneToFile("res://scenes/Battle.tscn");
            };
            bar.AddChild(back);

            var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
            body.AddThemeConstantOverride("separation", 14);
            column.AddChild(body);

            // Left: the obstacle, then the party.
            var left = new VBoxContainer { CustomMinimumSize = new Vector2(500, 0) };
            left.AddThemeConstantOverride("separation", 8);
            body.AddChild(left);
            obstacleTitle = new Label();
            obstacleTitle.AddThemeFontSizeOverride("font_size", 22);
            obstacleTitle.AddThemeColorOverride("font_color", Gold);
            left.AddChild(obstacleTitle);
            obstacleBody = new RichTextLabel { BbcodeEnabled = true, FitContent = true, CustomMinimumSize = new Vector2(500, 220), ScrollActive = false };
            left.AddChild(Panel(obstacleBody));
            partyBody = new RichTextLabel { BbcodeEnabled = true, FitContent = true, CustomMinimumSize = new Vector2(500, 150), ScrollActive = false };
            left.AddChild(Panel(partyBody));

            // Right: the question, then the log.
            var right = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            right.AddThemeConstantOverride("separation", 8);
            body.AddChild(right);
            var ask = new VBoxContainer();
            ask.AddThemeConstantOverride("separation", 6);
            question = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
            question.AddThemeColorOverride("font_color", Bone);
            question.AddThemeFontSizeOverride("font_size", 16);
            ask.AddChild(question);
            answers = new VBoxContainer();
            answers.AddThemeConstantOverride("separation", 4);
            ask.AddChild(answers);
            right.AddChild(Panel(ask, new Color(0.13f, 0.12f, 0.08f)));
            log = new RichTextLabel { BbcodeEnabled = true, ScrollFollowing = true, SizeFlagsVertical = SizeFlags.ExpandFill };
            right.AddChild(Panel(log));

            if (OS.GetCmdlineUserArgs() is var args && Array.IndexOf(args, "--skill") is var at and >= 0 && at + 1 < args.Length)
            {
                var index = Array.FindIndex(Challenges, c => c.Key == args[at + 1]);
                picker.Select(Math.Max(0, index));
            }

            botToggle.ButtonPressed = OS.GetCmdlineUserArgs().Contains("--bot");
            Begin();
        }

        private static PanelContainer Panel(Control inside, Color? colour = null)
        {
            var panel = new PanelContainer();
            panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                BgColor = colour ?? new Color(0.12f, 0.12f, 0.15f),
                ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 10, ContentMarginBottom = 10,
                CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4, CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
            });
            panel.AddChild(inside);
            if (inside is RichTextLabel { FitContent: false } grows)
            {
                panel.SizeFlagsVertical = SizeFlags.ExpandFill;
            }

            return panel;
        }

        /// <summary>Starts the chosen challenge afresh, with the Quickstart five at full strength.</summary>
        private void Begin()
        {
            running?.Cancel();
            running = new CancellationTokenSource();
            var token = running.Token;
            finished = null;
            shown = null;
            while (incoming.TryDequeue(out _))
            {
            }

            log.Clear();
            party =
            [
                CombatantFactory.FromCharacter(SampleEncounters.SallaRinehart(data), new GridPosition(0, 0)),
                CombatantFactory.FromCharacter(SampleEncounters.DrovanThale(data), new GridPosition(1, 0)),
                CombatantFactory.FromCharacter(SampleEncounters.JorenBlacke(data), new GridPosition(2, 0)),
                CombatantFactory.FromCharacter(SampleEncounters.MirellaVask(data), new GridPosition(3, 0)),
                CombatantFactory.FromCharacter(SampleEncounters.ElthricDane(data), new GridPosition(4, 0)),
            ];
            var key = Challenges[Math.Max(0, picker.Selected)].Key;
            var sequence = key switch
            {
                "stair" => SampleSkillChallenges.TheStair(),
                "snare" => SampleSkillChallenges.TheSnare(),
                "vent" => SampleSkillChallenges.TheVent(),
                _ => SampleSkillChallenges.TheWagon(),
            };

            human = botToggle.ButtonPressed ? null : new HumanSkillBrain(token);
            SkillPartyBrain brain = human is not null ? human : new PacedSkillBrain(700, token);
            var played = new SkillChallenge(party, sequence, new RandomDiceRoller(new Random()), brain);
            played.Logged += line => incoming.Enqueue(line);
            challenge = played;
            Task.Run(() =>
            {
                try
                {
                    var result = played.Run();
                    if (!token.IsCancellationRequested)
                    {
                        finished = result;
                    }
                }
                catch (OperationCanceledException)
                {
                    // Started again, or left: the old challenge is simply abandoned.
                }
            }, token);
        }

        public override void _Process(double delta)
        {
            while (incoming.TryDequeue(out var line))
            {
                log.AppendText(Format(line) + "\n");
            }

            ShowObstacle();
            ShowParty();
            ShowQuestion();
        }

        private static string Format(string line) =>
            line.StartsWith("— ") ? $"\n[b][color=#d8a657]{line}[/color][/b]"
            : line.StartsWith("Hurdle") ? $"[color=#d8a657]{line}[/color]"
            : line.Contains("Opposition") || line.StartsWith("The GM") ? $"[color=#e07a4a]{line}[/color]"
            : line.Contains("it's done") || line.Contains("gives something away") || line.Contains("beaten") ? $"[color=#9fd3a8]{line}[/color]"
            : line.Contains("it fails") || line.Contains("lost") ? $"[color=#e8a0a0]{line}[/color]"
            : line;

        private void ShowObstacle()
        {
            if (challenge?.Current is not { } obstacle)
            {
                obstacleTitle.Text = "…";
                return;
            }

            var p = challenge.Progress;
            obstacleTitle.Text = obstacle.Name;
            var moment = obstacle.Shape == SkillAdversaryShape.Moment;
            var lines = new List<string>
            {
                $"[i]{obstacle.Type} — {obstacle.Behavior} {obstacle.Shape}[/i]",
                "Stakes: " + obstacle.StakesKind switch
                {
                    SkillStakesKind.Wound => $"{obstacle.StakesWoundType} wounds{(obstacle.StakesIsDirect ? " (Direct)" : string.Empty)}, by how far a check misses",
                    SkillStakesKind.Stamina => "Stamina, by how far a check misses",
                    _ => string.IsNullOrWhiteSpace(obstacle.StakesStoryText) ? "a Story consequence" : obstacle.StakesStoryText,
                },
            };
            if (obstacle.Behavior == SkillAdversaryBehavior.Active)
            {
                lines.Add($"[color=#e07a4a]Opposition: {Signed(obstacle.OppositionModifier)} v {obstacle.OppositionVsThreshold} — "
                          + (SkillChallenge.TimingOf(obstacle) == OppositionTiming.EachRound ? "declared each round (it can be Interrupted)" : "fired by a failed check")
                          + "[/color]");
            }

            lines.Add(string.Empty);
            lines.Add(moment ? "A Moment: one success before one failure." : $"Hurdle {Math.Min(p.Hurdle + 1, p.Required)} of {p.Required}" + (p.HurdleName is { } h ? $": {h}" : string.Empty));
            lines.Add("Cleared  " + Pips(p.Successes, p.Required, "●", "○", "#9fd3a8") + "      Failures  " + Pips(p.Failures, p.Allowed, "✕", "□", "#e8a0a0"));
            lines.Add($"Round {challenge.Round}      GM Opportunities: {challenge.Opportunities}/3");
            if (finished is { } done)
            {
                lines.Add(string.Empty);
                lines.Add(done.Cleared ? $"[b][color=#9fd3a8]Done — {done.Ending}[/color][/b]" : $"[b][color=#e8a0a0]Over — {done.Ending}[/color][/b]");
            }

            obstacleBody.Text = string.Join("\n", lines);
        }

        private void ShowParty()
        {
            var rows = party.Select((c, i) =>
            {
                var colour = PartyColours[i % PartyColours.Length].ToHtml(false);
                var down = c.Body <= 0 ? " [color=#e8a0a0](down)[/color]" : c.Stamina <= 0 ? " [color=#e8a0a0](spent)[/color]" : string.Empty;
                return $"[color=#{colour}][b]{c.Name}[/b][/color]{down}\n   Body {c.Body}/{c.BodyMax}   Mind {c.Mind}/{c.MindMax}   Soul {c.Soul}/{c.SoulMax}   Stamina {c.Stamina}/{c.StaminaMax}";
            });
            partyBody.Text = string.Join("\n", rows);
        }

        private void ShowQuestion()
        {
            var pending = human?.Pending;
            if (ReferenceEquals(pending, shown))
            {
                return;
            }

            shown = pending;
            foreach (var child in answers.GetChildren())
            {
                child.QueueFree();
            }

            if (pending is null)
            {
                question.Text = finished is not null ? "The challenge is over. Start again, or pick another."
                    : botToggle.ButtonPressed ? "The bot is playing the party." : "…";
                return;
            }

            question.Text = pending.Question;
            for (var i = 0; i < pending.Options.Count; i++)
            {
                var (label, hint) = pending.Options[i];
                var index = i;
                var button = new Button { Text = label, TooltipText = hint ?? string.Empty, Alignment = HorizontalAlignment.Left };
                button.Pressed += () => human?.Answer(index);
                answers.AddChild(button);
            }
        }

        private static string Pips(int have, int of, string full, string empty, string colour) =>
            $"[color={colour}]{string.Concat(Enumerable.Repeat(full + " ", Math.Min(have, of)))}[/color]{string.Concat(Enumerable.Repeat(empty + " ", Math.Max(0, of - have)))}";

        private static string Signed(int n) => n >= 0 ? $"+{n}" : $"−{-n}";

        public override void _ExitTree() => running?.Cancel();
    }
}
