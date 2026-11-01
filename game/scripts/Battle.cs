using System.Collections.Generic;
using System.Linq;
using Godot;
using WoundBound.Game.Combat;
using WoundBound.Game.Combat.Brains;
using WoundBound.Game.Combat.Brains.Utility;
using WoundBound.Game.Data;
using WoundBound.Rules.Calculation;
using WoundBound.Rules.Catalog;
using WoundBound.Rules.Enums;
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
        /// <summary>The room the board is drawn in; a big field gets smaller squares to fit it.</summary>
        private static readonly Vector2 BoardArea = new(800, 672);
        private const int LargestCell = 72;
        private const double AdversaryTurnDelay = 0.7;
        private const double BotStepDelay = 0.4;
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
        private static readonly Color ChoosableRing = new(0.6f, 0.85f, 1f);

        private GameData data = null!;
        private Encounter encounter = null!;
        private GridPosition? hovered;
        private int linesShown;
        private bool stepPending;

        /// <summary>Plays the party when Bot v Bot is on; null while a person is at the controls.</summary>
        private BotDriver? bot;
        private CheckButton botToggle = null!;
        private OptionButton tacticPicker = null!;
        private OptionButton gmPicker = null!;

        /// <summary>The GM for a new fight, from the picker: the utility GM at each skill, or the simple placeholder.</summary>
        private IGmBrain PickedGm() => gmPicker.Selected switch
        {
            1 => new UtilityGmBrain(BrainSkill.Balanced, new System.Random()),
            2 => new UtilityGmBrain(BrainSkill.Poor, new System.Random()),
            3 => new SimpleGmBrain(),
            _ => new UtilityGmBrain(BrainSkill.Best, new System.Random()),
        };
        private OptionButton fightPicker = null!;
        private HFlowContainer chooseRow = null!;
        private Label partyLabel = null!;

        /// <summary>The character whose sheet is shown: whoever is acting, else the last one hovered or acting.</summary>
        private Combatant? shown;
        private Button panicDodgeButton = null!;
        private Button takeHitButton = null!;

        private Label statusLabel = null!;
        private Label characterLabel = null!;
        private Label hoverLabel = null!;
        private PanelContainer reactionBox = null!;
        private Label reactionLabel = null!;
        private Button swapButton = null!;
        private Button extraActionButton = null!;
        private readonly Dictionary<CombatAction, Button> standGroundButtons = new();
        private Button endTurnButton = null!;
        private readonly Dictionary<ControlAction, Button> controlButtons = new();
        private Button standUpButton = null!;
        private Button breakFreeButton = null!;
        private Button letGoButton = null!;
        private HFlowContainer leaverButtons = null!;
        private string leaverButtonsFor = string.Empty;

        /// <summary>Grab, Shove or Trip, chosen from its button; the next foe clicked is the target. Null for a plain attack.</summary>
        private ControlAction? armed;

        /// <summary>Help, Harry, Heal, Meditate, Pray or Shield, chosen from its button; the next creature clicked is who it's for.</summary>
        private CombatAction? armedAction;

        /// <summary>The Drag button is down: the next empty square clicked is where to haul the held creature.</summary>
        private bool armedDrag;

        private readonly Dictionary<CombatAction, Button> aidButtons = new();
        private Button raiseShieldButton = null!;
        private Button dragButton = null!;

        private void Disarm()
        {
            armed = null;
            armedAction = null;
            armedDrag = false;
        }

        /// <summary>Whether the armed Help, Harry, Heal, Meditate, Pray or Shield could be aimed at <paramref name="target"/>.</summary>
        private static string? ArmedRefusal(Combatant actor, CombatAction action, Combatant target) => action switch
        {
            CombatAction.Help or CombatAction.Harry => Encounter.AidRefusal(actor, action, target),
            CombatAction.Shield => Encounter.ShieldRefusal(actor, target),
            _ => Encounter.TendRefusal(actor, action, target),
        };
        private RichTextLabel logLabel = null!;

        /// <summary>How big a square is drawn: as big as fits the board area, up to <see cref="LargestCell"/>.</summary>
        private int CellSize => encounter is null
            ? LargestCell
            : Mathf.Min(LargestCell, Mathf.Min((int)BoardArea.X / encounter.Width, (int)BoardArea.Y / encounter.Height));

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
                || bot is not null
                || hovered is not { } clicked)
            {
                return;
            }

            // Choosing who goes next: click one of the characters still to act.
            if (encounter.Phase == EncounterPhase.ChoosingCharacter)
            {
                if (encounter.At(clicked) is { } chosen)
                {
                    encounter.TryBeginTurn(chosen);
                    Refresh();
                }

                return;
            }

            if (encounter.Phase != EncounterPhase.PartyTurn)
            {
                return;
            }

            if (encounter.At(clicked) is { } occupant)
            {
                if (armed is { } action)
                {
                    encounter.TryControl(action, occupant);
                }
                else if (armedAction is { } combatAction)
                {
                    _ = combatAction switch
                    {
                        CombatAction.Help or CombatAction.Harry => encounter.TryAid(combatAction, occupant),
                        CombatAction.Shield => encounter.TryRaiseShield(occupant),
                        _ => encounter.TryTend(combatAction, occupant),
                    };
                }
                else
                {
                    encounter.TryAttack(occupant);
                }
            }
            else if (armedDrag)
            {
                encounter.TryDrag(clicked);
            }
            else
            {
                encounter.TryMove(clicked);
            }

            Disarm();

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

            // Gone from the field altogether.
            if (combatant.HasFled)
            {
                return;
            }

            if (combatant.IsOutOfFight && !combatant.IsDefenceless)
            {
                // Left where it fell, so the board remembers the fight.
                DrawString(font, new Vector2(rect.Position.X, centre.Y + 8), "✕", HorizontalAlignment.Center, CellSize, Mathf.Max(10, CellSize * 2 / 5),
                    new Color(0.6f, 0.6f, 0.6f, 0.5f));
                return;
            }

            // A character dying or stable lies there, faded, still to be reached.
            var colour = combatant.Side == Side.Party ? PartyToken : AdversaryToken;
            DrawCircle(centre, radius, combatant.IsDefenceless ? colour with { A = 0.35f } : colour);

            if (combatant == encounter.Active)
            {
                DrawArc(centre, radius + 4, 0, Mathf.Tau, 48, ActiveRing, 3);
            }
            else if (encounter.Phase == EncounterPhase.ChoosingCharacter && encounter.PartyStillToAct.Contains(combatant))
            {
                DrawArc(centre, radius + 4, 0, Mathf.Tau, 48, ChoosableRing, 3);
            }
            else if (active is not null && (armed is { } action
                         ? Encounter.ControlRefusal(active, action, combatant) is null
                         : armedAction is { } combatAction
                             ? ArmedRefusal(active, combatAction, combatant) is null
                             : encounter.CanAttack(active, combatant)))
            {
                DrawArc(centre, radius + 4, 0, Mathf.Tau, 48, TargetRing, 3);
            }

            DrawString(font, new Vector2(rect.Position.X, centre.Y + 6), Initials(combatant.Name), HorizontalAlignment.Center,
                CellSize, Mathf.Max(9, CellSize / 4), Colors.White);

            // What's been done to it, across the top of the square.
            var marks = string.Join(" ", new[]
            {
                combatant.IsProne ? "prone" : null,
                combatant.HeldBy is not null ? "held" : null,
                combatant.Holding is not null ? "holding" : null,
                combatant.IsBroken ? "running" : null,
                combatant.BodyStatus == BodyStatus.Dying && combatant.Side == Side.Party ? "dying" : null,
                combatant.BodyStatus == BodyStatus.Stable && combatant.Side == Side.Party ? "stable" : null,
                combatant.ShieldBonus > 0 ? "shield" : null,
                combatant.IsHelped ? "helped" : null,
                combatant.IsHarried ? "harried" : null,
            }.Where(m => m is not null));
            if (marks.Length > 0)
            {
                DrawString(font, new Vector2(rect.Position.X, rect.Position.Y + 13), marks, HorizontalAlignment.Center, CellSize, Mathf.Max(7, CellSize / 6),
                    new Color(1f, 0.85f, 0.5f));
            }

            // Body bar along the foot of the square.
            var inset = CellSize / 9f;
            var bar = new Rect2(rect.Position.X + inset, rect.End.Y - inset - 2, CellSize - inset * 2, Mathf.Max(2, CellSize / 14));
            DrawRect(bar, new Color(0, 0, 0, 0.6f));
            var fraction = Mathf.Clamp((float)combatant.Body / combatant.BodyMax, 0, 1);
            DrawRect(new Rect2(bar.Position, new Vector2(bar.Size.X * fraction, bar.Size.Y)), new Color(0.35f, 0.85f, 0.4f));
        }

        private void StartEncounter()
        {
            encounter = fightPicker.Selected switch
            {
                1 => SampleEncounters.SignpostAmbush(data, new RandomDiceRoller(), PickedGm(), fourPlayers: true),
                2 => SampleEncounters.PartyVersusAmbush(data, new RandomDiceRoller(), PickedGm()),
                3 => SampleEncounters.FighterVersusRatkin(data, new RandomDiceRoller(), PickedGm()),
                _ => SampleEncounters.SignpostAmbush(data, new RandomDiceRoller(), PickedGm()),
            };
            shown = null;
            Disarm();
            stepPending = false;
            bot = null;
            UpdateBot();
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

            // The GM's turns always play themselves; with Bot v Bot on, so does the party.
            var gmToAct = encounter.Phase == EncounterPhase.AdversaryTurn;
            if ((gmToAct || (bot is not null && !encounter.IsOver)) && !stepPending)
            {
                stepPending = true;
                var waitingOn = encounter;
                GetTree().CreateTimer(gmToAct ? AdversaryTurnDelay : BotStepDelay).Timeout += () =>
                {
                    stepPending = false;

                    // A restart mid-pause replaces the encounter; the old one's turn is moot.
                    if (waitingOn != encounter)
                    {
                        return;
                    }

                    if (bot is not null)
                    {
                        bot.Step();
                    }
                    else if (encounter.Phase == EncounterPhase.AdversaryTurn)
                    {
                        encounter.RunAdversaryTurn();
                    }

                    Refresh();
                };
            }
        }

        /// <summary>Puts a bot in the player's seat, or takes it out, to match the toggle and the chosen tactic.</summary>
        private void UpdateBot()
        {
            bot = botToggle.ButtonPressed
                ? new BotDriver(encounter, tacticPicker.Selected < BrainSkill.Levels.Count
                    ? new UtilityPartyBrain(BrainSkill.Levels[Mathf.Max(0, tacticPicker.Selected)], new System.Random())
                    : new TacticalPartyBrain(PartyTactics.Presets[tacticPicker.Selected - BrainSkill.Levels.Count]))
                : null;
        }

        /// <summary>Shows the 1-Stamina choice — panic dodge, stand firm, or arrest — while the encounter is waiting on it.</summary>
        private void UpdateReactionPrompt()
        {
            reactionBox.Visible = encounter.Pending is not null;
            if (encounter.Pending is not { } pending)
            {
                return;
            }

            reactionLabel.Text = pending.Prompt +
                (pending.WouldRiposte ? "\nIt's a critical, so the miss would be a Riposte: the attacker takes the wound." : string.Empty) +
                "\nIt costs 1 Stamina and their tactical spend for the round, and hands the GM an Immediate Opportunity.";

            // Somebody leaving reach: a button for each thing the character may do about it.
            var isLeaver = pending.Kind == ReactionKind.LeaverReaction;
            panicDodgeButton.Visible = !isLeaver;
            takeHitButton.Visible = !isLeaver;
            var wanted = isLeaver ? string.Join("|", pending.Options ?? []) + pending.Attacker.Name : string.Empty;
            if (wanted != leaverButtonsFor)
            {
                foreach (var child in leaverButtons.GetChildren())
                {
                    child.QueueFree();
                }

                foreach (var option in isLeaver ? pending.Options ?? [] : [])
                {
                    var button = new Button
                    {
                        Text = option == LeaverResponse.LetThemGo ? "Let them go" : $"{option} (1 Stamina)",
                        Disabled = bot is not null,
                    };
                    button.Pressed += () =>
                    {
                        encounter.RespondToLeaver(option);
                        Refresh();
                    };
                    leaverButtons.AddChild(button);
                }

                leaverButtonsFor = wanted;
            }

            if (isLeaver)
            {
                return;
            }

            (panicDodgeButton.Text, takeHitButton.Text) = pending.Kind switch
            {
                ReactionKind.PanicDodge => ("Panic dodge (1 Stamina)", "Take the hit"),
                ReactionKind.StandFirm => ("Stand firm (1 Stamina, +2 ST)", "Let it happen"),
                ReactionKind.RaiseShield => ($"Raise shield (1 Stamina, +{pending.Bonus})", "Take the hit"),
                _ => ("Arrest (1 Stamina)", "Let it carry them"),
            };
        }

        private void UpdateHud()
        {
            statusLabel.Text = $"Round {encounter.Round} · " + encounter.Phase switch
            {
                EncounterPhase.ChoosingCharacter => "Choose who acts next",
                EncounterPhase.PartyTurn => $"Your turn: {encounter.Active?.Name}",
                EncounterPhase.AdversaryTurn => "The GM is acting…",
                EncounterPhase.AwaitingReaction => "React?",
                EncounterPhase.Victory => "Victory!",
                _ => "Defeat."
            } + $"\nGM Opportunities: {encounter.BankedOpportunities}/{Encounter.MaxBankedOpportunities} banked" +
              (encounter.ImmediateOpportunities > 0 ? $", {encounter.ImmediateOpportunities} immediate" : string.Empty) +
              string.Concat(encounter.Objectives.Select(o => $"\nObjective: {o.Name} — " + o.StatusIn(encounter) switch
              {
                  ObjectiveStatus.Achieved => "achieved",
                  ObjectiveStatus.Failed => "failed",
                  _ => "in play",
              }));

            UpdateReactionPrompt();

            UpdateParty();

            var character = shown ?? encounter.Combatants.First(c => c.Side == Side.Party);
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
                $"{Threshold("DT", character.DodgeThreshold, character.BaseDodgeThreshold)} · " +
                $"{Threshold("ST", character.StabilityThreshold, character.BaseStabilityThreshold)} · " +
                $"{Threshold("RT", character.ResolveThreshold, character.BaseResolveThreshold)} · " +
                $"{character.Armour} armour (absorbs {armourAbsorbs})" +
                (character.IsStandingGround
                    ? $"\nStanding your ground ({string.Join(", ", character.StandingGround)}): no moving, and Disadvantage on attacks, until your next turn."
                    : string.Empty) +
                (character.Conditions.Count > 0
                    ? $"\nConditions: {string.Join(", ", character.Conditions.Select(c => ConditionCatalog.For(c).Name))}" +
                      (character.IsOffGuard ? " — Off-guard: −2 DT, Disadvantage on attacks." : string.Empty)
                    : string.Empty) +
                (character.HeldBy is { } holder ? $"\nHeld by {holder.Name}: no moving until you break free." : string.Empty) +
                (character.Holding is { } held ? $"\nHolding {held.Name}, with a hand." : string.Empty) +
                $"\nHands-on: 2d12+{character.UnarmedAttackModifier} vs ST, reach {character.OwnReach}" +
                (character.HasFreeHand ? ", a hand free to grab with." : ", no hand free to grab with.");

            static string Threshold(string name, int? now, int? baseValue) =>
                now is null ? $"{name} —" : now == baseValue ? $"{name} {now}" : $"{name} {now} ({baseValue}{now - baseValue:+0;−0})";

            // With a bot at the controls the buttons are for watching, not pressing.
            var isPartyTurn = bot is null && encounter.Phase == EncounterPhase.PartyTurn && encounter.Active is not null;
            panicDodgeButton.Disabled = bot is not null;
            takeHitButton.Disabled = bot is not null;
            hoverLabel.Text = DescribeHovered(isPartyTurn ? encounter.Active : null);

            var refusal = isPartyTurn ? encounter.ExtraActionRefusal(encounter.Active!) : null;
            extraActionButton.Disabled = !isPartyTurn || refusal is not null;
            extraActionButton.TooltipText = refusal ?? "Gain an extra action. Hands the GM a banked Opportunity.";

            var swapRefusal = isPartyTurn ? Encounter.SwapRefusal(encounter.Active!) : null;
            swapButton.Text = $"Swap to {character.ReadyWeapon?.Name ?? "empty hands"}";
            swapButton.Disabled = !isPartyTurn || swapRefusal is not null;
            swapButton.TooltipText = swapRefusal ?? "Costs a move (or an action once the move is spent). Not movement, and no Opportunity for the GM.";

            foreach (var (action, button) in standGroundButtons)
            {
                var why = isPartyTurn ? Encounter.StandGroundRefusal(encounter.Active!, action) : null;
                button.Disabled = !isPartyTurn || why is not null;
                button.TooltipText = why ??
                    $"{CombatActionCatalog.For(action).Effect}. Takes the move (or the action, if the move's gone). " +
                    "No moving this turn, and Disadvantage on attacks until your next turn. No Opportunity for the GM.";
            }

            if (!isPartyTurn)
            {
                Disarm();
            }

            var acting = isPartyTurn ? encounter.Active : null;
            foreach (var (action, button) in aidButtons)
            {
                var candidates = encounter.Combatants.Where(c => c.OccupiesSquare);
                button.Disabled = acting is null || !candidates.Any(c => ArmedRefusal(acting, action, c) is null);
                button.ButtonPressed = armedAction == action;
            }

            var shieldWhy = acting is null ? "Not your turn." : Encounter.ShieldRefusal(acting);
            raiseShieldButton.Disabled = shieldWhy is not null;
            raiseShieldButton.TooltipText = shieldWhy ??
                $"Raise your {acting!.InHandShield!.Weight} shield with the action: +{InventoryCalculator.ShieldDodgeThresholdBonus(acting.InHandShield.Weight)} DT until your next turn. " +
                "Or keep the action, and raise it for 1 Stamina once you see a blow would land.";
            dragButton.Disabled = acting?.Holding is null;
            dragButton.ButtonPressed = armedDrag;

            foreach (var (action, button) in controlButtons)
            {
                var actor = isPartyTurn ? encounter.Active : null;
                var anyTarget = actor is not null && encounter.Fighting(Side.Adversaries)
                    .Any(foe => Encounter.ControlRefusal(actor, action, foe) is null);
                button.Disabled = !anyTarget;
                button.ButtonPressed = armed == action;
                button.TooltipText = action switch
                {
                    ControlAction.Grab => "Grab a foe beside you: they're Held, and can't move until they break free. Needs a free hand.",
                    ControlAction.Shove => "Shove a foe beside you a square straight away. Into something solid, they take the rest as Body wounds.",
                    _ => "Trip a foe beside you: Prone, so Off-guard (−2 DT) until they spend a move to stand.",
                } + " Unarmed roll against their ST; your action, no Stamina, no damage. Then click the foe.";
            }

            var me = isPartyTurn ? encounter.Active : null;
            standUpButton.Disabled = me is null || Encounter.StandUpRefusal(me) is not null;
            breakFreeButton.Disabled = me is null || Encounter.BreakFreeRefusal(me) is not null;
            letGoButton.Disabled = me?.Holding is null;

            endTurnButton.Disabled = !isPartyTurn;
        }

        /// <summary>
        /// Picks whose sheet to show, lists the party, and offers a button for each character the
        /// players could send in next.
        /// </summary>
        private void UpdateParty()
        {
            if (encounter.Active is { Side: Side.Party } acting)
            {
                shown = acting;
            }
            else if (hovered is { } square && encounter.At(square) is { Side: Side.Party } pointedAt)
            {
                shown = pointedAt;
            }

            var party = encounter.Combatants.Where(c => c.Side == Side.Party).ToList();
            partyLabel.Visible = party.Count > 1;
            partyLabel.Text = string.Join("\n", party.Select(c =>
                $"{(c == encounter.Active ? "▶ " : "   ")}{c.Name}: Body {c.Body}/{c.BodyMax}, Stamina {c.Stamina}/{c.StaminaMax} — " +
                (c.IsDefenceless ? (c.BodyStatus == BodyStatus.Dying ? "DYING — Heal them, or they roll to hold on" : "stable, on 0")
                    : c.IsOutOfFight ? (c.BodyStatus == BodyStatus.Dead ? "dead" : "out of the fight")
                    : c == encounter.Active ? "acting"
                    : encounter.PartyStillToAct.Contains(c) ? "still to act"
                    : "has acted")));

            // Rebuilt only when who can be chosen changes, so a button isn't freed under the mouse.
            var choosing = bot is null && encounter.Phase == EncounterPhase.ChoosingCharacter;
            var candidates = choosing ? encounter.PartyStillToAct.ToList() : new List<Combatant>();
            var wanted = string.Join("|", candidates.Select(c => c.Name));
            if (wanted != chooseRowFor)
            {
                foreach (var child in chooseRow.GetChildren())
                {
                    child.QueueFree();
                }

                foreach (var candidate in candidates)
                {
                    var button = new Button { Text = $"{candidate.Name} acts" };
                    button.Pressed += () =>
                    {
                        encounter.TryBeginTurn(candidate);
                        Refresh();
                    };
                    chooseRow.AddChild(button);
                }

                chooseRowFor = wanted;
            }

            chooseRow.Visible = candidates.Count > 0;
        }

        private string chooseRowFor = string.Empty;

        /// <summary>What clicking the square under the mouse would do, or how to play when it's over nothing useful.</summary>
        private string DescribeHovered(Combatant? active)
        {
            const string howToPlay =
                "Your characters are the blue tokens. You choose who goes next — click a token with a pale ring, " +
                "or its button — and the GM acts between your turns.\n" +
                "• Move: click a highlighted square. Blue costs your move; amber spends your action too.\n" +
                "• Swap: trade the loadout in hand for the one at the ready, for a move.\n" +
                "• Dodge / Grounded / Resolute: +2 to a Threshold in place of your move — but no moving, and attacks at Disadvantage.\n" +
                "• Attack: click a foe with a red ring — in Reach for a blade, beyond Close for a bow.\n" +
                "• Grab / Shove / Trip: press one, then click a foe beside you. An unarmed roll against their ST.\n" +
                "• End turn when you're done, and the GM takes one adversary turn.";

            if (hovered is not { } square)
            {
                return howToPlay;
            }

            if (encounter.Phase == EncounterPhase.ChoosingCharacter && encounter.At(square) is { Side: Side.Party } candidate)
            {
                return encounter.PartyStillToAct.Contains(candidate)
                    ? $"Click to have {candidate.Name} act next."
                    : $"{candidate.Name} has already acted this round.";
            }

            if (encounter.At(square) is { Side: Side.Adversaries } foe)
            {
                var stats = $"{foe.Name}: Body {foe.Body}/{foe.BodyMax} · DT {foe.DodgeThreshold} · " +
                            $"{foe.Attack.Name} +{foe.Attack.AttackModifier}, {foe.Attack.DamageDiceCount}d{foe.Attack.DamageDieSides}" +
                            (foe.IsMinion ? "\nMinion: any hit that gets past its armour drops it." : string.Empty);

                stats += $" · ST {foe.StabilityThreshold}" +
                         (foe.IsProne ? " · Prone" : string.Empty) + (foe.HeldBy is { } holder ? $" · Held by {holder.Name}" : string.Empty);

                if (active is null)
                {
                    return stats;
                }

                if (armed is { } action)
                {
                    var verb = action.ToString().ToLowerInvariant();
                    return stats + (Encounter.ControlRefusal(active, action, foe) is { } why
                        ? $"\nCan't {verb}: {why}"
                        : $"\nClick to {verb}: 2d12+{active.UnarmedAttackModifier} vs ST {foe.StabilityThreshold}.");
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
                    null when active.IsStandingGround => "You're standing your ground — no moving until your next turn.",
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
                Position = new Vector2(BoardOrigin.X * 2 + BoardArea.X, BoardOrigin.Y),
                CustomMinimumSize = new Vector2(1280 - BoardArea.X - BoardOrigin.X * 3, 720 - BoardOrigin.Y * 2),
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
            partyLabel = WrappingLabel(column);
            chooseRow = new HFlowContainer();
            chooseRow.AddThemeConstantOverride("h_separation", 8);
            column.AddChild(chooseRow);
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

            foreach (var action in new[] { CombatAction.Dodge, CombatAction.Grounded, CombatAction.Resolute })
            {
                var threshold = CombatActionCatalog.For(action).Effect.Split(' ')[1]; // "+2 DT until your turn" → "DT"
                var button = new Button { Text = $"{action} (+2 {threshold})" };
                button.Pressed += () =>
                {
                    encounter.TryStandGround(action);
                    Refresh();
                };
                buttons.AddChild(button);
                standGroundButtons[action] = button;
            }

            foreach (var action in System.Enum.GetValues<ControlAction>())
            {
                var button = new Button { Text = action.ToString(), ToggleMode = true };
                button.Pressed += () =>
                {
                    var was = armed;
                    Disarm();
                    armed = was == action ? null : action;
                    Refresh();
                };
                buttons.AddChild(button);
                controlButtons[action] = button;
            }

            foreach (var (action, label, tip) in new[]
            {
                (CombatAction.Help, "Help", "An ally beside you has Advantage on their next roll. Your action; it lapses at your next turn."),
                (CombatAction.Harry, "Harry", "A foe beside you has Disadvantage on its next roll. Your action; it lapses at your next turn."),
                (CombatAction.Heal, "Heal", "+1 Body to yourself or an ally beside you — the dying included — once per fresh wound. Your action."),
                (CombatAction.Meditate, "Meditate", "+1 Mind to yourself or an ally beside you, once per fresh wound. Your action."),
                (CombatAction.Pray, "Pray", "+1 Soul to yourself or an ally beside you, once per fresh wound. Your action."),
                (CombatAction.Shield, "Shield an ally", "Raise a Medium or Heavy shield over an ally within your Reach, with the action."),
            })
            {
                var button = new Button { Text = label, ToggleMode = true, TooltipText = tip + " Then click who it's for." };
                button.Pressed += () =>
                {
                    var was = armedAction;
                    Disarm();
                    armedAction = was == action ? null : action;
                    Refresh();
                };
                buttons.AddChild(button);
                aidButtons[action] = button;
            }

            raiseShieldButton = new Button { Text = "Raise shield" };
            raiseShieldButton.Pressed += () =>
            {
                encounter.TryRaiseShield();
                Refresh();
            };
            buttons.AddChild(raiseShieldButton);

            dragButton = new Button
            {
                Text = "Drag",
                ToggleMode = true,
                TooltipText = "Move hauling whoever you hold: rolled against their ST, and a move dearer. Then click where.",
            };
            dragButton.Pressed += () =>
            {
                var was = armedDrag;
                Disarm();
                armedDrag = !was;
                Refresh();
            };
            buttons.AddChild(dragButton);

            standUpButton = new Button { Text = "Stand up", TooltipText = "Get up from Prone, for a move. Not movement: it leaves nobody's reach." };
            standUpButton.Pressed += () =>
            {
                encounter.TryStandUp();
                Refresh();
            };
            buttons.AddChild(standUpButton);

            breakFreeButton = new Button { Text = "Break free", TooltipText = "Your action: the Grab's roll against your holder's ST." };
            breakFreeButton.Pressed += () =>
            {
                encounter.TryBreakFree();
                Refresh();
            };
            buttons.AddChild(breakFreeButton);

            letGoButton = new Button { Text = "Let go", TooltipText = "Release whoever you're holding. Free." };
            letGoButton.Pressed += () =>
            {
                encounter.TryLetGo();
                Refresh();
            };
            buttons.AddChild(letGoButton);

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

            // Bot v Bot: a party brain takes the player's seat, and the fight plays itself.
            var botRow = new HBoxContainer();
            botRow.AddThemeConstantOverride("separation", 8);
            column.AddChild(botRow);

            fightPicker = new OptionButton();
            fightPicker.AddItem("Signpost ambush (Quickstart)");
            fightPicker.AddItem("Signpost ambush, four players");
            fightPicker.AddItem("Roadside ambush (party of three)");
            fightPicker.AddItem("The Dumb Fighter v three ratkin");
            fightPicker.ItemSelected += _ => StartEncounter();
            botRow.AddChild(fightPicker);

            botToggle = new CheckButton { Text = "Bot plays the party" };
            botToggle.Toggled += _ =>
            {
                UpdateBot();
                Refresh();
            };
            botRow.AddChild(botToggle);

            tacticPicker = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            foreach (var level in BrainSkill.Levels)
            {
                tacticPicker.AddItem($"Weighs every option ({level.Name})");
            }

            foreach (var tactic in PartyTactics.Presets)
            {
                tacticPicker.AddItem(tactic.Name);
            }

            tacticPicker.ItemSelected += _ =>
            {
                if (bot is not null)
                {
                    UpdateBot();
                }
            };
            botRow.AddChild(tacticPicker);

            var gmRow = new HBoxContainer();
            gmRow.AddThemeConstantOverride("separation", 8);
            column.AddChild(gmRow);
            gmRow.AddChild(new Label { Text = "GM plays:" });
            gmPicker = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TooltipText = "Takes effect from the next fight (Restart)." };
            gmPicker.AddItem("Best — weighs every option, takes the most effective");
            gmPicker.AddItem("Balanced — usually the best");
            gmPicker.AddItem("Poor — loose and careless");
            gmPicker.AddItem("Simple — goes for the nearest");
            gmPicker.ItemSelected += _ => StartEncounter();
            gmRow.AddChild(gmPicker);

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

            panicDodgeButton = new Button { Text = "Panic dodge (1 Stamina)" };
            panicDodgeButton.Pressed += () =>
            {
                encounter.RespondToReaction(spend: true);
                Refresh();
            };
            reactionButtons.AddChild(panicDodgeButton);

            takeHitButton = new Button { Text = "Take the hit" };
            takeHitButton.Pressed += () =>
            {
                encounter.RespondToReaction(spend: false);
                Refresh();
            };
            reactionButtons.AddChild(takeHitButton);

            leaverButtons = new HFlowContainer();
            leaverButtons.AddThemeConstantOverride("h_separation", 8);
            reactionColumn.AddChild(leaverButtons);

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

        private Rect2 SquareRect(GridPosition square) =>
            new(BoardOrigin + new Vector2(square.X, square.Y) * CellSize, new Vector2(CellSize, CellSize));

        private GridPosition SquareAt(Vector2 point)
        {
            var local = (point - BoardOrigin) / CellSize;
            return new GridPosition(Mathf.FloorToInt(local.X), Mathf.FloorToInt(local.Y));
        }

        private static string Initials(string name) =>
            string.Concat(name.Split(' ').Where(w => w != "The" && w.Length > 0).Select(w => w[0]));
    }
}
