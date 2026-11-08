using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using WoundBound.Game.Combat;
using WoundBound.Game.Figures;

namespace WoundBoundGame
{
    /// <summary>
    /// The things that stand on solid squares, built from simple low-poly shapes: lumpy flat-shaded
    /// stones and leaf clumps, boxes, cylinders and cones, each varied by a hash of its square so the
    /// same map always looks the same and no two trees quite match. Tall ones (Full cover, walls)
    /// stand well over a crouching person; low ones (Partial) come to the waist.
    /// </summary>
    public partial class Board3D
    {
        private static readonly Color Bark = new(0.33f, 0.23f, 0.15f);
        private static readonly Color Leaf = new(0.25f, 0.42f, 0.18f);
        private static readonly Color PineLeaf = new(0.15f, 0.3f, 0.17f);
        private static readonly Color BirchLeaf = new(0.47f, 0.58f, 0.24f);
        private static readonly Color Granite = new(0.5f, 0.49f, 0.47f);
        private static readonly Color Timber = new(0.52f, 0.38f, 0.23f);
        private static readonly Color DarkTimber = new(0.27f, 0.19f, 0.13f);
        private static readonly Color Plaster = new(0.78f, 0.73f, 0.62f);
        private static readonly Color Thatch = new(0.62f, 0.5f, 0.28f);
        private static readonly Color Iron = new(0.22f, 0.22f, 0.24f);

        /// <summary>Lays out every prop afresh — on building, and when a piece of cover breaks.</summary>
        private void BuildProps(Encounter fight, SceneryPlan? previous)
        {
            propLayer?.QueueFree();
            propLayer = new Node3D();
            terrain.AddChild(propLayer);
            props.Clear();
            if (previous is not null)
            {
                plan = new SceneryPlan(fight.Setting, previous);
            }

            var done = new HashSet<GridPosition>();
            foreach (var (square, prop) in plan!.Props.OrderBy(p => p.Key.Y).ThenBy(p => p.Key.X))
            {
                if (done.Contains(square))
                {
                    continue;
                }

                var rng = new Rng(SceneryPlan.Hash(square, 7));
                var root = new Node3D { Position = new Vector3(square.X + 0.5f, FootHeight(square), square.Y + 0.5f) };
                propLayer.AddChild(root);
                var materials = new List<(StandardMaterial3D, Color)>();
                var squares = new List<GridPosition> { square };
                var kit = new Kit(root, materials, rng);

                switch (prop)
                {
                    case Prop.Tree: BuildTree(kit); break;
                    case Prop.Pine: BuildPine(kit); break;
                    case Prop.Birch: BuildBirch(kit); break;
                    case Prop.CrateStack: BuildCrateStack(kit); break;
                    case Prop.Boulder: BuildBoulder(kit); break;
                    case Prop.Pillar: BuildPillar(kit); break;
                    case Prop.StoneWall: BuildStoneCourses(kit, square, 1.6f, p => p == Prop.StoneWall); break;
                    case Prop.LowWall: BuildStoneCourses(kit, square, 0.6f, p => p == Prop.LowWall); break;
                    case Prop.HutWall: BuildHutWall(kit, square); break;
                    case Prop.RockFace: BuildRockFace(kit); break;
                    case Prop.Bush: BuildBush(kit); break;
                    case Prop.Hedge: BuildHedge(kit, square); break;
                    case Prop.Rock: BuildRock(kit); break;
                    case Prop.Crate: BuildCrate(kit); break;
                    case Prop.Barrel: BuildBarrels(kit); break;
                    case Prop.Cart: squares.AddRange(BuildCart(kit, square, done)); break;
                    case Prop.Fence: BuildFence(kit, square); break;
                    case Prop.Log: BuildLog(kit, square); break;
                    case Prop.Stump: BuildStump(kit); break;
                    case Prop.Reeds: BuildReedClump(kit); break;
                }

                done.UnionWith(squares);
                props.Add(new PropView(root, [.. squares], materials));
            }

            BuildRoofs(fight);
        }

        /// <summary>A deterministic random stream for one prop.</summary>
        private struct Rng(uint seed)
        {
            private uint state = seed == 0 ? 0x9e3779b9u : seed;

            public float Next()
            {
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;
                return (state & 0xffffff) / (float)0x1000000;
            }

            public float Range(float a, float b) => a + (b - a) * Next();

            public uint Seed() => (uint)(Next() * uint.MaxValue);
        }

        /// <summary>What a prop builder adds parts with: its root, the materials it made (for the fog), and its random stream.</summary>
        private sealed class Kit(Node3D root, List<(StandardMaterial3D, Color)> materials, Rng rng)
        {
            private Rng rng = rng;

            public Node3D Root => root;

            public float R(float a, float b) => rng.Range(a, b);

            public uint Seed() => rng.Seed();

            public StandardMaterial3D Paint(Color colour, float vary = 0.06f, float roughness = 0.9f)
            {
                var c = colour.Lightened(R(0, vary)).Darkened(R(0, vary));
                var material = new StandardMaterial3D { AlbedoColor = c, Roughness = roughness };
                materials.Add((material, c));
                return material;
            }

            public MeshInstance3D Add(Mesh mesh, Vector3 at, StandardMaterial3D material, Vector3? turn = null, Vector3? scale = null, Node3D? parent = null)
            {
                var part = new MeshInstance3D
                {
                    Mesh = mesh,
                    Position = at,
                    MaterialOverride = material,
                    RotationDegrees = turn ?? Vector3.Zero,
                    Scale = scale ?? Vector3.One,
                };
                (parent ?? root).AddChild(part);
                return part;
            }
        }

        // ---- Shapes ----

        private static readonly Vector3[] IcoVerts = MakeIco();

        private static Vector3[] MakeIco()
        {
            var t = (1 + Mathf.Sqrt(5)) / 2;
            return new[]
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
            }.Select(v => v.Normalized()).ToArray();
        }

        private static readonly int[] IcoFaces =
        [
            0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11,
            1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
            3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9,
            4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
        ];

        private static readonly Dictionary<(int, int), Mesh> LumpCache = new();

        /// <summary>
        /// A lumpy, flat-shaded blob: an icosahedron split once, its points pushed in and out — a stone,
        /// a clump of leaves. <paramref name="radius"/> across, stretched by <paramref name="scale"/>.
        /// </summary>
        private static Mesh Lumpy(float radius, Vector3 scale, uint seed, float rough = 0.22f)
        {
            // A handful of shapes per roughness, reused: the scale and turn on each part do the rest.
            var key = ((int)(seed % 12), (int)(rough * 100));
            if (!LumpCache.TryGetValue(key, out var mesh))
            {
                var points = new List<Vector3>(IcoVerts);
                var mid = new Dictionary<(int, int), int>();
                int Mid(int a, int b)
                {
                    var k = a < b ? (a, b) : (b, a);
                    if (!mid.TryGetValue(k, out var i))
                    {
                        i = points.Count;
                        points.Add(((points[a] + points[b]) / 2).Normalized());
                        mid[k] = i;
                    }

                    return i;
                }

                var faces = new List<int>();
                for (var f = 0; f < IcoFaces.Length; f += 3)
                {
                    int a = IcoFaces[f], b = IcoFaces[f + 1], c = IcoFaces[f + 2];
                    int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                    faces.AddRange([a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca]);
                }

                var rng = new Rng((uint)key.Item1 * 2654435761u + 17);
                var pushed = points.Select(p => p * (1 + rng.Range(-rough, rough))).ToArray();
                var surface = new SurfaceTool();
                surface.Begin(Mesh.PrimitiveType.Triangles);
                surface.SetSmoothGroup(uint.MaxValue);
                for (var f = 0; f < faces.Count; f += 3)
                {
                    // The icosahedron winds anticlockwise from outside; Godot's front faces are clockwise.
                    surface.AddVertex(pushed[faces[f]]);
                    surface.AddVertex(pushed[faces[f + 2]]);
                    surface.AddVertex(pushed[faces[f + 1]]);
                }

                surface.GenerateNormals();
                mesh = surface.Commit();
                LumpCache[key] = mesh;
            }

            return new ScaledMesh(mesh, scale * radius).Mesh;
        }

        /// <summary>A mesh with a scale baked in, so a part can carry its own scale and turn besides.</summary>
        private readonly struct ScaledMesh(Mesh mesh, Vector3 scale)
        {
            public Mesh Mesh => Bake(mesh, scale);

            private static readonly Dictionary<(Mesh, Vector3), Mesh> Baked = new();

            private static Mesh Bake(Mesh mesh, Vector3 scale)
            {
                var rounded = new Vector3(MathF.Round(scale.X, 2), MathF.Round(scale.Y, 2), MathF.Round(scale.Z, 2));
                if (Baked.TryGetValue((mesh, rounded), out var done))
                {
                    return done;
                }

                var tool = new MeshDataTool();
                var array = new ArrayMesh();
                array.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, mesh.SurfaceGetArrays(0));
                tool.CreateFromSurface(array, 0);
                for (var i = 0; i < tool.GetVertexCount(); i++)
                {
                    tool.SetVertex(i, tool.GetVertex(i) * rounded);
                    var n = tool.GetVertexNormal(i);
                    tool.SetVertexNormal(i, new Vector3(n.X / Math.Max(1e-3f, rounded.X), n.Y / Math.Max(1e-3f, rounded.Y), n.Z / Math.Max(1e-3f, rounded.Z)).Normalized());
                }

                var result = new ArrayMesh();
                tool.CommitToSurface(result);
                Baked[(mesh, rounded)] = result;
                return result;
            }
        }

        private static CylinderMesh Cylinder(float top, float bottom, float height, int sides = 7) =>
            new() { TopRadius = top, BottomRadius = bottom, Height = height, RadialSegments = sides, Rings = 1 };

        // ---- Tall ----

        private static void BuildTree(Kit k)
        {
            var height = k.R(1.5f, 1.85f);
            var lean = new Vector3(k.R(-4, 4), k.R(0, 360), k.R(-4, 4));
            var trunk = new Node3D { RotationDegrees = lean };
            k.Root.AddChild(trunk);
            k.Add(Cylinder(0.08f, 0.14f, height), new Vector3(0, height / 2, 0), k.Paint(Bark), parent: trunk);
            var leaf = k.Paint(Leaf, 0.1f);
            var deep = k.Paint(Leaf.Darkened(0.2f), 0.08f);
            k.Add(Lumpy(0.62f, new Vector3(1, 0.9f, 1), k.Seed()), new Vector3(0, height + 0.35f, 0), leaf, new Vector3(0, k.R(0, 360), 0), parent: trunk);
            for (var i = 0; i < 3; i++)
            {
                var angle = Mathf.Tau * i / 3 + k.R(0, 1);
                k.Add(Lumpy(k.R(0.38f, 0.46f), new Vector3(1, 0.85f, 1), k.Seed()),
                    new Vector3(Mathf.Cos(angle) * 0.34f, height + k.R(-0.1f, 0.2f), Mathf.Sin(angle) * 0.3f), i == 1 ? deep : leaf, new Vector3(0, k.R(0, 360), 0), parent: trunk);
            }
        }

        private static void BuildPine(Kit k)
        {
            k.Add(Cylinder(0.06f, 0.1f, 0.7f), new Vector3(0, 0.35f, 0), k.Paint(Bark));
            var needles = k.Paint(PineLeaf, 0.08f);
            var y = 0.5f;
            var width = k.R(0.56f, 0.64f);
            for (var tier = 0; tier < 3; tier++)
            {
                var h = 1.15f - tier * 0.15f;
                k.Add(Cylinder(0, width, h, 8), new Vector3(0, y + h / 2, 0), needles, new Vector3(k.R(-3, 3), k.R(0, 45), k.R(-3, 3)));
                y += h * 0.55f;
                width *= 0.74f;
            }
        }

        private static void BuildBirch(Kit k)
        {
            var bark = k.Paint(new Color(0.86f, 0.84f, 0.78f), 0.04f);
            var dark = k.Paint(new Color(0.15f, 0.14f, 0.13f), 0.02f);
            var height = k.R(1.9f, 2.3f);
            k.Add(Cylinder(0.05f, 0.085f, height), new Vector3(0, height / 2, 0), bark, new Vector3(k.R(-3, 3), 0, k.R(-3, 3)));
            for (var i = 0; i < 3; i++)
            {
                k.Add(new BoxMesh { Size = new Vector3(0.17f, 0.035f, 0.04f) }, new Vector3(0, 0.35f + i * 0.45f + k.R(0, 0.1f), 0), dark, new Vector3(0, k.R(0, 360), 0));
            }

            var leaf = k.Paint(BirchLeaf, 0.1f);
            for (var i = 0; i < 4; i++)
            {
                var angle = Mathf.Tau * i / 4 + k.R(0, 1);
                k.Add(Lumpy(k.R(0.3f, 0.38f), new Vector3(0.9f, 1.2f, 0.9f), k.Seed()),
                    new Vector3(Mathf.Cos(angle) * 0.22f, height - 0.15f + i * 0.15f, Mathf.Sin(angle) * 0.2f), leaf, new Vector3(0, k.R(0, 360), 0));
            }
        }

        private static void BuildCrateStack(Kit k)
        {
            var turn = k.R(-12, 12);
            Crate(k, new Vector3(-0.19f, 0, -0.17f), 0.55f, turn);
            Crate(k, new Vector3(0.2f, 0, 0.17f), 0.55f, turn + k.R(-8, 8));
            Crate(k, new Vector3(-0.2f, 0, 0.24f), 0.42f, turn + k.R(-15, 15));
            Crate(k, new Vector3(0.02f, 0.55f, -0.02f), 0.58f, turn + k.R(-15, 15));
            Crate(k, new Vector3(0.04f, 1.13f, 0f), 0.4f, turn + k.R(-25, 25));
        }

        /// <summary>A crate <paramref name="size"/> on a side, its foot at <paramref name="at"/>, with dark battens on its edges.</summary>
        private static void Crate(Kit k, Vector3 at, float size, float turn)
        {
            var box = new Node3D { Position = at, RotationDegrees = new Vector3(0, turn, 0) };
            k.Root.AddChild(box);
            k.Add(new BoxMesh { Size = Vector3.One * size }, new Vector3(0, size / 2, 0), k.Paint(Timber, 0.1f), parent: box);
            var batten = k.Paint(DarkTimber, 0.05f);
            var t = size * 0.1f;
            foreach (var (x, z) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
            {
                k.Add(new BoxMesh { Size = new Vector3(t, size + 0.01f, t) }, new Vector3(x * (size / 2 - t / 2 + 0.006f), size / 2, z * (size / 2 - t / 2 + 0.006f)), batten, parent: box);
            }

            k.Add(new BoxMesh { Size = new Vector3(size + 0.012f, t, size + 0.012f) }, new Vector3(0, size - t / 2, 0), batten, parent: box);
        }

        private static void BuildBoulder(Kit k)
        {
            var stone = k.Paint(Granite, 0.08f);
            k.Add(Lumpy(0.6f, new Vector3(1, 1.05f, 0.9f), k.Seed(), 0.25f), new Vector3(0, 0.55f, 0), stone, new Vector3(k.R(0, 30), k.R(0, 360), k.R(0, 20)));
            k.Add(Lumpy(0.3f, new Vector3(1, 0.7f, 1), k.Seed(), 0.25f), new Vector3(k.R(-0.3f, 0.3f), 0.15f, 0.35f), stone, new Vector3(0, k.R(0, 360), 0));
            // Moss on its shoulder.
            k.Add(Lumpy(0.36f, new Vector3(1, 0.3f, 1), k.Seed()), new Vector3(0.05f, 1.02f, -0.05f), k.Paint(new Color(0.3f, 0.42f, 0.2f), 0.06f), new Vector3(0, k.R(0, 360), 0));
        }

        private static void BuildPillar(Kit k)
        {
            var stone = k.Paint(new Color(0.62f, 0.6f, 0.56f), 0.05f);
            k.Add(new BoxMesh { Size = new Vector3(0.82f, 0.18f, 0.82f) }, new Vector3(0, 0.09f, 0), stone);
            k.Add(Cylinder(0.27f, 0.31f, 1.5f, 10), new Vector3(0, 0.93f, 0), stone);
            k.Add(new BoxMesh { Size = new Vector3(0.78f, 0.2f, 0.78f) }, new Vector3(0, 1.78f, 0), stone);
        }

        /// <summary>Courses of rough stone, filling the square and meeting the wall in the squares beside it.</summary>
        private void BuildStoneCourses(Kit k, GridPosition square, float height, Func<Prop, bool> same)
        {
            var courses = Mathf.Max(2, Mathf.RoundToInt(height / 0.27f));
            var h = height / courses;
            for (var c = 0; c < courses; c++)
            {
                var stones = 3;
                var offset = c % 2 == 0 ? 0 : 0.5f / stones;
                for (var s = 0; s < stones + (c % 2); s++)
                {
                    var x0 = Mathf.Max(-0.5f, -0.5f + s / (float)stones - offset);
                    var x1 = Mathf.Min(0.5f, -0.5f + (s + 1) / (float)stones - offset);
                    if (x1 - x0 < 0.05f)
                    {
                        continue;
                    }

                    var colour = Granite.Lerp(new Color(0.42f, 0.4f, 0.37f), k.R(0, 1));
                    k.Add(new BoxMesh { Size = new Vector3(x1 - x0 - 0.02f, h - 0.02f, 0.98f - k.R(0, 0.08f)) },
                        new Vector3((x0 + x1) / 2, h * c + h / 2, 0), k.Paint(colour, 0.03f));
                }
            }

            // Laid along the run: turned a quarter if the wall runs north to south.
            var ns = same(plan!.PropAt(new GridPosition(square.X, square.Y - 1)) ?? Prop.Bush) || same(plan.PropAt(new GridPosition(square.X, square.Y + 1)) ?? Prop.Bush);
            var ew = same(plan.PropAt(new GridPosition(square.X - 1, square.Y)) ?? Prop.Bush) || same(plan.PropAt(new GridPosition(square.X + 1, square.Y)) ?? Prop.Bush);
            if (ns && !ew)
            {
                k.Root.RotationDegrees = new Vector3(0, 90, 0);
            }
        }

        private void BuildHutWall(Kit k, GridPosition square)
        {
            const float height = 1.55f;
            k.Add(new BoxMesh { Size = new Vector3(1.0f, height, 1.0f) }, new Vector3(0, height / 2, 0), k.Paint(Plaster, 0.04f));
            var beam = k.Paint(DarkTimber, 0.03f);
            // Timber at the corners that face out of the hut, and along the top.
            foreach (var (dx, dy) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
            {
                var outside = plan!.PropAt(new GridPosition(square.X + dx, square.Y)) != Prop.HutWall || plan.PropAt(new GridPosition(square.X, square.Y + dy)) != Prop.HutWall;
                if (outside)
                {
                    k.Add(new BoxMesh { Size = new Vector3(0.1f, height, 0.1f) }, new Vector3(dx * 0.46f, height / 2, dy * 0.46f), beam);
                }
            }

            k.Add(new BoxMesh { Size = new Vector3(1.02f, 0.1f, 1.02f) }, new Vector3(0, height - 0.05f, 0), beam);
            k.Add(new BoxMesh { Size = new Vector3(1.02f, 0.12f, 1.02f) }, new Vector3(0, 0.06f, 0), k.Paint(Granite, 0.03f));
        }

        /// <summary>A thatched roof over every block of hut walls two squares or more across.</summary>
        private void BuildRoofs(Encounter fight)
        {
            var huts = plan!.Props.Where(p => p.Value == Prop.HutWall).Select(p => p.Key).ToHashSet();
            while (huts.Count > 0)
            {
                var start = huts.First();
                var block = new HashSet<GridPosition>();
                var open = new Queue<GridPosition>([start]);
                while (open.TryDequeue(out var sq))
                {
                    if (!huts.Remove(sq))
                    {
                        continue;
                    }

                    block.Add(sq);
                    foreach (var next in new[] { new GridPosition(sq.X + 1, sq.Y), new GridPosition(sq.X - 1, sq.Y), new GridPosition(sq.X, sq.Y + 1), new GridPosition(sq.X, sq.Y - 1) })
                    {
                        open.Enqueue(next);
                    }
                }

                int x0 = block.Min(s => s.X), x1 = block.Max(s => s.X) + 1, y0 = block.Min(s => s.Y), y1 = block.Max(s => s.Y) + 1;
                if (x1 - x0 < 2 || y1 - y0 < 2)
                {
                    continue;
                }

                // The ridge runs the long way; eaves overhang a little.
                var alongX = x1 - x0 >= y1 - y0;
                var span = alongX ? y1 - y0 : x1 - x0;
                var length = alongX ? x1 - x0 : y1 - y0;
                var root = new Node3D { Position = new Vector3((x0 + x1) / 2f, 1.55f, (y0 + y1) / 2f), RotationDegrees = new Vector3(0, alongX ? 0 : 90, 0) };
                propLayer!.AddChild(root);
                var materials = new List<(StandardMaterial3D, Color)>();
                var kit = new Kit(root, materials, new Rng(SceneryPlan.Hash(new GridPosition(x0, y0), 3)));
                var rise = span * 0.55f;
                kit.Add(new PrismMesh { Size = new Vector3(span + 0.4f, rise, length + 0.3f) }, new Vector3(0, rise / 2, 0), kit.Paint(Thatch, 0.04f), new Vector3(0, 90, 0));
                kit.Add(new BoxMesh { Size = new Vector3(length + 0.34f, 0.1f, 0.14f) }, new Vector3(0, rise, 0), kit.Paint(Thatch.Darkened(0.25f), 0.03f));
                props.Add(new PropView(root, [.. block], materials));
            }
        }

        private static void BuildRockFace(Kit k)
        {
            var stone = k.Paint(new Color(0.45f, 0.43f, 0.4f), 0.06f);
            k.Add(Lumpy(0.62f, new Vector3(0.95f, 1.5f, 0.95f), k.Seed(), 0.28f), new Vector3(0, 0.8f, 0), stone, new Vector3(0, k.R(0, 360), 0));
            k.Add(Lumpy(0.4f, new Vector3(1, 0.8f, 1), k.Seed(), 0.28f), new Vector3(k.R(-0.2f, 0.2f), 0.25f, k.R(-0.2f, 0.2f)), stone, new Vector3(0, k.R(0, 360), 0));
        }

        // ---- Low ----

        private static void BuildBush(Kit k)
        {
            var leaf = k.Paint(Leaf.Lightened(0.05f), 0.12f);
            var deep = k.Paint(Leaf.Darkened(0.15f), 0.08f);
            var count = 4;
            for (var i = 0; i < count; i++)
            {
                var angle = Mathf.Tau * i / count + k.R(0, 1);
                var r = k.R(0.24f, 0.3f);
                k.Add(Lumpy(r, new Vector3(1, 0.85f, 1), k.Seed()), new Vector3(Mathf.Cos(angle) * 0.2f, r * 0.8f, Mathf.Sin(angle) * 0.2f), i % 2 == 0 ? leaf : deep, new Vector3(0, k.R(0, 360), 0));
            }

            k.Add(Lumpy(0.28f, new Vector3(1, 0.9f, 1), k.Seed()), new Vector3(0, 0.36f, 0), leaf, new Vector3(0, k.R(0, 360), 0));
            if (k.R(0, 1) < 0.4f)
            {
                var berry = k.Paint(k.R(0, 1) < 0.5f ? new Color(0.75f, 0.2f, 0.2f) : new Color(0.92f, 0.88f, 0.75f), 0.05f);
                for (var i = 0; i < 6; i++)
                {
                    var angle = k.R(0, Mathf.Tau);
                    k.Add(new SphereMesh { Radius = 0.03f, Height = 0.06f, RadialSegments = 6, Rings = 3 }, new Vector3(Mathf.Cos(angle) * 0.36f, k.R(0.25f, 0.5f), Mathf.Sin(angle) * 0.36f), berry);
                }
            }
        }

        private void BuildHedge(Kit k, GridPosition square)
        {
            var leaf = k.Paint(Leaf.Darkened(0.05f), 0.08f);
            k.Add(new BoxMesh { Size = new Vector3(0.7f, 0.42f, 0.7f) }, new Vector3(0, 0.24f, 0), leaf);
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1), (0, 0) })
            {
                if ((dx, dy) != (0, 0) && plan!.PropAt(new GridPosition(square.X + dx, square.Y + dy)) != Prop.Hedge)
                {
                    continue;
                }

                for (var i = 0; i < 2; i++)
                {
                    var r = k.R(0.22f, 0.28f);
                    k.Add(Lumpy(r, new Vector3(1, 0.9f, 1), k.Seed()), new Vector3(dx * k.R(0.22f, 0.4f), 0.32f + k.R(0, 0.12f), dy * k.R(0.22f, 0.4f)), leaf, new Vector3(0, k.R(0, 360), 0));
                }
            }
        }

        private static void BuildRock(Kit k)
        {
            var stone = k.Paint(Granite, 0.08f);
            k.Add(Lumpy(0.4f, new Vector3(1.1f, 0.75f, 0.9f), k.Seed(), 0.25f), new Vector3(0, 0.26f, 0), stone, new Vector3(0, k.R(0, 360), k.R(-8, 8)));
            k.Add(Lumpy(0.2f, new Vector3(1, 0.7f, 1), k.Seed(), 0.25f), new Vector3(k.R(-0.35f, 0.35f), 0.08f, k.R(0.2f, 0.38f)), stone, new Vector3(0, k.R(0, 360), 0));
        }

        private static void BuildCrate(Kit k)
        {
            var turn = k.R(-20, 20);
            Crate(k, new Vector3(-0.1f, 0, 0), 0.55f, turn);
            if (k.R(0, 1) < 0.6f)
            {
                Crate(k, new Vector3(0.28f, 0, 0.25f), 0.36f, turn + k.R(-25, 25));
            }
        }

        private static void BuildBarrels(Kit k)
        {
            Barrel(k, new Vector3(-0.15f, 0, -0.1f));
            if (k.R(0, 1) < 0.7f)
            {
                Barrel(k, new Vector3(0.22f, 0, 0.2f));
            }
        }

        private static void Barrel(Kit k, Vector3 at)
        {
            var wood = k.Paint(Timber.Darkened(0.1f), 0.1f);
            var band = k.Paint(Iron, 0.03f, 0.5f);
            k.Add(Cylinder(0.22f, 0.22f, 0.62f, 10), at + new Vector3(0, 0.31f, 0), wood);
            k.Add(Cylinder(0.25f, 0.25f, 0.24f, 10), at + new Vector3(0, 0.31f, 0), wood);
            foreach (var y in new[] { 0.1f, 0.52f })
            {
                k.Add(Cylinder(0.235f, 0.235f, 0.04f, 10), at + new Vector3(0, y, 0), band);
            }
        }

        /// <summary>A cart over this square and the next cart square along, if there is one; returns the squares it took besides this one.</summary>
        private IEnumerable<GridPosition> BuildCart(Kit k, GridPosition square, HashSet<GridPosition> done)
        {
            var east = new GridPosition(square.X + 1, square.Y);
            var south = new GridPosition(square.X, square.Y + 1);
            GridPosition? other = plan!.PropAt(east) == Prop.Cart && !done.Contains(east) ? east
                : plan.PropAt(south) == Prop.Cart && !done.Contains(south) ? south : null;
            var length = other is null ? 0.95f : 1.85f;
            var alongX = other is null || other.Value == east;
            var holder = new Node3D
            {
                Position = other is null ? Vector3.Zero : alongX ? new Vector3(0.5f, 0, 0) : new Vector3(0, 0, 0.5f),
                RotationDegrees = new Vector3(0, alongX ? 0 : 90, 0),
            };
            k.Root.AddChild(holder);
            var wood = k.Paint(Timber, 0.08f);
            var dark = k.Paint(DarkTimber, 0.05f);
            k.Add(new BoxMesh { Size = new Vector3(length, 0.07f, 0.8f) }, new Vector3(0, 0.36f, 0), wood, parent: holder);
            foreach (var z in new[] { -0.38f, 0.38f })
            {
                k.Add(new BoxMesh { Size = new Vector3(length, 0.22f, 0.05f) }, new Vector3(0, 0.5f, z), wood, parent: holder);
            }

            k.Add(new BoxMesh { Size = new Vector3(0.05f, 0.22f, 0.8f) }, new Vector3(-length / 2, 0.5f, 0), wood, parent: holder);
            k.Add(new BoxMesh { Size = new Vector3(0.05f, 0.22f, 0.8f) }, new Vector3(length / 2, 0.5f, 0), wood, parent: holder);
            foreach (var z in new[] { -0.45f, 0.45f })
            {
                k.Add(Cylinder(0.3f, 0.3f, 0.06f, 12), new Vector3(other is null ? 0 : -length * 0.18f, 0.3f, z), dark, new Vector3(90, 0, 0), parent: holder);
                k.Add(Cylinder(0.06f, 0.06f, 0.08f, 8), new Vector3(other is null ? 0 : -length * 0.18f, 0.3f, z * 1.06f), k.Paint(Iron, 0.02f, 0.5f), new Vector3(90, 0, 0), parent: holder);
            }

            // Its load: a sack or two.
            var sack = k.Paint(new Color(0.7f, 0.62f, 0.45f), 0.06f);
            for (var i = 0; i < (other is null ? 1 : 3); i++)
            {
                k.Add(Lumpy(0.2f, new Vector3(1.2f, 0.75f, 1), k.Seed(), 0.12f), new Vector3(-length / 2 + 0.35f + i * 0.5f, 0.52f, k.R(-0.15f, 0.15f)), sack, new Vector3(0, k.R(0, 360), 0), parent: holder);
            }

            return other is { } o ? [o] : [];
        }

        private void BuildFence(Kit k, GridPosition square)
        {
            var wood = k.Paint(Timber.Darkened(0.15f), 0.08f);
            k.Add(new BoxMesh { Size = new Vector3(0.09f, 0.7f, 0.09f) }, new Vector3(0, 0.35f, 0), wood);
            var any = false;
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                if (plan!.PropAt(new GridPosition(square.X + dx, square.Y + dy)) != Prop.Fence)
                {
                    continue;
                }

                any = true;
                Rails(k, wood, dx, dy);
            }

            if (!any)
            {
                Rails(k, wood, 1, 0);
                Rails(k, wood, -1, 0);
            }
        }

        private static void Rails(Kit k, StandardMaterial3D wood, int dx, int dy)
        {
            foreach (var y in new[] { 0.25f, 0.55f })
            {
                k.Add(new BoxMesh { Size = dx != 0 ? new Vector3(0.5f, 0.07f, 0.04f) : new Vector3(0.04f, 0.07f, 0.5f) }, new Vector3(dx * 0.25f, y, dy * 0.25f), wood);
            }
        }

        private void BuildLog(Kit k, GridPosition square)
        {
            var ns = plan!.PropAt(new GridPosition(square.X, square.Y - 1)) == Prop.Log || plan.PropAt(new GridPosition(square.X, square.Y + 1)) == Prop.Log;
            var turn = ns ? 90 : 0;
            k.Add(Cylinder(0.24f, 0.26f, 1.0f, 9), new Vector3(0, 0.25f, 0), k.Paint(Bark, 0.08f), new Vector3(0, turn, 90));
            k.Add(Lumpy(0.14f, new Vector3(1, 0.5f, 1), k.Seed()), new Vector3(k.R(-0.2f, 0.2f), 0.48f, 0), k.Paint(new Color(0.3f, 0.42f, 0.2f), 0.05f), new Vector3(0, k.R(0, 360), 0));
        }

        private static void BuildStump(Kit k)
        {
            k.Add(Cylinder(0.27f, 0.32f, 0.5f, 9), new Vector3(0, 0.25f, 0), k.Paint(Bark, 0.08f));
            k.Add(Cylinder(0.25f, 0.25f, 0.02f, 9), new Vector3(0, 0.505f, 0), k.Paint(new Color(0.7f, 0.58f, 0.4f), 0.05f));
            k.Add(Cylinder(0.12f, 0.14f, 0.7f, 7), new Vector3(0.25f, 0.13f, 0.2f), k.Paint(Bark, 0.08f), new Vector3(0, k.R(0, 360), 85));
        }

        private static void BuildReedClump(Kit k)
        {
            var reed = k.Paint(new Color(0.5f, 0.55f, 0.28f), 0.1f);
            var head = k.Paint(new Color(0.35f, 0.24f, 0.14f), 0.05f);
            for (var i = 0; i < 16; i++)
            {
                var at = new Vector3(k.R(-0.4f, 0.4f), 0, k.R(-0.4f, 0.4f));
                var h = k.R(0.5f, 0.75f);
                k.Add(new BoxMesh { Size = new Vector3(0.025f, h, 0.025f) }, at + new Vector3(0, h / 2, 0), reed, new Vector3(k.R(-8, 8), 0, k.R(-8, 8)));
                if (i % 3 == 0)
                {
                    k.Add(Cylinder(0.03f, 0.03f, 0.12f, 6), at + new Vector3(0, h, 0), head);
                }
            }
        }
    }
}
