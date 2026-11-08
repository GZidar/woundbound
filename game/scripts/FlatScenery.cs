using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using WoundBound.Game.Combat;
using WoundBound.Game.Figures;

namespace WoundBoundGame
{
    /// <summary>
    /// The place on the flat board (the user, 2026-11-08): the same scenery the 3D board builds,
    /// drawn as a map seen from above — ground in its colours, water with ripples, high ground with
    /// hachures down its slopes and teeth along its cliffs, and a top-down mark for every prop: a
    /// tree's crown, a bush, a boulder, crates, a cart, walls and a hut's roof. Tall props (Full
    /// cover, walls) fill their square; low ones (Partial) sit smaller in it.
    ///
    /// <para>Behind it all can go a battlemap picture (<see cref="Backdrop"/>): then the picture is the
    /// ground, and the water, elevation and props are drawn lightly over it — still there to be
    /// seen and used, since the picture alone doesn't say what hides you and what doesn't.</para>
    /// </summary>
    public sealed class FlatScenery(CanvasItem canvas)
    {
        private SceneryPlan? plan;
        private Setting? planFor;
        private Setting setting = Setting.Daylight;
        private Func<GridPosition, Rect2> squareRect = _ => new Rect2();
        private float cell = 1;
        private readonly Dictionary<string, Texture2D?> textures = new();

        /// <summary>The plan the board was last drawn from: what stands on each square.</summary>
        public SceneryPlan Plan(Setting of)
        {
            if (!ReferenceEquals(planFor, of))
            {
                plan = new SceneryPlan(of, plan);
                planFor = of;
            }

            return plan!;
        }

        /// <summary>The picture for <paramref name="backdrop"/>, loaded once; null if it can't be read.</summary>
        public Texture2D? Texture(Backdrop backdrop)
        {
            if (!textures.TryGetValue(backdrop.Path, out var texture))
            {
                var image = Image.LoadFromFile(backdrop.Path);
                texture = image is null || image.IsEmpty() ? null : ImageTexture.CreateFromImage(image);
                textures[backdrop.Path] = texture;
            }

            return texture;
        }

        /// <summary>Hands over a picture already loaded, so it isn't read from disk twice.</summary>
        public void Prime(string path, Texture2D texture) => textures[path] = texture;

        /// <summary>
        /// Draws <paramref name="of"/>, <paramref name="width"/> by <paramref name="height"/> squares,
        /// each laid where <paramref name="squareAt"/> says, <paramref name="size"/> pixels across:
        /// the ground, water, high ground and props, over <paramref name="backdrop"/> if there is one.
        /// </summary>
        public void Draw(Setting of, int width, int height, Func<GridPosition, Rect2> squareAt, float size, Backdrop? backdrop, IEnumerable<GridPosition> fires)
        {
            setting = of;
            squareRect = squareAt;
            cell = size;
            var plan = Plan(of);
            var picture = backdrop is null ? null : Texture(backdrop);
            // Which squares the picture covers, in squares; the rest get the board's own ground.
            var pictureCovers = new Rect2();

            if (picture is not null)
            {
                // The part of the picture under the map, scaled to the map's squares — only as much as
                // the picture holds; past its edges the board's own ground shows.
                var pps = backdrop!.PixelsPerSquare;
                var wanted = new Rect2(backdrop.OriginX, backdrop.OriginY, pps * width, pps * height);
                var source = wanted.Intersection(new Rect2(Vector2.Zero, picture.GetSize()));
                if (source.HasArea())
                {
                    var corner = squareRect(new GridPosition(0, 0)).Position;
                    var target = new Rect2(corner + (source.Position - wanted.Position) / pps * cell, source.Size / pps * cell);
                    canvas.DrawTextureRectRegion(picture, target, source);
                    pictureCovers = new Rect2((source.Position - wanted.Position) / pps, source.Size / pps);
                }
            }

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var square = new GridPosition(x, y);
                    var rect = squareRect(square);
                    var underPicture = picture is not null && pictureCovers.Encloses(new Rect2(x, y, 1, 1));
                    if (!underPicture)
                    {
                        var colour = Board3D.SurfaceColours[plan.SurfaceAt(square)];
                        var m = (SceneryPlan.Hash(square, 5) & 0xff) / 255f - 0.5f;
                        canvas.DrawRect(rect, colour.Lightened(Math.Max(0, m) * 0.1f).Darkened(Math.Max(0, -m) * 0.12f));
                        if (plan.IsRough(square))
                        {
                            Speckle(rect, square, colour.Darkened(0.35f));
                        }
                    }

                    if (setting.Water.TryGetValue(square, out var depth))
                    {
                        var water = depth == WaterDepth.Deep ? new Color(0.09f, 0.22f, 0.38f) : new Color(0.22f, 0.44f, 0.55f);
                        canvas.DrawRect(rect, underPicture ? water with { A = 0.3f } : water);
                        Ripple(rect, square, new Color(0.75f, 0.88f, 0.92f, underPicture ? 0.6f : 0.45f));
                    }

                    if (setting.Raised.Contains(square))
                    {
                        if (!underPicture)
                        {
                            canvas.DrawRect(rect, new Color(1, 1, 1, 0.08f));
                        }

                        Elevation(square, rect, setting);
                    }
                }
            }

            // Props last, as each may spread a little past its square.
            var alpha = picture is null ? 1f : 0.8f;
            var done = new HashSet<GridPosition>();
            foreach (var (square, prop) in plan.Props.OrderBy(p => p.Key.Y).ThenBy(p => p.Key.X))
            {
                if (done.Add(square))
                {
                    FlatProp(square, prop, plan, done, alpha);
                }
            }

            FlatRoofs(plan, alpha);
            foreach (var fire in fires)
            {
                var c = squareRect(fire).GetCenter();
                canvas.DrawCircle(c, cell * 0.3f, new Color(0.35f, 0.33f, 0.3f));
                canvas.DrawCircle(c, cell * 0.22f, new Color(0.95f, 0.45f, 0.1f));
                canvas.DrawCircle(c, cell * 0.12f, new Color(1f, 0.85f, 0.4f));
            }
        }

        /// <summary>Small stones scattered on rough ground.</summary>
        private void Speckle(Rect2 rect, GridPosition square, Color colour)
        {
            var h = SceneryPlan.Hash(square, 9);
            for (var i = 0; i < 4; i++)
            {
                var at = rect.Position + new Vector2((h >> (i * 4) & 0xf) / 16f * 0.8f + 0.1f, (h >> (i * 4 + 16) & 0xf) / 16f * 0.8f + 0.1f) * rect.Size;
                canvas.DrawCircle(at, Math.Max(1, rect.Size.X * 0.045f), colour);
            }
        }

        /// <summary>A wavy line across the water.</summary>
        private void Ripple(Rect2 rect, GridPosition square, Color colour)
        {
            var y = rect.Position.Y + rect.Size.Y * (0.3f + (SceneryPlan.Hash(square, 4) & 0xf) / 16f * 0.4f);
            var points = new Vector2[7];
            for (var i = 0; i < points.Length; i++)
            {
                var t = i / (points.Length - 1f);
                points[i] = new Vector2(rect.Position.X + rect.Size.X * (0.15f + 0.7f * t), y + Mathf.Sin(t * Mathf.Tau) * rect.Size.Y * 0.06f);
            }

            canvas.DrawPolyline(points, colour, Math.Max(1, rect.Size.X / 24));
        }

        /// <summary>
        /// The edges where high ground meets lower: hachures (short strokes pointing downhill) where it
        /// slopes, a heavy line with teeth where it drops sheer.
        /// </summary>
        private void Elevation(GridPosition square, Rect2 rect, Setting setting)
        {
            var cliff = setting.Scenery.Cliffs.Contains(square);
            var ink = new Color(0.22f, 0.18f, 0.14f, 0.8f);
            var s = rect.Size.X;
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                if (setting.Raised.Contains(new GridPosition(square.X + dx, square.Y + dy)))
                {
                    continue;
                }

                var outward = new Vector2(dx, dy);
                var along = new Vector2(-dy, dx);
                var edgeMid = rect.GetCenter() + outward * s / 2;
                if (cliff)
                {
                    canvas.DrawLine(edgeMid - along * s / 2, edgeMid + along * s / 2, ink, Math.Max(2, s / 10));
                    for (var i = 0; i < 3; i++)
                    {
                        var at = edgeMid + along * s * (i - 1) * 0.3f;
                        canvas.DrawColoredPolygon([at - along * s * 0.08f, at + along * s * 0.08f, at + outward * s * 0.14f], ink);
                    }
                }
                else
                {
                    for (var i = 0; i < 4; i++)
                    {
                        var at = edgeMid + along * s * (i - 1.5f) * 0.22f;
                        canvas.DrawLine(at - outward * s * 0.28f, at, ink with { A = 0.55f }, Math.Max(1, s / 20));
                    }
                }
            }
        }

        // ---- Props from above ----

        private static readonly Color FlatLeaf = new(0.2f, 0.36f, 0.15f);
        private static readonly Color FlatStone = new(0.52f, 0.5f, 0.47f);
        private static readonly Color FlatWood = new(0.55f, 0.4f, 0.24f);
        private static readonly Color FlatInk = new(0.12f, 0.1f, 0.08f);

        private void FlatProp(GridPosition square, Prop prop, SceneryPlan plan, HashSet<GridPosition> done, float alpha)
        {
            var rect = squareRect(square);
            var c = rect.GetCenter();
            var s = rect.Size.X;
            var h = SceneryPlan.Hash(square, 7);
            float R(int shift) => ((h >> shift) & 0xff) / 255f;
            Color A(Color colour) => colour with { A = colour.A * alpha };
            void Blob(Vector2 at, float radius, Color colour, int sides = 9, float rough = 0.18f, uint salt = 0)
            {
                var points = new Vector2[sides];
                var k = SceneryPlan.Hash(square, salt + 11);
                for (var i = 0; i < sides; i++)
                {
                    var r = radius * (1 + rough * (((k >> (i * 3 % 29)) & 0x7) / 7f - 0.5f) * 2);
                    var angle = Mathf.Tau * i / sides;
                    points[i] = at + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * r;
                }

                canvas.DrawColoredPolygon(points, A(colour));
                canvas.DrawPolyline([.. points, points[0]], A(colour.Darkened(0.45f)), Math.Max(1, s / 28));
            }

            void Box(Vector2 at, float size, float turn, Color colour, bool cross)
            {
                var corners = new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) }
                    .Select(v => at + v.Rotated(turn) * size / 2).ToArray();
                canvas.DrawColoredPolygon(corners, A(colour));
                canvas.DrawPolyline([.. corners, corners[0]], A(colour.Darkened(0.5f)), Math.Max(1, s / 24));
                if (cross)
                {
                    canvas.DrawLine(corners[0], corners[2], A(colour.Darkened(0.35f)), Math.Max(1, s / 30));
                    canvas.DrawLine(corners[1], corners[3], A(colour.Darkened(0.35f)), Math.Max(1, s / 30));
                }
            }

            switch (prop)
            {
                case Prop.Tree:
                case Prop.Birch:
                {
                    var leaf = prop == Prop.Birch ? new Color(0.45f, 0.56f, 0.22f) : FlatLeaf.Lightened(R(0) * 0.12f);
                    Blob(c + new Vector2(s * 0.04f, s * 0.05f), s * 0.5f, new Color(0, 0, 0, 0.25f), 11, 0.1f);
                    Blob(c, s * 0.47f, leaf, 11, 0.12f);
                    Blob(c + new Vector2(-s * 0.1f, -s * 0.1f), s * 0.25f, leaf.Lightened(0.15f), 9, 0.15f, 3);
                    break;
                }

                case Prop.Pine:
                {
                    // A star of boughs.
                    var points = new Vector2[16];
                    for (var i = 0; i < 16; i++)
                    {
                        var angle = Mathf.Tau * i / 16 + R(8);
                        points[i] = c + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * s * (i % 2 == 0 ? 0.48f : 0.3f);
                    }

                    canvas.DrawColoredPolygon(points, A(new Color(0.12f, 0.26f, 0.15f)));
                    canvas.DrawPolyline([.. points, points[0]], A(new Color(0.05f, 0.12f, 0.06f)), Math.Max(1, s / 28));
                    canvas.DrawCircle(c, s * 0.06f, A(new Color(0.3f, 0.22f, 0.14f)));
                    break;
                }

                case Prop.Bush:
                    for (var i = 0; i < 3; i++)
                    {
                        var angle = Mathf.Tau * i / 3 + R(0) * 2;
                        Blob(c + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * s * 0.13f, s * 0.2f, FlatLeaf.Lightened(0.12f + i * 0.04f), 8, 0.15f, (uint)i);
                    }

                    break;
                case Prop.Hedge:
                    Blob(c, s * 0.3f, FlatLeaf.Lightened(0.08f), 10, 0.12f);
                    foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    {
                        if (plan.PropAt(new GridPosition(square.X + dx, square.Y + dy)) == Prop.Hedge)
                        {
                            Blob(c + new Vector2(dx, dy) * s * 0.3f, s * 0.24f, FlatLeaf.Lightened(0.08f), 9, 0.12f, (uint)(dx + 2 * dy + 5));
                        }
                    }

                    break;
                case Prop.Boulder:
                    Blob(c, s * 0.46f, FlatStone, 9, 0.2f);
                    Blob(c + new Vector2(-s * 0.08f, -s * 0.1f), s * 0.2f, FlatStone.Lightened(0.15f), 7, 0.2f, 2);
                    break;
                case Prop.Rock:
                    Blob(c, s * 0.3f, FlatStone, 8, 0.25f);
                    break;
                case Prop.RockFace:
                    Blob(c, s * 0.52f, new Color(0.42f, 0.4f, 0.37f), 10, 0.22f);
                    break;
                case Prop.CrateStack:
                    Box(c + new Vector2(-s * 0.14f, -s * 0.12f), s * 0.5f, R(0) * 0.4f - 0.2f, FlatWood, true);
                    Box(c + new Vector2(s * 0.14f, s * 0.13f), s * 0.5f, R(8) * 0.4f - 0.2f, FlatWood.Darkened(0.08f), true);
                    Box(c, s * 0.42f, R(16) * 0.6f - 0.3f, FlatWood.Lightened(0.1f), true);
                    break;
                case Prop.Crate:
                    Box(c, s * 0.5f, R(0) * 0.6f - 0.3f, FlatWood, true);
                    break;
                case Prop.Barrel:
                    canvas.DrawCircle(c, s * 0.27f, A(FlatWood.Darkened(0.15f)));
                    canvas.DrawArc(c, s * 0.27f, 0, Mathf.Tau, 20, A(new Color(0.2f, 0.2f, 0.22f)), Math.Max(1, s / 18));
                    canvas.DrawArc(c, s * 0.16f, 0, Mathf.Tau, 16, A(new Color(0.2f, 0.2f, 0.22f)), Math.Max(1, s / 30));
                    break;
                case Prop.Cart:
                {
                    var east = new GridPosition(square.X + 1, square.Y);
                    var south = new GridPosition(square.X, square.Y + 1);
                    GridPosition? other = plan.PropAt(east) == Prop.Cart && !done.Contains(east) ? east
                        : plan.PropAt(south) == Prop.Cart && !done.Contains(south) ? south : null;
                    var end = other is { } o ? squareRect(o).GetCenter() : c;
                    if (other is { } took)
                    {
                        done.Add(took);
                    }

                    var mid = (c + end) / 2;
                    var alongX = other is null || other.Value == east;
                    var length = other is null ? s * 0.85f : s * 1.8f;
                    var size = alongX ? new Vector2(length, s * 0.7f) : new Vector2(s * 0.7f, length);
                    var bed = new Rect2(mid - size / 2, size);
                    foreach (var side in new[] { -1, 1 })
                    {
                        var wheel = alongX ? new Vector2(mid.X - length * 0.15f, mid.Y + side * s * 0.4f) : new Vector2(mid.X + side * s * 0.4f, mid.Y - length * 0.15f);
                        canvas.DrawRect(new Rect2(wheel - (alongX ? new Vector2(s * 0.25f, s * 0.05f) : new Vector2(s * 0.05f, s * 0.25f)), alongX ? new Vector2(s * 0.5f, s * 0.1f) : new Vector2(s * 0.1f, s * 0.5f)), A(FlatInk));
                    }

                    canvas.DrawRect(bed, A(FlatWood));
                    canvas.DrawRect(bed, A(FlatWood.Darkened(0.5f)), filled: false, width: Math.Max(1, s / 16));
                    canvas.DrawCircle(mid, s * 0.16f, A(new Color(0.72f, 0.64f, 0.46f)));
                    break;
                }

                case Prop.Fence:
                {
                    var any = false;
                    foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    {
                        if (plan.PropAt(new GridPosition(square.X + dx, square.Y + dy)) == Prop.Fence)
                        {
                            any = true;
                            canvas.DrawLine(c, c + new Vector2(dx, dy) * s / 2, A(FlatWood.Darkened(0.2f)), Math.Max(2, s / 10));
                        }
                    }

                    if (!any)
                    {
                        canvas.DrawLine(c - new Vector2(s * 0.45f, 0), c + new Vector2(s * 0.45f, 0), A(FlatWood.Darkened(0.2f)), Math.Max(2, s / 10));
                    }

                    canvas.DrawCircle(c, s * 0.08f, A(FlatWood.Darkened(0.4f)));
                    break;
                }

                case Prop.StoneWall:
                case Prop.LowWall:
                {
                    var tall = prop == Prop.StoneWall;
                    var inset = tall ? 0 : s * 0.2f;
                    canvas.DrawRect(rect.Grow(-inset), A(tall ? new Color(0.46f, 0.44f, 0.41f) : FlatStone));
                    // Courses of stone, seen from above.
                    for (var i = 1; i < 3; i++)
                    {
                        var yy = rect.Position.Y + inset + (s - 2 * inset) * i / 3f;
                        canvas.DrawLine(new Vector2(rect.Position.X + inset, yy), new Vector2(rect.End.X - inset, yy), A(FlatInk with { A = 0.35f }), 1);
                    }

                    canvas.DrawRect(rect.Grow(-inset), A(FlatInk with { A = 0.6f }), filled: false, width: Math.Max(1, s / 20));
                    break;
                }

                case Prop.HutWall:
                    canvas.DrawRect(rect, A(new Color(0.78f, 0.73f, 0.62f)));
                    canvas.DrawRect(rect, A(new Color(0.27f, 0.19f, 0.13f)), filled: false, width: Math.Max(2, s / 10));
                    break;
                case Prop.Pillar:
                    canvas.DrawRect(rect.Grow(-s * 0.08f), A(new Color(0.6f, 0.58f, 0.54f)));
                    canvas.DrawCircle(c, s * 0.3f, A(new Color(0.68f, 0.66f, 0.62f)));
                    canvas.DrawArc(c, s * 0.3f, 0, Mathf.Tau, 20, A(FlatInk with { A = 0.6f }), Math.Max(1, s / 24));
                    break;
                case Prop.Log:
                {
                    var ns = plan.PropAt(new GridPosition(square.X, square.Y - 1)) == Prop.Log || plan.PropAt(new GridPosition(square.X, square.Y + 1)) == Prop.Log;
                    var size = ns ? new Vector2(s * 0.5f, s) : new Vector2(s, s * 0.5f);
                    canvas.DrawRect(new Rect2(c - size / 2, size), A(new Color(0.36f, 0.25f, 0.16f)));
                    canvas.DrawRect(new Rect2(c - size / 2, size), A(FlatInk with { A = 0.6f }), filled: false, width: Math.Max(1, s / 24));
                    break;
                }

                case Prop.Stump:
                    canvas.DrawCircle(c, s * 0.28f, A(new Color(0.36f, 0.25f, 0.16f)));
                    canvas.DrawCircle(c, s * 0.22f, A(new Color(0.7f, 0.58f, 0.4f)));
                    canvas.DrawArc(c, s * 0.12f, 0, Mathf.Tau, 14, A(new Color(0.5f, 0.38f, 0.25f)), 1);
                    break;
                case Prop.Reeds:
                    for (var i = 0; i < 9; i++)
                    {
                        var at = c + new Vector2(R(i * 3 % 24) - 0.5f, R((i * 5 + 4) % 24) - 0.5f) * s * 0.7f;
                        canvas.DrawLine(at, at + new Vector2(s * 0.06f, -s * 0.18f), A(new Color(0.45f, 0.52f, 0.25f)), Math.Max(1, s / 24));
                        canvas.DrawCircle(at + new Vector2(s * 0.06f, -s * 0.18f), Math.Max(1, s * 0.03f), A(new Color(0.35f, 0.24f, 0.14f)));
                    }

                    break;
            }
        }

        /// <summary>A hut's roof from above, over every block of hut walls two squares or more across: thatch, with its ridge.</summary>
        private void FlatRoofs(SceneryPlan plan, float alpha)
        {
            var huts = plan.Props.Where(p => p.Value == Prop.HutWall).Select(p => p.Key).ToHashSet();
            while (huts.Count > 0)
            {
                var block = new HashSet<GridPosition>();
                var open = new Queue<GridPosition>([huts.First()]);
                while (open.TryDequeue(out var sq))
                {
                    if (!huts.Remove(sq))
                    {
                        continue;
                    }

                    block.Add(sq);
                    open.Enqueue(new GridPosition(sq.X + 1, sq.Y));
                    open.Enqueue(new GridPosition(sq.X - 1, sq.Y));
                    open.Enqueue(new GridPosition(sq.X, sq.Y + 1));
                    open.Enqueue(new GridPosition(sq.X, sq.Y - 1));
                }

                int x0 = block.Min(s => s.X), x1 = block.Max(s => s.X), y0 = block.Min(s => s.Y), y1 = block.Max(s => s.Y);
                if (x1 == x0 || y1 == y0)
                {
                    continue;
                }

                var area = new Rect2(squareRect(new GridPosition(x0, y0)).Position, squareRect(new GridPosition(x1, y1)).End - squareRect(new GridPosition(x0, y0)).Position);
                var thatch = new Color(0.62f, 0.5f, 0.28f, alpha);
                canvas.DrawRect(area.Grow(cell * 0.1f), thatch);
                canvas.DrawRect(area.Grow(cell * 0.1f), thatch.Darkened(0.45f), filled: false, width: Math.Max(1, cell / 16f));
                var alongX = area.Size.X >= area.Size.Y;
                var mid = area.GetCenter();
                canvas.DrawLine(alongX ? new Vector2(area.Position.X, mid.Y) : new Vector2(mid.X, area.Position.Y), alongX ? new Vector2(area.End.X, mid.Y) : new Vector2(mid.X, area.End.Y), thatch.Darkened(0.35f), Math.Max(2, cell / 10f));
            }
        }
    }
}
