using System.Text.Json;
using System.Text.Json.Serialization;
using WoundBound.Rules.Catalog;

namespace WoundBound.Game.Data
{
    /// <summary>
    /// The reference catalogues the session tracker keeps as JSON (its wwwroot/data), parsed the
    /// same way it parses them. Takes the JSON text rather than a path: in Godot the files live
    /// under res://, which only Godot's own file API can open once the game is exported.
    /// </summary>
    public sealed class GameData
    {
        public const string RangeBandsFile = "range-bands.json";
        public const string StartingAdversariesFile = "starting-adversaries.json";
        public const string StartingWeaponsFile = "starting-weapons.json";
        public const string EnvironmentFile = "environment.json";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            Converters = { new JsonStringEnumConverter() },
            PropertyNameCaseInsensitive = true
        };

        public required RangeBandTables RangeBands { get; init; }

        public required IReadOnlyList<StartingAdversaryInfo> Adversaries { get; init; }

        public required IReadOnlyList<StartingWeaponInfo> Weapons { get; init; }

        /// <summary>The three environmental levers' grades (Core Rules §9, "Environment").</summary>
        public required EnvironmentTables Environment { get; init; }

        /// <param name="readFile">Returns the text of one of the catalogue files named above.</param>
        public static GameData Load(Func<string, string> readFile) => new()
        {
            RangeBands = Parse<RangeBandTables>(readFile, RangeBandsFile),
            Adversaries = Parse<List<StartingAdversaryInfo>>(readFile, StartingAdversariesFile),
            Weapons = Parse<List<StartingWeaponInfo>>(readFile, StartingWeaponsFile),
            Environment = Parse<EnvironmentTables>(readFile, EnvironmentFile),
        };

        public StartingAdversaryInfo Adversary(string name) =>
            Adversaries.FirstOrDefault(a => a.Name == name)
            ?? throw new KeyNotFoundException($"No adversary named '{name}' in {StartingAdversariesFile}.");

        public StartingWeaponInfo Weapon(string name) =>
            Weapons.FirstOrDefault(w => w.Name == name)
            ?? throw new KeyNotFoundException($"No weapon named '{name}' in {StartingWeaponsFile}.");

        private static T Parse<T>(Func<string, string> readFile, string file) =>
            JsonSerializer.Deserialize<T>(readFile(file), JsonOptions)
            ?? throw new InvalidDataException($"'{file}' is empty.");
    }
}
