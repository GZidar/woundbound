using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using WoundBound.Game.Combat;
using WoundBound.Rules.Enums;

namespace WoundBoundGame
{
    /// <summary>
    /// The scene editor (the user's battlemap utility, 2026-11-08): paint what every square does —
    /// walls, cover, water, rough ground, high ground — and what stands on it, over a battlemap
    /// picture if there is one, and where each side starts; then save it, or try it out in a fight.
    ///
    /// <para>The board is drawn exactly as the battle screen's flat board draws it (<see cref="FlatScenery"/>).
    /// Left drag paints with the chosen brush, right drag clears that brush's layer, middle drag
    /// pans, the wheel zooms; the Rectangle tool fills a dragged box. Ctrl+Z undoes, Ctrl+S saves.</para>
    /// </summary>
    public partial class SceneEditor : Node2D
    {
        /// <summary>A scene sent to the battle screen to be tried out; the battle screen fights on it until another fight is picked.</summary>
        public static SceneMap? Trying { get; set; }

        /// <summary>The scene last open in the editor, so coming back from trying it carries on with it.</summary>
        private static SceneMap? lastOpen;

        private static string? lastPath;

        private enum Layer
        {
            Solid,
            Water,
            Ground,
            Height,
            Zones,
            Prop,
            Surface,
            Fire,
            Hazard,
        }

        /// <summary>A brush: the layer it paints, the character it puts there, and for props, the prop it names.</summary>
        private sealed record Brush(string Name, Layer Layer, char Value, string Tip, Prop? Prop = null, Surface? Surface = null);

        private static readonly Vector2 BoardOrigin = new(12, 12);
        private static readonly Vector2 BoardSize = new(880, 696);

        private SceneMap map = SceneMap.Blank(SampleEncounters.FieldWidth, SampleEncounters.FieldHeight);
        private Setting setting = Setting.Daylight;
        private FlatScenery scenery = null!;
        private string? path;
        private readonly List<string> undo = new();

        private Brush brush = null!;
        private bool rectangleTool;
        private int brushSize = 1;
        private bool rulesView;

        private float cell = 28;
        private Vector2 pan;
        private GridPosition? hover;
        private GridPosition? dragFrom;
        private bool painting;
        private bool erasing;
        private bool panning;

        private LineEdit nameEdit = null!;
        private SpinBox widthSpin = null!;
        private SpinBox heightSpin = null!;
        private OptionButton biomePick = null!;
        private OptionButton lightPick = null!;
        private Label pictureLabel = null!;
        private SpinBox pictureSize = null!;
        private SpinBox pictureX = null!;
        private SpinBox pictureY = null!;
        private Label status = null!;
        private bool filling;

        public override void _Ready()
        {
            scenery = new FlatScenery(this);
            if (lastOpen is not null)
            {
                map = SceneMap.FromJson(lastOpen.ToJson());
                path = lastPath;
            }

            BuildPanel();
            Rebuild();
            Fit();
            ShowMap();

            // From the command line, for checking: --scene <file>, --battlemap <picture>, --fit, --rules.
            var args = OS.GetCmdlineUserArgs();
            string? After(string flag) => Array.IndexOf(args, flag) is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;
            if (After("--scene") is { } scene)
            {
                Open(scene);
            }

            if (After("--battlemap") is { } battlemap)
            {
                LoadPicture(battlemap);
            }

            if (args.Contains("--fit"))
            {
                FitToPicture();
            }

            rulesView = args.Contains("--rules");
        }

        // ---- The board ----

        private Rect2 SquareRect(GridPosition square) => new(BoardOrigin + pan + new Vector2(square.X, square.Y) * cell, new Vector2(cell, cell));

        private GridPosition SquareAt(Vector2 point)
        {
            var at = (point - BoardOrigin - pan) / cell;
            return new GridPosition(Mathf.FloorToInt(at.X), Mathf.FloorToInt(at.Y));
        }

        private bool OverBoard(Vector2 point) => new Rect2(BoardOrigin, BoardSize).HasPoint(point);

        /// <summary>Zooms and pans so the whole map fits the board area.</summary>
        private void Fit()
        {
            cell = Mathf.Max(4, Mathf.Min(BoardSize.X / map.Width, BoardSize.Y / map.Height));
            pan = (BoardSize - new Vector2(map.Width, map.Height) * cell) / 2;
        }

        /// <summary>The scene as the rules see it, made afresh after every change.</summary>
        private void Rebuild()
        {
            setting = map.ToSetting();
            lastOpen = map;
            lastPath = path;
            if (warnings is not null)
            {
                warnings.Text = string.Join(System.Environment.NewLine, map.Warnings());
            }

            QueueRedraw();
        }

        private Label? warnings;

        public override void _Draw()
        {
            DrawRect(new Rect2(Vector2.Zero, GetViewportRect().Size), new Color(0.09f, 0.09f, 0.11f));
            scenery.Draw(setting, map.Width, map.Height, SquareRect, cell, map.Backdrop, setting.Fires);

            for (var y = 0; y < map.Height; y++)
            {
                for (var x = 0; x < map.Width; x++)
                {
                    var square = new GridPosition(x, y);
                    var rect = SquareRect(square);
                    if (rulesView)
                    {
                        RulesMark(square, rect);
                    }

                    var current = SceneMap.At(map.Hazards, x, y);
                    if (current != '.')
                    {
                        var way = current switch { 'n' => Vector2.Up, 'e' => Vector2.Right, 's' => Vector2.Down, _ => Vector2.Left };
                        var c = rect.GetCenter();
                        var ink = new Color(0.95f, 0.75f, 0.2f, 0.9f);
                        DrawLine(c - way * cell * 0.35f, c + way * cell * 0.3f, ink, Mathf.Max(1, cell / 12));
                        DrawColoredPolygon([c + way * cell * 0.42f, c + way * cell * 0.18f + way.Orthogonal() * cell * 0.15f, c + way * cell * 0.18f - way.Orthogonal() * cell * 0.15f], ink);
                    }

                    var zone = SceneMap.At(map.Zones, x, y);
                    if (zone != '.')
                    {
                        var colour = zone == 'p' ? new Color(0.25f, 0.55f, 0.95f, 0.38f) : new Color(0.9f, 0.45f, 0.2f, 0.32f);
                        DrawRect(rect, colour);
                        if (zone == 'h')
                        {
                            DrawLine(rect.Position, rect.End, colour with { A = 0.8f }, 1);
                        }
                    }

                    DrawRect(rect, new Color(0, 0, 0, 0.18f), filled: false, width: 1);
                }
            }

            // Where the brush would land.
            foreach (var square in Footprint())
            {
                DrawRect(SquareRect(square), new Color(1, 1, 1, 0.85f), filled: false, width: 2);
            }

            // Over the side panel, the board stops.
            DrawRect(new Rect2(BoardOrigin + new Vector2(BoardSize.X, -BoardOrigin.Y), new Vector2(GetViewportRect().Size.X, GetViewportRect().Size.Y)), new Color(0.09f, 0.09f, 0.11f));
            DrawRect(new Rect2(BoardOrigin, BoardSize), new Color(0.4f, 0.4f, 0.45f), filled: false, width: 1);
        }

        /// <summary>What a square does, as a plain coloured mark — for tracing over a busy picture.</summary>
        private void RulesMark(GridPosition square, Rect2 rect)
        {
            var inner = rect.Grow(-cell * 0.12f);
            if (setting.IsWall(square))
            {
                DrawRect(inner, new Color(0.75f, 0.15f, 0.15f, 0.65f));
            }
            else if (setting.CoverAt(square) == Cover.Full)
            {
                DrawRect(inner, new Color(0.95f, 0.55f, 0.1f, 0.6f));
            }
            else if (setting.CoverAt(square) == Cover.Partial)
            {
                DrawRect(inner, new Color(0.95f, 0.85f, 0.2f, 0.55f));
            }

            if (setting.Water.TryGetValue(square, out var depth))
            {
                DrawRect(rect, depth == WaterDepth.Deep ? new Color(0.1f, 0.3f, 0.9f, 0.5f) : new Color(0.3f, 0.65f, 1f, 0.4f));
            }

            switch (setting.GroundAt(square))
            {
                case Terrain.Broken when !setting.Water.ContainsKey(square):
                    DrawCircle(rect.GetCenter(), cell * 0.12f, new Color(0.55f, 0.35f, 0.15f, 0.85f));
                    break;
                case Terrain.Sure:
                    DrawRect(rect.Grow(-cell * 0.35f), new Color(0.85f, 0.85f, 0.9f, 0.7f));
                    break;
            }

            if (setting.Raised.Contains(square))
            {
                DrawRect(rect.Grow(-2), setting.Scenery.Cliffs.Contains(square) ? new Color(0.2f, 0.1f, 0.05f, 0.9f) : new Color(0.95f, 0.95f, 0.6f, 0.9f), filled: false, width: 2);
            }
        }

        /// <summary>The squares the brush covers now: the dragged box with the Rectangle tool, else the brush's size about the pointer.</summary>
        private IEnumerable<GridPosition> Footprint()
        {
            if (hover is not { } at)
            {
                return [];
            }

            if (rectangleTool && dragFrom is { } from)
            {
                return Setting.Squares((from.X, from.Y, at.X, at.Y)).Where(s => map.Inside(s.X, s.Y));
            }

            var half = (brushSize - 1) / 2;
            return Setting.Squares((at.X - half, at.Y - half, at.X - half + brushSize - 1, at.Y - half + brushSize - 1)).Where(s => map.Inside(s.X, s.Y));
        }

        // ---- Painting ----

        private void Remember()
        {
            undo.Add(map.ToJson());
            if (undo.Count > 100)
            {
                undo.RemoveAt(0);
            }
        }

        private void Undo()
        {
            if (undo.Count == 0)
            {
                return;
            }

            map = SceneMap.FromJson(undo[^1]);
            undo.RemoveAt(undo.Count - 1);
            ShowMap();
            Rebuild();
        }

        /// <summary>Paints (or with <paramref name="clear"/>, clears) the brush's layer on <paramref name="squares"/>.</summary>
        private void Apply(IEnumerable<GridPosition> squares, bool clear)
        {
            foreach (var sq in squares)
            {
                int x = sq.X, y = sq.Y;
                switch (brush.Layer)
                {
                    case Layer.Solid:
                        map.Set(map.Solid, x, y, clear ? '.' : brush.Value);
                        map.SetProp(x, y, null);
                        break;
                    case Layer.Water:
                        map.Set(map.Water, x, y, clear ? '.' : brush.Value);
                        break;
                    case Layer.Ground:
                        map.Set(map.Ground, x, y, clear ? '.' : brush.Value);
                        break;
                    case Layer.Height:
                        map.Set(map.Heights, x, y, clear ? '.' : brush.Value);
                        break;
                    case Layer.Zones:
                        map.Set(map.Zones, x, y, clear ? '.' : brush.Value);
                        break;
                    case Layer.Prop:
                        // A prop is the cover it stands for: a tree is Full cover, a barrel Partial, a wall a wall.
                        map.Set(map.Solid, x, y, clear ? '.' : brush.Value);
                        map.SetProp(x, y, clear ? null : brush.Prop);
                        break;
                    case Layer.Surface:
                        map.SetSurface(x, y, clear ? null : brush.Surface);
                        break;
                    case Layer.Fire:
                        map.SetFire(x, y, !clear);
                        break;
                    case Layer.Hazard:
                        map.Set(map.Hazards, x, y, clear ? '.' : brush.Value);
                        break;
                }
            }

            Rebuild();
        }

        public override void _UnhandledInput(InputEvent @event)
        {
            switch (@event)
            {
                case InputEventKey { Pressed: true, Keycode: Key.Z, CtrlPressed: true }:
                    Undo();
                    return;
                case InputEventKey { Pressed: true, Keycode: Key.S, CtrlPressed: true }:
                    Save(saveAs: false);
                    return;
                case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown, Pressed: true } wheel when OverBoard(wheel.Position):
                {
                    var before = (wheel.Position - BoardOrigin - pan) / cell;
                    cell = Mathf.Clamp(cell * (wheel.ButtonIndex == MouseButton.WheelUp ? 1.15f : 1 / 1.15f), 4, 120);
                    pan = wheel.Position - BoardOrigin - before * cell;
                    QueueRedraw();
                    return;
                }

                case InputEventMouseButton { ButtonIndex: MouseButton.Middle } middle:
                    panning = middle.Pressed && OverBoard(middle.Position);
                    return;
                case InputEventMouseButton { ButtonIndex: MouseButton.Left or MouseButton.Right } click:
                    if (click.Pressed && OverBoard(click.Position))
                    {
                        Remember();
                        erasing = click.ButtonIndex == MouseButton.Right;
                        painting = true;
                        dragFrom = SquareAt(click.Position);
                        hover = dragFrom;
                        if (!rectangleTool)
                        {
                            Apply(Footprint(), erasing);
                        }
                    }
                    else if (!click.Pressed && painting)
                    {
                        if (rectangleTool)
                        {
                            Apply(Footprint(), erasing);
                        }

                        painting = false;
                        dragFrom = null;
                        QueueRedraw();
                    }

                    return;
                case InputEventMouseMotion motion:
                    if (panning)
                    {
                        pan += motion.Relative;
                        QueueRedraw();
                        return;
                    }

                    var now = OverBoard(motion.Position) ? SquareAt(motion.Position) : (GridPosition?)null;
                    if (now != hover)
                    {
                        hover = now;
                        if (painting && !rectangleTool && now is not null)
                        {
                            Apply(Footprint(), erasing);
                        }

                        status.Text = now is { } sq && map.Inside(sq.X, sq.Y) ? Describe(sq) : string.Empty;
                        QueueRedraw();
                    }

                    return;
            }
        }

        /// <summary>A line about the square under the pointer: what it is and what it does.</summary>
        private string Describe(GridPosition sq)
        {
            var parts = new List<string> { $"({sq.X}, {sq.Y})" };
            var plan = scenery.Plan(setting);
            if (plan.PropAt(sq) is { } prop)
            {
                parts.Add($"{prop} — {(setting.IsWall(sq) ? "wall" : setting.CoverAt(sq) == Cover.Full ? "Full cover" : "Partial cover")}{(setting.Unbreakable.Contains(sq) ? ", unbreakable" : string.Empty)}");
            }

            if (setting.Water.TryGetValue(sq, out var depth))
            {
                parts.Add($"{depth.ToString().ToLowerInvariant()} water");
            }

            parts.Add($"{plan.SurfaceAt(sq).ToString().ToLowerInvariant()}, {setting.GroundAt(sq)} ground");
            if (setting.Raised.Contains(sq))
            {
                parts.Add(setting.Scenery.Cliffs.Contains(sq) ? "raised, cliff" : "raised");
            }

            return string.Join(" · ", parts);
        }

        // ---- The panel ----

        private void BuildPanel()
        {
            var scroller = new ScrollContainer
            {
                Position = new Vector2(BoardOrigin.X + BoardSize.X + 12, 8),
                Size = new Vector2(GetViewportRect().Size.X - BoardOrigin.X - BoardSize.X - 20, GetViewportRect().Size.Y - 16),
                HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            };
            AddChild(scroller);
            var panel = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            panel.AddThemeConstantOverride("separation", 6);
            scroller.AddChild(panel);

            Row(panel, Btn("New", () => { Remember(); map = SceneMap.Blank(map.Width, map.Height); map.Backdrop = null; path = null; ShowMap(); Rebuild(); Fit(); }),
                Btn("Open…", () => FileDialogFor(save: false).PopupCentered(new Vector2I(900, 600))),
                Btn("Save", () => Save(saveAs: false)),
                Btn("Save as…", () => Save(saveAs: true)));
            Row(panel, Btn("Try it", TryIt, "Fight the Quickstart five against a band on this scene."),
                Btn("Back", () => GetTree().ChangeSceneToFile("res://scenes/Battle.tscn"), "Back to the battle screen."));

            nameEdit = new LineEdit { PlaceholderText = "Scene name" };
            nameEdit.TextChanged += text => { map.Name = text; lastOpen = map; };
            panel.AddChild(nameEdit);

            widthSpin = new SpinBox { MinValue = 6, MaxValue = 80, TooltipText = "Squares across." };
            heightSpin = new SpinBox { MinValue = 6, MaxValue = 80, TooltipText = "Squares down." };
            widthSpin.ValueChanged += _ => Resize();
            heightSpin.ValueChanged += _ => Resize();
            Row(panel, new Label { Text = "Size" }, widthSpin, new Label { Text = "×" }, heightSpin);

            biomePick = Pick(Enum.GetNames<Biome>(), "The kind of country: what unnamed cover and ground look like.");
            biomePick.ItemSelected += i => { if (!filling) { Remember(); map.Biome = (Biome)(int)i; Rebuild(); } };
            lightPick = Pick(["Day", "Dusk", "Night"], "How well lit the scene is (Visibility Good, Poor or None).");
            lightPick.ItemSelected += i => { if (!filling) { Remember(); map.Visibility = (Visibility)(int)i; Rebuild(); } };
            Row(panel, biomePick, lightPick);

            BuildWhoFights(panel);

            // The battlemap.
            panel.AddChild(new HSeparator());
            Row(panel, Btn("Battlemap…", () => PictureDialog().PopupCentered(new Vector2I(900, 600)), "Paint over a battlemap picture; its grid is found for you."),
                Btn("Clear", () => { Remember(); map.Backdrop = null; ShowMap(); Rebuild(); }),
                Btn("Fit map to it", FitToPicture, "Make the scene as many squares as the picture holds."));
            pictureLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(300, 0) };
            panel.AddChild(pictureLabel);
            pictureSize = Spin(0, 400, 0.05, "How many of the picture's pixels make one square.");
            pictureX = Spin(-4000, 8000, 0.5, "Where square (0, 0)'s left edge is in the picture.");
            pictureY = Spin(-4000, 8000, 0.5, "Where square (0, 0)'s top edge is in the picture.");
            Row(panel, new Label { Text = "px" }, pictureSize, Btn("×2", () => pictureSize.Value *= 2), Btn("÷2", () => pictureSize.Value /= 2));
            Row(panel, new Label { Text = "left" }, pictureX, new Label { Text = "top" }, pictureY);
            suggestButton = Btn("Suggest from picture (Claude)", Suggest,
                "Ask Claude to read the battlemap and fill in walls, cover, water, ground and high ground for you to check. Replaces those layers (Ctrl+Z undoes). One paid API call.");
            panel.AddChild(suggestButton);
            keyEdit = new LineEdit { PlaceholderText = "Anthropic API key (or set ANTHROPIC_API_KEY)", Secret = true };
            var remember = new CheckBox { Text = "Remember key on this computer", TooltipText = "Keep the key in the game's user folder." };
            var saved = LoadKey();
            keyEdit.Text = saved ?? string.Empty;
            remember.ButtonPressed = saved is not null;
            remember.Toggled += on => SaveKey(on ? keyEdit.Text : null);
            keyEdit.TextChanged += text => { if (remember.ButtonPressed) { SaveKey(text); } };
            panel.AddChild(keyEdit);
            panel.AddChild(remember);

            // The tools.
            panel.AddChild(new HSeparator());
            var rect = new CheckButton { Text = "Rectangle", TooltipText = "Drag a box to fill, rather than painting square by square." };
            rect.Toggled += on => rectangleTool = on;
            var size = Pick(["1×1", "2×2", "3×3"], "Brush size.");
            size.ItemSelected += i => brushSize = (int)i + 1;
            var rules = new CheckButton { Text = "Rules view", TooltipText = "Colour each square by what it does — for tracing over a busy picture." };
            rules.Toggled += on => { rulesView = on; QueueRedraw(); };
            Row(panel, rect, size);
            panel.AddChild(rules);

            var group = new ButtonGroup();
            Brush? first = null;
            void Section(string title, params Brush[] brushes)
            {
                panel.AddChild(new Label { Text = title, Modulate = new Color(0.8f, 0.75f, 0.6f) });
                var flow = new HFlowContainer();
                flow.AddThemeConstantOverride("h_separation", 4);
                flow.AddThemeConstantOverride("v_separation", 4);
                panel.AddChild(flow);
                foreach (var b in brushes)
                {
                    var button = new Button { Text = b.Name, ToggleMode = true, ButtonGroup = group, TooltipText = b.Tip };
                    button.Pressed += () => brush = b;
                    flow.AddChild(button);
                    if (first is null)
                    {
                        first = b;
                        button.ButtonPressed = true;
                    }
                }
            }

            Brush P(string name, Prop prop, char value, string tip) => new(name, Layer.Prop, value, tip, Prop: prop);
            Section("Full cover (tall)",
                P("Tree", Prop.Tree, 'F', "A tree: Full cover."), P("Pine", Prop.Pine, 'F', "A pine: Full cover."), P("Birch", Prop.Birch, 'F', "A birch: Full cover."),
                P("Crate stack", Prop.CrateStack, 'F', "Crates stacked high: Full cover."), P("Boulder", Prop.Boulder, 'U', "A boulder: Full cover no blade will break."),
                new Brush("Full (auto)", Layer.Solid, 'F', "Full cover, drawn as the biome would have it."));
            Section("Partial cover (low)",
                P("Bush", Prop.Bush, 'P', "A bush: Partial cover."), P("Hedge", Prop.Hedge, 'P', "Hedge: Partial cover."), P("Rock", Prop.Rock, 'u', "A rock: Partial cover, unbreakable."),
                P("Crate", Prop.Crate, 'P', "A crate: Partial cover."), P("Barrel", Prop.Barrel, 'P', "Barrels: Partial cover."), P("Cart", Prop.Cart, 'P', "A cart — paint two squares side by side: Partial cover."),
                P("Fence", Prop.Fence, 'P', "Fence: Partial cover."), P("Low wall", Prop.LowWall, 'P', "A low wall: Partial cover."), P("Log", Prop.Log, 'P', "A fallen log: Partial cover."),
                P("Stump", Prop.Stump, 'P', "A stump: Partial cover."), P("Reeds", Prop.Reeds, 'P', "Reeds: Partial cover."),
                new Brush("Partial (auto)", Layer.Solid, 'P', "Partial cover, drawn as the biome would have it."));
            Section("Walls (block sight and movement)",
                P("Stone wall", Prop.StoneWall, '#', "A wall."), P("Hut wall", Prop.HutWall, '#', "A building's wall; a block of them gets a roof."),
                P("Pillar", Prop.Pillar, '#', "A pillar."), P("Rock face", Prop.RockFace, '#', "Sheer rock."));
            Section("Water",
                new Brush("Shallow", Layer.Water, 's', "Wading water: Broken ground."), new Brush("Deep", Layer.Water, 'd', "Deep water: Broken, and must be swum."));
            Section("Ground",
                new Brush("Broken", Layer.Ground, 'b', "Rough going: costs extra to cross."), new Brush("Sure", Layer.Ground, 'r', "Sure footing: a road, a floor."));
            Section("Height",
                new Brush("Raised", Layer.Height, 'r', "High ground, sloping at its edges: +1 to strike down, +1 DT from below."),
                new Brush("Cliff", Layer.Height, 'c', "High ground with sheer sides."));
            Section("Who starts where",
                new Brush("Party", Layer.Zones, 'p', "Where the party starts."), new Brush("GM in view", Layer.Zones, 'g', "Where the GM may place creatures, in the open."),
                new Brush("GM hidden", Layer.Zones, 'h', "Where the GM may place creatures lying in wait."));
            Section("Fire and hazards",
                new Brush("Fire", Layer.Fire, '.', "A built fire: light out to Long, Fuel for a Fire caster."),
                new Brush("Current ↑", Layer.Hazard, 'n', "A current sweeping north: Athletics or Endurance 18, or swept 4 squares and off your feet."),
                new Brush("Current →", Layer.Hazard, 'e', "A current sweeping east."),
                new Brush("Current ↓", Layer.Hazard, 's', "A current sweeping south."),
                new Brush("Current ←", Layer.Hazard, 'w', "A current sweeping west."));
            Section("Looks (no effect on the rules)",
                Enum.GetValues<Surface>().Select(sf => new Brush(sf.ToString(), Layer.Surface, '.', $"Paint the ground as {sf.ToString().ToLowerInvariant()}.", Surface: sf)).ToArray());
            brush = first!;

            panel.AddChild(new HSeparator());
            panel.AddChild(new Label
            {
                Text = "Left drag paints · right drag clears that layer · middle drag pans · wheel zooms · Ctrl+Z undo · Ctrl+S save",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                Modulate = new Color(0.7f, 0.7f, 0.75f),
            });
            status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(300, 0) };
            panel.AddChild(status);
            warnings = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(300, 0), Modulate = new Color(1f, 0.75f, 0.4f) };
            panel.AddChild(warnings);

            static void Row(Control into, params Control[] parts)
            {
                var row = new HBoxContainer();
                row.AddThemeConstantOverride("separation", 6);
                into.AddChild(row);
                foreach (var part in parts)
                {
                    row.AddChild(part);
                }
            }

            static Button Btn(string text, Action pressed, string tip = "")
            {
                var b = new Button { Text = text, TooltipText = tip };
                b.Pressed += pressed;
                return b;
            }

            static OptionButton Pick(string[] items, string tip)
            {
                var pick = new OptionButton { TooltipText = tip };
                foreach (var item in items)
                {
                    pick.AddItem(item);
                }

                return pick;
            }

            SpinBox Spin(double min, double max, double step, string tip)
            {
                var spin = new SpinBox { MinValue = min, MaxValue = max, Step = step, TooltipText = tip, CustomMinimumSize = new Vector2(90, 0) };
                spin.ValueChanged += _ => PictureTuned();
                return spin;
            }
        }

        // ---- Who fights ----

        private WoundBound.Game.Data.GameData? data;
        private readonly Dictionary<string, CheckBox> partyBoxes = new();
        private VBoxContainer foeRows = null!;
        private string[] statblocks = [];

        private void BuildWhoFights(VBoxContainer panel)
        {
            data = WoundBound.Game.Data.GameData.Load(file => FileAccess.GetFileAsString($"res://data/rules/{file}"));
            statblocks = data.Adversaries.Select(a => a.Name).Order().ToArray();
            panel.AddChild(new HSeparator());
            panel.AddChild(new Label { Text = "Party (none ticked: the Quickstart five)", Modulate = new Color(0.8f, 0.75f, 0.6f) });
            var flow = new HFlowContainer();
            panel.AddChild(flow);
            foreach (var name in SampleEncounters.QuickstartParty.Keys)
            {
                var box = new CheckBox { Text = name.Split(' ')[0] == "The" ? "Dumb Fighter" : name.Split(' ')[0], TooltipText = name };
                box.Toggled += _ => { if (!filling) { Remember(); map.Party = partyBoxes.Where(kv => kv.Value.ButtonPressed).Select(kv => kv.Key).ToList(); Rebuild(); } };
                partyBoxes[name] = box;
                flow.AddChild(box);
            }

            panel.AddChild(new Label { Text = "Foes (none: the clearing's band)", Modulate = new Color(0.8f, 0.75f, 0.6f) });
            foeRows = new VBoxContainer();
            panel.AddChild(foeRows);
            var add = new Button { Text = "Add foe", TooltipText = "Another statblock for the GM's side." };
            add.Pressed += () =>
            {
                Remember();
                map.Foes.Add(new SceneMap.FoeGroup(statblocks.FirstOrDefault(n => n == "Roadside Bandit") ?? statblocks[0], 1));
                ShowFoes();
            };
            panel.AddChild(add);
        }

        /// <summary>One row per group of foes: which statblock, how many, and a button to take them off.</summary>
        private void ShowFoes()
        {
            foreach (var child in foeRows.GetChildren())
            {
                child.QueueFree();
            }

            for (var i = 0; i < map.Foes.Count; i++)
            {
                var index = i;
                var group = map.Foes[i];
                var row = new HBoxContainer();
                var pick = new OptionButton { CustomMinimumSize = new Vector2(200, 0), ClipText = true, FitToLongestItem = false };
                foreach (var name in statblocks)
                {
                    pick.AddItem(name);
                }

                pick.Select(Math.Max(0, Array.IndexOf(statblocks, group.Adversary)));
                pick.ItemSelected += sel => { Remember(); map.Foes[index] = map.Foes[index] with { Adversary = statblocks[sel] }; Rebuild(); };
                var count = new SpinBox { MinValue = 1, MaxValue = 20, Value = group.Count };
                count.ValueChanged += v => { map.Foes[index] = map.Foes[index] with { Count = (int)v }; Rebuild(); };
                var remove = new Button { Text = "✕", TooltipText = "Take these off." };
                remove.Pressed += () => { Remember(); map.Foes.RemoveAt(index); ShowFoes(); Rebuild(); };
                row.AddChild(pick);
                row.AddChild(count);
                row.AddChild(remove);
                foeRows.AddChild(row);
            }
        }

        /// <summary>Puts the scene's own values into the panel.</summary>
        private void ShowMap()
        {
            filling = true;
            foreach (var (name, box) in partyBoxes)
            {
                box.ButtonPressed = map.Party.Contains(name);
            }

            ShowFoes();
            nameEdit.Text = map.Name;
            widthSpin.Value = map.Width;
            heightSpin.Value = map.Height;
            biomePick.Select((int)map.Biome);
            lightPick.Select(Math.Min(2, (int)map.Visibility));
            pictureSize.Value = map.Backdrop?.PixelsPerSquare ?? 0;
            pictureX.Value = map.Backdrop?.OriginX ?? 0;
            pictureY.Value = map.Backdrop?.OriginY ?? 0;
            pictureSize.Editable = pictureX.Editable = pictureY.Editable = map.Backdrop is not null;
            pictureLabel.Text = map.Backdrop is null ? "No battlemap" : System.IO.Path.GetFileName(map.Backdrop.Path);
            filling = false;
        }

        private void Resize()
        {
            if (filling || ((int)widthSpin.Value == map.Width && (int)heightSpin.Value == map.Height))
            {
                return;
            }

            Remember();
            map.Width = (int)widthSpin.Value;
            map.Height = (int)heightSpin.Value;
            map.Normalise();
            Rebuild();
            Fit();
        }

        private void PictureTuned()
        {
            if (filling || map.Backdrop is not { } backdrop)
            {
                return;
            }

            map.Backdrop = backdrop with { PixelsPerSquare = (float)Math.Max(1, pictureSize.Value), OriginX = (float)pictureX.Value, OriginY = (float)pictureY.Value };
            Rebuild();
        }

        private FileDialog PictureDialog()
        {
            var dialog = new FileDialog
            {
                FileMode = FileDialog.FileModeEnum.OpenFile,
                Access = FileDialog.AccessEnum.Filesystem,
                Filters = ["*.jpg, *.jpeg, *.png, *.webp ; Battlemap pictures"],
                UseNativeDialog = true,
                Title = "Choose a battlemap",
            };
            AddChild(dialog);
            dialog.FileSelected += LoadPicture;
            return dialog;
        }

        /// <summary>Loads a battlemap picture and lays the scene's squares over the middle of its grid.</summary>
        public void LoadPicture(string file)
        {
            if (Battlemaps.Load(file) is not { } read)
            {
                pictureLabel.Text = $"Couldn't read {System.IO.Path.GetFileName(file)}.";
                return;
            }

            Remember();
            scenery.Prime(file, read.Texture);
            map.Backdrop = read.Fit(file, map.Width, map.Height);
            ShowMap();
            pictureLabel.Text = $"{System.IO.Path.GetFileName(file)} — {read.Note}";
            Rebuild();
        }

        /// <summary>Makes the scene exactly as many squares as the picture holds, starting at its first grid line.</summary>
        private void FitToPicture()
        {
            if (map.Backdrop is not { } backdrop)
            {
                return;
            }

            var texture = scenery.Texture(backdrop);
            if (texture is null)
            {
                return;
            }

            Remember();
            var pitch = backdrop.PixelsPerSquare;
            var originX = backdrop.OriginX % pitch;
            var originY = backdrop.OriginY % pitch;
            map.Width = Math.Clamp((int)((texture.GetWidth() - originX) / pitch + 0.01f), 6, 80);
            map.Height = Math.Clamp((int)((texture.GetHeight() - originY) / pitch + 0.01f), 6, 80);
            map.Backdrop = backdrop with { OriginX = originX, OriginY = originY };
            map.Normalise();
            ShowMap();
            Rebuild();
            Fit();
        }

        // ---- Claude's suggestion ----

        private Button suggestButton = null!;
        private LineEdit keyEdit = null!;
        private const string KeyFile = "user://claude-key.cfg";

        private static string? LoadKey()
        {
            var config = new ConfigFile();
            return config.Load(KeyFile) == Error.Ok && config.GetValue("anthropic", "key", "").AsString() is { Length: > 0 } key ? key : null;
        }

        private static void SaveKey(string? key)
        {
            if (key is null)
            {
                DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(KeyFile));
                return;
            }

            var config = new ConfigFile();
            config.SetValue("anthropic", "key", key);
            config.Save(KeyFile);
        }

        /// <summary>
        /// Sends the part of the battlemap under the scene — scaled to the model's image limit — to
        /// Claude, and fills the layers from its answer when it comes back.
        /// </summary>
        private void Suggest()
        {
            if (map.Backdrop is not { } backdrop || scenery.Texture(backdrop)?.GetImage() is not { } image)
            {
                status.Text = "Load a battlemap first: the suggestion is read from the picture.";
                return;
            }

            // The picture under the map, and no more.
            var pps = backdrop.PixelsPerSquare;
            var want = new Rect2I((int)Math.Floor(backdrop.OriginX), (int)Math.Floor(backdrop.OriginY), (int)Math.Ceiling(pps * map.Width), (int)Math.Ceiling(pps * map.Height));
            var crop = want.Intersection(new Rect2I(0, 0, image.GetWidth(), image.GetHeight()));
            if (crop.Size.X < 8 || crop.Size.Y < 8)
            {
                status.Text = "The map doesn't lie over the picture: check the square size and offset.";
                return;
            }

            var part = image.GetRegion(crop);
            var scale = Math.Min(1f, (float)SceneSuggester.MaxImageEdge / Math.Max(crop.Size.X, crop.Size.Y));
            if (scale < 1)
            {
                part.Resize(Mathf.RoundToInt(crop.Size.X * scale), Mathf.RoundToInt(crop.Size.Y * scale), Image.Interpolation.Lanczos);
            }

            var jpeg = part.SaveJpgToBuffer(0.9f);
            double originX = (backdrop.OriginX - crop.Position.X) * scale, originY = (backdrop.OriginY - crop.Position.Y) * scale;
            double pitch = pps * scale;
            int width = map.Width, height = map.Height;
            var key = keyEdit.Text.Trim();
            suggestButton.Disabled = true;
            status.Text = "Asking Claude to read the picture… (this can take a minute)";
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    var suggestion = await SceneSuggester.Ask(jpeg, width, height, originX, originY, pitch, key);
                    Callable.From(() => Suggested(suggestion, width, height)).CallDeferred();
                }
                catch (Exception e)
                {
                    var message = e.Message;
                    Callable.From(() =>
                    {
                        suggestButton.Disabled = false;
                        status.Text = $"Claude couldn't make a suggestion: {message}";
                    }).CallDeferred();
                }
            });
        }

        private void Suggested(SceneSuggester.Suggestion suggestion, int width, int height)
        {
            suggestButton.Disabled = false;
            if (map.Width != width || map.Height != height)
            {
                status.Text = "The scene changed size while Claude was reading it; ask again.";
                return;
            }

            Remember();
            SceneSuggester.Apply(map, suggestion);
            Rebuild();
            status.Text = "Claude's suggestion is in — check it over (Rules view helps; Ctrl+Z takes it back). " + suggestion.Notes;
        }

        // ---- Files ----

        private static string ScenesFolder
        {
            get
            {
                var folder = ProjectSettings.GlobalizePath("user://scenes");
                DirAccess.MakeDirRecursiveAbsolute(folder);
                return folder;
            }
        }

        private FileDialog FileDialogFor(bool save)
        {
            var dialog = new FileDialog
            {
                FileMode = save ? FileDialog.FileModeEnum.SaveFile : FileDialog.FileModeEnum.OpenFile,
                Access = FileDialog.AccessEnum.Filesystem,
                Filters = ["*.scene.json ; Wound Bound scenes"],
                CurrentDir = ScenesFolder,
                CurrentFile = save ? $"{(string.IsNullOrWhiteSpace(map.Name) ? "scene" : map.Name)}.scene.json" : string.Empty,
                UseNativeDialog = true,
                Title = save ? "Save the scene" : "Open a scene",
            };
            AddChild(dialog);
            dialog.FileSelected += file =>
            {
                if (save)
                {
                    path = file;
                    Write();
                }
                else
                {
                    Open(file);
                }
            };
            return dialog;
        }

        private void Save(bool saveAs)
        {
            if (path is null || saveAs)
            {
                FileDialogFor(save: true).PopupCentered(new Vector2I(900, 600));
                return;
            }

            Write();
        }

        private void Write()
        {
            using var file = FileAccess.Open(path!, FileAccess.ModeFlags.Write);
            if (file is null)
            {
                status.Text = $"Couldn't save to {path}.";
                return;
            }

            file.StoreString(map.ToJson());
            lastPath = path;
            status.Text = $"Saved {System.IO.Path.GetFileName(path)}.";
        }

        public void Open(string file)
        {
            try
            {
                Remember();
                map = SceneMap.FromJson(FileAccess.GetFileAsString(file));
                path = file;
                ShowMap();
                Rebuild();
                Fit();
                status.Text = $"Opened {System.IO.Path.GetFileName(file)}.";
            }
            catch (Exception e)
            {
                status.Text = $"Couldn't open {System.IO.Path.GetFileName(file)}: {e.Message}";
            }
        }

        private void TryIt()
        {
            Trying = SceneMap.FromJson(map.ToJson());
            GetTree().ChangeSceneToFile("res://scenes/Battle.tscn");
        }
    }
}
