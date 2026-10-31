using System.Collections.Generic;
using System.Linq;
using Godot;
using WoundBound.Game.Combat;
using WoundBound.Game.Data;
using WoundBound.Rules.Calculation;
using Side = WoundBound.Game.Combat.Side;

namespace WoundBoundGame
{
    /// <summary>
    /// The battle screen: draws the board, turns clicks into encounter commands, and shows what
    /// happened. It decides nothing — every rule lives in <see cref="Encounter"/> and the Rules
    /// library under it, so this class can be thrown away and redrawn without touching a formula.
    /// </summary>
    public partial class Battle : Node2D
    {
        private const int CellSize = 72;
        private const double AdversaryTurnDelay = 0.7;
        private static readonly Vector2 BoardOrigin = new(24, 24);

        private static readonly Color LightSquare = new(0.20f, 0.21f, 0.24f);
        private static readonly Color DarkSquare = new(0.17f, 0.18f, 0.21f);
        private static readonly Color GridLine = new(0.28f, 0.29f, 0.33f);
        private static readonly Color OneMove = new(0.25f, 0.45f, 0.75f, 0.45f);
        private static readonly Color TwoMoves = new(0.80f, 0.60f, 0.20f, 0.35f);
        private static readonly Color HoverOutline = new(1f, 1f, 1f, 0.8f);
        private static readonly Color PartyToken = new(0.25f, 0.55f, 0.90f);
        private static readonly Color AdversaryToken = new(0.75f, 0.25f, 0.22f);
        private static readonly Color ActiveRing = new(1f, 0.85f, 0.3f);
        private static readonly Color TargetRing = new(1f, 0.35f, 0.3f);

        private GameData data = null!;
        private Encounter encounter = null!;
        private GridPosition? hovered;
        private int linesShown;
        private bool adversaryTurnPending;

        private Label statusLabel = null!;
        private Label characterLabel = null!;
        private Label hoverLabel = null!;
        private PanelContainer reactionBox = null!;
        private Label reactionLabel = null!;
        private Button swapButton = null!;
        private Button extraActionButton = null!;
        private Button endTurnButton = null!;
        private RichTextLabel logLabel = null!;

        private float BoardPixelWidth => SampleEncounters.BoardWidth * CellSize;

        public override void _Ready()
        {
            data = GameData.Load(file => FileAccess.GetFileAsString($"res://data/rules/{file}"));
            BuildHud();
            StartEncounter();
        }

        public override void _UnhandledInput(InputEvent @event)
        {
            if (@event is InputEventMouseMotion)
            {
                var square = SquareAt(GetLocalMousePosition());
                GridPosition? next = encounter.IsOnBoard(square) ? square : null;
                if (next != hovered)
                {
                    hovered = next;
                    Refresh();
                }

                return;
            }

            if (@event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }
                || encounter.Phase != EncounterPhase.PartyTurn
                || hovered is not { } clicked)
            {
                return;
            }

            if (encounter.At(clicked) is { } occupant)
            {
                encounter.TryAttack(occupant);
            }
            else
            {
                encounter.TryMove(clicked);
            }

            Refresh();
        }

        public override void _Draw()
        {
            var active = encounter.Phase == EncounterPhase.PartyTurn ? encounter.Active : null;
            var reachable = active is null
                ? new Dictionary<GridPosition, int>()
                : encounter.ReachableSquares(active);

            for (var y = 0; y < encounter.Height; y++)
            {
                for (var x = 0; x < encounter.Width; x++)
                {
                    var square = new GridPosition(x, y);
                    var rect = SquareRect(square);
                    DrawRect(rect, (x + y) % 2 == 0 ? LightSquare : DarkSquare);

                    if (reachable.TryGetValue(square, out var cost))
                    {
                        DrawRect(rect, cost == 1 ? OneMove : TwoMoves);
                    }

                    DrawRect(rect, GridLine, filled: false, width: 1);
                }
            }

            if (hovered is { } hover)
            {
                DrawRect(SquareRect(hover).Grow(-2), HoverOutline, filled: false, width: 2);
            }

            foreach (var combatant in encounter.Combatants)
            {
                DrawToken(combatant, active);
            }
        }

        private void DrawToken(Combatant combatant, Combatant? active)
        {
            var font = ThemeDB.FallbackFont;
            var rect = SquareRect(combatant.Position);
            var centre = rect.GetCenter();
            var radius = CellSize * 0.36f;

            if (combatant.IsOutOfFight)
            {
                // Left where it fell, so the board remembers the fight.
                DrawString(font, new Vector2(rect.Position.X, centre.Y + 8), "✕", HorizontalAlignment.Center, CellSize, 28,
                    new Color(0.6f, 0.6f, 0.6f, 0.5f));
                return;
            }

            DrawCircle(centre, radius, combatant.Side == Side.Party ? PartyToken : AdversaryToken);

            if (combatant == encounter.Active)
            {
                DrawArc(centre, radius + 4, 0, Mathf.Tau, 48, ActiveRing, 3);
            }
            else if (active is not null && encounter.CanAttack(active, combatant))
            {
                DrawArc(centre, radius + 4, 0, Mathf.Tau, 48, TargetRing, 3);
            }

            DrawString(font, new Vector2(rect.Position.X, centre.Y + 6), Initials(combatant.Name), HorizontalAlignment.Center,
                CellSize, 18, Colors.White);

            // Body bar along the foot of the square.
            var bar = new Rect2(rect.Position.X + 8, rect.End.Y - 10, CellSize - 16, 5);
            DrawRect(bar, new Color(0, 0, 0, 0.6f));
            var fraction = Mathf.Clamp((float)combatant.Body / combatant.BodyMax, 0, 1);
            DrawRect(new Rect2(bar.Position, new Vector2(bar.Size.X * fraction, bar.Size.Y)), new Color(0.35f, 0.85f, 0.4f));
        }

        private void StartEncounter()
        {
            encounter = SampleEncounters.FighterVersusRatkin(data, new RandomDiceRoller());
            adversaryTurnPending = false;
            linesShown = 0;
            logLabel.Clear();
            Refresh();
        }

        private void Refresh()
        {
            foreach (var line in encounter.Log.Skip(linesShown))
            {
                logLabel.AddText(line + "\n");
            }

            linesShown = encounter.Log.Count;

            UpdateHud();
            QueueRedraw();

            if (encounter.Phase == EncounterPhase.AdversaryTurn && !adversaryTurnPending)
            {
                adversaryTurnPending = true;
                var waitingOn = encounter;
                GetTree().CreateTimer(AdversaryTurnDelay).Timeout += () =>
                {
                    // A restart mid-pause replaces the encounter; the old one's turn is moot.
                    if (waitingOn != encounter)
                    {
                        return;
                    }

                    adversaryTurnPending = false;
                    encounter.RunAdversaryTurn();
                    Refresh();
                };
            }
        }

        /// <summary>Shows the panic-dodge choice while the encounter is waiting on it, and hides it otherwise.</summary>
        private void UpdateReactionPrompt()
        {
            reactionBox.Visible = encounter.Pending is not null;
            if (encounter.Pending is not { } pending)
            {
                return;
            }

            var roll = pending.Roll;
            reactionLabel.Text =
                $"{pending.Attacker.Name}'s {pending.Attacker.Attack.Name} rolls ({string.Join("+", roll.CountedDice)})" +
                $"{(roll.Modifier >= 0 ? "+" : "−")}{System.Math.Abs(roll.Modifier)} = {roll.Total} against your DT {pending.DodgeThreshold}: a hit.\n" +
                $"A panic dodge raises your DT to {pending.RaisedDodgeThreshold} and makes it miss" +
                (pending.WouldRiposte ? " — and it's a critical, so the miss is a Riposte: the attacker takes the wound." : ".") +
                "\nIt costs 1 Stamina and your tactical spend for the round, and hands the GM an Immediate Opportunity.";
        }

        private void UpdateHud()
        {
            statusLabel.Text = $"Round {encounter.Round} · " + encounter.Phase switch
            {
                EncounterPhase.PartyTurn => $"Your turn: {encounter.Active?.Name}",
                EncounterPhase.AdversaryTurn => "The GM is acting…",
                EncounterPhase.AwaitingReaction => "React?",
                EncounterPhase.Victory => "Victory!",
                _ => "Defeat."
            } + $"\nGM Opportunities: {encounter.BankedOpportunities}/{Encounter.MaxBankedOpportunities} banked" +
              (encounter.ImmediateOpportunities > 0 ? $", {encounter.ImmediateOpportunities} immediate" : string.Empty);

            UpdateReactionPrompt();

            var character = encounter.Combatants.First(c => c.Side == Side.Party);
            var attack = character.Attack;
            var armourAbsorbs = InventoryCalculator.ArmourDamageAbsorbed(character.Armour);
            characterLabel.Text =
                $"{character.Name}\n" +
                $"Body {character.Body}/{character.BodyMax} ({character.BodyStatus})   Stamina {character.Stamina}/{character.StaminaMax}\n" +
                $"Tactical spends this round: {character.TacticalSpendsThisRound}/{character.TacticalSpendsPerRound}\n" +
                $"Move: {(character.HasMove ? "available" : "used")}   Actions left: {character.ActionsRemaining}\n" +
                $"In hand: {attack.Name} +{attack.AttackModifier}, 1d{attack.DamageDieSides}+{attack.DamageModifier}" +
                (attack.IsRanged ? " (ranged — not at Close)" : $" (Reach {attack.Reach})") + "\n" +
                $"At the ready: {character.ReadyWeapon?.Name ?? "nothing"}\n" +
                $"DT {character.DodgeThreshold} · {character.Armour} armour (absorbs {armourAbsorbs})";

            var isPartyTurn = encounter.Phase == EncounterPhase.PartyTurn && encounter.Active is not null;
            hoverLabel.Text = DescribeHovered(isPartyTurn ? encounter.Active : null);

            var refusal = isPartyTurn ? encounter.ExtraActionRefusal(encounter.Active!) : null;
            extraActionButton.Disabled = !isPartyTurn || refusal is not null;
            extraActionButton.TooltipText = refusal ?? "Gain an extra action. Hands the GM a banked Opportunity.";

            var swapRefusal = isPartyTurn ? Encounter.SwapRefusal(encounter.Active!) : null;
            swapButton.Text = $"Swap to {character.ReadyWeapon?.Name ?? "empty hands"}";
            swapButton.Disabled = !isPartyTurn || swapRefusal is not null;
            swapButton.TooltipText = swapRefusal ?? "Costs a move (or an action once the move is spent). Not movement, and no Opportunity for the GM.";

            endTurnButton.Disabled = !isPartyTurn;
        }

        /// <summary>What clicking the square under the mouse would do, or how to play when it's over nothing useful.</summary>
        private string DescribeHovered(Combatant? active)
        {
            const string howToPlay =
                "You are the blue token (yellow ring on your turn).\n" +
                "• Move: click a highlighted square. Blue costs your move; amber spends your action too.\n" +
                "• Swap: trade the loadout in hand for the one at the ready, for a move.\n" +
                "• Attack: click a ratkin with a red ring — in Reach for a blade, beyond Close for a bow.\n" +
                "• End turn when you're done, and the GM moves one ratkin.";

            if (hovered is not { } square)
            {
                return howToPlay;
            }

            if (encounter.At(square) is { Side: Side.Adversaries } foe)
            {
                var stats = $"{foe.Name}: Body {foe.Body}/{foe.BodyMax} · DT {foe.DodgeThreshold} · " +
                            $"{foe.Attack.Name} +{foe.Attack.AttackModifier}, {foe.Attack.DamageDiceCount}d{foe.Attack.DamageDieSides}" +
                            (foe.IsMinion ? "\nMinion: any hit that gets past its armour drops it." : string.Empty);

                if (active is null)
                {
                    return stats;
                }

                var plan = encounter.PlanAttack(active, foe);
                if (!plan.CanAttack)
                {
                    return stats + $"\n{plan.Refusal}";
                }

                var damage = plan.DamageDieSides is { } die
                    ? $"1d{die}+{active.Attack.DamageModifier}"
                    : $"no die, just +{active.Attack.DamageModifier}";
                return stats + (active.ActionsRemaining > 0
                    ? $"\nClick to attack: 2d12+{active.Attack.AttackModifier} vs DT {plan.DodgeThreshold}, {damage}."
                    : "\nNo action left to attack with — spend Stamina for another, or end your turn.") +
                    (active.Attack.IsRanged ? $"\n{plan.Note}" : string.Empty);
            }

            if (active is not null && encounter.At(square) is null)
            {
                var squares = active.Position.DistanceTo(square);
                return encounter.MoveCostTo(active, square) switch
                {
                    1 => $"Click to move {squares} square(s). Costs your move.",
                    { } cost => $"Click to move {squares} squares. Costs {cost} moves — your move and your action, so no attack after.",
                    null => Encounter.MovesAvailable(active) == 0
                        ? "Nothing left to move with this turn."
                        : "Too far to reach with what you have left this turn."
                };
            }

            return howToPlay;
        }

        private void BuildHud()
        {
            var layer = new CanvasLayer();
            AddChild(layer);

            var panel = new PanelContainer
            {
                Position = new Vector2(BoardOrigin.X * 2 + BoardPixelWidth, BoardOrigin.Y),
                CustomMinimumSize = new Vector2(1280 - BoardPixelWidth - BoardOrigin.X * 3, 720 - BoardOrigin.Y * 2),
            };
            layer.AddChild(panel);

            var margin = new MarginContainer();
            foreach (var side in new[] { "left", "right", "top", "bottom" })
            {
                margin.AddThemeConstantOverride($"margin_{side}", 12);
            }

            panel.AddChild(margin);

            var column = new VBoxContainer();
            column.AddThemeConstantOverride("separation", 10);
            margin.AddChild(column);

            statusLabel = WrappingLabel(column);
            statusLabel.AddThemeFontSizeOverride("font_size", 20);
            characterLabel = WrappingLabel(column);
            hoverLabel = WrappingLabel(column);
            hoverLabel.AddThemeColorOverride("font_color", new Color(0.75f, 0.75f, 0.8f));

            var buttons = new HFlowContainer();
            buttons.AddThemeConstantOverride("h_separation", 8);
            buttons.AddThemeConstantOverride("v_separation", 8);
            column.AddChild(buttons);

            swapButton = new Button();
            swapButton.Pressed += () =>
            {
                encounter.TrySwapLoadout();
                Refresh();
            };
            buttons.AddChild(swapButton);

            extraActionButton = new Button { Text = "+1 action (1 Stamina)" };
            extraActionButton.Pressed += () =>
            {
                encounter.TryBuyExtraAction();
                Refresh();
            };
            buttons.AddChild(extraActionButton);

            endTurnButton = new Button { Text = "End turn" };
            endTurnButton.Pressed += () =>
            {
                encounter.EndTurn();
                Refresh();
            };
            buttons.AddChild(endTurnButton);

            var restartButton = new Button { Text = "Restart" };
            restartButton.Pressed += StartEncounter;
            buttons.AddChild(restartButton);

            reactionBox = new PanelContainer { Visible = false };
            reactionBox.AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                BgColor = new Color(0.35f, 0.12f, 0.10f),
                ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 8, ContentMarginBottom = 8,
            });
            column.AddChild(reactionBox);

            var reactionColumn = new VBoxContainer();
            reactionColumn.AddThemeConstantOverride("separation", 8);
            reactionBox.AddChild(reactionColumn);

            reactionLabel = WrappingLabel(reactionColumn);

            var reactionButtons = new HBoxContainer();
            reactionButtons.AddThemeConstantOverride("separation", 8);
            reactionColumn.AddChild(reactionButtons);

            var dodgeButton = new Button { Text = "Panic dodge (1 Stamina)" };
            dodgeButton.Pressed += () =>
            {
                encounter.RespondToReaction(dodge: true);
                Refresh();
            };
            reactionButtons.AddChild(dodgeButton);

            var takeItButton = new Button { Text = "Take the hit" };
            takeItButton.Pressed += () =>
            {
                encounter.RespondToReaction(dodge: false);
                Refresh();
            };
            reactionButtons.AddChild(takeItButton);

            logLabel = new RichTextLabel
            {
                ScrollFollowing = true,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill,
                SelectionEnabled = true,
            };
            column.AddChild(logLabel);
        }

        private static Label WrappingLabel(Container parent)
        {
            var label = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
            parent.AddChild(label);
            return label;
        }

        private static Rect2 SquareRect(GridPosition square) =>
            new(BoardOrigin + new Vector2(square.X, square.Y) * CellSize, new Vector2(CellSize, CellSize));

        private static GridPosition SquareAt(Vector2 point)
        {
            var local = (point - BoardOrigin) / CellSize;
            return new GridPosition(Mathf.FloorToInt(local.X), Mathf.FloorToInt(local.Y));
        }

        private static string Initials(string name) =>
            string.Concat(name.Split(' ').Where(w => w != "The" && w.Length > 0).Select(w => w[0]));
    }
}
