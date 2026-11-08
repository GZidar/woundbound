using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WoundBound.Rules.Enums;

namespace WoundBound.Game.Combat
{
    /// <summary>
    /// A scene painted in the editor (the user's battlemap utility, 2026-11-08): what every square
    /// does — walls, cover, water, ground, high ground — and what it looks like, over a battlemap
    /// picture if there is one, with where the party and the GM's creatures start. Saved as JSON
    /// whose layers are rows of characters, one a square, so a map file can be read and diffed by eye:
    ///
    /// <code>
    /// solid:  . open   # wall   F full cover   P partial cover   U / u unbreakable full / partial
    /// water:  . dry    s shallow   d deep
    /// ground: . firm   b broken   r sure footing
    /// elevation: . level  r raised (slopes)   c raised, cliff-sided
    /// zones:  . none   p party start   g GM's creatures   h GM's creatures, hidden
    /// hazards: . none   n e s w   a current sweeping that way (Athletics or Endurance 18, or swept a Short way and off your feet)
    /// </code>
    /// <para>And who fights there: the party by name (the Quickstart characters), the foes by
    /// statblock and number — left empty, the Quickstart five against the clearing's band — and
    /// where fires burn.</para>
    /// </summary>
    public sealed class SceneMap
    {
        public string Name { get; set; } = "New scene";

        public int Width { get; set; } = SampleEncounters.FieldWidth;

        public int Height { get; set; } = SampleEncounters.FieldHeight;

        public Biome Biome { get; set; } = Biome.Meadow;

        public Visibility Visibility { get; set; } = Visibility.Good;

        /// <summary>Whether poor sight is weather — mist, fog, smoke — rather than the dark: no light helps, and the board is misted over.</summary>
        public bool Obscured { get; set; }

        public Backdrop? Backdrop { get; set; }

        public List<string> Solid { get; set; } = [];

        public List<string> Water { get; set; } = [];

        public List<string> Ground { get; set; } = [];

        [JsonPropertyName("elevation")]
        public List<string> Heights { get; set; } = [];

        public List<string> Zones { get; set; } = [];

        public List<string> Hazards { get; set; } = [];

        /// <summary>Built fires: light out to Long, and Fuel for a Fire caster.</summary>
        public List<PlacedFire> Fires { get; set; } = [];

        /// <summary>The party, by character name; empty means the Quickstart five.</summary>
        public List<string> Party { get; set; } = [];

        /// <summary>The GM's creatures, by statblock name and how many; empty means the clearing's band.</summary>
        public List<FoeGroup> Foes { get; set; } = [];

        public sealed record PlacedFire(int X, int Y);

        public sealed record FoeGroup(string Adversary, int Count);

        /// <summary>Props named for particular squares, over what the biome would put there.</summary>
        public List<PlacedProp> Props { get; set; } = [];

        /// <summary>Surfaces named for particular squares: a road, a yard.</summary>
        public List<PlacedSurface> Surfaces { get; set; } = [];

        public sealed record PlacedProp(int X, int Y, Prop Prop);

        public sealed record PlacedSurface(int X, int Y, Surface Surface);

        /// <summary>A blank scene <paramref name="width"/> by <paramref name="height"/>: open, dry, firm, level ground.</summary>
        public static SceneMap Blank(int width, int height, string name = "New scene")
        {
            var map = new SceneMap { Name = name, Width = width, Height = height };
            map.Normalise();
            return map;
        }

        /// <summary>Pads or trims every layer to the scene's size, filling with '.', so every square has a character in every layer.</summary>
        public void Normalise()
        {
            foreach (var layer in new[] { Solid, Water, Ground, Heights, Zones, Hazards })
            {
                Fit(layer);
            }

            Props.RemoveAll(p => !Inside(p.X, p.Y));
            Fires.RemoveAll(f => !Inside(f.X, f.Y));
            Surfaces.RemoveAll(s => !Inside(s.X, s.Y));
        }

        private void Fit(List<string> rows)
        {
            while (rows.Count < Height)
            {
                rows.Add(string.Empty);
            }

            if (rows.Count > Height)
            {
                rows.RemoveRange(Height, rows.Count - Height);
            }

            for (var y = 0; y < Height; y++)
            {
                rows[y] = rows[y].Length >= Width ? rows[y][..Width] : rows[y].PadRight(Width, '.');
            }
        }

        public bool Inside(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

        /// <summary>The character for square (<paramref name="x"/>, <paramref name="y"/>) in a layer.</summary>
        public static char At(List<string> layer, int x, int y) => y < layer.Count && x < layer[y].Length ? layer[y][x] : '.';

        /// <summary>Sets the character for square (<paramref name="x"/>, <paramref name="y"/>) in a layer.</summary>
        public void Set(List<string> layer, int x, int y, char value)
        {
            if (!Inside(x, y))
            {
                return;
            }

            Fit(layer);
            var row = new StringBuilder(layer[y]) { [x] = value };
            layer[y] = row.ToString();
        }

        /// <summary>Names (or with null, unnames) the prop on a square.</summary>
        public void SetProp(int x, int y, Prop? prop)
        {
            Props.RemoveAll(p => p.X == x && p.Y == y);
            if (prop is { } named && Inside(x, y))
            {
                Props.Add(new PlacedProp(x, y, named));
            }
        }

        /// <summary>Lights (or with false, puts out) a fire on a square.</summary>
        public void SetFire(int x, int y, bool burning)
        {
            Fires.RemoveAll(f => f.X == x && f.Y == y);
            if (burning && Inside(x, y))
            {
                Fires.Add(new PlacedFire(x, y));
            }
        }

        /// <summary>Names (or with null, unnames) the surface of a square.</summary>
        public void SetSurface(int x, int y, Surface? surface)
        {
            Surfaces.RemoveAll(s => s.X == x && s.Y == y);
            if (surface is { } named && Inside(x, y))
            {
                Surfaces.Add(new PlacedSurface(x, y, named));
            }
        }

        private IEnumerable<GridPosition> Where(List<string> layer, params char[] values)
        {
            for (var y = 0; y < Height; y++)
            {
                for (var x = 0; x < Width; x++)
                {
                    if (values.Contains(At(layer, x, y)))
                    {
                        yield return new GridPosition(x, y);
                    }
                }
            }
        }

        /// <summary>Where the party starts: the squares painted 'p', in reading order.</summary>
        public IReadOnlyList<GridPosition> PartyStart => Where(Zones, 'p').ToList();

        /// <summary>
        /// What might go wrong fighting on this scene, in a line each: no party start painted, too
        /// few squares for the party, or GM zones too far from the party to be fought from — a
        /// creature beyond Long (<paramref name="longSquares"/>) of everyone has already left the board.
        /// </summary>
        public IReadOnlyList<string> Warnings(int longSquares = 16)
        {
            var warnings = new List<string>();
            var party = PartyStart;
            if (party.Count == 0)
            {
                warnings.Add("No party start painted: the party will start at the west edge.");
            }
            else if (party.Count < 5)
            {
                warnings.Add($"Only {party.Count} party start square{(party.Count == 1 ? string.Empty : "s")}: the rest of the five start nearby.");
            }

            var zones = Where(Zones, 'g', 'h').ToList();
            if (party.Count > 0 && zones.Count > 0)
            {
                var far = zones.Count(z => party.Min(p => p.DistanceTo(z)) > longSquares - 2);
                if (far == zones.Count)
                {
                    warnings.Add($"Every GM square is beyond Long ({longSquares}) of the party: creatures there would have left the board, so they'll be placed near the party instead.");
                }
                else if (far > 0)
                {
                    warnings.Add($"{far} GM square{(far == 1 ? " is" : "s are")} beyond Long of the party and won't be used.");
                }
            }

            return warnings;
        }

        /// <summary>The scene as the rules see it.</summary>
        public Setting ToSetting()
        {
            Normalise();
            var cover = new Dictionary<GridPosition, Cover>();
            foreach (var sq in Where(Solid, 'F', 'U'))
            {
                cover[sq] = Cover.Full;
            }

            foreach (var sq in Where(Solid, 'P', 'u'))
            {
                cover[sq] = Cover.Partial;
            }

            var water = Where(Water, 's').ToDictionary(sq => sq, _ => WaterDepth.Shallow);
            foreach (var sq in Where(Water, 'd'))
            {
                water[sq] = WaterDepth.Deep;
            }

            var ground = Where(Ground, 'b').ToDictionary(sq => sq, _ => Terrain.Broken);
            foreach (var sq in Where(Ground, 'r'))
            {
                ground[sq] = Terrain.Sure;
            }

            var gm = Where(Zones, 'g').ToHashSet();
            var hidden = Where(Zones, 'h').ToHashSet();
            var zones = new List<DeploymentZone>();
            if (hidden.Count > 0)
            {
                zones.Add(new DeploymentZone("lying in wait", hidden, Hidden: true));
            }

            if (gm.Count > 0)
            {
                zones.Add(new DeploymentZone("in the open", gm, Hidden: false));
            }

            var hazards = new List<Hazard>();
            foreach (var (dir, dx, dy, way) in new[] { ('n', 0, -1, "north"), ('e', 1, 0, "east"), ('s', 0, 1, "south"), ('w', -1, 0, "west") })
            {
                var squares = Where(Hazards, dir).ToHashSet();
                if (squares.Count > 0)
                {
                    hazards.Add(new Hazard($"the current ({way})", $"A strong pull {way}ward that drags the unwary off their feet.", squares,
                        [new HazardCheck("Athletics", 18), new HazardCheck("Endurance", 18)],
                        new HazardEffect.Swept(Squares: 4, Dx: dx, Dy: dy, LosesFooting: true)));
                }
            }

            var unlit = Visibility != Visibility.Good;
            return new Setting
            {
                Name = Name,
                Visibility = Visibility,
                Cause = unlit ? (Obscured ? VisibilityCause.Obscured : VisibilityCause.Unlit) : null,
                Walls = Where(Solid, '#').ToHashSet(),
                CoverPieces = cover,
                Unbreakable = Where(Solid, 'U', 'u').ToHashSet(),
                Water = water,
                Ground = ground,
                Raised = Where(Heights, 'r', 'c').ToHashSet(),
                Deployment = zones,
                Hazards = hazards,
                Fires = Fires.Select(f => new GridPosition(f.X, f.Y)).ToHashSet(),
                Scenery = new Scenery
                {
                    Biome = Biome,
                    Backdrop = Backdrop,
                    Cliffs = Where(Heights, 'c').ToHashSet(),
                    Props = Props.ToDictionary(p => new GridPosition(p.X, p.Y), p => p.Prop),
                    Surfaces = Surfaces.ToDictionary(s => new GridPosition(s.X, s.Y), s => s.Surface),
                },
            };
        }

        private static readonly JsonSerializerOptions Json = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() },
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        public string ToJson()
        {
            Normalise();
            return JsonSerializer.Serialize(this, Json);
        }

        public static SceneMap FromJson(string json)
        {
            var map = JsonSerializer.Deserialize<SceneMap>(json, Json) ?? throw new InvalidDataException("Not a scene.");
            map.Normalise();
            return map;
        }
    }
}
