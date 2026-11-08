using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using WoundBound.Game.Combat;
using WoundBound.Game.Figures;
using WoundBound.Rules.Enums;

namespace WoundBoundGame
{
    /// <summary>
    /// The place itself (the user's request, 2026-11-08): ground that is grass or road or mud rather
    /// than a checkerboard, water that moves, high ground standing up off the field, and on every
    /// solid square the thing it is — a tree, a bush, a crate stack, a cart, a wall, a hut — with
    /// grass, flowers and pebbles scattered about between them.
    ///
    /// <para>What the square does still decides how it looks: <see cref="SceneryPlan"/> keeps every
    /// prop as tall as its cover, and the scatter stays ankle-high, so nothing that looks like it
    /// would hide you fails to, and nothing decorative looks like cover.</para>
    ///
    /// <para>The fog of war is a texture, one texel a square, that the ground, the water and the
    /// scatter read in their shaders; props hide or darken square by square.</para>
    /// </summary>
    public partial class Board3D
    {
        /// <summary>How far past the map's edges the ground is drawn — the map is a window on ground that goes on.</summary>
        private const int Margin = 7;

        private const float RaisedHeight = 0.42f;
        private const float WaterLine = -0.07f;
        private const float ShallowBed = -0.17f;
        private const float DeepBed = -0.34f;

        private SceneryPlan? plan;
        private Setting? builtSetting;
        private Node3D? propLayer;
        private readonly List<PropView> props = new();
        private readonly List<ShaderMaterial> fogged = new();
        private ImageTexture? fogTexture;
        private readonly Dictionary<GridPosition, (Node3D Flames, OmniLight3D Light)> fires = new();
        private DirectionalLight3D sun = null!;
        private WorldEnvironment world = null!;

        /// <summary>A prop as drawn: its node, the squares it stands on, and its materials' own colours, for darkening in the fog.</summary>
        private sealed record PropView(Node3D Root, GridPosition[] Squares, List<(StandardMaterial3D Material, Color Colour)> Materials)
        {
            /// <summary>The materials that fade when a figure is behind the prop; empty for props that never fade.</summary>
            public List<StandardMaterial3D> Canopy { get; init; } = new();

            public Vector3 CanopyCentre { get; init; }

            public float CanopyRadius { get; init; }

            /// <summary>How opaque the canopy is drawn now.</summary>
            public float Opacity { get; set; } = 1;
        }

        /// <summary>How opaque a canopy is drawn with somebody behind it.</summary>
        private const float FadedOpacity = 0.28f;

        /// <summary>The height a creature stands at on <paramref name="square"/>: up on high ground, wading in water.</summary>
        private float FootHeight(GridPosition square)
        {
            if (encounter is not { } fight)
            {
                return 0;
            }

            var setting = fight.Setting;
            if (setting.Raised.Contains(square))
            {
                return RaisedHeight;
            }

            return setting.Water.TryGetValue(square, out var depth) ? (depth == WaterDepth.Deep ? DeepBed + 0.05f : ShallowBed + 0.04f) : 0;
        }

        /// <summary>The height of what is seen on <paramref name="square"/>: the top of high ground, the surface of water.</summary>
        public float SurfaceHeight(GridPosition square)
        {
            if (encounter is not { } fight)
            {
                return 0;
            }

            return fight.Setting.Raised.Contains(square) ? RaisedHeight : fight.Setting.Water.ContainsKey(square) ? WaterLine : 0;
        }

        /// <summary>
        /// The height of the ground at a point on <paramref name="square"/>'s mesh. Flat across most
        /// squares; but high ground that isn't a cliff (Scenery.Cliffs) banks down at its edges to
        /// meet the lower ground beside it, so it slopes. The slope stays inside the raised square,
        /// whose middle stands at full height: what looks like high ground is high ground.
        /// </summary>
        private float VertexHeight(Setting setting, GridPosition square, float vx, float vy)
        {
            var top = GroundTop(setting, square);
            if (!setting.Raised.Contains(square) || setting.Scenery.Cliffs.Contains(square))
            {
                return top;
            }

            var onEdge = vx <= square.X + 0.001f || vx >= square.X + 0.999f || vy <= square.Y + 0.001f || vy >= square.Y + 0.999f;
            if (!onEdge)
            {
                return top;
            }

            // As low as the lowest ground meeting at this point, cliffs aside: a cliff beside it stands up whole.
            var low = top;
            foreach (var (dx, dy) in new[] { (-0.25f, -0.25f), (0.25f, -0.25f), (-0.25f, 0.25f), (0.25f, 0.25f) })
            {
                var other = new GridPosition(Mathf.FloorToInt(vx + dx), Mathf.FloorToInt(vy + dy));
                if (other != square && !(setting.Raised.Contains(other) && setting.Scenery.Cliffs.Contains(other)))
                {
                    low = Math.Min(low, GroundTop(setting, other));
                }
            }

            return Math.Max(low, 0);
        }

        private float GroundTop(Setting setting, GridPosition square) =>
            setting.Raised.Contains(square) ? RaisedHeight
            : setting.Water.TryGetValue(square, out var depth) ? (depth == WaterDepth.Deep ? DeepBed : ShallowBed)
            : 0;

        // ---- Building ----

        private void BuildScenery(Encounter fight)
        {
            fogged.Clear();
            fires.Clear();
            plan = new SceneryPlan(fight.Setting);
            builtSetting = fight.Setting;
            fogTexture = null;
            Light(fight.Setting);
            BuildGroundMesh(fight);
            BuildWater(fight);
            BuildProps(fight, previous: null);
            BuildScatter(fight);
            BuildFires(fight);
            BuildMist(fight);
            ApplyFogTexture(fight, null);
        }

        /// <summary>
        /// The light the scene is seen by: the sun by day; at dusk a low warm one; at night a dim blue
        /// moon, so the fires and the fog do the rest.
        /// </summary>
        private void Light(Setting setting)
        {
            var environment = world.Environment;
            if (setting.IsUnlit && setting.Visibility == Visibility.None)
            {
                sun.LightColor = new Color(0.62f, 0.7f, 1f);
                sun.LightEnergy = 0.55f;
                environment.AmbientLightColor = new Color(0.32f, 0.36f, 0.5f);
                environment.AmbientLightEnergy = 0.75f;
            }
            else if (setting.IsUnlit)
            {
                sun.LightColor = new Color(1f, 0.78f, 0.6f);
                sun.LightEnergy = 0.8f;
                environment.AmbientLightColor = new Color(0.5f, 0.45f, 0.5f);
                environment.AmbientLightEnergy = 0.8f;
            }
            else
            {
                sun.LightColor = new Color(1f, 0.97f, 0.9f);
                sun.LightEnergy = 1.15f;
                environment.AmbientLightColor = new Color(0.55f, 0.58f, 0.62f);
                environment.AmbientLightEnergy = 0.85f;
            }
        }

        internal static readonly Dictionary<Surface, Color> SurfaceColours = new()
        {
            [Surface.Grass] = new(0.34f, 0.47f, 0.22f),
            [Surface.LongGrass] = new(0.42f, 0.48f, 0.24f),
            [Surface.Moss] = new(0.25f, 0.37f, 0.2f),
            [Surface.Leaves] = new(0.42f, 0.32f, 0.18f),
            [Surface.Dirt] = new(0.43f, 0.34f, 0.23f),
            [Surface.Road] = new(0.52f, 0.43f, 0.31f),
            [Surface.Gravel] = new(0.49f, 0.47f, 0.43f),
            [Surface.Mud] = new(0.29f, 0.24f, 0.17f),
            [Surface.Sand] = new(0.66f, 0.6f, 0.43f),
            [Surface.Flagstone] = new(0.53f, 0.52f, 0.5f),
            [Surface.Stone] = new(0.46f, 0.45f, 0.43f),
        };

        private Color GroundColour(Setting setting, GridPosition square)
        {
            var colour = SurfaceColours[plan!.SurfaceAt(square)];
            if (setting.HazardAt(square) is not null)
            {
                colour = colour.Lerp(new Color(0.85f, 0.65f, 0.2f), 0.18f);
            }

            return colour;
        }

        /// <summary>A little brightness to and fro, the same for the same point every time, so ground isn't one flat colour.</summary>
        private static float Mottle(float x, float y, uint salt = 0)
        {
            var h = SceneryPlan.Hash(new GridPosition(Mathf.RoundToInt(x * 2), Mathf.RoundToInt(y * 2)), salt);
            return (h & 0xffff) / 65535f - 0.5f;
        }

        /// <summary>
        /// The ground as one mesh: every square split in four, its corners coloured by the squares that
        /// meet there at the same height, so grass shades into road and road into mud; high ground
        /// standing up on rock sides; banks dropping down under the water.
        /// </summary>
        private void BuildGroundMesh(Encounter fight)
        {
            var setting = fight.Setting;
            var surface = new SurfaceTool();
            surface.Begin(Mesh.PrimitiveType.Triangles);
            int x0 = -Margin, y0 = -Margin, x1 = fight.Width + Margin, y1 = fight.Height + Margin;

            Color CornerColour(float cx, float cy, float top)
            {
                // The squares touching this point at this height, blended.
                var sum = new Color(0, 0, 0);
                var n = 0;
                for (var dx = -1; dx <= 0; dx++)
                {
                    for (var dy = -1; dy <= 0; dy++)
                    {
                        var sq = new GridPosition(Mathf.FloorToInt(cx + dx * 0.5f + 0.25f), Mathf.FloorToInt(cy + dy * 0.5f + 0.25f));
                        if (Math.Abs(GroundTop(setting, sq) - top) > 0.01f)
                        {
                            continue;
                        }

                        var c = GroundColour(setting, sq);
                        sum = new Color(sum.R + c.R, sum.G + c.G, sum.B + c.B);
                        n++;
                    }
                }

                var colour = n == 0 ? new Color(0.4f, 0.4f, 0.4f) : new Color(sum.R / n, sum.G / n, sum.B / n);
                var m = Mottle(cx, cy);
                return colour.Lightened(Math.Max(0, m) * 0.08f).Darkened(Math.Max(0, -m) * 0.1f);
            }

            for (var y = y0; y < y1; y++)
            {
                for (var x = x0; x < x1; x++)
                {
                    var square = new GridPosition(x, y);
                    var top = GroundTop(setting, square);
                    Vector3 At(float vx, float vy) => new(vx, VertexHeight(setting, square, vx, vy), vy);
                    for (var i = 0; i < 2; i++)
                    {
                        for (var j = 0; j < 2; j++)
                        {
                            float ax = x + i * 0.5f, ay = y + j * 0.5f, bx = ax + 0.5f, by = ay + 0.5f;
                            Vector3 a = At(ax, ay), b = At(bx, ay), c = At(bx, by), d = At(ax, by);
                            Color ca = CornerColour(ax, ay, top), cb = CornerColour(bx, ay, top), cc = CornerColour(bx, by, top), cd = CornerColour(ax, by, top);
                            // Split along the diagonal that keeps a bank's slope smooth.
                            if (Math.Abs(a.Y - c.Y) <= Math.Abs(b.Y - d.Y))
                            {
                                Tri(surface, a, ca, b, cb, c, cc);
                                Tri(surface, a, ca, c, cc, d, cd);
                            }
                            else
                            {
                                Tri(surface, a, ca, b, cb, d, cd);
                                Tri(surface, b, cb, c, cc, d, cd);
                            }
                        }
                    }

                    // A sheer side wherever this square's edge stands above its neighbour's: a cliff
                    // under high ground, a bank down into water. A sloped edge meets its neighbour and needs none.
                    var rock = setting.Raised.Contains(square);
                    var side = rock ? new Color(0.4f, 0.38f, 0.36f) : new Color(0.33f, 0.27f, 0.2f);
                    var shade = side.Darkened(0.25f);
                    foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    {
                        var next = new GridPosition(x + dx, y + dy);
                        // The edge shared with the neighbour, from left to right as seen from it.
                        Vector2 p, q;
                        switch (dx, dy)
                        {
                            case (1, 0): p = new Vector2(x + 1, y + 1); q = new Vector2(x + 1, y); break;
                            case (-1, 0): p = new Vector2(x, y); q = new Vector2(x, y + 1); break;
                            case (0, 1): p = new Vector2(x, y + 1); q = new Vector2(x + 1, y + 1); break;
                            default: p = new Vector2(x + 1, y); q = new Vector2(x, y); break;
                        }

                        var m = (p + q) / 2;
                        foreach (var (u, v) in new[] { (p, m), (m, q) })
                        {
                            float ut = VertexHeight(setting, square, u.X, u.Y), vt = VertexHeight(setting, square, v.X, v.Y);
                            float ub = VertexHeight(setting, next, u.X, u.Y), vb = VertexHeight(setting, next, v.X, v.Y);
                            if (ut <= ub + 0.001f && vt <= vb + 0.001f)
                            {
                                continue;
                            }

                            ub = Math.Min(ub, ut);
                            vb = Math.Min(vb, vt);
                            Vector3 pt = new(u.X, ut, u.Y), qt = new(v.X, vt, v.Y), pb = new(u.X, ub, u.Y), qb = new(v.X, vb, v.Y);
                            Tri(surface, pt, side, qt, side, qb, shade);
                            Tri(surface, pt, side, qb, shade, pb, shade);
                        }
                    }
                }
            }

            surface.GenerateNormals();
            var material = FogShader(GroundShader);
            material.SetShaderParameter("map_size", new Vector2(fight.Width, fight.Height));
            terrain.AddChild(new MeshInstance3D { Mesh = surface.Commit(), MaterialOverride = material });
        }

        private static void Tri(SurfaceTool surface, Vector3 a, Color ca, Vector3 b, Color cb, Vector3 c, Color cc)
        {
            // Godot's front faces wind clockwise seen from the front, as a, b, c do seen from above.
            surface.SetColor(ca);
            surface.AddVertex(a);
            surface.SetColor(cb);
            surface.AddVertex(b);
            surface.SetColor(cc);
            surface.AddVertex(c);
        }

        /// <summary>The water: one sheet over every wet square, deeper blue where it's deep, with foam where it meets the bank.</summary>
        private void BuildWater(Encounter fight)
        {
            var setting = fight.Setting;
            if (setting.Water.Count == 0)
            {
                return;
            }

            float DepthAt(int cx, int cy)
            {
                // A corner is as deep as the squares round it; shallow where any of them is dry.
                var deep = 0;
                var dry = 0;
                for (var dx = -1; dx <= 0; dx++)
                {
                    for (var dy = -1; dy <= 0; dy++)
                    {
                        if (!setting.Water.TryGetValue(new GridPosition(cx + dx, cy + dy), out var depth))
                        {
                            dry++;
                        }
                        else if (depth == WaterDepth.Deep)
                        {
                            deep++;
                        }
                    }
                }

                return dry > 0 ? 0 : deep / 4f;
            }

            float ShoreAt(int cx, int cy)
            {
                for (var dx = -1; dx <= 0; dx++)
                {
                    for (var dy = -1; dy <= 0; dy++)
                    {
                        if (!setting.Water.ContainsKey(new GridPosition(cx + dx, cy + dy)))
                        {
                            return 1;
                        }
                    }
                }

                return 0;
            }

            var surface = new SurfaceTool();
            surface.Begin(Mesh.PrimitiveType.Triangles);
            foreach (var square in setting.Water.Keys)
            {
                if (square.X < -Margin || square.Y < -Margin || square.X >= fight.Width + Margin || square.Y >= fight.Height + Margin)
                {
                    continue;
                }

                int x = square.X, y = square.Y;
                Vector3 a = new(x, WaterLine, y), b = new(x + 1, WaterLine, y), c = new(x + 1, WaterLine, y + 1), d = new(x, WaterLine, y + 1);
                Color Corner(int cx, int cy) => new(DepthAt(cx, cy), ShoreAt(cx, cy), 0);
                Tri(surface, a, Corner(x, y), b, Corner(x + 1, y), c, Corner(x + 1, y + 1));
                Tri(surface, a, Corner(x, y), c, Corner(x + 1, y + 1), d, Corner(x, y + 1));
            }

            surface.GenerateNormals();
            var material = FogShader(WaterShader);
            terrain.AddChild(new MeshInstance3D { Mesh = surface.Commit(), MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        }

        /// <summary>Lays a campfire on every built fire, lit while it burns.</summary>
        private void BuildFires(Encounter fight)
        {
            foreach (var square in fight.Setting.Fires)
            {
                var root = new Node3D { Position = new Vector3(square.X + 0.5f, FootHeight(square), square.Y + 0.5f) };
                terrain.AddChild(root);
                var stone = new StandardMaterial3D { AlbedoColor = new Color(0.42f, 0.4f, 0.38f), Roughness = 1 };
                for (var k = 0; k < 9; k++)
                {
                    var angle = Mathf.Tau * k / 9;
                    root.AddChild(new MeshInstance3D
                    {
                        Mesh = Lumpy(0.07f, new Vector3(1.2f, 0.7f, 1), (uint)k + 11),
                        MaterialOverride = stone,
                        Position = new Vector3(Mathf.Cos(angle) * 0.3f, 0.04f, Mathf.Sin(angle) * 0.3f),
                    });
                }

                var wood = new StandardMaterial3D { AlbedoColor = new Color(0.3f, 0.2f, 0.12f), Roughness = 1 };
                for (var k = 0; k < 3; k++)
                {
                    root.AddChild(new MeshInstance3D
                    {
                        Mesh = new CylinderMesh { TopRadius = 0.035f, BottomRadius = 0.035f, Height = 0.45f, RadialSegments = 6 },
                        MaterialOverride = wood,
                        Position = new Vector3(0, 0.07f, 0),
                        RotationDegrees = new Vector3(70, k * 60, 0),
                    });
                }

                var flames = new Node3D();
                root.AddChild(flames);
                foreach (var (size, colour, at) in new[]
                {
                    (0.16f, new Color(1f, 0.45f, 0.1f), new Vector3(0, 0, 0)),
                    (0.1f, new Color(1f, 0.75f, 0.25f), new Vector3(0.04f, 0, -0.03f)),
                    (0.07f, new Color(1f, 0.92f, 0.6f), new Vector3(-0.03f, 0, 0.02f)),
                })
                {
                    flames.AddChild(new MeshInstance3D
                    {
                        Mesh = new CylinderMesh { TopRadius = 0, BottomRadius = size, Height = size * 3.2f, RadialSegments = 7 },
                        MaterialOverride = new StandardMaterial3D
                        {
                            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                            AlbedoColor = colour with { A = 0.9f },
                            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                        },
                        Position = at + Vector3.Up * (0.06f + size * 1.6f),
                        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                    });
                }

                var light = new OmniLight3D { LightColor = new Color(1f, 0.6f, 0.28f), LightEnergy = 2.2f, OmniRange = 7, Position = Vector3.Up * 0.6f, ShadowEnabled = true };
                root.AddChild(light);
                fires[square] = (flames, light);
            }
        }

        private double fireClock;

        /// <summary>Flickers the fires that burn and puts out the ones spent; rebuilds the props if a piece of cover has broken.</summary>
        private void AnimateScenery(Encounter fight, double delta)
        {
            if (!ReferenceEquals(fight.Setting, builtSetting))
            {
                var before = plan;
                builtSetting = fight.Setting;
                BuildProps(fight, before);
                ApplyFog(fog);
            }

            FadeCanopies(delta);
            if (fires.Count == 0)
            {
                return;
            }

            fireClock += delta;
            var burning = fight.Fires.ToHashSet();
            foreach (var (square, (flames, light)) in fires)
            {
                var lit = burning.Contains(square);
                flames.Visible = lit;
                light.Visible = lit;
                if (!lit)
                {
                    continue;
                }

                var t = (float)fireClock + square.X * 1.7f;
                var flicker = 0.85f + 0.1f * Mathf.Sin(t * 9.1f) + 0.06f * Mathf.Sin(t * 23.3f + 1.3f);
                flames.Scale = new Vector3(1, flicker, 1);
                flames.RotationDegrees = new Vector3(0, t * 40, 0);
                light.LightEnergy = 2.2f * flicker;
            }
        }

        /// <summary>
        /// Fades a tree's leaves, or a roof, while a figure stands behind it as the camera sees it —
        /// further from the camera and within the canopy's outline on the screen — so nobody is lost
        /// behind the scenery. Whichever way the camera turns, the test follows it.
        /// </summary>
        private void FadeCanopies(double delta)
        {
            var eye = camera.GlobalPosition;
            var forward = -camera.GlobalTransform.Basis.Z;
            var right = camera.GlobalTransform.Basis.X;
            var shown = figures.Values.Where(f => f.Visible).Select(f => f.GlobalPosition + Vector3.Up * 0.6f).ToList();
            var ease = 1 - Mathf.Exp(-10 * (float)delta);
            foreach (var view in props)
            {
                if (view.Canopy.Count == 0 || !view.Root.Visible)
                {
                    continue;
                }

                var centre = view.Root.GlobalPosition + view.CanopyCentre;
                var onScreen = camera.UnprojectPosition(centre);
                var reach = onScreen.DistanceTo(camera.UnprojectPosition(centre + right * view.CanopyRadius));
                var depth = (centre - eye).Dot(forward);
                var behind = shown.Any(at => (at - eye).Dot(forward) > depth - 0.3f && camera.UnprojectPosition(at).DistanceTo(onScreen) < reach * 1.15f);
                var target = behind ? FadedOpacity : 1f;
                if (view.Opacity == target)
                {
                    continue;
                }

                view.Opacity = Mathf.Abs(view.Opacity - target) < 0.01f ? target : Mathf.Lerp(view.Opacity, target, ease);
                foreach (var material in view.Canopy)
                {
                    material.Transparency = view.Opacity < 0.999f ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled;
                    material.AlbedoColor = material.AlbedoColor with { A = view.Opacity };
                }
            }
        }

        /// <summary>
        /// Lays a battlemap picture on the ground where it covers the board (null takes it away): the
        /// ground shows the art, water becomes a sheen over it, and grass and pebbles aren't strewn on it.
        /// Props still stand on their squares, so what hides you is still there to see.
        /// </summary>
        public void SetBackdrop(Backdrop? backdrop, Texture2D? picture)
        {
            var on = backdrop is not null && picture is not null && encounter is not null;
            foreach (var material in fogged)
            {
                material.SetShaderParameter("backdrop_on", on ? 1f : 0f);
                if (!on)
                {
                    continue;
                }

                material.SetShaderParameter("backdrop_tex", picture!);
                material.SetShaderParameter("backdrop_origin", new Vector2(backdrop!.OriginX, backdrop.OriginY));
                material.SetShaderParameter("backdrop_pps", backdrop.PixelsPerSquare);
                material.SetShaderParameter("backdrop_size", picture!.GetSize());
                material.SetShaderParameter("board_size", new Vector2(encounter!.Width, encounter.Height));
            }
        }

        // ---- The fog ----

        /// <summary>How brightly a square is drawn at each Visibility: Good in full, Poor dimmed, None dark.</summary>
        private static float Brightness(Visibility visibility) => visibility switch { Visibility.Good => 1f, Visibility.Poor => 0.62f, _ => 0.36f };

        /// <summary>
        /// Writes what the party sees, and how well lit each square is, into the texture the ground,
        /// water and scatter read (red: seen now, remembered, never seen; green: the light), and hides,
        /// darkens or dims props to match.
        /// </summary>
        private void ApplyFogTexture(Encounter fight, FogOfWar? sight)
        {
            var light = new float[fight.Width, fight.Height];
            for (var y = 0; y < fight.Height; y++)
            {
                for (var x = 0; x < fight.Width; x++)
                {
                    light[x, y] = Brightness(fight.VisibilityAtSquare(new GridPosition(x, y)));
                }
            }

            float LightAt(GridPosition sq) => light[Math.Clamp(sq.X, 0, fight.Width - 1), Math.Clamp(sq.Y, 0, fight.Height - 1)];

            foreach (var view in props)
            {
                var state = sight is null ? FogState.Seen : view.Squares.Select(sight.StateOf).Max();
                view.Root.Visible = state != FogState.Unseen;
                var lit = state == FogState.Remembered ? 0.45f : view.Squares.Max(LightAt);
                foreach (var (material, colour) in view.Materials)
                {
                    var shade = lit >= 0.999f ? colour : colour.Darkened(1 - lit);
                    material.AlbedoColor = shade with { A = material.AlbedoColor.A };
                }
            }

            foreach (var (square, (flames, _)) in fires)
            {
                var state = sight?.StateOf(square) ?? FogState.Seen;
                flames.GetParent<Node3D>().Visible = state != FogState.Unseen;
            }

            var image = Image.CreateEmpty(fight.Width, fight.Height, false, Image.Format.Rg8);
            for (var y = 0; y < fight.Height; y++)
            {
                for (var x = 0; x < fight.Width; x++)
                {
                    var state = sight?.StateOf(new GridPosition(x, y)) ?? FogState.Seen;
                    var v = state switch { FogState.Seen => 1f, FogState.Remembered => 0.5f, _ => 0f };
                    image.SetPixel(x, y, new Color(v, light[x, y], 0));
                }
            }

            if (fogTexture is null || fogTexture.GetWidth() != fight.Width || fogTexture.GetHeight() != fight.Height)
            {
                fogTexture = ImageTexture.CreateFromImage(image);
            }
            else
            {
                fogTexture.Update(image);
            }

            foreach (var material in fogged)
            {
                material.SetShaderParameter("fog_map", fogTexture);
                material.SetShaderParameter("fog_size", new Vector2(fight.Width, fight.Height));
                material.SetShaderParameter("fog_on", sight is null ? 0f : 1f);
            }
        }

        private ShaderMaterial FogShader(Shader shader)
        {
            var material = new ShaderMaterial { Shader = shader };
            material.SetShaderParameter("void_colour", Void);
            if (fogTexture is not null)
            {
                material.SetShaderParameter("fog_map", fogTexture);
            }

            fogged.Add(material);
            return material;
        }

        /// <summary>The fog as every scenery shader reads it: 1 seen now, ½ remembered, 0 never seen, soft at the edges.</summary>
        private const string FogCode = @"
uniform sampler2D fog_map : filter_linear, repeat_disable;
uniform vec2 fog_size = vec2(1.0);
uniform float fog_on = 0.0;
uniform vec3 void_colour = vec3(0.09, 0.09, 0.11);
float fog_at(vec2 xz) { return fog_on > 0.5 ? texture(fog_map, xz / fog_size).r : 1.0; }
float light_at(vec2 xz) { return texture(fog_map, xz / fog_size).g; }
uniform sampler2D backdrop_tex : source_color, filter_linear, repeat_disable;
uniform float backdrop_on = 0.0;
uniform vec2 backdrop_origin = vec2(0.0);
uniform float backdrop_pps = 1.0;
uniform vec2 backdrop_size = vec2(1.0);
uniform vec2 board_size = vec2(1.0);
// The battlemap picture's colour at a point on the board, alpha 1 where it covers the board and 0 elsewhere.
vec4 backdrop_at(vec2 xz) {
	if (backdrop_on < 0.5 || any(lessThan(xz, vec2(0.0))) || any(greaterThan(xz, board_size))) { return vec4(0.0); }
	vec2 uv = (backdrop_origin + xz * backdrop_pps) / backdrop_size;
	if (any(lessThan(uv, vec2(0.0))) || any(greaterThan(uv, vec2(1.0)))) { return vec4(0.0); }
	return vec4(texture(backdrop_tex, uv).rgb, 1.0);
}
";

        private static readonly Shader GroundShader = new()
        {
            Code = @"shader_type spatial;
render_mode cull_back, diffuse_burley;
" + FogCode + @"
uniform vec2 map_size = vec2(1.0);
uniform float grid_strength = 0.13;
varying vec3 world_pos;
float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float noise(vec2 p) {
	vec2 i = floor(p), f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), u.x), mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), u.x), u.y);
}
void vertex() { world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz; }
void fragment() {
	vec3 col = COLOR.rgb;
	vec2 p = world_pos.xz;
	// Grain: broad patches and fine speckle, so no stretch of ground is one flat colour.
	float grain = noise(p * 1.3) * 0.5 + noise(p * 4.1) * 0.3 + noise(p * 13.0) * 0.2;
	col *= 0.86 + grain * 0.28;
	// A battlemap, where there is one, is the ground.
	vec4 picture = backdrop_at(p);
	col = mix(col, picture.rgb, picture.a);
	// Off the map, the ground fades into the dark, and has no grid.
	vec2 outside = max(max(-p, p - map_size), vec2(0.0));
	float away = length(outside);
	bool inside = away <= 0.0;
	if (inside) {
		vec2 f = abs(fract(p) - 0.5);
		float edge = max(f.x, f.y);
		float w = fwidth(edge) * 1.2;
		col *= 1.0 - grid_strength * smoothstep(0.5 - w * 1.5, 0.5, edge);
	}
	float seen = fog_at(p);
	float known = smoothstep(0.0, 0.45, seen);
	float lit = mix(0.42, 1.0, smoothstep(0.5, 1.0, seen));
	float fade = 1.0 - smoothstep(1.0, 7.0, away);
	ALBEDO = col * lit * known * fade * light_at(p);
	EMISSION = void_colour * (1.0 - known * fade);
	ROUGHNESS = 0.95;
	SPECULAR = 0.15;
}
",
        };

        private static readonly Shader WaterShader = new()
        {
            Code = @"shader_type spatial;
render_mode blend_mix, cull_disabled, depth_draw_opaque, diffuse_burley;
" + FogCode + @"
uniform vec3 shallow_colour = vec3(0.22, 0.45, 0.55);
uniform vec3 deep_colour = vec3(0.07, 0.2, 0.36);
varying vec3 world_pos;
float wave(vec2 p, float t) {
	return sin(p.x * 2.1 + t * 1.3) * 0.5 + sin(p.y * 2.7 - t * 1.1) * 0.5 + sin((p.x + p.y) * 4.3 + t * 2.0) * 0.25;
}
void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	VERTEX.y += wave(world_pos.xz, TIME) * 0.008;
}
void fragment() {
	float deep = COLOR.r;
	float shore = COLOR.g;
	vec2 p = world_pos.xz;
	float t = TIME;
	vec3 col = mix(shallow_colour, deep_colour, deep);
	// Small moving glints, and foam along the bank.
	float r = sin(p.x * 7.0 + t * 1.7 + sin(p.y * 3.0 + t)) * sin(p.y * 6.0 - t * 1.3 + sin(p.x * 2.0));
	col += vec3(0.1, 0.12, 0.12) * smoothstep(0.75, 1.0, r);
	float foam = shore * smoothstep(0.35, 0.9, sin(p.x * 9.0 + t * 0.8) * 0.5 + sin(p.y * 8.0 - t * 0.6) * 0.5 + shore * 0.6);
	col = mix(col, vec3(0.85, 0.9, 0.9), foam * 0.45);
	// The slope of the waves, for the light to catch.
	float e = 0.05;
	float h = wave(p, t);
	vec3 n = normalize(vec3(-(wave(p + vec2(e, 0.0), t) - h) / e * 0.06, 1.0, -(wave(p + vec2(0.0, e), t) - h) / e * 0.06));
	NORMAL = normalize((VIEW_MATRIX * vec4(n, 0.0)).xyz);
	float seen = fog_at(p);
	float known = smoothstep(0.0, 0.45, seen);
	float lit = mix(0.42, 1.0, smoothstep(0.5, 1.0, seen));
	ALBEDO = col * lit * known * light_at(p);
	EMISSION = void_colour * (1.0 - known);
	// Over a battlemap the picture shows its own water: this is only a sheen on it.
	float painted = backdrop_at(p).a;
	ALPHA = mix(mix(mix(0.62, 0.9, deep), 0.3, painted), 1.0, 1.0 - known);
	ROUGHNESS = 0.12;
	SPECULAR = 0.7;
}
",
        };
    }
}
