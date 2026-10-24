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
    /// The game's cars.json (the setting played's, the core's when none is), as the car catalog editor (a developer's
    /// screen) writes it: what was set by hand for a car goes into the setting's data, which ships, never into catalog.db,
    /// which does not. Written to the installed
    /// copy next to the program, and to the source tree's when the program runs from a build of it (the installed copy
    /// is the build's, copied from the source tree), one car per line, the file's notes and every other field kept.
    /// </summary>
    public static class BaseCarsFile
    {
        private const string ProjectFile = "Street Corsa.csproj";

        /// <summary>The installed setting's cars.json (<see cref="ModSet.Game"/>)</summary>
        public static string InstalledPath => Path.Combine(ModSet.Installed.Game.Folder, ContentTypes.Cars, CarBook.CarsFile);

        /// <summary>The source tree's, above the program; null when it does not run from a build of the source</summary>
        public static string? SourcePath(string start)
        {
            for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
            {
                if (!File.Exists(Path.Combine(dir.FullName, ProjectFile))) continue;
                var game = Path.Combine(dir.FullName, ModSet.FolderName, Path.GetFileName(ModSet.Installed.Game.Folder));
                return Directory.Exists(game) ? Path.Combine(game, ContentTypes.Cars, CarBook.CarsFile) : null;
            }

            return null;
        }

        /// <summary>
        /// Each car's fields as given (a null figure takes the field out; a car left with none is taken out), into every
        /// copy of the file; the copies written, the installed one first
        /// </summary>
        public static IReadOnlyList<string> Save(IReadOnlyDictionary<string, CarFields> cars) => SaveAll(text => With(text, cars));

        /// <summary>
        /// A car's engine offset into every copy of the file (zero takes it out), the car's other fields kept; the copies
        /// written, the installed one first
        /// </summary>
        public static IReadOnlyList<string> SaveEngineOffset(string carId, System.Numerics.Vector3 offset) =>
            SaveAll(text => WithEngineOffset(text, carId, offset));

        /// <summary>
        /// The file's text with the car's engine offset set, to the millimetre (one that comes to nothing takes it out); throws
        /// as <see cref="With"/> does
        /// </summary>
        public static string WithEngineOffset(string? text, string carId, System.Numerics.Vector3 offset)
        {
            // + 0.0: a nudge back to the start leaves -0, which would be written as such
            var mm = new[] { offset.X, offset.Y, offset.Z }.Select(v => Math.Round((double)v, 3) + 0.0).ToArray();
            var value = mm.All(v => v == 0) ? null : new JsonArray(mm[0], mm[1], mm[2]);
            return WithCars(text, entries => SetFields(entries, carId, entry => Set(entry, "engine_offset", value)));
        }

        /// <summary>
        /// What the game's own cars.json gives each car (not the add-ons'), as the editor writes it back; nothing when
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
        public static string With(string? text, IReadOnlyDictionary<string, CarFields> cars) => WithCars(text, entries =>
        {
            foreach (var (id, fields) in cars)
            {
                SetFields(entries, id, entry =>
                {
                    Set(entry, "engine", fields.Engine is { Length: > 0 } engine ? JsonValue.Create(engine) : null);
                    Set(entry, "layout", fields.Layout is { } layout ? JsonValue.Create(StreetCorsa.Parts.EngineLayouts.Name(layout)) : null);
                    // Prices are the game's own now (the car's body and parts): a price an older editor wrote goes
                    Set(entry, "price", null);
                    Set(entry, "common", fields.Common is { } common ? JsonValue.Create(Math.Round((double)common, 2)) : null);
                    Set(entry, "running_gear", RunningGear(fields.Gear));
                });
            }
        });

        /// <summary>The picks as cars.json holds them, only what is picked; null for none</summary>
        private static JsonObject? RunningGear(RunningGearPicks? picks)
        {
            if (picks is not { IsEmpty: false }) return null;
            var gear = ModMerge.NewObject();
            if (picks.Axle is { Length: > 0 } axle) gear["axle"] = axle;
            if (picks.Diff is { Length: > 0 } diff) gear["diff"] = diff;
            if (picks.Ratio is { } ratio) gear["ratio"] = Math.Round(ratio, 2);
            if (picks.Shafts is { Length: > 0 } shafts) gear["shafts"] = shafts;
            if (picks.BrakesFront is { Length: > 0 } brakesFront) gear["brakes_front"] = brakesFront;
            if (picks.BrakesRear is { Length: > 0 } brakesRear) gear["brakes_rear"] = brakesRear;
            if (picks.SpringsFront is { Length: > 0 } springsFront) gear["springs_front"] = springsFront;
            if (picks.SpringsRear is { Length: > 0 } springsRear) gear["springs_rear"] = springsRear;
            return gear;
        }

        /// <summary>The file's text changed into every copy of it, each in one go; the copies written, the installed one first</summary>
        private static IReadOnlyList<string> SaveAll(Func<string?, string> change)
        {
            var written = new List<string>();
            foreach (var path in new[] { InstalledPath, SourcePath(AppContext.BaseDirectory) }.OfType<string>()
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                // In one go (temp file and rename): a crash halfway leaves the old file, never half of one
                SafeFile.WriteAllText(path, change(File.Exists(path) ? File.ReadAllText(path) : null), SafeFile.Utf8NoBom);
                written.Add(path);
            }

            return written;
        }

        /// <summary>The file's text with its cars changed; a file that is there but does not read throws (see <see cref="With"/>)</summary>
        private static string WithCars(string? text, Action<JsonObject> change)
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

            change(entries);
            return Write(file);
        }

        /// <summary>A car's entry changed; one left with no field is taken out</summary>
        private static void SetFields(JsonObject entries, string id, Action<JsonObject> change)
        {
            var entry = entries[id] as JsonObject ?? ModMerge.NewObject();
            change(entry);
            if (entry.Count == 0) entries.Remove(id);
            else entries[id] = entry;
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
    /// <param name="Gear">The factory running gear picked; null or none picked takes it out</param>
    public sealed record CarFields(string? Engine, StreetCorsa.Parts.EngineLayout? Layout, float? Common, RunningGearPicks? Gear = null);
}
