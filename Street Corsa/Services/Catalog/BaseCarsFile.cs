using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using StreetCorsa.Helpers;
using StreetCorsa.Mods;
using StreetCorsa.Parts.Cars;

namespace StreetCorsa.Services.Catalog
{
    /// <summary>
    /// The base game's cars.json, as the car catalog editor (a developer's screen) writes it: what was set by hand for a
    /// car goes into the base game's data, which ships, never into catalog.db, which does not. Written to the installed
    /// copy next to the program, and to the source tree's when the program runs from a build of it (the installed copy
    /// is the build's, copied from the source tree), one car per line, the file's notes and every other field kept.
    /// </summary>
    public static class BaseCarsFile
    {
        private const string ProjectFile = "Street Corsa.csproj";

        /// <summary>The installed base game's cars.json</summary>
        public static string InstalledPath => Path.Combine(ModSet.Installed.Base.Folder, ContentTypes.Cars, CarBook.CarsFile);

        /// <summary>The source tree's, above the program; null when it does not run from a build of the source</summary>
        public static string? SourcePath(string start)
        {
            for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
            {
                if (!File.Exists(Path.Combine(dir.FullName, ProjectFile))) continue;
                var baseGame = Path.Combine(dir.FullName, ModSet.FolderName, ModIds.BaseId);
                return Directory.Exists(baseGame) ? Path.Combine(baseGame, ContentTypes.Cars, CarBook.CarsFile) : null;
            }

            return null;
        }

        /// <summary>
        /// Each car's fields as given (a null figure takes the field out; a car left with none is taken out), into every
        /// copy of the file; the copies written, the installed one first
        /// </summary>
        public static IReadOnlyList<string> Save(IReadOnlyDictionary<string, CarFields> cars)
        {
            var written = new List<string>();
            foreach (var path in new[] { InstalledPath, SourcePath(AppContext.BaseDirectory) }.OfType<string>()
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                // In one go (temp file and rename): a crash halfway leaves the old file, never half of one
                SafeFile.WriteAllText(path, With(File.Exists(path) ? File.ReadAllText(path) : null, cars), SafeFile.Utf8NoBom);
                written.Add(path);
            }

            return written;
        }

        /// <summary>
        /// What the base game's own cars.json gives each car (not the mods'), as the editor writes it back; nothing when
        /// the file is not there or does not read
        /// </summary>
        public static IReadOnlyDictionary<string, CarEntry> Given()
        {
            try
            {
                if (!File.Exists(InstalledPath)) return new Dictionary<string, CarEntry>();
                var file = ModMerge.Parse(File.ReadAllText(InstalledPath));
                if (file?["cars"] is not JsonObject) return new Dictionary<string, CarEntry>();
                var only = ModMerge.NewObject();
                only["cars"] = file["cars"]!.DeepClone();
                return CarBook.Read(only, []).Cars;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new Dictionary<string, CarEntry>();
            }
        }

        /// <summary>
        /// The file's text with the cars' fields set. A file that is there but does not read throws
        /// <see cref="InvalidDataException"/>: written over, it would lose every car it holds.
        /// </summary>
        public static string With(string? text, IReadOnlyDictionary<string, CarFields> cars)
        {
            var file = ModMerge.Parse(text);
            if (file == null)
            {
                if (!string.IsNullOrWhiteSpace(text))
                    throw new InvalidDataException($"{CarBook.CarsFile} does not read as JSON (a missing comma, a car named twice?): mend it by hand and save again");
                file = ModMerge.NewObject();
            }
            if (file["cars"] is not JsonObject entries)
            {
                entries = ModMerge.NewObject();
                file["cars"] = entries;
            }

            foreach (var (id, fields) in cars)
            {
                var entry = entries[id] as JsonObject ?? ModMerge.NewObject();
                Set(entry, "engine", fields.Engine is { Length: > 0 } engine ? JsonValue.Create(engine) : null);
                Set(entry, "layout", fields.Layout is { } layout ? JsonValue.Create(layout.ToString().ToLowerInvariant()) : null);
                Set(entry, "price", fields.Price is { } price ? JsonValue.Create(price) : null);
                Set(entry, "common", fields.Common is { } common ? JsonValue.Create(Math.Round((double)common, 2)) : null);
                if (entry.Count == 0) entries.Remove(id);
                else entries[id] = entry;
            }

            return Write(file);
        }

        private static void Set(JsonObject entry, string name, JsonNode? value)
        {
            var key = entry.FirstOrDefault(p => p.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Key;
            if (key != null) entry.Remove(key);
            if (value != null) entry[name] = value;
        }

        /// <summary>The notes and anything else as they are, then the cars by id, one per line</summary>
        private static string Write(JsonObject file)
        {
            var text = new StringBuilder("{\n");
            var parts = new List<string>();
            foreach (var (name, value) in file)
            {
                if (name.Equals("cars", StringComparison.OrdinalIgnoreCase) || value == null) continue;
                parts.Add($"  {JsonSerializer.Serialize(name)}: {Indented(value)}");
            }

            var cars = (JsonObject)file["cars"]!;
            var lines = cars.Where(c => c.Value is JsonObject).OrderBy(c => c.Key, StringComparer.OrdinalIgnoreCase)
                .Select(c => $"    {JsonSerializer.Serialize(c.Key)}: {{ {string.Join(", ", ((JsonObject)c.Value!).Select(f => $"{JsonSerializer.Serialize(f.Key)}: {f.Value?.ToJsonString() ?? "null"}"))} }}");
            parts.Add("  \"cars\": {\n" + string.Join(",\n", lines) + "\n  }");
            text.Append(string.Join(",\n", parts));
            text.Append("\n}\n");
            return text.ToString();
        }

        private static string Indented(JsonNode value) =>
            value.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping })
                .Replace("\r\n", "\n").Replace("\n", "\n  ");
    }

    /// <summary>What the editor sets for a car: a null field is not given (worked out from the car)</summary>
    public sealed record CarFields(string? Engine, StreetCorsa.Parts.EngineLayout? Layout, decimal? Price, float? Common);
}
