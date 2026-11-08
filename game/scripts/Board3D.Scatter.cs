using System;
using System.Collections.Generic;
using Godot;
using WoundBound.Game.Combat;
using WoundBound.Game.Figures;

namespace WoundBoundGame
{
    /// <summary>
    /// What lies about between the props: grass tufts that sway, flowers in the meadow, pebbles on
    /// the road and scree, leaf litter under trees, reeds at the water's edge. All of it ankle-high
    /// at most and drawn as instances in a handful of meshes, so it costs little and never reads as
    /// cover. None of it on a solid square, and none on open water.
    /// </summary>
    public partial class Board3D
    {
        private enum Bit
        {
            Tuft,
            TallTuft,
            Flower,
            Pebble,
            Litter,
            Reed,
        }

        private void BuildScatter(Encounter fight)
        {
            var setting = fight.Setting;
            var placed = new Dictionary<Bit, List<(Transform3D, Color)>>();
            foreach (Bit bit in Enum.GetValues<Bit>())
            {
                placed[bit] = new List<(Transform3D, Color)>();
            }

            for (var y = -Margin; y < fight.Height + Margin; y++)
            {
                for (var x = -Margin; x < fight.Width + Margin; x++)
                {
                    var square = new GridPosition(x, y);
                    if (setting.IsSolid(square))
                    {
                        continue;
                    }

                    var rng = new Rng(SceneryPlan.Hash(square, 101));
                    var top = GroundTop(setting, square);
                    void Put(Bit bit, int count, Func<Color> colour, float minScale = 0.8f, float maxScale = 1.2f)
                    {
                        for (var i = 0; i < count; i++)
                        {
                            var at = new Vector3(x + rng.Range(0.06f, 0.94f), top, y + rng.Range(0.06f, 0.94f));
                            var s = rng.Range(minScale, maxScale);
                            var basis = new Basis(Vector3.Up, rng.Range(0, Mathf.Tau)).Scaled(Vector3.One * s);
                            placed[bit].Add((new Transform3D(basis, at), colour()));
                        }
                    }

                    Color Vary(Color c, float by = 0.12f) => c.Lightened(rng.Range(0, by)).Darkened(rng.Range(0, by));

                    if (setting.Water.TryGetValue(square, out var depth))
                    {
                        // Reeds stand in the shallows near the bank.
                        if (depth == WaterDepth.Shallow && rng.Next() < 0.35f)
                        {
                            Put(Bit.Reed, 3 + (int)(rng.Next() * 4), () => Vary(new Color(0.48f, 0.54f, 0.28f)));
                        }

                        continue;
                    }

                    var rough = plan!.IsRough(square);
                    var grass = new Color(0.34f, 0.5f, 0.22f);
                    switch (plan.SurfaceAt(square))
                    {
                        case Surface.Grass:
                            Put(Bit.Tuft, 5 + (int)(rng.Next() * 4), () => Vary(grass));
                            if (rng.Next() < 0.12f)
                            {
                                var petal = (int)(rng.Next() * 4) switch
                                {
                                    0 => new Color(0.95f, 0.9f, 0.4f),
                                    1 => new Color(0.95f, 0.95f, 0.95f),
                                    2 => new Color(0.65f, 0.5f, 0.85f),
                                    _ => new Color(0.9f, 0.45f, 0.35f),
                                };
                                Put(Bit.Flower, 3 + (int)(rng.Next() * 4), () => Vary(petal, 0.08f));
                            }

                            break;
                        case Surface.LongGrass:
                            Put(Bit.TallTuft, 6 + (int)(rng.Next() * 4), () => Vary(new Color(0.46f, 0.5f, 0.25f)));
                            break;
                        case Surface.Moss:
                            Put(Bit.Tuft, 2, () => Vary(new Color(0.26f, 0.4f, 0.2f)), 0.6f, 0.9f);
                            Put(Bit.Litter, 3, () => Vary(new Color(0.45f, 0.32f, 0.16f), 0.2f));
                            break;
                        case Surface.Leaves:
                            Put(Bit.Litter, 9, () => Vary(rng.Next() < 0.5f ? new Color(0.6f, 0.36f, 0.14f) : new Color(0.5f, 0.42f, 0.18f), 0.2f));
                            Put(Bit.Tuft, 1, () => Vary(new Color(0.3f, 0.42f, 0.2f)));
                            break;
                        case Surface.Dirt:
                            Put(Bit.Pebble, 2, () => Vary(new Color(0.48f, 0.43f, 0.36f)), 0.5f, 1f);
                            if (rng.Next() < 0.4f)
                            {
                                Put(Bit.Tuft, 1, () => Vary(grass), 0.6f, 0.9f);
                            }

                            break;
                        case Surface.Road:
                            Put(Bit.Pebble, rng.Next() < 0.5f ? 1 : 0, () => Vary(new Color(0.55f, 0.5f, 0.42f)), 0.4f, 0.8f);
                            break;
                        case Surface.Gravel:
                            Put(Bit.Pebble, 6, () => Vary(new Color(0.52f, 0.5f, 0.47f)), 0.6f, 1.4f);
                            break;
                        case Surface.Stone:
                            // Bare rock: scree and a few stones the size of a fist or two.
                            Put(Bit.Pebble, 5, () => Vary(new Color(0.5f, 0.48f, 0.45f)), 0.8f, 1.6f);
                            Put(Bit.Pebble, 2, () => Vary(new Color(0.46f, 0.45f, 0.42f)), 2.2f, 3.2f);
                            if (rng.Next() < 0.4f)
                            {
                                Put(Bit.Tuft, 1, () => Vary(new Color(0.36f, 0.44f, 0.22f)), 0.6f, 0.9f);
                            }

                            break;
                        case Surface.Mud:
                            if (rng.Next() < 0.3f)
                            {
                                Put(Bit.Reed, 2, () => Vary(new Color(0.45f, 0.5f, 0.26f)));
                            }

                            break;
                        case Surface.Sand:
                            Put(Bit.Pebble, rng.Next() < 0.4f ? 1 : 0, () => Vary(new Color(0.62f, 0.58f, 0.5f)), 0.5f, 0.9f);
                            break;
                    }

                    // Broken ground is strewn with stones and scrub, whatever it is.
                    if (rough)
                    {
                        Put(Bit.Pebble, 4, () => Vary(new Color(0.5f, 0.48f, 0.44f)), 1.2f, 2.2f);
                        Put(Bit.TallTuft, 2, () => Vary(new Color(0.4f, 0.46f, 0.22f)));
                    }
                }
            }

            foreach (var (bit, list) in placed)
            {
                if (list.Count == 0)
                {
                    continue;
                }

                var multi = new MultiMesh
                {
                    TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                    UseColors = true,
                    Mesh = BitMesh(bit),
                    InstanceCount = list.Count,
                };
                for (var i = 0; i < list.Count; i++)
                {
                    multi.SetInstanceTransform(i, list[i].Item1);
                    multi.SetInstanceColor(i, list[i].Item2);
                }

                var material = FogShader(ScatterShader);
                material.SetShaderParameter("sway", bit is Bit.Tuft or Bit.TallTuft or Bit.Flower or Bit.Reed ? 1f : 0f);
                terrain.AddChild(new MultiMeshInstance3D
                {
                    Multimesh = multi,
                    MaterialOverride = material,
                    CastShadow = bit == Bit.Pebble ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
                });
            }
        }

        /// <summary>
        /// The mesh each bit of scatter is an instance of, in vertex colours that the instance colour
        /// tints: a fan of blades darker at the root, a flower on a stalk, a pebble, a fallen leaf, a reed.
        /// The vertex colour's alpha is how far up the bit a point is, for the sway.
        /// </summary>
        private static Mesh BitMesh(Bit bit)
        {
            var s = new SurfaceTool();
            s.Begin(Mesh.PrimitiveType.Triangles);
            void Blade(float angle, float lean, float height, float width, Color root, Color tip)
            {
                var dir = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                var side = new Vector3(-dir.Z, 0, dir.X) * width;
                var foot = dir * 0.02f;
                var top = dir * lean + Vector3.Up * height;
                // Both faces, so a blade is seen from either side.
                foreach (var flip in new[] { false, true })
                {
                    Vector3 a = foot - side, b = foot + side, c = top;
                    s.SetColor(root with { A = 0 });
                    s.AddVertex(a);
                    s.SetColor(flip ? tip with { A = 1 } : root with { A = 0 });
                    s.AddVertex(flip ? c : b);
                    s.SetColor(flip ? root with { A = 0 } : tip with { A = 1 });
                    s.AddVertex(flip ? b : c);
                }
            }

            switch (bit)
            {
                case Bit.Tuft:
                case Bit.TallTuft:
                {
                    var tall = bit == Bit.TallTuft ? 1.7f : 1f;
                    for (var i = 0; i < 6; i++)
                    {
                        Blade(Mathf.Tau * i / 6 + i * 0.4f, 0.05f + (i % 3) * 0.02f, (0.1f + (i % 3) * 0.03f) * tall, 0.018f, new Color(0.6f, 0.65f, 0.6f), new Color(1.15f, 1.15f, 1f));
                    }

                    break;
                }

                case Bit.Reed:
                    for (var i = 0; i < 4; i++)
                    {
                        Blade(Mathf.Tau * i / 4 + 0.3f, 0.03f, 0.28f + (i % 2) * 0.1f, 0.012f, new Color(0.7f, 0.75f, 0.7f), new Color(1.1f, 1.05f, 0.9f));
                    }

                    break;
                case Bit.Flower:
                {
                    Blade(0, 0.01f, 0.13f, 0.008f, new Color(0.3f, 0.45f, 0.2f, 0), new Color(0.3f, 0.45f, 0.2f, 1));
                    // A small cross of petals at the top, in the instance colour.
                    var head = new Vector3(0.01f, 0.13f, 0);
                    for (var i = 0; i < 5; i++)
                    {
                        var a = Mathf.Tau * i / 5;
                        var p = head + new Vector3(Mathf.Cos(a), 0.2f, Mathf.Sin(a)) * 0.03f;
                        var q = head + new Vector3(Mathf.Cos(a + 0.6f), 0.2f, Mathf.Sin(a + 0.6f)) * 0.03f;
                        foreach (var (u, v) in new[] { (p, q), (q, p) })
                        {
                            s.SetColor(new Color(1.6f, 1.6f, 0.5f, 1));
                            s.AddVertex(head);
                            s.SetColor(new Color(1, 1, 1, 1));
                            s.AddVertex(u);
                            s.SetColor(new Color(1, 1, 1, 1));
                            s.AddVertex(v);
                        }
                    }

                    break;
                }

                case Bit.Pebble:
                {
                    var mesh = Lumpy(0.035f, new Vector3(1.2f, 0.6f, 1), 5, 0.3f);
                    var arrays = mesh.SurfaceGetArrays(0);
                    var verts = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                    var colours = new Color[verts.Length];
                    Array.Fill(colours, new Color(1, 1, 1, 0));
                    arrays[(int)Mesh.ArrayType.Color] = colours;
                    var pebble = new ArrayMesh();
                    pebble.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
                    return pebble;
                }

                case Bit.Litter:
                {
                    // A leaf lying flat, a hair above the ground.
                    Vector3 a = new(-0.045f, 0.012f, 0), b = new(0, 0.012f, 0.03f), c = new(0.045f, 0.012f, 0), d = new(0, 0.012f, -0.03f);
                    foreach (var (p, q, r) in new[] { (a, b, c), (a, c, d) })
                    {
                        s.SetColor(new Color(1, 1, 1, 0));
                        s.AddVertex(p);
                        s.AddVertex(r);
                        s.AddVertex(q);
                    }

                    break;
                }
            }

            s.GenerateNormals();
            return s.Commit();
        }

        private static readonly Shader ScatterShader = new()
        {
            Code = @"shader_type spatial;
render_mode cull_disabled, diffuse_burley;
" + FogCode + @"
uniform float sway = 0.0;
varying vec3 world_pos;
varying vec4 tint;
void vertex() {
	vec3 origin = (MODEL_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
	float up = COLOR.a;
	VERTEX.x += sway * up * up * sin(TIME * 1.6 + origin.x * 1.3 + origin.z * 0.7) * 0.025;
	VERTEX.z += sway * up * up * cos(TIME * 1.3 + origin.z * 1.1) * 0.02;
	world_pos = origin;
	tint = vec4(COLOR.rgb, 1.0); // the instance colour is already multiplied in
}
void fragment() {
	float seen = fog_at(world_pos.xz);
	if (seen < 0.2 || backdrop_at(world_pos.xz).a > 0.5) { discard; }
	float lit = mix(0.42, 1.0, smoothstep(0.5, 1.0, seen));
	ALBEDO = tint.rgb * lit * light_at(world_pos.xz);
	ROUGHNESS = 0.9;
	SPECULAR = 0.1;
	// Thin things let a little light through.
	BACKLIGHT = vec3(0.25) * sway;
}
",
        };
    }
}
