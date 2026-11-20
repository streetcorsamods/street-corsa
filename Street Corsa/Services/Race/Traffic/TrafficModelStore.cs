using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using StreetCorsa.Configuration;
using StreetCorsa.Helpers;
using StreetCorsa.Services.Race.Traffic.Conversion;

namespace StreetCorsa.Services.Race.Traffic
{
    /// <summary>
    /// The traffic models the game made from the player's cars (<see cref="TrafficCarConverter"/>), kept in its own
    /// folder and named to the mode by race.ini's <c>TRAFFIC_MODELS</c>: nothing goes into the traffic tool's folder or
    /// any other of AC's, so there is nothing to put back after a race. A model is its <c>&lt;id&gt;.json</c> and the
    /// files it names; it is made again when its car changes, the converter does, or it was made for other textures.
    /// </summary>
    public sealed partial class TrafficModelStore
    {
        /// <summary>Room counted for one model before it is made: the largest seen (2026-11-15: 11-42 MB, 23 on average)</summary>
        public const long BytesPerModel = 40L << 20;

        /// <summary>Room left over on the drive after the models</summary>
        public const long Headroom = 1L << 30;

        /// <summary>The game's own folder of traffic models</summary>
        public static string DefaultRoot => Path.Combine(AppSettings.AppDataPath, "Traffic", "models");

        public string Root { get; }

        public TrafficModelStore(string? root = null) => Root = root ?? DefaultRoot;

        /// <summary>A model in the folder: its id, the car it was made from and whose that is, and its files' size</summary>
        public sealed record Model(string Id, string? Car, string? Name, string? Author, long Bytes, bool Made);

        /// <summary>Whether a model is there (its definition is)</summary>
        public bool Has(string id) => File.Exists(DefinitionOf(id));

        public string DefinitionOf(string id) => Path.Combine(Root, id + ".json");

        /// <summary>
        /// Whether the model of a recipe is there and up to date: made by this converter from the car it would be made
        /// from now, as that car is now, by the recipe as it is now, with textures of this size
        /// </summary>
        public bool IsFresh(string id, string carFolder, TrafficCarConverter.Models models, int textureSide, TrafficCarRecipe recipe)
        {
            if (Source(id) is not { } source) return false;
            return Text(source, "fingerprint") == TrafficCarConverter.Fingerprint(carFolder, models)
                && Text(source, "recipe") == recipe.Stamp()
                && source["maxTexture"] is JsonValue side && side.TryGetValue<int>(out var made) && made == textureSide;
        }

        private string FailedOf(string id) => Path.Combine(Root, id + ".failed");

        /// <summary>What a model that would not be made was tried from: the car as it is, the recipe, the textures' size</summary>
        private static string Tried(string carFolder, TrafficCarConverter.Models models, int textureSide, TrafficCarRecipe recipe) =>
            $"{TrafficCarConverter.Fingerprint(carFolder, models)}|{textureSide}|{recipe.Stamp()}";

        /// <summary>
        /// Remembers that a car would not make a model (no LODs, encrypted), and why: it is not tried again, drive after
        /// drive, until the car, the recipe or the converter changes
        /// </summary>
        public void MarkFailed(string id, string carFolder, TrafficCarConverter.Models models, int textureSide, TrafficCarRecipe recipe, string? why)
        {
            try
            {
                Directory.CreateDirectory(Root);
                File.WriteAllLines(FailedOf(id), [Tried(carFolder, models, textureSide, recipe), why ?? string.Empty]);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Not remembered: it is tried again next time
            }
        }

        /// <summary>Why the car would not make the model the last time, when nothing has changed since; null when it is worth a try</summary>
        public string? WhyFailed(string id, string carFolder, TrafficCarConverter.Models models, int textureSide, TrafficCarRecipe recipe)
        {
            try
            {
                if (!File.Exists(FailedOf(id))) return null;
                var lines = File.ReadAllLines(FailedOf(id));
                return lines.Length > 0 && lines[0] == Tried(carFolder, models, textureSide, recipe) ? lines.ElementAtOrDefault(1) ?? string.Empty : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>A model made after all: what was remembered of its failing is forgotten</summary>
        public void ForgetFailed(string id)
        {
            try
            {
                File.Delete(FailedOf(id));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        /// <summary>Every model in the folder</summary>
        public List<Model> List()
        {
            var models = new List<Model>();
            if (!Directory.Exists(Root)) return models;
            foreach (var file in Directory.EnumerateFiles(Root, "*.json"))
            {
                var id = Path.GetFileNameWithoutExtension(file);
                // Read once: its source block and the files it names
                var definition = Definition(file);
                var source = definition?["source"] as JsonObject;
                models.Add(new Model(id, Text(source, "car"), Text(source, "name"), Text(source, "author"),
                    Files(id, Named(definition, Root) ?? []).Sum(Length), source?["converter"] != null));
            }
            return models.OrderBy(m => m.Name ?? m.Id, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>The room free on the folder's drive, in bytes; long.MaxValue when it cannot be told</summary>
        public long FreeSpace()
        {
            try
            {
                return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(Root))!).AvailableFreeSpace;
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
            {
                return long.MaxValue;
            }
        }

        /// <summary>How many models of <see cref="BytesPerModel"/> fit on the drive, keeping <see cref="Headroom"/></summary>
        public int RoomFor() => FreeSpace() is var free && free == long.MaxValue ? int.MaxValue : (int)Math.Clamp((free - Headroom) / BytesPerModel, 0, int.MaxValue);

        /// <summary>A model and its files taken out of the folder</summary>
        public void Delete(string id)
        {
            if (!PathNames.IsSafeSegment(id)) return;
            foreach (var file in Files(id).Where(f => !f.Equals(DefinitionOf(id), StringComparison.OrdinalIgnoreCase))) File.Delete(file);
            var skins = Path.Combine(Root, id + "_skins");
            if (Directory.Exists(skins)) Directory.Delete(skins, true);
            if (File.Exists(DefinitionOf(id))) File.Delete(DefinitionOf(id));
            ForgetFailed(id);
        }

        /// <summary>Every model taken out</summary>
        public void DeleteAll()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }

        /// <summary>
        /// A model the player already has in the traffic tool's folder (one the game has no recipe for), brought into
        /// the game's folder so the mode, reading only that, still casts it: hard-linked where the drive allows it (no
        /// second copy), copied where not, and a file already there as the tool's is (its size and time) left alone, so
        /// a model is brought in once, not drive after drive. The traffic tool's folder is only read. The definition
        /// goes in last: a model is there when its .json is. False when it is not there whole, or names a file outside
        /// its folder.
        /// </summary>
        /// <exception cref="IOException">A file would not be placed, or the drive has no room for the copies</exception>
        public bool Adopt(string id, string toolFolder)
        {
            if (!PathNames.IsSafeSegment(id)) return false;
            var definition = Path.Combine(toolFolder, id + ".json");
            if (!File.Exists(definition)) return false;
            var named = Named(Definition(definition), toolFolder);
            if (named == null || named.Any(f => !File.Exists(Path.Combine(toolFolder, f)))) return false;

            var files = new List<string>();
            var skins = Path.Combine(toolFolder, id + "_skins");
            if (Directory.Exists(skins))
                files.AddRange(Directory.EnumerateFiles(skins, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(toolFolder, f)));
            files.AddRange(named);
            files.Add(id + ".json");

            var toPlace = files.Where(f => !Same(Path.Combine(toolFolder, f), Path.Combine(Root, f))).ToList();
            if (toPlace.Count == 0) return true;

            // Across drives nothing links: the copies need their room, and the headroom stays
            if (!SameDrive(toolFolder, Root))
            {
                var needed = toPlace.Sum(f => Length(Path.Combine(toolFolder, f)));
                if (FreeSpace() is var free && free != long.MaxValue && free - Headroom < needed)
                    throw new IOException($"no room on the drive for {id} ({needed >> 20} MB to copy, {free >> 20} MB free)");
            }

            Directory.CreateDirectory(Root);
            foreach (var file in toPlace)
            {
                var target = Path.Combine(Root, file);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                Place(Path.Combine(toolFolder, file), target);
            }
            return true;
        }

        /// <summary>A hard link where source and target share a drive, else a copy; whatever was there before replaced</summary>
        private static void Place(string source, string target)
        {
            if (File.Exists(target)) File.Delete(target);
            if (!CreateHardLink(target, source, IntPtr.Zero)) File.Copy(source, target);
        }

        /// <summary>Whether the target is the source already: there, of its size and its time (a hard link's and a copy's both are)</summary>
        private static bool Same(string source, string target)
        {
            try
            {
                var (from, to) = (new FileInfo(source), new FileInfo(target));
                return to.Exists && from.Length == to.Length && from.LastWriteTimeUtc == to.LastWriteTimeUtc;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static bool SameDrive(string a, string b) =>
            string.Equals(Path.GetPathRoot(Path.GetFullPath(a)), Path.GetPathRoot(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);

        private static long Length(string file)
        {
            try
            {
                return new FileInfo(file).Length;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return 0;
            }
        }

        [LibraryImport("kernel32.dll", EntryPoint = "CreateHardLinkW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

        /// <summary>A definition file read; null when it does not read or is no object</summary>
        private static JsonObject? Definition(string file)
        {
            try
            {
                return JsonNode.Parse(File.ReadAllText(file)) as JsonObject;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
            {
                return null;
            }
        }

        /// <summary>
        /// The model files a definition names beside it, relative; null when it does not read, or names one that is
        /// not inside <paramref name="folder"/> (a rooted path, a <c>..</c>): a definition is somebody's download, and
        /// what it names is linked, copied over and deleted
        /// </summary>
        private static List<string>? Named(JsonObject? definition, string folder)
        {
            if (definition == null) return null;
            var files = new List<string>();
            foreach (var key in new[] { "main", "lod", "collider" })
            {
                if (definition[key] is not JsonValue v || !v.TryGetValue<string>(out var f) || f.Length == 0) continue;
                if (!IsInside(folder, f)) return null;
                files.Add(f);
            }
            return files;
        }

        /// <summary>Whether a name, put beside the files of <paramref name="folder"/>, is a file inside it</summary>
        internal static bool IsInside(string folder, string name)
        {
            try
            {
                if (Path.IsPathRooted(name)) return false;
                var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar;
                return Path.GetFullPath(Path.Combine(root, name)).StartsWith(root, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return false;
            }
        }

        /// <summary>A model's files: its definition, what it names, its skins</summary>
        private IEnumerable<string> Files(string id) => Files(id, Named(Definition(DefinitionOf(id)), Root) ?? []);

        private IEnumerable<string> Files(string id, IReadOnlyList<string> named)
        {
            var definition = DefinitionOf(id);
            if (!File.Exists(definition)) yield break;
            yield return definition;
            foreach (var file in named)
            {
                var path = Path.Combine(Root, file);
                if (File.Exists(path)) yield return path;
            }
            var skins = Path.Combine(Root, id + "_skins");
            if (Directory.Exists(skins))
            {
                foreach (var file in Directory.EnumerateFiles(skins, "*", SearchOption.AllDirectories)) yield return file;
            }
        }

        /// <summary>A made model's <c>source</c> block; null for none (not there, unreadable, or adopted)</summary>
        private JsonObject? Source(string id)
        {
            try
            {
                return File.Exists(DefinitionOf(id)) ? Definition(DefinitionOf(id))?["source"] as JsonObject : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        private static string? Text(JsonObject? o, string key) => o?[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
    }
}
