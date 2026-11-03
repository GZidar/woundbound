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
        private static readonly Vector2 BoardArea = new(800, 592);
        private const int LargestCell = 72;
        private const double AdversaryTurnDelay = 0.7;
        private const double BotStepDelay = 0.4;
        private static readonly Vector2 BoardOrigin = new(24, 104);

        /// <summary>Where the side panel starts — above the board, beside the roster.</summary>
        private const float HudTop = 24;

        private static readonly Color LightSquare = new(0.20f, 0.21f, 0.24f);
        private static readonly Color DarkSquare = new(0.17f, 0.18f, 0.21f);
        private static readonly Color GridLine = new(0.28f, 0.29f, 0.33f);
        // The palette is the Owlbear extension's (the rules repo, OwlbearFrontend), picked so that it
        // stays apart for a red/green colour-blind player: party blue, hostile rust — the danger colour
        // throughout — neutral yellow, gold for the accent, bone for plain. Moves are priced as its map
        // overlay prices them: bone for one move, blue for two.
        private static readonly Color Bone = Color.FromHtml("#e8e6e3");
        private static readonly Color PartyBlue = Color.FromHtml("#56b4e9");
        private static readonly Color Rust = Color.FromHtml("#c04a10");
        private static readonly Color OneMove = Bone with { A = 0.22f };
        private static readonly Color TwoMoves = PartyBlue with { A = 0.35f };
        private static readonly Color HoverOutline = new(1f, 1f, 1f, 0.8f);
        private static readonly Color PartyToken = PartyBlue;
        private static readonly Color AdversaryToken = Rust;
        private static readonly Color ActiveRing = new(1f, 0.85f, 0.3f);
        private static readonly Color TargetRing = new(1f, 0.35f, 0.3f);
        private static readonly Color ChoosableRing = new(0.6f, 0.85f, 1f);

        /// <summary>
        /// Demo mode, from the command line (<c>-- --demo --fight camp --seed 7</c>): the bots play
        /// both seats at a pace a viewer can follow, and the game quits once the fight is over — for
        /// recording with Godot's Movie Maker (<c>--write-movie</c>).
        /// </summary>
        private bool demo;
        private int? seed;
        private bool quitting;

        /// <summary>What somebody just did, said over their token for a few seconds.</summary>
        private sealed class Bubble(Combatant who, string text, bool isResult)
        {
            public Combatant Who { get; } = who;
            public string Text { get; } = text;
            public bool IsResult { get; } = isResult;
            public double Age { get; set; }
        }

        private readonly List<Bubble> bubbles = new();
        private double BubbleLife => demo ? 4.2 : 2.6;
        private const int BubbleWidth = 230;

        /// <summary>Where each token is drawn, eased along towards its square so a move can be followed.</summary>
        private readonly Dictionary<Combatant, Vector2> tokenAt = new();

        /// <summary>The centre of the square a token belongs on — pinned to the edge when it's off the map.</summary>
        private Vector2 SquareCentreOf(Combatant c) =>
            SquareRect(new GridPosition(Mathf.Clamp(c.Position.X, 0, encounter.Width - 1), Mathf.Clamp(c.Position.Y, 0, encounter.Height - 1))).GetCenter()
            + Vector2.One * (CellSize * (c.Across - 1) / 2f);

        private Vector2 TokenCentre(Combatant c) => tokenAt.TryGetValue(c, out var at) ? at : SquareCentreOf(c);

        /// <summary>Where each character's avatar is drawn in the roster, eased towards where it belongs.</summary>
        private readonly Dictionary<Combatant, Vector2> rosterAt = new();

        /// <summary>The characters who have finished their turn this round, in the order they went.</summary>
        private readonly List<Combatant> actedThisRound = new();
        private int rosterRound;

        /// <summary>
        /// A colour for each character, so the roster, their token and their bubbles agree: the
        /// Okabe-Ito set the extension's palette is drawn from, which a colour-blind player can tell
        /// apart — and never the rust that marks the other side.
        /// </summary>
        private static readonly Color[] PartyColours =
        [
            Color.FromHtml("#56b4e9"), Color.FromHtml("#e69f00"), Color.FromHtml("#009e73"),
            Color.FromHtml("#cc79a7"), Color.FromHtml("#f0e442"), Color.FromHtml("#0072b2"),
        ];

        private Color ColourOf(Combatant c)
        {
            if (c.Side != Side.Party)
            {
                return AdversaryToken;
            }

            var index = encounter.Combatants.Where(x => x.Side == Side.Party).ToList().IndexOf(c);
            return PartyColours[Mathf.Max(0, index) % PartyColours.Length];
        }

        private VBoxContainer hudColumn = null!;

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
            1 => new UtilityGmBrain(BrainSkill.Balanced, new System.Random(), PickedKnowledge()),
            2 => new UtilityGmBrain(BrainSkill.Poor, new System.Random(), PickedKnowledge()),
            3 => new SimpleGmBrain(),
            _ => new UtilityGmBrain(BrainSkill.Best, new System.Random(), PickedKnowledge()),
        };

        /// <summary>The GM knowledge dial from its picker: with the skill, the sheets, or only what it has seen.</summary>
        private Knowledge? PickedKnowledge() => knowledgePicker.Selected switch
        {
            1 => Knowledge.Sheets,
            2 => Knowledge.Seen,
            _ => null,
        };

        private OptionButton knowledgePicker = null!;
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
        private Button takeUpButton = null!;
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
        private Button dropLightButton = null!;
        private Button lightButton = null!;

        private static readonly Color BrokenGround = new(0.45f, 0.32f, 0.18f, 0.35f);
        private static readonly Color ShallowWater = new(0.25f, 0.45f, 0.65f, 0.55f);
        private static readonly Color DeepWater = new(0.10f, 0.22f, 0.45f, 0.85f);
        private static readonly Color Wall = new(0.42f, 0.40f, 0.38f);
        private static readonly Color CoverWood = new(0.55f, 0.40f, 0.24f);
        private static readonly Color HazardEdge = new(0.95f, 0.75f, 0.2f, 0.55f);
        private Button regainFootingButton = null!;
        private Button duckBackButton = null!;
        private static readonly Color SureGround = new(0.55f, 0.55f, 0.6f, 0.15f);
        private static readonly Color Gloom = new(0f, 0f, 0.03f, 0.35f);
        private static readonly Color Darkness = new(0f, 0f, 0.03f, 0.62f);

        /// <summary>A spell chosen from the spell row; the next square or creature clicked is where it goes.</summary>
        private WoundBound.Rules.Models.SpellDefinition? armedSpell;

        private HFlowContainer spellButtons = null!;
        private string spellButtonsFor = string.Empty;
        private CheckBox payInBodyBox = null!;
        private CheckBox overclockBox = null!;
        private CheckBox sacrificeBox = null!;
        private CheckBox sustainBox = null!;
        private CheckBox backlashBox = null!;
        private OptionButton thresholdPicker = null!;
        private OptionButton tapPicker = null!;
        private OptionButton effectPicker = null!;

        /// <summary>How far below its own Tier a Bind is cast: 0 its own effect, 1 a Tier lower, 2 two lower (Core Rules §11, "Motion Spells").</summary>
        private OptionButton bindPicker = null!;
        private OptionButton shapePicker = null!;
        private readonly Dictionary<NimbleExploit, Button> exploitButtons = new();
        private readonly Dictionary<WeaponManoeuvre, Button> manoeuvreButtons = new();

        /// <summary>A manoeuvre chosen from its button; the next foe clicked is attacked with it.</summary>
        private WeaponManoeuvre? armedManoeuvre;

        /// <summary>The manoeuvre the armed one would be, at <paramref name="target"/> — Cleave's second target the weakest other foe in reach.</summary>
        private Manoeuvre? ManoeuvreAt(Combatant attacker, Combatant target) => ManoeuvreAt(attacker, target, armedManoeuvre);

        /// <summary>The manoeuvre <paramref name="kind"/> would be, at <paramref name="target"/>.</summary>
        private Manoeuvre? ManoeuvreAt(Combatant attacker, Combatant target, WeaponManoeuvre? kind) => kind switch
        {
            null => null,
            WeaponManoeuvre.Cleave => new Manoeuvre(WeaponManoeuvre.Cleave, encounter.Fighting(Side.Adversaries)
                .Where(e => e != target && attacker.CanReach(e)).OrderBy(e => e.Body).FirstOrDefault()),
            { } other => new Manoeuvre(other),
        };
        private static readonly Color SpellArea = new(0.65f, 0.35f, 0.95f, 0.35f);
        private static readonly Color Saddle = new(0.45f, 0.3f, 0.18f);

        private static readonly ThresholdType[] BuffThresholds = [ThresholdType.Dodge, ThresholdType.Stability, ThresholdType.Resolve, ThresholdType.Fortitude];

        private void Disarm()
        {
            armed = null;
            armedAction = null;
            armedDrag = false;
            armedSpell = null;
            armedManoeuvre = null;
        }

        /// <summary>
        /// The casting the armed spell would be with the mouse on <paramref name="square"/>: into
        /// whoever stands there, or — for a Volume — a block with that square as its corner nearest
        /// the caster, spreading away from them.
        /// </summary>
        private SpellCast? CastAt(Combatant caster, GridPosition square)
        {
            if (armedSpell is not { } spell)
            {
                return null;
            }

            var cast = new SpellCast(spell)
            {
                PayInBody = payInBodyBox.ButtonPressed,
                Overclock = overclockBox.ButtonPressed,
                Sacrifice = sacrificeBox.ButtonPressed,
                Sustain = sustainBox.ButtonPressed,
                Backlash = backlashBox.ButtonPressed,
                Threshold = spell.SpellType is SpellType.Buff or SpellType.Bane ? BuffThresholds[Mathf.Max(0, thresholdPicker.Selected)] : null,
            };

            if (spell.SpellType == SpellType.Control)
            {
                cast = cast with { Effect = (ControlEffect)Mathf.Max(0, effectPicker.Selected) };
            }

            // A Bind may produce a lower Tier's effect than its own: Stuck or Slowed from a T3, Slowed from a T2.
            if (IsTieredBind(spell))
            {
                var lower = Mathf.Max((int)Tier.T1, (int)spell.SpellTier - Mathf.Max(0, bindPicker.Selected));
                cast = cast with { BindAs = lower < (int)spell.SpellTier ? (Tier)lower : null };
            }

            // Elemental Fuel: the pack, the light in hand, or a fire burning within reach.
            if (spell.Source == MagicSource.ElementalMagic)
            {
                cast = tapPicker.Selected switch
                {
                    1 => cast with
                    {
                        Tap = caster.Build?.InventoryItems.Any(i => InventoryCalculator.IsLitLight(i) && i.Name.Contains("lantern", System.StringComparison.OrdinalIgnoreCase)) == true
                            && caster.Build.InventoryItems.All(i => !(InventoryCalculator.IsLitLight(i) && i.Name.Contains("torch", System.StringComparison.OrdinalIgnoreCase)))
                            ? FuelSource.Lantern
                            : FuelSource.Torch,
                    },
                    2 when encounter.Fires.Where(f => f.DistanceTo(caster.Position) <= 1).Select(f => (GridPosition?)f).FirstOrDefault() is { } fire
                        => cast with { Tap = FuelSource.Campfire, TapAt = fire },
                    2 when encounter.LightsOnTheGround.Where(l => l.DistanceTo(caster.Position) <= 1).Select(l => (GridPosition?)l).FirstOrDefault() is { } dropped
                        => cast with { Tap = FuelSource.Torch, TapAt = dropped },
                    _ => cast,
                };
            }

            // An Illusion stands where it's put: a wound into the foe clicked, anything else on the
            // square clicked, its block running from there.
            if (spell.SpellType == SpellType.Illusion)
            {
                return Encounter.FormOf(spell) == IllusionForm.Wound
                    ? encounter.AnyAt(square) is { } subject ? cast with { Target = subject } : null
                    : cast with { Corner = square };
            }

            // An area spell's Shape is named as it's cast: a Line or a Cone starts beside the caster and
            // is aimed the way of the square clicked.
            if (spell.Target == SpellTarget.Many && spell.Range != SpellRange.Touch && shapePicker.Selected is 1 or 2)
            {
                var aim = (System.Math.Sign(square.X - caster.Position.X), System.Math.Sign(square.Y - caster.Position.Y));
                if (aim == (0, 0))
                {
                    return null;
                }

                return cast with
                {
                    Shape = shapePicker.Selected == 1 ? SpellShape.Line : SpellShape.Cone,
                    Origin = new GridPosition(caster.Position.X + aim.Item1, caster.Position.Y + aim.Item2),
                    Aim = aim,
                };
            }

            if (spell.Target == SpellTarget.Many && spell.Range != SpellRange.Touch)
            {
                cast = cast with { Shape = SpellShape.Volume };
                var across = WoundBound.Game.Combat.SpellArea.Across(spell.Size) - 1;
                return cast with
                {
                    Corner = new GridPosition(square.X >= caster.Position.X ? square.X : square.X - across,
                        square.Y >= caster.Position.Y ? square.Y : square.Y - across),
                };
            }

            return encounter.AnyAt(square) is { } target ? cast with { Target = target } : null;
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
            ReadDemoArguments();
            StartEncounter();
            if (OS.GetCmdlineUserArgs() is var args && System.Array.IndexOf(args, "--radial") is var at and >= 0 && at + 1 < args.Length)
            {
                OpenRadialPath(args[at + 1]);
                if (System.Array.IndexOf(args, "--radial-click") is var c and >= 0 && c + 1 < args.Length)
                {
                    ClickRadial(args[c + 1]);
                }
            }
        }

        /// <summary>Reads <c>--demo</c>, <c>--fight</c>, <c>--seed</c> and <c>--gm</c> from the user arguments, after Godot's own <c>--</c>.</summary>
        private void ReadDemoArguments()
        {
            var args = OS.GetCmdlineUserArgs();
            string? Value(string name) => System.Array.IndexOf(args, name) is var at and >= 0 && at + 1 < args.Length ? args[at + 1] : null;

            demo = args.Contains("--demo");
            seed = int.TryParse(Value("--seed"), out var s) ? s : null;
            var fights = new[] { "signpost", "casters", "camp", "signpost4", "ford", "fordcasters", "party", "solo", "witches", "rivals", "swarm", "swarmopen", "champion", "guardians", "stag", "outriders", "glamour", "roadblock", "gnoll" };
            if (Value("--fight") is { } fight && System.Array.IndexOf(fights, fight) is var index and >= 0)
            {
                fightPicker.Select(index);
            }

            if (Value("--gm") is { } gm)
            {
                gmPicker.Select(gm switch { "balanced" => 1, "poor" => 2, "simple" => 3, _ => 0 });
            }

            if (Value("--knowledge") is { } knows)
            {
                knowledgePicker.Select(knows switch { "sheets" => 1, "seen" => 2, _ => 0 });
            }

            if (demo)
            {
                tacticPicker.Select(0);
                botToggle.SetPressedNoSignal(true);

                // Nobody is at the controls: only what a viewer follows stays on the panel.
                foreach (var child in hudColumn.GetChildren().OfType<Control>())
                {
                    child.Visible = child == statusLabel || child is TabContainer || child == reactionBox;
                }
            }
        }

        public override void _Process(double delta)
        {
            var animating = false;
            foreach (var bubble in bubbles)
            {
                bubble.Age += delta;
            }

            animating |= bubbles.RemoveAll(b => b.Age > BubbleLife) > 0 || bubbles.Count > 0;

            var glide = 1 - Mathf.Exp(-9 * (float)delta);
            foreach (var c in encounter.Combatants)
            {
                var target = SquareCentreOf(c);
                var at = tokenAt.TryGetValue(c, out var was) ? was : target;
                var next = at.DistanceTo(target) < 0.5f ? target : at.Lerp(target, glide);
                animating |= next != target;
                tokenAt[c] = next;
            }

            UpdateRosterOrder();
            var ease = 1 - Mathf.Exp(-6 * (float)delta);
            foreach (var (who, target) in RosterTargets())
            {
                var at = rosterAt.TryGetValue(who, out var was) ? was : target;
                var next = at.Lerp(target, ease);
                animating |= next.DistanceTo(target) > 0.5f;
                rosterAt[who] = next;
            }

            if (animating)
            {
                QueueRedraw();
            }
        }

        /// <summary>Keeps the order characters finished their turns in, and starts it afresh each round.</summary>
        private void UpdateRosterOrder()
        {
            if (encounter.Round != rosterRound)
            {
                rosterRound = encounter.Round;
                actedThisRound.Clear();
            }

            foreach (var c in encounter.Combatants.Where(c => c.Side == Side.Party))
            {
                if (!encounter.PartyStillToAct.Contains(c) && c != encounter.Active && !actedThisRound.Contains(c))
                {
                    actedThisRound.Add(c);
                }
            }
        }

        private const float RosterY = 52;
        private const float RosterStep = 64;
        private const float YetToActX = 130;
        private const float ActingX = 470;
        private const float ActedX = 590;

        /// <summary>Where each avatar belongs: yet to act on the left, the one acting in the middle, those who have acted on the right.</summary>
        private IEnumerable<(Combatant Who, Vector2 At)> RosterTargets()
        {
            var yet = encounter.Combatants.Where(c => c.Side == Side.Party && encounter.PartyStillToAct.Contains(c) && c != encounter.Active).ToList();
            for (var i = 0; i < yet.Count; i++)
            {
                yield return (yet[i], new Vector2(YetToActX + i * RosterStep, RosterY));
            }

            if (encounter.Active is { Side: Side.Party } acting)
            {
                yield return (acting, new Vector2(ActingX, RosterY));
            }

            for (var i = 0; i < actedThisRound.Count; i++)
            {
                yield return (actedThisRound[i], new Vector2(ActedX + i * RosterStep * 0.75f, RosterY));
            }
        }

        /// <summary>The roster across the top: who has yet to act this round, who is acting, who has acted — and the GM, when it's theirs.</summary>
        private void DrawRoster()
        {
            var font = ThemeDB.FallbackFont;
            var dim = new Color(0.75f, 0.75f, 0.8f);
            DrawRect(new Rect2(BoardOrigin.X, 8, BoardArea.X, 84), new Color(0.12f, 0.13f, 0.16f));
            DrawString(font, new Vector2(BoardOrigin.X + 10, 26), $"Round {encounter.Round}", HorizontalAlignment.Left, -1, 14, Colors.White);
            DrawString(font, new Vector2(YetToActX - 24, 26), "Yet to act", HorizontalAlignment.Left, -1, 13, dim);
            DrawString(font, new Vector2(ActingX - 24, 26), "Acting", HorizontalAlignment.Left, -1, 13, ActiveRing);
            DrawString(font, new Vector2(ActedX - 24, 26), "Acted", HorizontalAlignment.Left, -1, 13, dim);

            foreach (var (who, _) in RosterTargets())
            {
                var at = rosterAt.TryGetValue(who, out var known) ? known : new Vector2(YetToActX, RosterY);
                var colour = ColourOf(who);
                var down = who.IsOutOfFight;
                var acted = actedThisRound.Contains(who);
                DrawCircle(at, 20, down ? colour with { A = 0.25f } : acted ? colour.Darkened(0.35f) : colour);
                if (who == encounter.Active)
                {
                    DrawArc(at, 24, 0, Mathf.Tau, 48, ActiveRing, 3);
                }

                DrawString(font, at + new Vector2(-20, 5), Initials(who.Name), HorizontalAlignment.Center, 40, 14, Colors.White);
                DrawString(font, at + new Vector2(-32, 36), who.Name.Split(' ')[0], HorizontalAlignment.Center, 64, 11, down ? dim with { A = 0.5f } : dim);
            }

            // The GM's chip: lit while an adversary is taking its turn.
            var gmActing = encounter.Phase == EncounterPhase.AdversaryTurn || (encounter.Active is { Side: Side.Adversaries });
            var gmAt = new Vector2(BoardOrigin.X + BoardArea.X - 40, RosterY);
            DrawCircle(gmAt, 20, gmActing ? AdversaryToken : AdversaryToken.Darkened(0.6f));
            if (gmActing)
            {
                DrawArc(gmAt, 24, 0, Mathf.Tau, 48, ActiveRing, 3);
            }

            DrawString(font, gmAt + new Vector2(-20, 5), "GM", HorizontalAlignment.Center, 40, 14, Colors.White);
            DrawString(font, gmAt + new Vector2(-40, 36), gmActing ? "acting" : $"{encounter.GmOpportunities} Opp.", HorizontalAlignment.Center, 80, 11, dim);
        }

        /// <summary>
        /// Turns a line of the log into a bubble: what somebody did, over them — "casts Smite at the
        /// Ambusher on the ridge" — and how a roll went, over whoever it landed on.
        /// </summary>
        private BattleLog battleLog = null!;

        /// <summary>The reaction on offer, explained — shown in its ring's hover box and above the tabs.</summary>
        private string reactionExplained = string.Empty;

        /// <summary>Whether the old action buttons are shown (Options tab): the reaction box keeps its buttons only then.</summary>
        private bool actionRowsShown;

        /// <summary>Each log line as the battle log shows it, formatted as it was written — when whose turn it was is still known.</summary>
        private readonly List<string> formattedLog = new();

        private void OnLogged(string line)
        {
            var gmActing = encounter.Phase == EncounterPhase.AdversaryTurn
                           || (encounter.Phase == EncounterPhase.AwaitingReaction && encounter.Active?.Side != Side.Party);
            formattedLog.Add(battleLog.Format(line, encounter.Combatants, gmActing));

            if (line.StartsWith("—") || line.EndsWith("'s turn.") || line.StartsWith("The GM banks") || line.StartsWith("Objective"))
            {
                return;
            }

            var byName = encounter.Combatants.OrderByDescending(c => c.Name.Length).ToList();
            if (byName.FirstOrDefault(c => line.StartsWith(c.Name + " ")) is { } actor)
            {
                Say(actor, line[(actor.Name.Length + 1)..], isResult: false);
                return;
            }

            if (byName.FirstOrDefault(c => line.StartsWith(c.Name + ":")) is { } hurt)
            {
                Say(hurt, line[(hurt.Name.Length + 1)..].Trim(), isResult: true);
                return;
            }

            // "Smite at Ambusher on the ridge: (9+5)+4 = 18 vs DT 17 — hit, 3 Soul."
            if (line.IndexOf(": (", System.StringComparison.Ordinal) is var colon and > 0
                && byName.FirstOrDefault(c => line[..colon].EndsWith(c.Name)) is { } target)
            {
                Say(target, line[..colon].Replace(" at " + target.Name, string.Empty) + line[colon..], isResult: true);
            }
        }

        private void Say(Combatant who, string text, bool isResult)
        {
            text = text.Trim().TrimEnd('.');
            if (text.Length == 0)
            {
                return;
            }

            text = char.ToUpperInvariant(text[0]) + text[1..];
            bubbles.Add(new Bubble(who, text.Length > 140 ? text[..137] + "…" : text, isResult));
            while (bubbles.Count(b => b.Who == who) > 2)
            {
                bubbles.Remove(bubbles.First(b => b.Who == who));
            }

            QueueRedraw();
        }

        /// <summary>The bubbles, over each token, the newest nearest it, fading as they go.</summary>
        private void DrawBubbles()
        {
            var font = ThemeDB.FallbackFont;
            const int size = 13;
            foreach (var group in bubbles.GroupBy(b => b.Who))
            {
                var who = group.Key;
                var anchor = TokenCentre(who) - new Vector2(0, CellSize * 0.45f);
                foreach (var bubble in group.Reverse())
                {
                    var fade = Mathf.Clamp((float)((BubbleLife - bubble.Age) / 0.6), 0, 1) * Mathf.Clamp((float)(bubble.Age / 0.15), 0, 1);
                    var textSize = font.GetMultilineStringSize(bubble.Text, HorizontalAlignment.Left, BubbleWidth, size);
                    var box = new Vector2(textSize.X + 16, textSize.Y + 10);
                    var topLeft = new Vector2(Mathf.Clamp(anchor.X - box.X / 2, 4, BoardOrigin.X + BoardArea.X - box.X), Mathf.Max(BoardOrigin.Y + 2, anchor.Y - box.Y - 8));
                    var fill = bubble.IsResult ? new Color(0.16f, 0.16f, 0.2f, 0.92f * fade) : new Color(0.97f, 0.96f, 0.92f, 0.95f * fade);
                    var edge = (bubble.IsResult ? new Color(0.8f, 0.8f, 0.85f) : ColourOf(who)) with { A = fade };
                    DrawStyleBox(new StyleBoxFlat
                    {
                        BgColor = fill,
                        BorderColor = edge,
                        BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
                        CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8, CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8,
                    }, new Rect2(topLeft, box));
                    var tail = new Vector2(Mathf.Clamp(anchor.X, topLeft.X + 10, topLeft.X + box.X - 10), topLeft.Y + box.Y);
                    DrawColoredPolygon([tail + new Vector2(-6, -1), tail + new Vector2(6, -1), tail + new Vector2(0, 8)], fill);
                    DrawMultilineString(font, topLeft + new Vector2(8, 5 + font.GetAscent(size)), bubble.Text, HorizontalAlignment.Left, BubbleWidth, size, -1,
                        (bubble.IsResult ? new Color(0.95f, 0.92f, 0.8f) : new Color(0.1f, 0.1f, 0.12f)) with { A = fade });
                    anchor = new Vector2(anchor.X, topLeft.Y - 4);
                }
            }
        }

        public override void _UnhandledInput(InputEvent @event)
        {
            // Escape with no ring open lets go of whatever action was armed.
            if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape })
            {
                Disarm();
                Refresh();
                return;
            }

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
                if (encounter.AnyAt(clicked) is { } chosen)
                {
                    encounter.TryBeginTurn(chosen);
                    Refresh();
                }

                return;
            }

            // A reaction waiting on a player: clicking the one being asked brings its ring back.
            if (encounter.Pending is { } pending && pending.Defender.Side == Side.Party && encounter.AnyAt(clicked) == pending.Defender)
            {
                OpenReaction(pending);
                return;
            }

            if (encounter.Phase != EncounterPhase.PartyTurn)
            {
                return;
            }

            // Clicking the one acting, with nothing armed, opens their actions around them.
            if (armedSpell is null && armed is null && armedAction is null && !armedDrag && armedManoeuvre is null
                && encounter.Active is { } self && encounter.AnyAt(clicked) == self)
            {
                OpenActions();
                return;
            }

            if (armedSpell is not null && encounter.Active is { } caster)
            {
                if (CastAt(caster, clicked) is { } cast)
                {
                    encounter.TryCast(cast);
                }
            }
            else if (encounter.AnyAt(clicked) is { } occupant)
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
                else if (encounter.Active is { } striker && occupant.Side != striker.Side)
                {
                    // A plain click strikes foes only; an ally is struck only by choosing them from the menu.
                    encounter.TryAttack(occupant, sacrificeBox.ButtonPressed, ManoeuvreAt(striker, occupant));
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

                    // Water and walls first, then rough ground and sure footing, then the dark over everything.
                    if (encounter.Setting.Water.TryGetValue(square, out var depth))
                    {
                        DrawRect(rect, depth == WaterDepth.Deep ? DeepWater : ShallowWater);
                    }

                    if (encounter.Setting.IsWall(square))
                    {
                        DrawRect(rect, Wall);
                    }
                    else if (encounter.Setting.CoverAt(square) is var cover && cover != Cover.None)
                    {
                        // Full cover stands the height of the square; partial is a half-height barrier.
                        var piece = cover == Cover.Full ? rect.Grow(-2) : new Rect2(rect.Position + new Vector2(2, rect.Size.Y * 0.45f), new Vector2(rect.Size.X - 4, rect.Size.Y * 0.4f));
                        DrawRect(piece, CoverWood);
                        DrawRect(piece, CoverWood.Darkened(0.4f), filled: false, width: 1.5f);
                    }
                    else if (!encounter.Setting.Water.ContainsKey(square))
                    switch (encounter.GroundAt(square))
                    {
                        case Terrain.Broken:
                            DrawRect(rect, BrokenGround);
                            DrawString(ThemeDB.FallbackFont, rect.Position + new Vector2(2, CellSize * 0.35f), "∴", HorizontalAlignment.Left, CellSize, Mathf.Max(8, CellSize / 3),
                                new Color(0.75f, 0.6f, 0.4f, 0.6f));
                            break;
                        case Terrain.Sure:
                            DrawRect(rect, SureGround);
                            break;
                    }

                    if (reachable.TryGetValue(square, out var cost))
                    {
                        DrawRect(rect, cost == 1 ? OneMove : TwoMoves);
                    }

                    if (encounter.Setting.HazardAt(square) is not null)
                    {
                        DrawRect(rect.Grow(-3), HazardEdge, filled: false, width: 2);
                    }

                    var seen = encounter.VisibilityAtSquare(square);
                    if (seen != Visibility.Good)
                    {
                        DrawRect(rect, seen == Visibility.None ? Darkness : Gloom);
                    }

                    DrawRect(rect, GridLine, filled: false, width: 1);
                }
            }

            // Lights burning where they were dropped, and built fires.
            foreach (var light in encounter.LightsOnTheGround.Concat(encounter.Fires))
            {
                DrawCircle(SquareRect(light).GetCenter(), CellSize * 0.12f, new Color(1f, 0.7f, 0.25f));
            }

            // Mounts standing riderless, where their riders left them.
            foreach (var steed in encounter.Steeds.Where(s => s.Rider is { IsRiding: false }))
            {
                var at = SquareRect(steed.Position).GetCenter();
                DrawCircle(at, CellSize * 0.3f, Saddle with { A = 0.7f });
                DrawString(ThemeDB.FallbackFont, at + new Vector2(-CellSize / 2f, CellSize * 0.12f), "H", HorizontalAlignment.Center, CellSize,
                    Mathf.Max(10, CellSize * 2 / 5), new Color(1, 1, 1, 0.8f));
            }

            // Weapons lying on the ground — dropped, thrown, or knocked from a hand: a short grey blade.
            var lying = encounter.GroundWeapons.Select(g => g.At)
                .Concat(encounter.Combatants.Where(c => c.DroppedWeapon is not null && c.DroppedAt is not null).Select(c => c.DroppedAt!.Value));
            foreach (var at in lying.Distinct())
            {
                var rect = SquareRect(at);
                var from = rect.Position + new Vector2(rect.Size.X * 0.62f, rect.Size.Y * 0.62f);
                DrawLine(from, from + new Vector2(rect.Size.X * 0.28f, -rect.Size.Y * 0.28f), new Color(0.75f, 0.75f, 0.8f), 3);
            }

            // The armed spell's ground, where the mouse would put it.
            if (active is not null && hovered is { } aim && CastAt(active, aim) is { } preview
                && encounter.PlanCast(active, preview) is { CanCast: true } aimed)
            {
                foreach (var square in aimed.Area.Count > 0 ? aimed.Area : aimed.Targets.Select(t => t.Position))
                {
                    DrawRect(SquareRect(square), SpellArea);
                }
            }

            if (hovered is { } hover)
            {
                DrawRect(SquareRect(hover).Grow(-2), HoverOutline, filled: false, width: 2);
            }

            // Illusions, as the party knows them: phantom walls hatched, phantom figures as ghosts.
            foreach (var illusion in encounter.Illusions)
            {
                var ghost = new Color(0.75f, 0.6f, 1f, 0.35f);
                if (illusion.Form == IllusionForm.Wall)
                {
                    foreach (var square in illusion.Squares)
                    {
                        DrawRect(SquareRect(square).Grow(-3), ghost);
                        DrawRect(SquareRect(square).Grow(-3), ghost with { A = 0.8f }, filled: false, width: 2);
                    }
                }
                else if (illusion.Phantom is { } phantom)
                {
                    var centre = SquareRect(phantom.Position).GetCenter();
                    DrawCircle(centre, CellSize * 0.36f, ColourOf(illusion.Caster) with { A = 0.3f });
                    DrawArc(centre, CellSize * 0.36f, 0, Mathf.Tau, 32, ghost with { A = 0.9f }, 2);
                    DrawString(ThemeDB.FallbackFont, new Vector2(centre.X - CellSize / 2f, centre.Y + 6), "?", HorizontalAlignment.Center,
                        CellSize, Mathf.Max(9, CellSize / 4), Colors.White with { A = 0.7f });
                }
                else if (illusion.Form == IllusionForm.Reinforcements)
                {
                    foreach (var square in illusion.Squares)
                    {
                        DrawRect(SquareRect(square).Grow(-6), ghost, filled: false, width: 2);
                    }
                }
            }

            // A Swarm first, beneath: whoever stands in the mass is drawn on top of it.
            foreach (var combatant in encounter.Combatants.OrderBy(c => c.HoldsGround))
            {
                DrawToken(combatant, active);
            }

            DrawRoster();
            DrawBubbles();
        }

        /// <summary>
        /// The places a token's markers take, as fractions of its square: the four corners — bottom
        /// right, then anticlockwise — then the middles of the four sides, as the Owlbear extension
        /// draws them, so a table that uses both learns one place to look.
        /// </summary>
        private static readonly Vector2[] MarkerSlots =
        [
            new(1, 1), new(1, 0), new(0, 0), new(0, 1),
            new(1, 0.5f), new(0.5f, 0), new(0, 0.5f), new(0.5f, 1),
        ];

        private static readonly Dictionary<string, Texture2D?> MarkerTextures = new();

        /// <summary>
        /// What a creature is marked with, in the order drawn: first what its wound tracks have done
        /// to it (rust discs), then its conditions in the order they went on, then a mount (gold-rimmed).
        /// </summary>
        private static List<string> MarkersOf(Combatant c)
        {
            var markers = new List<string>();
            if (c.BodyStatus == BodyStatus.Dead)
            {
                markers.Add("dead");
            }
            else if (c.BodyStatus == BodyStatus.Dying)
            {
                markers.Add("dying");
            }
            else if (c.BodyStatus == BodyStatus.Stable)
            {
                markers.Add("stable");
            }

            if (c.IsStunned)
            {
                markers.Add("stunned");
            }

            if (c.IsNumbed)
            {
                markers.Add("numbed");
            }

            markers.AddRange(c.Conditions.Select(k => k.ToString().ToLowerInvariant()));
            if (c.IsRiding)
            {
                markers.Add("mounted");
            }

            return markers;
        }

        /// <summary>Draws a token's markers on its corners and sides — a ninth and more folded into a "+N" disc.</summary>
        private void DrawMarkers(Combatant combatant, Rect2 square)
        {
            var markers = MarkersOf(combatant);
            if (markers.Count == 0)
            {
                return;
            }

            if (markers.Count > MarkerSlots.Length)
            {
                var folded = markers.Count - MarkerSlots.Length + 1;
                markers = markers.Take(MarkerSlots.Length - 1).Append($"more-{Mathf.Min(9, folded)}").ToList();
            }

            var size = Mathf.Max(10, CellSize * 0.32f * Mathf.Max(1, combatant.Across * 0.75f));
            for (var i = 0; i < markers.Count; i++)
            {
                if (MarkerTexture(markers[i]) is not { } texture)
                {
                    continue;
                }

                var at = square.Position + square.Size * MarkerSlots[i] - Vector2.One * size / 2;
                DrawTextureRect(texture, new Rect2(at, new Vector2(size, size)), tile: false);
            }
        }

        /// <summary>A condition marker (game/assets/conditions, from the Owlbear extension), or null if there's no such file.</summary>
        private static Texture2D? MarkerTexture(string key)
        {
            if (MarkerTextures.TryGetValue(key, out var known))
            {
                return known;
            }

            Texture2D? texture = null;
            var path = $"res://assets/conditions/{key}.svg";
            if (FileAccess.FileExists(path))
            {
                var image = new Image();
                if (image.LoadSvgFromString(FileAccess.GetFileAsString(path), 0.64f) == Error.Ok)
                {
                    texture = ImageTexture.CreateFromImage(image);
                }
            }

            MarkerTextures[key] = texture;
            return texture;
        }

        private void DrawToken(Combatant combatant, Combatant? active)
        {
            var font = ThemeDB.FallbackFont;

            // Past the map's open edge: drawn pinned to the edge, small and faded, with how far out.
            var shownAt = new GridPosition(Mathf.Clamp(combatant.Position.X, 0, encounter.Width - 1), Mathf.Clamp(combatant.Position.Y, 0, encounter.Height - 1));
            var offMap = shownAt != combatant.Position;
            var centre = TokenCentre(combatant);
            var across = CellSize * combatant.Across;
            var rect = new Rect2(centre - new Vector2(across, across) / 2, new Vector2(across, across));
            var radius = across * (offMap ? 0.22f : combatant.IsSwarm ? 0.46f : 0.36f);

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

            // In the saddle: the horse under them, a brown disc a little wider than the rider.
            if (combatant.IsRiding)
            {
                DrawCircle(centre, radius + 6, Saddle);
            }

            // A character dying or stable lies there, faded, still to be reached.
            var colour = ColourOf(combatant);
            DrawCircle(centre, radius, combatant.IsDefenceless ? colour with { A = 0.35f } : combatant.IsSwarm ? colour with { A = 0.5f } : colour);

            DrawMarkers(combatant, rect);

            if (combatant == encounter.Active)
            {
                DrawArc(centre, radius + 4, 0, Mathf.Tau, 48, ActiveRing, 3);
            }
            else if (encounter.Phase == EncounterPhase.ChoosingCharacter && encounter.PartyStillToAct.Contains(combatant))
            {
                DrawArc(centre, radius + 4, 0, Mathf.Tau, 48, ChoosableRing, 3);
            }
            else if (active is not null && armedSpell is not null)
            {
                if (CastAt(active, combatant.Position) is { } aimAt && encounter.PlanCast(active, aimAt) is { CanCast: true } aimedAt
                    && aimedAt.Targets.Contains(combatant))
                {
                    DrawArc(centre, radius + 4, 0, Mathf.Tau, 48, new Color(0.75f, 0.5f, 1f), 3);
                }
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
                across, Mathf.Max(9, CellSize / 4), Colors.White);

            if (offMap)
            {
                var arrow = (combatant.Position.X - shownAt.X, combatant.Position.Y - shownAt.Y) switch
                {
                    ( > 0, _) => "→",
                    ( < 0, _) => "←",
                    (_, > 0) => "↓",
                    _ => "↑",
                };
                DrawString(font, new Vector2(rect.Position.X, rect.End.Y - 2), $"{arrow}{combatant.Position.DistanceTo(shownAt)}", HorizontalAlignment.Center,
                    CellSize, Mathf.Max(8, CellSize / 4), new Color(1f, 1f, 1f, 0.8f));
            }

            // What's been done to it, across the top of the square.
            var marks = string.Join(" ", new[]
            {
                // Conditions and what the wound tracks have done are markers on the corners now (DrawMarkers).
                combatant.Holding is not null ? "holding" : null,
                combatant.IsBroken ? "running" : null,
                combatant.ShieldBonus > 0 ? "shield" : null,
                combatant.IsHelped ? "helped" : null,
                combatant.IsHarried ? "harried" : null,
                combatant.CarriedLights > 0 ? "light" : null,
                combatant.NeedsFooting ? "off footing" : null,
                encounter.SpellsInPlay.Any(e => e.Target == combatant && e.Type == SpellType.Buff) ? "blessed" : null,
                encounter.SpellsInPlay.Any(e => e.Target == combatant && e.Type == SpellType.Bane) ? "cursed" : null,
            }.Where(m => m is not null));
            if (marks.Length > 0)
            {
                DrawString(font, new Vector2(rect.Position.X, rect.Position.Y + 13), marks, HorizontalAlignment.Center, across, Mathf.Max(7, CellSize / 6),
                    new Color(1f, 0.85f, 0.5f));
            }

            // Body bar along the foot of the square.
            var inset = CellSize / 9f;
            var bar = new Rect2(rect.Position.X + inset, rect.End.Y - inset - 2, across - inset * 2, Mathf.Max(2, CellSize / 14));
            DrawRect(bar, new Color(0, 0, 0, 0.6f));
            var fraction = Mathf.Clamp((float)combatant.Body / combatant.BodyMax, 0, 1);
            DrawRect(new Rect2(bar.Position, new Vector2(bar.Size.X * fraction, bar.Size.Y)), fraction <= 1f / 3 ? Rust.Lightened(0.15f) : Bone);
        }

        private void StartEncounter()
        {
            encounter = fightPicker.Selected switch
            {
                1 => SampleEncounters.SignpostWithCasters(data, Dice(), PickedGm()),
                2 => SampleEncounters.CampfireAmbush(data, Dice(), PickedGm()),
                3 => SampleEncounters.SignpostAmbush(data, Dice(), PickedGm(), fourPlayers: true),
                4 => SampleEncounters.FerryShrineAmbush(data, Dice(), PickedGm()),
                5 => SampleEncounters.FerryShrineWithCasters(data, Dice(), PickedGm()),
                6 => SampleEncounters.PartyVersusAmbush(data, Dice(), PickedGm()),
                7 => SampleEncounters.FighterVersusRatkin(data, Dice(), PickedGm()),
                8 => SampleEncounters.HedgeWitchesToll(data, Dice(), PickedGm()),
                9 => SampleEncounters.RivalBand(data, Dice(), PickedGm()),
                10 => SampleEncounters.RatswarmOnTheCauseway(data, Dice(), PickedGm()),
                11 => SampleEncounters.RatswarmOnTheCauseway(data, Dice(), PickedGm(), openGround: true),
                12 => SampleEncounters.ChampionOnTheGreen(data, Dice(), PickedGm()),
                13 => SampleEncounters.ShrineGuardians(data, Dice(), PickedGm()),
                14 => SampleEncounters.HollowStagInTheBirchwood(data, Dice(), PickedGm()),
                15 => SampleEncounters.Outriders(data, Dice(), PickedGm()),
                16 => SampleEncounters.GlamouredMill(data, Dice(), PickedGm()),
                17 => SampleEncounters.Roadblock(data, Dice(), PickedGm()),
                18 => SampleEncounters.GnollRaid(data, Dice(), PickedGm()),
                _ => SampleEncounters.SignpostAmbush(data, Dice(), PickedGm()),
            };
            // Lines written while the fight was set up came before anyone was listening.
            battleLog = new BattleLog(ColourOf);
            formattedLog.Clear();
            formattedLog.AddRange(encounter.Log.Select(line => battleLog.Format(line, encounter.Combatants, gmActing: false)));
            encounter.Logged += OnLogged;
            bubbles.Clear();
            rosterAt.Clear();
            tokenAt.Clear();
            actedThisRound.Clear();
            rosterRound = 0;
            shown = null;
            Disarm();
            stepPending = false;
            bot = null;
            UpdateBot();
            linesShown = 0;
            logLabel.Clear();
            Refresh();
        }

        /// <summary>Dice for a new fight: seeded from <c>--seed</c>, so a recorded fight can be played again exactly.</summary>
        private RandomDiceRoller Dice() => seed is { } s ? new RandomDiceRoller(new System.Random(s)) : new RandomDiceRoller();

        private void Refresh()
        {
            foreach (var line in formattedLog.Skip(linesShown))
            {
                logLabel.AppendText(line);
            }

            linesShown = formattedLog.Count;

            UpdateHud();
            QueueRedraw();
            UpdatePrompts();

            // The GM's turns always play themselves; with Bot v Bot on, so does the party.
            var gmToAct = encounter.Phase == EncounterPhase.AdversaryTurn;
            if ((gmToAct || (bot is not null && !encounter.IsOver)) && !stepPending)
            {
                stepPending = true;
                var waitingOn = encounter;
                GetTree().CreateTimer(demo ? 1.7 : gmToAct ? AdversaryTurnDelay : BotStepDelay).Timeout += () =>
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

            // A demo ends itself a few seconds after the fight does, so a recording stops cleanly.
            if (demo && encounter.IsOver && !quitting)
            {
                quitting = true;
                GetTree().CreateTimer(6).Timeout += () => GetTree().Quit();
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

            reactionExplained = pending.Prompt +
                (pending.WouldRiposte ? "\nIt's a critical, so the miss would be a Riposte: the attacker takes the wound." : string.Empty) +
                pending.Kind switch
                {
                    ReactionKind.Backlash or ReactionKind.Interrupt or ReactionKind.Shelter => "\nA choice, not a Stamina spend: nothing for the GM.",
                    ReactionKind.Sustain => "\nIt costs 1 Stamina and their tactical spend for the round, and hands the GM a banked Opportunity.",
                    _ => "\nIt costs 1 Stamina and their tactical spend for the round, and hands the GM an Immediate Opportunity.",
                };
            reactionLabel.Text = reactionExplained + (bot is null ? $"\nThe choice is in the ring round {pending.Defender.Name} — click them to bring it back." : string.Empty);

            // The choice itself is made on the ring; the buttons stay only for whoever shows the action buttons.
            var isLeaver = pending.Kind == ReactionKind.LeaverReaction;
            panicDodgeButton.Visible = !isLeaver && actionRowsShown;
            takeHitButton.Visible = !isLeaver && actionRowsShown;
            leaverButtons.Visible = actionRowsShown;
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

            (panicDodgeButton.Text, takeHitButton.Text) = ReactionChoices(pending);
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
                EncounterPhase.Withdrew => "The party got away.",
                _ => "Defeat."
            } + $"\n{encounter.Setting.Describe()}" +
              $"\nGM Opportunities: {encounter.BankedOpportunities}/{Encounter.MaxBankedOpportunities} banked" +
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
                $"Body {character.Body}/{character.BodyMax} ({character.BodyStatus})   Mind {character.Mind}/{character.MindMax}   Soul {character.Soul}/{character.SoulMax}   Stamina {character.Stamina}/{character.StaminaMax}\n" +
                $"Tactical spends this round: {character.TacticalSpendsThisRound}/{character.TacticalSpendsPerRound}\n" +
                $"Move: {(character.HasMove ? "available" : "used")}   Actions left: {character.ActionsRemaining}\n" +
                $"In hand: {attack.Name} +{attack.AttackModifier}, 1d{attack.DamageDieSides}+{attack.DamageModifier}" +
                (attack.IsRanged ? " (ranged — not at Close)" : $" (Reach {attack.Reach})") + "\n" +
                (character.OffHandWeapon is { } offHand ? $"Off hand: {offHand.Name} (1 Stamina to strike with it after an Attack)\n" : string.Empty) +
                $"At the ready: {character.ReadyWeapon?.Name ?? "nothing"}\n" +
                $"{Threshold("DT", character.DodgeThreshold, character.BaseDodgeThreshold)} · " +
                $"{Threshold("ST", character.StabilityThreshold, character.BaseStabilityThreshold)} · " +
                $"{Threshold("RT", character.ResolveThreshold, character.BaseResolveThreshold)} · " +
                $"{Threshold("FT", character.FortitudeThreshold, character.BaseFortitudeThreshold)} · " +
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
                (character.HasFreeHand ? ", a hand free to grab with." : ", no hand free to grab with.") +
                (encounter.Setting.Visibility != Visibility.Good
                    ? $"\nSeen at {encounter.VisibilityOf(character)}" + (character.CarriedLights > 0 ? ", lit by the light you carry." : ".")
                    : string.Empty) +
                (encounter.GroundAt(character.Position) is var ground && ground != Terrain.Firm
                    ? $"\nStanding on {ground} ground" + (encounter.Setting.Water.TryGetValue(character.Position, out var wet) ? $" — {wet.ToString().ToLowerInvariant()} water." : ".")
                    : string.Empty) +
                (character.NeedsFooting ? "\nOff your footing: your action goes on regaining it." : string.Empty) +
                (character.IsDazed ? $"\n{(character.IsStunned ? "Stunned" : "Numbed")}: no actions until an ally Meditates, Prays or heals the track." : string.Empty) +
                (character.Sustaining is { } holding ? $"\nSustaining {holding}: 1 Stamina at the start of each turn to keep it." : string.Empty) +
                string.Concat(encounter.SpellsInPlay.Where(e => e.Target == character)
                    .Select(e => $"\nUnder {e.Caster.Name}'s {e.Spell}" + (e.Threshold is { } t ? $" ({e.Amount:+0;−0} {t})" : e.Condition is { } c ? $" ({c})" : string.Empty)
                                 + (e.Sustained ? ", held" : string.Empty)));

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
            var footingWhy = acting is null ? "Not your turn." : Encounter.RegainFootingRefusal(acting);
            regainFootingButton.Disabled = footingWhy is not null;
            regainFootingButton.TooltipText = footingWhy ?? "Spend your action getting your footing back — nothing else takes an action until you do.";
            var duckWhy = acting is null ? "Not your turn." : Encounter.DuckBackRefusal(acting);
            duckBackButton.Disabled = duckWhy is not null;
            duckBackButton.TooltipText = duckWhy ?? "Duck back into full cover after attacking out of it: 1 Stamina, tactical — the GM gains an Immediate Opportunity.";
            var dropWhy = acting is null ? "Not your turn." : Encounter.DropLightRefusal(acting);
            dropLightButton.Disabled = dropWhy is not null;
            dropLightButton.TooltipText = dropWhy ?? "Let go of your light — free. It burns on where it falls, and you step out of it.";
            var lightWhy = acting is null ? "Not your turn." : Encounter.LightRefusal(acting);
            lightButton.Disabled = lightWhy is not null;
            lightButton.TooltipText = lightWhy ?? "Light a torch or lantern from your pack, into a free hand, with your action.";

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

            UpdateSpellRow(acting);

            foreach (var (exploit, button) in exploitButtons)
            {
                var why = acting is null ? "Not your turn." : Encounter.ExploitRefusal(acting, exploit);
                button.Visible = acting?.IsNimble == true;
                button.Disabled = why is not null;
                button.TooltipText = (why is null ? string.Empty : why + "\n") + ExploitCalculator.ConditionText(exploit) + " — " + ExploitCalculator.EffectText(exploit) +
                                     "\n1 Stamina, a tactical spend — and no Opportunity for the GM.";
            }

            var permitted = acting is null ? [] : Encounter.ManoeuvresOf(acting);
            foreach (var (kind, button) in manoeuvreButtons)
            {
                button.Visible = permitted.Contains(kind);
                button.ButtonPressed = armedManoeuvre == kind;
            }

            var me = isPartyTurn ? encounter.Active : null;
            standUpButton.Disabled = me is null || Encounter.StandUpRefusal(me) is not null;
            breakFreeButton.Disabled = me is null || Encounter.BreakFreeRefusal(me) is not null;
            takeUpButton.Visible = me?.DroppedWeapon is not null;
            takeUpButton.Disabled = me is null || Encounter.TakeUpRefusal(me) is not null;
            letGoButton.Disabled = me?.Holding is null;

            endTurnButton.Disabled = !isPartyTurn;
        }

        /// <summary>A button for each spell on the acting character's sheet, and the casting options that apply to them.</summary>
        private void UpdateSpellRow(Combatant? caster)
        {
            var spells = caster?.Spells ?? [];
            var wanted = caster is null ? string.Empty : caster.Name + "|" + string.Join("|", spells.Select(sp => sp.Name));
            if (wanted != spellButtonsFor)
            {
                foreach (var child in spellButtons.GetChildren())
                {
                    child.QueueFree();
                }

                foreach (var spell in spells)
                {
                    var button = new Button
                    {
                        Text = spell.Name,
                        ToggleMode = true,
                        TooltipText = $"{spell.SpellType} · {spell.Target} · {(spell.Range == SpellRange.Touch ? "Touch" : spell.Shape?.ToString() ?? "Ray")} · " +
                                      $"{SpellCalculator.SourceLabel(spell.Source)}\n{spell.Notes}\nThen click " +
                                      (spell.Target == SpellTarget.Many && spell.Range != SpellRange.Touch
                                          ? "a square: a block spreads away from you from there, or a Line or Cone is aimed that way (pick the Shape below)."
                                          : "who it's for."),
                    };
                    button.Pressed += () =>
                    {
                        var was = armedSpell;
                        Disarm();
                        armedSpell = was == spell ? null : spell;
                        Refresh();
                    };
                    spellButtons.AddChild(button);
                }

                spellButtonsFor = wanted;
            }

            foreach (var child in spellButtons.GetChildren())
            {
                if (child is Button button)
                {
                    button.ButtonPressed = armedSpell?.Name == button.Text;
                    button.Disabled = caster is null || caster.ActionsRemaining < 1;
                }
            }

            spellButtons.Visible = spells.Count > 0;
            payInBodyBox.Visible = caster?.MagicAccess.Any(a => a.CanBleedForPower) == true;
            overclockBox.Visible = caster?.HasOverclock == true;
            overclockBox.Disabled = caster?.HasOverclockedThisRound != false;
            sacrificeBox.Visible = caster?.HasSacrifice == true;
            sacrificeBox.Disabled = caster is null || Encounter.SacrificeRefusal(caster) is not null;
            backlashBox.Visible = spells.Count > 0;
            sustainBox.Visible = armedSpell is { } s && SpellCalculator.CanBeSustained(s.SpellType);
            thresholdPicker.Visible = armedSpell is { SpellType: SpellType.Buff or SpellType.Bane };
            tapPicker.Visible = armedSpell is { Source: MagicSource.ElementalMagic };
            effectPicker.Visible = armedSpell is { SpellType: SpellType.Control };
            bindPicker.Visible = armedSpell is { } bind && IsTieredBind(bind);
            shapePicker.Visible = armedSpell is { Target: SpellTarget.Many } many && many.Range != SpellRange.Touch;
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
            else if (hovered is { } square && encounter.AnyAt(square) is { Side: Side.Party } pointedAt)
            {
                shown = pointedAt;
            }

            var party = encounter.Combatants.Where(c => c.Side == Side.Party).ToList();
            partyLabel.Visible = party.Count > 1 && !demo;
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
            // The radial menu explains each action as it's hovered; this is only the way in.
            const string howToPlay = "Click the acting character for their actions; click a highlighted square to move. Escape steps back.";

            if (hovered is not { } square)
            {
                return howToPlay;
            }

            if (active is not null && armedSpell is { } armedOne)
            {
                if (CastAt(active, square) is not { } cast)
                {
                    return $"{armedOne.Name}: click who it's for.";
                }

                var plan = encounter.PlanCast(active, cast);
                if (!plan.CanCast)
                {
                    return $"{armedOne.Name}: {plan.Refusal}";
                }

                var paid = plan.Fuel is { } fuel
                    ? $"{fuel.Cost} {fuel.Element} Fuel ({InventoryCalculator.DescribeFuelPayment(fuel, InventoryCalculator.FuelUnitsHeld(active.Build!, fuel.Element))})"
                    : plan.WoundsPaid > 0 ? $"{plan.WoundsPaid} {plan.Track}" : "no wounds";
                var roll = SpellCalculator.FixedThresholdFor(armedOne.SpellType) is { } against && armedOne.SpellType != SpellType.Heal
                    ? $", rolling 2d12+{plan.CastBonus} ({plan.CastAbility}) vs {against}"
                    : ", no roll to land";
                return $"Click to cast {armedOne.Name} at {plan.Range} — {paid}{(cast.Overclock ? " and 1 Stamina" : string.Empty)}{roll}, " +
                       $"catching {string.Join(", ", plan.Targets.Select(t => t.Name))}.";
            }

            if (encounter.Phase == EncounterPhase.ChoosingCharacter && encounter.AnyAt(square) is { Side: Side.Party } candidate)
            {
                return encounter.PartyStillToAct.Contains(candidate)
                    ? $"Click to have {candidate.Name} act next."
                    : $"{candidate.Name} has already acted this round.";
            }

            if (encounter.AnyAt(square) is { Side: Side.Adversaries } foe)
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
                Position = new Vector2(BoardOrigin.X * 2 + BoardArea.X, HudTop),
                CustomMinimumSize = new Vector2(1280 - BoardArea.X - BoardOrigin.X * 3, 720 - HudTop * 2),
            };
            layer.AddChild(panel);

            var margin = new MarginContainer();
            foreach (var side in new[] { "left", "right", "top", "bottom" })
            {
                margin.AddThemeConstantOverride($"margin_{side}", 12);
            }

            panel.AddChild(margin);

            panel.ClipContents = true;
            panel.Size = panel.CustomMinimumSize;
            var column = new VBoxContainer();
            column.AddThemeConstantOverride("separation", 10);
            margin.AddChild(column);
            hudColumn = column;

            statusLabel = WrappingLabel(column);
            statusLabel.AddThemeFontSizeOverride("font_size", 16);
            partyLabel = WrappingLabel(column);
            chooseRow = new HFlowContainer();
            chooseRow.AddThemeConstantOverride("h_separation", 8);
            column.AddChild(chooseRow);
            characterLabel = WrappingLabel(column);
            hoverLabel = WrappingLabel(column);
            hoverLabel.AddThemeColorOverride("font_color", new Color(0.75f, 0.75f, 0.8f));

            // The actions live on the radial menu round the acting token; these rows are the same
            // actions as buttons, kept out of sight unless asked for.
            var showButtons = new CheckButton { Text = "Show action buttons", TooltipText = "Click the acting character's token for their actions. These are the same, as buttons." };
            column.AddChild(showButtons);
            var actionRows = new VBoxContainer { Visible = false };
            actionRows.AddThemeConstantOverride("separation", 10);
            column.AddChild(actionRows);
            showButtons.Toggled += on =>
            {
                actionRows.Visible = on;
                actionRowsShown = on;
            };

            var buttons = new HFlowContainer();
            buttons.AddThemeConstantOverride("h_separation", 8);
            buttons.AddThemeConstantOverride("v_separation", 8);
            actionRows.AddChild(buttons);

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

            duckBackButton = new Button { Text = "Duck back" };
            duckBackButton.Pressed += () =>
            {
                encounter.TryDuckBack();
                Refresh();
            };
            buttons.AddChild(duckBackButton);

            regainFootingButton = new Button { Text = "Regain footing" };
            regainFootingButton.Pressed += () =>
            {
                encounter.TryRegainFooting();
                Refresh();
            };
            buttons.AddChild(regainFootingButton);

            dropLightButton = new Button { Text = "Drop light" };
            dropLightButton.Pressed += () =>
            {
                encounter.TryDropLight();
                Refresh();
            };
            buttons.AddChild(dropLightButton);

            lightButton = new Button { Text = "Light a torch" };
            lightButton.Pressed += () =>
            {
                encounter.TryLight();
                Refresh();
            };
            buttons.AddChild(lightButton);

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

            takeUpButton = new Button { Text = "Take up weapon", TooltipText = "Your action: pick up the weapon a Disarm knocked to your feet, and stop being Off-guard.", Visible = false };
            takeUpButton.Pressed += () =>
            {
                encounter.TryTakeUpWeapon();
                Refresh();
            };
            buttons.AddChild(takeUpButton);

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

            // Magic: a button per spell, and the choices made in the same breath as the cast.
            spellButtons = new HFlowContainer();
            spellButtons.AddThemeConstantOverride("h_separation", 8);
            actionRows.AddChild(spellButtons);
            var castOptions = new HFlowContainer();
            castOptions.AddThemeConstantOverride("h_separation", 8);
            actionRows.AddChild(castOptions);
            payInBodyBox = new CheckBox { Text = "Pay in Body", TooltipText = "Bleeding for Power: pay an Aether or Divine spell's cost from Body instead. Never below 1 Body." };
            overclockBox = new CheckBox { Text = "Overclock", TooltipText = "Once a round: 1 Stamina pays 1 wound of the cost. Not a tactical spend." };
            sacrificeBox = new CheckBox { Text = "Sacrifice", TooltipText = "Once a round: 1 Body before an attack or a cast, for Advantage on its roll." };
            sustainBox = new CheckBox { Text = "Sustain", TooltipText = "Declare it held: 1 Stamina at the start of each of your turns keeps it going." };
            backlashBox = new CheckBox { Text = "Backlash", TooltipText = "If paying would take Mind or Soul to 0, hold at 1 and pay the rest from Body." };
            thresholdPicker = new OptionButton { TooltipText = "Which Threshold a Buff or Bane moves." };
            foreach (var threshold in BuffThresholds)
            {
                thresholdPicker.AddItem(threshold.ToString());
            }

            tapPicker = new OptionButton { TooltipText = "Where an Elemental spell's Fuel comes from, after what the place offers. A tapped source is spent whole — and a light goes out with it." };
            tapPicker.AddItem("Fuel: pack only");
            tapPicker.AddItem("Fuel: tap the light in hand");
            tapPicker.AddItem("Fuel: tap a fire beside me");

            effectPicker = new OptionButton { TooltipText = "What a Control spell does to whoever it lands on. It lasts by Tier, and is rechecked free at the end of each of your turns." };
            effectPicker.AddItem("Fear: gives ground a band a turn");
            effectPicker.AddItem("Confusion: attacks whoever is in reach");
            effectPicker.AddItem("Compulsion: drops its weapon and kneels");

            bindPicker = new OptionButton { TooltipText = "A Bind may produce its own Tier's effect or a lower one: Frozen (T3), Stuck (T2), Slowed (T1)." };
            bindPicker.AddItem("Bind: its own Tier's effect");
            bindPicker.AddItem("Bind: one Tier lower");
            bindPicker.AddItem("Bind: two Tiers lower");

            shapePicker = new OptionButton { TooltipText = "The Shape an area spell goes out in, named as you cast it: a block, a straight Line, or a Cone widening as it goes. A Line or a Cone starts beside you and is aimed the way of the square you click." };
            shapePicker.AddItem("Shape: Volume (a block)");
            shapePicker.AddItem("Shape: Line");
            shapePicker.AddItem("Shape: Cone");

            foreach (var option in new Control[] { payInBodyBox, overclockBox, sacrificeBox, sustainBox, backlashBox, thresholdPicker, tapPicker, effectPicker, bindPicker, shapePicker })
            {
                castOptions.AddChild(option);
            }

            // A Nimble character's Exploits, and a Martial character's manoeuvres.
            foreach (var exploit in ExploitCalculator.All)
            {
                var button = new Button { Text = $"{exploit} (1 Stamina)" };
                button.Pressed += () =>
                {
                    encounter.TryExploit(exploit);
                    Refresh();
                };
                castOptions.AddChild(button);
                exploitButtons[exploit] = button;
            }

            foreach (var kind in new[] { WeaponManoeuvre.Cleave, WeaponManoeuvre.Topple, WeaponManoeuvre.Ring, WeaponManoeuvre.Hook, WeaponManoeuvre.Disarm, WeaponManoeuvre.Sunder })
            {
                var button = new Button
                {
                    Text = kind.ToString(),
                    ToggleMode = true,
                    TooltipText = "A manoeuvre with your next attack: 1 Stamina, declared before the roll, and the GM banks an Opportunity. Then click the foe." +
                                  (kind == WeaponManoeuvre.Cleave ? " The sweep carries into the weakest other foe in your Reach." : string.Empty),
                };
                button.Pressed += () =>
                {
                    var was = armedManoeuvre;
                    Disarm();
                    armedManoeuvre = was == kind ? null : kind;
                    Refresh();
                };
                castOptions.AddChild(button);
                manoeuvreButtons[kind] = button;
            }

            // Bot v Bot: a party brain takes the player's seat, and the fight plays itself.
            var botRow = new HBoxContainer();
            botRow.AddThemeConstantOverride("separation", 8);
            column.AddChild(botRow);

            var restartButton = new Button { Text = "Restart" };
            restartButton.Pressed += StartEncounter;
            botRow.AddChild(restartButton);

            fightPicker = new OptionButton();
            fightPicker.FitToLongestItem = false;
            fightPicker.ClipText = true;
            fightPicker.AddItem("Signpost ambush (Quickstart)");
            fightPicker.AddItem("Signpost ambush, the Quickstart five (with casters)");
            fightPicker.AddItem("The camp at night: the five round their campfire");
            fightPicker.AddItem("Signpost ambush, four players");
            fightPicker.AddItem("Ambush at the Ferry Shrine ford (Inner Sea)");
            fightPicker.AddItem("The Ferry Shrine ford, the Quickstart five");
            fightPicker.AddItem("Roadside ambush (party of three)");
            fightPicker.AddItem("The Dumb Fighter v three ratkin");
            fightPicker.AddItem("The hedge-witches' toll (enemy casters)");
            fightPicker.AddItem("The Grey Lanterns: a rival company of five");
            fightPicker.AddItem("A Ratswarm on the fen causeway");
            fightPicker.AddItem("A Ratswarm on open ground");
            fightPicker.AddItem("A Noble Champion on the village green");
            fightPicker.AddItem("The shrine's guardians: a golem and two suits of mail");
            fightPicker.AddItem("The Hollow Stag: a Solo in the birchwood");
            fightPicker.AddItem("Outriders: bandits on horseback");
            fightPicker.AddItem("The glamoured mill: bats and a glamourer");
            fightPicker.AddItem("The roadblock: crossbows behind cover");
            fightPicker.AddItem("A Duskfang Gnoll with bandits (Rampage)");
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
            tacticPicker.FitToLongestItem = false;
            tacticPicker.ClipText = true;
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
            gmPicker.FitToLongestItem = false;
            gmPicker.ClipText = true;
            gmPicker.AddItem("Best — weighs every option, takes the most effective");
            gmPicker.AddItem("Balanced — usually the best");
            gmPicker.AddItem("Poor — loose and careless");
            gmPicker.AddItem("Simple — goes for the nearest");
            gmPicker.ItemSelected += _ => StartEncounter();
            gmRow.AddChild(gmPicker);

            // The second dial beside how cleverly the GM chooses: how much it knows of the party.
            var knowsRow = new HBoxContainer();
            knowsRow.AddThemeConstantOverride("separation", 8);
            column.AddChild(knowsRow);
            knowsRow.AddChild(new Label { Text = "GM knows:" });
            knowledgePicker = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TooltipText = "How much the GM knows of the party. Takes effect from the next fight." };
            knowledgePicker.FitToLongestItem = false;
            knowledgePicker.ClipText = true;
            knowledgePicker.AddItem("With its skill — sheets at Best, what it has seen below");
            knowledgePicker.AddItem("The sheets — every Threshold, every caster");
            knowledgePicker.AddItem("What it has seen — rolls, casts, the look of them");
            knowledgePicker.ItemSelected += _ => StartEncounter();
            knowsRow.AddChild(knowledgePicker);

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
                BbcodeEnabled = true,
            };
            foreach (var font in new[] { "normal_font_size", "bold_font_size" })
            {
                logLabel.AddThemeFontSizeOverride(font, 14);
            }

            column.AddChild(logLabel);

            // Above the tabs, what's needed at a glance: the round, a reaction being asked, the hover
            // line. Below, one tab at a time, each with the whole height: the log, the character
            // sheet, the party, and the game's options (the old action buttons among them).
            column.MoveChild(reactionBox, statusLabel.GetIndex() + 1);
            hoverLabel.Reparent(column);
            column.MoveChild(hoverLabel, reactionBox.GetIndex() + 1);

            var tabs = new TabContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
            column.AddChild(tabs);
            Tab("Log", scrolls: false, logLabel);
            Tab("Character", scrolls: true, characterLabel);
            Tab("Party", scrolls: true, partyLabel, chooseRow);
            Tab("Options", scrolls: true, showButtons, actionRows, botRow, gmRow);
            tabs.CurrentTab = 0;

            void Tab(string title, bool scrolls, params Control[] parts)
            {
                var page = new VBoxContainer { Name = title, SizeFlagsVertical = Control.SizeFlags.ExpandFill, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
                page.AddThemeConstantOverride("separation", 10);
                if (scrolls)
                {
                    var scroller = new ScrollContainer { Name = title, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
                    tabs.AddChild(scroller);
                    page.Name = "Page";
                    scroller.AddChild(page);
                }
                else
                {
                    tabs.AddChild(page);
                }

                foreach (var part in parts)
                {
                    part.Reparent(page);
                }
            }

            // Over everything else, so its rings sit on top of board and HUD alike.
            var top = new CanvasLayer { Layer = 10 };
            AddChild(top);
            radial = new RadialMenu();
            radial.Changed += Refresh;
            top.AddChild(radial);
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
