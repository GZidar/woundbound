using Godot;
using WoundBound.Game.Combat;
using WoundBound.Rules.Enums;

namespace WoundBoundGame
{
    /// <summary>
    /// Fog that looks like fog (the user's request, 2026-11-08): a few see-through sheets of slowly
    /// drifting, curling mist laid over the board. They read the same fog texture as the ground, so
    /// mist banks thick over squares the party has never seen and thin over those it only remembers,
    /// and clears where it sees now. Weather that obscures (Visibility Poor or None with an Obscured
    /// cause) mists the whole board, seen or not. Purely a look: what anyone sees is still the rules'.
    /// </summary>
    public partial class Board3D
    {
        /// <summary>How high each sheet floats, lowest first: low enough that the mist stays over the squares it belongs to at the isometric angle.</summary>
        private static readonly float[] MistHeights = [0.1f, 0.32f, 0.58f];

        private readonly System.Collections.Generic.List<ShaderMaterial> mistSheets = new();

        /// <summary>How much weather mist the scene has: none unless its poor sight is obscured, thicker at None.</summary>
        private static float WeatherMist(Setting setting) =>
            setting.Cause == VisibilityCause.Obscured ? setting.Visibility switch { Visibility.Poor => 0.4f, Visibility.None => 0.75f, _ => 0f } : 0f;

        private void BuildMist(Encounter fight)
        {
            mistSheets.Clear();
            var night = fight.Setting.IsUnlit;
            var colour = night ? new Color(0.42f, 0.46f, 0.55f) : new Color(0.72f, 0.74f, 0.76f);
            var size = new Vector2(fight.Width + (2 * Margin), fight.Height + (2 * Margin));
            for (var i = 0; i < MistHeights.Length; i++)
            {
                var material = FogShader(MistShader);
                material.SetShaderParameter("layer", (float)i);
                material.SetShaderParameter("weather", WeatherMist(fight.Setting));
                material.SetShaderParameter("mist_colour", new Vector3(colour.R, colour.G, colour.B));
                material.RenderPriority = i;
                mistSheets.Add(material);
                terrain.AddChild(new MeshInstance3D
                {
                    Mesh = new PlaneMesh { Size = size, SubdivideWidth = 0, SubdivideDepth = 0 },
                    Position = new Vector3(fight.Width / 2f, MistHeights[i], fight.Height / 2f),
                    MaterialOverride = material,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                });
            }
        }

        private static readonly Shader MistShader = new()
        {
            Code = @"shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled, shadows_disabled;
" + FogCode + @"
uniform float layer = 0.0;
uniform float weather = 0.0;
uniform vec3 mist_colour = vec3(0.72, 0.74, 0.76);
varying vec2 world_xz;

void vertex() {
	world_xz = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xz;
}

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float vnoise(vec2 p) {
	vec2 i = floor(p), f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), u.x), mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), u.x), u.y);
}
float fbm(vec2 p) {
	float v = 0.0, a = 0.5;
	for (int k = 0; k < 4; k++) { v += a * vnoise(p); p = p * 2.03 + vec2(17.0, 9.0); a *= 0.5; }
	return v;
}

// 1 where the square is remembered (seen before, not now), 0 otherwise.
float remembered(vec2 square) {
	ivec2 s = clamp(ivec2(square), ivec2(0), ivec2(fog_size) - 1);
	return 1.0 - step(0.2, abs(texelFetch(fog_map, s, 0).r - 0.5));
}

void fragment() {
	vec2 xz = world_xz;
	// Domain-warped noise: two slow drifting fields bend a third, so the mist curls as it moves.
	float t = TIME * (0.045 + 0.015 * layer);
	vec2 q = vec2(fbm(xz * 0.17 + vec2(t, t * 0.6) + layer * 5.3),
	              fbm(xz * 0.17 - vec2(t * 0.7, t) + layer * 3.1));
	float n = fbm(xz * 0.21 + 3.0 * q + vec2(t * 1.3, -t * 0.4));
	// 0 seen now, 0.5 remembered, 1 never seen; nothing when the fog is lifted.
	float war = fog_on > 0.5 ? 1.0 - texture(fog_map, xz / fog_size).r : 0.0;
	// Off the map the mist thins away, as the ground fades into the dark.
	vec2 outside = max(max(-xz, xz - fog_size), vec2(0.0));
	float fade = 1.0 - smoothstep(0.5, 4.0, length(outside));
	// Never seen: dark smoke curling over the void. Remembered: a pale veil. Weather: pale mist everywhere.
	float smoke = smoothstep(0.5, 1.0, war);
	// The veil goes only on squares actually remembered — blended square by square by hand, since
	// the filtered fog passes through ½ on its way from seen to never seen and would ring every void.
	vec2 g = xz - 0.5;
	vec2 cell = floor(g), f = g - cell;
	float r00 = remembered(cell), r10 = remembered(cell + vec2(1.0, 0.0));
	float r01 = remembered(cell + vec2(0.0, 1.0)), r11 = remembered(cell + vec2(1.0, 1.0));
	float memory = fog_on > 0.5 ? mix(mix(r00, r10, f.x), mix(r01, r11, f.x), f.y) : 0.0;
	float veil = max(memory * 0.75, weather);
	float wisps = smoothstep(0.3, 0.8, n);
	vec3 dark = void_colour * 1.3 + vec3(0.13) * wisps;
	vec3 pale = mist_colour * (0.8 + 0.4 * n);
	float a_smoke = smoke * (0.55 + 0.4 * wisps);
	float a_veil = veil * wisps * 0.7;
	float a = max(a_smoke, a_veil);
	ALBEDO = mix(pale, dark, a_smoke / max(a, 0.001));
	ALPHA = clamp(a * fade, 0.0, 0.9);
}
",
        };
    }
}
