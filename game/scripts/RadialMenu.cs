using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace WoundBoundGame
{
    /// <summary>
    /// One choice on a ring. A leaf does something (<see cref="Pick"/>); a branch opens another ring
    /// beside it (<see cref="Children"/>); a toggle flips and keeps the ring open (<see cref="IsOn"/>).
    /// </summary>
    /// <param name="Why">Why it can't be chosen now, or null when it can. A greyed item still shows its reason.</param>
    /// <param name="Hint">What it does, shown while the mouse is on it.</param>
    public sealed record RadialItem(string Label, string? Why = null, string? Hint = null)
    {
        public Action? Pick { get; init; }

        public Func<IReadOnlyList<RadialItem>>? Children { get; init; }

        public Func<bool>? IsOn { get; init; }

        /// <summary>The icon to draw it as (a file in res://assets/icons), its label moving to the hover box. Null: drawn as words.</summary>
        public string? Icon { get; init; }

        /// <summary>A direction, drawn as an arrow pointing that way (screen axes, y down). Null: no arrow.</summary>
        public Vector2? Arrow { get; init; }

        public bool IsBranch => Children is not null;

        /// <summary>A branch is open to look into even when nothing in it can be done: it shows why.</summary>
        public bool CanOpen => IsBranch || Why is null;
    }

    /// <summary>
    /// Rings of choices around a token, each further decision opening a ring beside the item that
    /// asked it. Escape (or a right click) closes the last ring opened; a click on nothing closes them
    /// all. Drawn over everything, and swallows the mouse only while a ring is open.
    /// </summary>
    public partial class RadialMenu : Control
    {
        private sealed class Ring(Vector2 centre, string title, IReadOnlyList<RadialItem> items)
        {
            public Vector2 Centre { get; set; } = centre;
            public string Title { get; } = title;
            public IReadOnlyList<RadialItem> Items { get; set; } = items;
            public Func<IReadOnlyList<RadialItem>>? Source { get; init; }
            public List<Rect2> Pills { get; } = new();

            /// <summary>Which item opened the next ring, drawn lit while that ring is open.</summary>
            public int? Opened { get; set; }
        }

        private const float PillHeight = 28;
        private const float FontSize = 15;
        private const float HintWidth = 300;

        private readonly List<Ring> rings = new();

        /// <summary>The token the first ring belongs to — where its leader line runs, if the ring had to shift to stay on screen.</summary>
        private Vector2 anchor;
        private (int Ring, int Item)? hover;

        /// <summary>Raised after any choice is made, so the board can redraw.</summary>
        public event Action? Changed;

        public bool IsOpen => rings.Count > 0;

        private static Font Font => ThemeDB.FallbackFont;

        public override void _Ready()
        {
            SetAnchorsPreset(LayoutPreset.FullRect);
            MouseFilter = MouseFilterEnum.Ignore;
        }

        /// <summary>Opens a first ring around <paramref name="centre"/>, closing any already open.</summary>
        public void Open(Vector2 centre, string title, Func<IReadOnlyList<RadialItem>> items)
        {
            rings.Clear();
            anchor = centre;
            Push(centre, title, items);
        }

        public void CloseAll()
        {
            if (rings.Count == 0)
            {
                return;
            }

            rings.Clear();
            hover = null;
            MouseFilter = MouseFilterEnum.Ignore;
            QueueRedraw();
        }

        /// <summary>Closes the last ring opened. False when none was open.</summary>
        public bool CloseLast()
        {
            if (rings.Count == 0)
            {
                return false;
            }

            rings.RemoveAt(rings.Count - 1);
            if (rings.Count > 0)
            {
                rings[^1].Opened = null;
            }

            hover = null;
            
            QueueRedraw();
            return true;
        }

        /// <summary>Re-reads every open ring's items: what can be done changes as choices are made.</summary>
        public void Reload()
        {
            foreach (var ring in rings.Where(r => r.Source is not null))
            {
                ring.Items = ring.Source!();
                Layout(ring);
            }

            QueueRedraw();
        }

        /// <summary>How many rings are open — for checking the menu without a mouse.</summary>
        public int OpenRings => rings.Count;

        /// <summary>Where the item on the last ring whose label starts with <paramref name="label"/> is drawn, or null.</summary>
        public Vector2? WhereIs(string label) =>
            rings.Count > 0 && rings[^1].Items.ToList().FindIndex(i => i.Label.StartsWith(label, StringComparison.OrdinalIgnoreCase)) is var at and >= 0
                ? rings[^1].Pills[at].GetCenter()
                : null;

        /// <summary>Chooses the item on the last ring whose label starts with <paramref name="label"/>, as a click would.</summary>
        public void Follow(string label)
        {
            if (rings.Count > 0 && rings[^1].Items.ToList().FindIndex(i => i.Label.StartsWith(label, StringComparison.OrdinalIgnoreCase)) is var at and >= 0)
            {
                hover = (rings.Count - 1, at);
                Choose(rings.Count - 1, at);
                hover = rings[^1].Pills.Count > 0 ? (rings.Count - 1, 0) : null;
            }
        }

        private void Push(Vector2 centre, string title, Func<IReadOnlyList<RadialItem>> items)
        {
            var ring = new Ring(centre, title, items()) { Source = items };
            Layout(ring);
            rings.Add(ring);
            QueueRedraw();
        }

        /// <summary>How wide an icon or arrow disc is drawn.</summary>
        private const float Disc = 44;

        private static readonly Dictionary<string, Texture2D?> Icons = new();

        /// <summary>
        /// The icon named <paramref name="key"/>, white on nothing: game-icons.net draws each as a white
        /// figure on a black square, and the square is dropped so the disc behind shows through. Null
        /// if there's no such file — the item is then drawn as words.
        /// </summary>
        private static Texture2D? IconFor(string? key)
        {
            if (key is null)
            {
                return null;
            }

            if (Icons.TryGetValue(key, out var known))
            {
                return known;
            }

            Texture2D? texture = null;
            var path = $"res://assets/icons/{key}.svg";
            if (FileAccess.FileExists(path))
            {
                var svg = FileAccess.GetFileAsString(path).Replace("<path d=\"M0 0h512v512H0z\"/>", string.Empty);
                var image = new Image();
                if (image.LoadSvgFromString(svg, 0.125f) == Error.Ok)
                {
                    texture = ImageTexture.CreateFromImage(image);
                }
            }

            Icons[key] = texture;
            return texture;
        }

        /// <summary>Drawn as a disc — an icon or an arrow — rather than as words.</summary>
        private static bool IsDisc(RadialItem item) => item.Arrow is not null || IconFor(item.Icon) is not null;

        private static float WidthOf(RadialItem item) =>
            IsDisc(item) ? Disc : Font.GetStringSize(Text(item), HorizontalAlignment.Left, -1, (int)FontSize).X + 22;

        /// <summary>How tall a row of the ring is: a disc's height if it has any, else a pill's.</summary>
        private static float RowHeight(IReadOnlyList<RadialItem> items) => items.Any(IsDisc) ? Disc : PillHeight;

        /// <summary>A ring of nothing but discs goes round a true circle.</summary>
        private static bool IsAllDiscs(IReadOnlyList<RadialItem> items) => items.Count > 0 && items.All(IsDisc);

        private static float CircleRadius(int count) => Math.Max(Hub + 10 + Disc / 2, count * (Disc + 10) / Mathf.Tau);

        private static string Text(RadialItem item) =>
            (item.IsOn is { } on ? (on() ? "● " : "○ ") : string.Empty) + item.Label + (item.IsBranch ? "  ›" : string.Empty);

        /// <summary>The disc at a ring's centre, which no pill comes nearer than.</summary>
        private const float Hub = 30;

        /// <summary>How far the middle of each column bows out from its ends.</summary>
        private const float Bow = 34;

        private const float RowGap = 8;

        /// <summary>
        /// The items in two bowed columns either side of the centre, like brackets round it: clockwise
        /// from the top right, down the right side and back up the left. The columns never meet,
        /// so no two pills touch however many there are — and the whole ring is kept on screen.
        /// </summary>
        private void Layout(Ring ring)
        {
            var count = ring.Items.Count;
            var right = (count + 1) / 2;
            var left = count - right;
            var screen = GetViewportRect().Size;
            var (halfWidth, halfHeight) = Extent(ring.Items);
            ring.Centre = new Vector2(
                Mathf.Clamp(ring.Centre.X, Math.Min(halfWidth + 6, screen.X / 2), Math.Max(screen.X - halfWidth - 6, screen.X / 2)),
                Mathf.Clamp(ring.Centre.Y, Math.Min(halfHeight + 6, screen.Y / 2), Math.Max(screen.Y - halfHeight - 6, screen.Y / 2)));

            ring.Pills.Clear();
            if (IsAllDiscs(ring.Items))
            {
                // Icons go evenly round a circle, clockwise from the top.
                var radius = CircleRadius(count);

                // Arrows stand where they point — North at the top — so the ring reads as a compass.
                var compass = ring.Items.All(item => item.Arrow is { } a && a != Vector2.Zero);
                for (var i = 0; i < count; i++)
                {
                    var angle = compass ? ring.Items[i].Arrow!.Value.Angle() : -Mathf.Pi / 2 + Mathf.Tau * i / count;
                    var at = ring.Centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                    ring.Pills.Add(new Rect2(at - new Vector2(Disc / 2, Disc / 2), new Vector2(Disc, Disc)));
                }

                return;
            }

            var height = RowHeight(ring.Items);
            for (var i = 0; i < count; i++)
            {
                var onRight = i < right;
                var row = onRight ? i : count - 1 - i;
                var rows = onRight ? right : left;
                var width = WidthOf(ring.Items[i]);
                var tall = IsDisc(ring.Items[i]) ? Disc : PillHeight;

                // Rows spaced evenly about the centre line; the middle ones bowed furthest out.
                var y = (row - (rows - 1) / 2f) * (height + RowGap);
                var span = Math.Max(1, (rows - 1) / 2f * (height + RowGap));
                var bow = rows == 1 ? Bow : Bow * Mathf.Sqrt(Math.Max(0, 1 - y * y / (span * span * 1.4f)));
                var inner = Hub + 10 + bow;
                var x = onRight ? inner + width / 2 : -inner - width / 2;
                ring.Pills.Add(new Rect2(ring.Centre + new Vector2(x, y) - new Vector2(width / 2, tall / 2), new Vector2(width, tall)));
            }
        }

        /// <summary>Rings older than the last two fold down to their hub, so a deep chain of choices never buries itself.</summary>
        private bool IsFolded(int ring) => ring < rings.Count - 2;

        private static Rect2 HubBox(Ring ring) => new(ring.Centre - new Vector2(Hub, Hub), new Vector2(Hub * 2, Hub * 2));

        /// <summary>Everything a ring covers: its hub and its pills.</summary>
        private static Rect2 Bounds(Ring ring) =>
            ring.Pills.Aggregate(new Rect2(ring.Centre - new Vector2(Hub, Hub), new Vector2(Hub * 2, Hub * 2)), (box, pill) => box.Merge(pill));

        /// <summary>How far a ring of <paramref name="items"/> reaches from its centre, across and up.</summary>
        private static (float HalfWidth, float HalfHeight) Extent(IReadOnlyList<RadialItem> items)
        {
            if (IsAllDiscs(items))
            {
                var reach = CircleRadius(items.Count) + Disc / 2;
                return (reach, reach);
            }

            var widest = items.Select(WidthOf).DefaultIfEmpty(60).Max();
            var rows = (items.Count + 1) / 2;
            return (Hub + 10 + Bow + widest, Math.Max(Hub, rows * (RowHeight(items) + RowGap) / 2f));
        }

        private (int Ring, int Item)? ItemAt(Vector2 point)
        {
            for (var r = rings.Count - 1; r >= 0 && !IsFolded(r); r--)
            {
                for (var i = 0; i < rings[r].Pills.Count; i++)
                {
                    if (rings[r].Pills[i].Grow(2).HasPoint(point))
                    {
                        return (r, i);
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// While a ring is open it takes every mouse button, mouse motion and Escape before anything
        /// else sees them — the board under it never gets a click meant for the menu. Read here rather
        /// than as GUI input, so it doesn't hang on this control's size or the HUD's mouse filters.
        /// </summary>
        public override void _Input(InputEvent @event)
        {
            if (!IsOpen)
            {
                return;
            }

            switch (MakeInputLocal(@event))
            {
                case InputEventKey { Pressed: true, Keycode: Key.Escape }:
                    CloseLast();
                    break;

                case InputEventMouseMotion motion:
                    var over = ItemAt(motion.Position);
                    if (over != hover)
                    {
                        hover = over;
                        QueueRedraw();
                    }

                    break;

                case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right }:
                    CloseLast();
                    break;

                case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click:
                    if (ItemAt(click.Position) is { } hit)
                    {
                        Choose(hit.Ring, hit.Item);
                    }
                    else
                    {
                        CloseAll();
                    }

                    break;

                case InputEventMouseButton:
                    // Releases and wheel turns: swallowed too, so the board sees nothing of the menu.
                    break;

                default:
                    return;
            }

            GetViewport().SetInputAsHandled();
        }

        private void Choose(int r, int i)
        {
            // Choosing on an earlier ring takes back whatever was opened after it.
            while (rings.Count > r + 1)
            {
                rings.RemoveAt(rings.Count - 1);
            }

            var ring = rings[r];
            var item = ring.Items[i];
            ring.Opened = null;

            if (item.Children is { } children)
            {
                ring.Opened = i;
                // The next ring opens beside the item — right, left, below or above, the first that's on
                // screen and clear of every ring already open.
                var pill = ring.Pills[i];
                var list = children();
                var next = new Ring(pill.GetCenter(), item.Label.Split(" (")[0], list) { Source = children };
                Layout(next);
                var size = Bounds(next).Size;
                var half = size / 2;
                var leftReach = next.Centre.X - Bounds(next).Position.X;
                var rightReach = Bounds(next).End.X - next.Centre.X;
                var candidates = new[]
                {
                    new Vector2(pill.End.X + 12 + leftReach, pill.GetCenter().Y),
                    new Vector2(pill.Position.X - 12 - rightReach, pill.GetCenter().Y),
                    new Vector2(pill.GetCenter().X, pill.End.Y + 12 + half.Y),
                    new Vector2(pill.GetCenter().X, pill.Position.Y - 12 - half.Y),
                };
                var screen = new Rect2(Vector2.Zero, GetViewportRect().Size);
                var best = candidates
                    .Select(at =>
                    {
                        next.Centre = at;
                        Layout(next);
                        var box = Bounds(next);
                        // Once this ring opens, all but its parent fold to their hubs.
                        var clash = rings.Select((r, k) => k < rings.Count - 1 ? HubBox(r) : Bounds(r)).Sum(b => b.Intersection(box).Area);
                        return (At: next.Centre, Score: clash + (screen.Encloses(box) ? 0 : 1e6f) + at.DistanceTo(next.Centre) * 50);
                    })
                    .OrderBy(c => c.Score)
                    .First();
                next.Centre = best.At;
                Layout(next);
                rings.Add(next);
                hover = null;
            }
            else if (item.Why is null)
            {
                item.Pick?.Invoke();
                if (item.IsOn is null)
                {
                    CloseAll();
                }
                else
                {
                    Reload();
                }

                Changed?.Invoke();
            }

            QueueRedraw();
        }

        public override void _Draw()
        {
            var lit = new Color(0.95f, 0.78f, 0.35f);
            if (rings.Count > 0)
            {
                DrawRect(GetViewportRect(), new Color(0, 0, 0, 0.3f));
            }
            if (rings.Count > 0 && rings[0].Centre.DistanceTo(anchor) > 4)
            {
                DrawLine(anchor, rings[0].Centre, new Color(lit, 0.6f), 2);
            }

            if (rings.Count > 0)
            {
                DrawArc(anchor, 15, 0, Mathf.Tau, 32, lit, 2);
            }
            for (var r = 0; r < rings.Count; r++)
            {
                var ring = rings[r];
                var faded = r < rings.Count - 1;
                if (IsFolded(r))
                {
                    // A breadcrumb: just the hub and its title, joined to the ring it led to.
                    DrawLine(ring.Centre, rings[r + 1].Centre, new Color(lit, 0.35f), 2);
                    DrawCircle(ring.Centre, Hub, new Color(0.08f, 0.08f, 0.1f));
                    DrawArc(ring.Centre, Hub, 0, Mathf.Tau, 40, new Color(lit, 0.3f), 2);
                    DrawCentred(ring.Title, ring.Centre, 12, new Color(0.9f, 0.9f, 0.9f, 0.6f), 64);
                    continue;
                }

                DrawCircle(ring.Centre, Hub, new Color(0.08f, 0.08f, 0.1f));
                DrawArc(ring.Centre, 30, 0, Mathf.Tau, 40, new Color(lit, faded ? 0.3f : 0.7f), 2);
                DrawCentred(ring.Title, ring.Centre, 12, new Color(0.9f, 0.9f, 0.9f, faded ? 0.5f : 1f), 64);

                for (var i = 0; i < ring.Items.Count; i++)
                {
                    var item = ring.Items[i];
                    var pill = ring.Pills[i];
                    var isHover = hover == (r, i);
                    var open = ring.Opened == i;
                    var able = item.Why is null;
                    DrawLine(ring.Centre, pill.GetCenter(), new Color(1, 1, 1, faded ? 0.05f : 0.12f), 1);
                    var fill = open ? new Color(0.45f, 0.33f, 0.1f)
                        : !able ? new Color(0.18f, 0.18f, 0.2f)
                        : isHover ? new Color(0.25f, 0.32f, 0.5f)
                        : new Color(0.14f, 0.18f, 0.28f);
                    fill = faded && !open ? fill.Darkened(0.35f) : fill;
                    var edge = able || item.IsBranch ? new Color(lit, isHover || open ? 1f : 0.45f) : new Color(0.4f, 0.4f, 0.42f);
                    var ink = new Color(able ? Colors.White : new Color(0.5f, 0.5f, 0.53f), faded && !open ? 0.6f : 1f);
                    if (IsDisc(item))
                    {
                        DrawDisc(item, pill.GetCenter(), fill, edge, ink, isHover);
                        continue;
                    }

                    DrawRect(pill, fill);
                    DrawRect(pill, edge, filled: false, width: isHover ? 2 : 1);
                    DrawCentred(Text(item), pill.GetCenter(), (int)FontSize, ink, pill.Size.X);
                }
            }

            if (hover is { } h && h.Ring < rings.Count && h.Item < rings[h.Ring].Items.Count)
            {
                var item = rings[h.Ring].Items[h.Item];

                // A disc shows no words of its own, so the box leads with its name.
                var name = IsDisc(item) ? Text(item).Replace("  ›", " ›") : null;
                var text = string.Join("\n", new[] { name, item.Why is { } why ? "Can't: " + why : null, item.Hint }.Where(t => !string.IsNullOrWhiteSpace(t)));
                if (text.Length > 0)
                {
                    DrawHint(text, rings[h.Ring].Pills[h.Item]);
                }
            }
        }

        /// <summary>
        /// An icon or an arrow in a disc. A branch carries a small notch on its rim — there's another
        /// ring behind it — and a toggle that's on is lit all round.
        /// </summary>
        private void DrawDisc(RadialItem item, Vector2 at, Color fill, Color edge, Color ink, bool isHover)
        {
            var radius = Disc / 2;
            DrawCircle(at, radius, fill);
            var on = item.IsOn?.Invoke() == true;
            DrawArc(at, radius, 0, Mathf.Tau, 40, on ? new Color(0.4f, 0.9f, 0.5f) : edge, isHover || on ? 2.5f : 1.5f);

            if (item.Arrow is { } way && way != Vector2.Zero)
            {
                var d = way.Normalized();
                var side = new Vector2(-d.Y, d.X);
                var tip = at + d * (radius - 9);
                var tail = at - d * (radius - 11);
                DrawLine(tail, tip - d * 6, ink, 3);
                DrawColoredPolygon([tip, tip - d * 10 + side * 7, tip - d * 10 - side * 7], ink);
            }
            else if (IconFor(item.Icon) is { } icon)
            {
                const float size = 28;
                DrawTextureRect(icon, new Rect2(at - new Vector2(size / 2, size / 2), new Vector2(size, size)), tile: false, modulate: ink);
            }

            if (item.IsBranch)
            {
                // The notch sits on the rim, at the bottom right.
                DrawCircle(at + new Vector2(radius * 0.72f, radius * 0.72f), 4, edge);
            }
        }

        private void DrawCentred(string text, Vector2 at, int size, Color colour, float width)
        {
            var measured = Font.GetStringSize(text, HorizontalAlignment.Left, -1, size);
            var origin = new Vector2(at.X - Math.Min(measured.X, width) / 2, at.Y + Font.GetAscent(size) / 2 - 2);
            DrawString(Font, origin, text, HorizontalAlignment.Center, Math.Max(measured.X, 1), size, colour);
        }

        /// <summary>The hovered item's reason and description, in a box beside it, kept on screen.</summary>
        private void DrawHint(string text, Rect2 pill)
        {
            const int size = 13;
            var measured = Font.GetMultilineStringSize(text, HorizontalAlignment.Left, HintWidth, size);
            var box = new Rect2(pill.Position + new Vector2(0, pill.Size.Y + 6), measured + new Vector2(16, 12));
            var screen = GetViewportRect().Size;
            if (box.End.Y > screen.Y)
            {
                box.Position = new Vector2(box.Position.X, pill.Position.Y - box.Size.Y - 6);
            }

            box.Position = new Vector2(Mathf.Clamp(box.Position.X, 4, Math.Max(4, screen.X - box.Size.X - 4)), Math.Max(4, box.Position.Y));
            DrawRect(box, new Color(0.05f, 0.05f, 0.07f, 0.95f));
            DrawRect(box, new Color(0.6f, 0.6f, 0.65f), filled: false, width: 1);
            DrawMultilineString(Font, box.Position + new Vector2(8, 6 + Font.GetAscent(size)), text, HorizontalAlignment.Left, HintWidth, size, -1, new Color(0.92f, 0.92f, 0.92f));
        }
    }
}
