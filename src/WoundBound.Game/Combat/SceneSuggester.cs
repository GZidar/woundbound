using System.Text.Json;
using Anthropic;
using Anthropic.Models.Beta.Messages;

namespace WoundBound.Game.Combat
{
    /// <summary>
    /// Asks Claude to read a battlemap picture and suggest what each square does (the user's
    /// battlemap utility, 2026-11-08): walls, cover and what it is, water, rough ground, high
    /// ground. The answer comes back in the editor's own character-grid layers, and fills the scene
    /// for the user to check and correct — a suggestion, never a finished scene.
    ///
    /// <para>One call to the Messages API with the picture (cropped to the map and scaled to fit the
    /// model's image limit) and the grid's geometry in that picture's pixels; structured output
    /// keeps the reply to the schema. The API key comes from the usual place (ANTHROPIC_API_KEY, or
    /// a key the caller passes). Each call is billed.</para>
    /// </summary>
    public static class SceneSuggester
    {
        /// <summary>The model asked: the current Opus.</summary>
        public const string Model = "claude-opus-5-5";

        /// <summary>The longest edge, in pixels, a picture is sent at: the model's high-resolution image limit.</summary>
        public const int MaxImageEdge = 2576;

        /// <summary>What Claude is asked to send back: one string per row in each layer, and the props it recognises.</summary>
        public sealed record Suggestion(List<string> Solid, List<string> Water, List<string> Ground, List<string> Elevation, List<PropGuess> Props, string Notes);

        public sealed record PropGuess(int X, int Y, Prop Prop);

        /// <summary>The instructions, given the scene's size and where its grid lies in the picture sent.</summary>
        public static string Prompt(int width, int height, double originX, double originY, double pixelsPerSquare) =>
            $$"""
            This is a top-down battlemap for a tabletop game. A grid of {{width}} columns by {{height}} rows is laid over it:
            square (0, 0) has its top-left corner at pixel ({{originX:0.#}}, {{originY:0.#}}) of this image, and each square is
            {{pixelsPerSquare:0.##}} pixels across, so square (x, y) covers pixels from ({{originX:0.#}} + x*{{pixelsPerSquare:0.##}}, {{originY:0.#}} + y*{{pixelsPerSquare:0.##}}).
            Columns run left to right (x), rows top to bottom (y).

            For every square, say what it does in the fight, in four layers. Each layer is exactly {{height}} strings,
            one per row from the top, each exactly {{width}} characters, one per square from the left.

            solid — what blocks the square:
              .  open
              #  a wall: building walls, sheer rock faces, pillars, solid cliffs nobody walks through or sees past
              F  full cover: something taller than a person that blocks most of a body — a tree trunk with its canopy, a stack of crates, a big boulder, a tent
              P  partial cover: waist-high — a bush, a low wall, a fence, a barrel, a cart, a log, a small rock
              Use solid only for things that really stand on the square; open ground, paths and floors are '.'.
              A tree's canopy spreads wide: mark only the square or two at its trunk, not every square under its leaves.
            water:
              .  dry   s  shallow water (wading)   d  deep water (must be swum)
            ground:
              .  firm   b  broken or rough going (rubble, scree, dense undergrowth, mud, steep slopes)   r  sure footing (a paved road or floor)
            elevation:
              .  level   r  high ground (a ridge, a hilltop, a raised platform) that slopes down at its edges   c  high ground with sheer, cliff-like sides

            Also list props: for each solid square you marked, what it most looks like, from this list:
            Tree, Pine, Birch, CrateStack, Boulder, Pillar, StoneWall, HutWall, RockFace, Bush, Hedge, Rock, Crate, Barrel, Cart, Fence, LowWall, Log, Stump, Reeds.
            Tall ones (Tree … RockFace) go only on '#' or 'F' squares; low ones (Bush … Reeds) only on 'P' squares. Use HutWall for buildings and tents.

            Mark what you can see; where you can't tell, leave it open, dry, firm and level. In notes, say in a sentence or two
            what you were unsure of.
            """;

        /// <summary>The JSON schema the reply must follow.</summary>
        public static Dictionary<string, JsonElement> Schema()
        {
            var rows = new { type = "array", items = new { type = "string" } };
            var props = Enum.GetNames<Prop>();
            return new Dictionary<string, JsonElement>
            {
                ["type"] = JsonSerializer.SerializeToElement("object"),
                ["additionalProperties"] = JsonSerializer.SerializeToElement(false),
                ["required"] = JsonSerializer.SerializeToElement(new[] { "solid", "water", "ground", "elevation", "props", "notes" }),
                ["properties"] = JsonSerializer.SerializeToElement(new Dictionary<string, object>
                {
                    ["solid"] = rows,
                    ["water"] = rows,
                    ["ground"] = rows,
                    ["elevation"] = rows,
                    ["notes"] = new { type = "string" },
                    ["props"] = new
                    {
                        type = "array",
                        items = new Dictionary<string, object>
                        {
                            ["type"] = "object",
                            ["additionalProperties"] = false,
                            ["required"] = new[] { "x", "y", "prop" },
                            ["properties"] = new Dictionary<string, object>
                            {
                                ["x"] = new { type = "integer" },
                                ["y"] = new { type = "integer" },
                                ["prop"] = new Dictionary<string, object> { ["type"] = "string", ["enum"] = props },
                            },
                        },
                    },
                }),
            };
        }

        private static readonly JsonSerializerOptions Json = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
        };

        /// <summary>Reads Claude's reply.</summary>
        public static Suggestion Parse(string json) =>
            JsonSerializer.Deserialize<Suggestion>(json, Json) ?? throw new InvalidDataException("Empty suggestion.");

        /// <summary>
        /// Lays a suggestion over <paramref name="map"/>'s four layers and its props, replacing what was
        /// there; only the characters each layer allows are kept (anything else counts as '.'), rows
        /// are fitted to the map's size, and props are kept only where they fit the cover painted under them.
        /// </summary>
        public static void Apply(SceneMap map, Suggestion suggestion)
        {
            Fill(map, map.Solid, suggestion.Solid, "#FPUu");
            Fill(map, map.Water, suggestion.Water, "sd");
            Fill(map, map.Ground, suggestion.Ground, "br");
            Fill(map, map.Heights, suggestion.Elevation, "rc");
            map.Props.Clear();
            foreach (var guess in suggestion.Props)
            {
                if (!map.Inside(guess.X, guess.Y))
                {
                    continue;
                }

                var solid = SceneMap.At(map.Solid, guess.X, guess.Y);
                var fits = guess.Prop.IsTall() ? solid is '#' or 'F' or 'U' : solid is 'P' or 'u';
                if (fits)
                {
                    map.SetProp(guess.X, guess.Y, guess.Prop);
                }
            }

            map.Normalise();
        }

        private static void Fill(SceneMap map, List<string> layer, List<string> rows, string allowed)
        {
            for (var y = 0; y < map.Height; y++)
            {
                var row = y < rows.Count ? rows[y] : string.Empty;
                for (var x = 0; x < map.Width; x++)
                {
                    var c = x < row.Length ? row[x] : '.';
                    map.Set(layer, x, y, allowed.Contains(c) ? c : '.');
                }
            }
        }

        /// <summary>
        /// Sends <paramref name="jpeg"/> — the picture already cropped and scaled, its grid at
        /// (<paramref name="originX"/>, <paramref name="originY"/>) and <paramref name="pixelsPerSquare"/> —
        /// and returns Claude's suggestion for a <paramref name="width"/> by <paramref name="height"/> scene.
        /// </summary>
        public static async Task<Suggestion> Ask(byte[] jpeg, int width, int height, double originX, double originY, double pixelsPerSquare,
            string? apiKey = null, CancellationToken cancel = default)
        {
            AnthropicClient client = string.IsNullOrWhiteSpace(apiKey) ? new() : new() { ApiKey = apiKey };
            var response = await client.Beta.Messages.Create(new MessageCreateParams
            {
                Model = Model,
                MaxTokens = 16000,
                // A refused request is re-served by a fallback model chosen for the refusal's kind.
                Betas = ["server-side-fallback-2026-07-01"],
                Fallbacks = new Default(),
                OutputConfig = new BetaOutputConfig
                {
                    Effort = Effort.High,
                    Format = new BetaJsonOutputFormat { Schema = Schema() },
                },
                Messages =
                [
                    new BetaUserMessageParam
                    {
                        Content = new List<BetaContentBlockParam>
                        {
                            new BetaImageBlockParam { Source = new BetaBase64ImageSource { Data = Convert.ToBase64String(jpeg), MediaType = MediaType.ImageJpeg } },
                            new BetaTextBlockParam { Text = Prompt(width, height, originX, originY, pixelsPerSquare) },
                        },
                    },
                ],
            }, cancel);

            if (response.StopReason == "refusal")
            {
                throw new InvalidOperationException("Claude declined to read this picture.");
            }

            if (response.StopReason == "max_tokens")
            {
                throw new InvalidOperationException("Claude's answer was cut short — try a smaller map.");
            }

            var text = string.Concat(response.Content.Select(b => b.TryPickText(out var t) ? t.Text : string.Empty));
            return Parse(text);
        }
    }
}
