using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AcTools.Kn5File;
using StreetCorsa.Parts.Export;

namespace StreetCorsa.Services.Race.Traffic.Conversion
{
    /// <summary>
    /// Makes a CSP traffic model from a car the player has installed: ported from Race Explorer's
    /// <c>traffic_car_from_mod.py</c> and <c>traffic_fleet_build.py</c>, which built the 105-model fleet raced since
    /// 2026-09. Nothing of anyone's is passed on: the model is made on the player's machine, from their own car, into
    /// the game's own folder, and never leaves it.
    /// <para>A traffic model is four files beside each other, as the traffic tool reads them:</para>
    /// <list type="bullet">
    /// <item>
    /// <c>&lt;id&gt;.kn5</c>, the near model: the car's LOD B with the full model's textures grafted in (AC's LOD files
    /// carry none), capped and block-compressed (<see cref="TrafficTextures"/>). The full model when there is no
    /// usable LOB B (none, unreadable, without materials, under 4,000 vertices, half the LOD C's, or no lighter than
    /// the full one), or when the recipe says so.
    /// </item>
    /// <item>
    /// <c>&lt;id&gt;_lod.kn5</c>, the far model: the car's LOD C, else its LOD D, when textured and under 30 MB (a
    /// "LOD" bigger is a full model under a LOD's name). A car with neither is not made: Content Manager's LOD
    /// generator gives it some.
    /// </item>
    /// <item><c>&lt;id&gt;_collider.kn5</c>: the car's own collider, already the right thing.</item>
    /// <item>
    /// <c>&lt;id&gt;.json</c>: its size (the far model's, its width held to the collider's when mirrors and open doors
    /// widen it), mass and wheel radius from its data, its lamps, its paint, its cruising speed, and where it came
    /// from (the car and its author).
    /// </item>
    /// </list>
    /// The models are lifted so their lowest point is on the road (CSP has no ride height, and a car authored with its
    /// origin elsewhere drives buried), and a dummy named BODY is renamed HULL (the tool resets BODY's rotation for body
    /// roll, which scatters a mod whose BODY is turned).
    /// </summary>
    public sealed class TrafficCarConverter
    {
        /// <summary>Bumped when the converter makes different models from the same car, so the old ones are made again</summary>
        public const int Version = 1;

        /// <summary>A "LOD" bigger than this is a full model under a LOD's name (the Fulvia's LOD C, 81 MB; the Holden VN's is a real 25 MB)</summary>
        public const long FarMostBytes = 30_000_000;

        /// <summary>A "LOD B" thinner than this is a far model under the wrong name</summary>
        public const int NearLeastVertices = 4000;

        // CarsList.lua's own fallbacks: both cars CSP ships restate them rather than describing themselves, so what is
        // not measured is left as CSP would have it
        private const double Front = 0.5, Rear = 4, TurningOffset = 2.3, Width = 1.6, WheelRadius = 0.33;
        private const double Mass = 1500, PhysicsWidth = 1.5, PhysicsLength = 3, GripForce = 7, SuspensionTravel = 0.05, SuspensionForce = 30, SuspensionDamping = 15;

        private static readonly Regex LodName = new(@"_lod[_\- ]?([bcd])(?:\b|\.)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly string _acRoot;
        private readonly ITexels _texels;

        public TrafficCarConverter(string acRoot, ITexels texels)
        {
            _acRoot = acRoot;
            _texels = texels;
        }

        /// <summary>What a conversion came to: the model's definition file, or why there is none, and what it noticed</summary>
        public sealed record Result(string? Definition, string? Problem, string? SourceCar, IReadOnlyList<string> Notes, long Bytes);

        /// <summary>A car's model files: its full model, LODs B to D, and collider (file names in its folder)</summary>
        public sealed record Models(string? Full, string? LodB, string? LodC, string? LodD, string? Collider);

        /// <summary>The first of a recipe's cars installed, as a folder; null when none is</summary>
        public string? SourceOf(TrafficCarRecipe recipe) =>
            recipe.Cars.Select(c => Path.Combine(_acRoot, "content", "cars", c)).FirstOrDefault(d => Directory.Exists(d) && Directory.EnumerateFiles(d, "*.kn5").Any());

        /// <summary>
        /// A car's model files: from its <c>lods.ini</c> (LOD_0 the full model, LOD_1 to LOD_3 B to D), which is what AC
        /// itself goes by; else by name (<c>_lod_b</c>, <c>_LOD_C</c>, <c>_lodd</c>, and <c>&lt;full&gt;_B</c>, as the
        /// 500 names its own), the first other file being the full model (a second is a body kit)
        /// </summary>
        public static Models ModelsOf(string carFolder, Func<string, string?>? data = null)
        {
            var files = Directory.EnumerateFiles(carFolder, "*.kn5").Select(Path.GetFileName).OfType<string>()
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
            string? Have(string? name) => name == null ? null : files.FirstOrDefault(f => f.Equals(Path.GetFileName(name), StringComparison.OrdinalIgnoreCase));
            var collider = Have("collider.kn5");

            string? text = null;
            try
            {
                text = (data ?? AcCarDataReader.ForCar(carFolder))("lods.ini");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
            }
            if (text != null)
            {
                var ini = new IniText(text);
                var full = Have(ini.Get("LOD_0", "FILE"));
                if (full != null)
                    return new Models(full, Have(ini.Get("LOD_1", "FILE")), Have(ini.Get("LOD_2", "FILE")), Have(ini.Get("LOD_3", "FILE")), collider);
            }

            string? fullByName = null, b = null, c = null, d = null;
            var rest = new List<string>();
            foreach (var file in files.Where(f => !f.Equals("collider.kn5", StringComparison.OrdinalIgnoreCase)))
            {
                var m = LodName.Match(file);
                if (!m.Success)
                {
                    rest.Add(file);
                    continue;
                }
                switch (char.ToLowerInvariant(m.Groups[1].Value[0]))
                {
                    case 'b': b ??= file; break;
                    case 'c': c ??= file; break;
                    default: d ??= file; break;
                }
            }
            fullByName = rest.FirstOrDefault();
            if (fullByName != null)
            {
                // <full>_B.kn5 and so on: the 500's own LODs
                var stem = Path.GetFileNameWithoutExtension(fullByName);
                string? Suffixed(char letter) => rest.FirstOrDefault(f => f.Equals($"{stem}_{letter}.kn5", StringComparison.OrdinalIgnoreCase));
                b ??= Suffixed('B');
                c ??= Suffixed('C');
                d ??= Suffixed('D');
            }
            return new Models(fullByName, b, c, d, collider);
        }

        /// <summary>
        /// The traffic model of <paramref name="recipe"/>, made from the first of its cars installed, into
        /// <paramref name="folder"/>, its textures at most <paramref name="maxTexture"/> a side. The files go in under
        /// temporary names and are renamed only once all are made, so a model is whole or not there.
        /// </summary>
        public Result Convert(TrafficCarRecipe recipe, string folder, int maxTexture, CancellationToken cancel = default)
        {
            var notes = new List<string>();
            var car = SourceOf(recipe);
            if (car == null) return new Result(null, "none of its cars is installed: " + string.Join(", ", recipe.Cars), null, notes, 0);
            var carId = Path.GetFileName(car);
            if (Catalog.EncryptedCars.IsEncrypted(car)) return new Result(null, $"{carId}'s model is encrypted", carId, notes, 0);

            var models = ModelsOf(car);
            if (models.Full == null || models.Collider == null) return new Result(null, $"{carId} has no full model or no collider.kn5", carId, notes, 0);

            cancel.ThrowIfCancellationRequested();
            var fullPath = Path.Combine(car, models.Full);
            var full = Kn5.FromFile(fullPath);
            if (full.TexturesData.Count == 0 && full.Textures.Count > 0 || full.Textures.Count > 0 && full.TexturesData.Values.All(t => t == null || t.Length == 0))
                return new Result(null, $"{models.Full} declares its textures but stores none (a packer keeps them after the tree, and CSP's traffic draws it as scrap)", carId, notes, 0);

            // The far model
            var (farPath, farLabel) = Far(car, models);
            if (farPath == null) return new Result(null, $"{carId} has no usable LOD C or LOD D: Content Manager's LOD generator can make them", carId, notes, 0);

            // The near model's geometry: the LOD B, else the full model
            cancel.ThrowIfCancellationRequested();
            var (nearPath, nearLabel) = recipe.NearFull ? (fullPath, "the full model (the recipe says so)") : Near(car, models, full);
            var near = nearPath == fullPath ? full : Kn5.FromFile(nearPath, SkippingTextureLoader.Instance);
            notes.Add($"near: {nearLabel}; far: {farLabel}");

            // Size: the far model's exterior; its width held to the collider's when it is over 35 cm wider (mirrors, an
            // open door); the collider alone is not trusted (modders copy a Kunos one)
            var far = Kn5.FromFile(farPath);
            var collider = Kn5.FromFile(Path.Combine(car, models.Collider));
            var box = TrafficGeometry.Measure(far) ?? TrafficGeometry.Measure(full);
            var hull = TrafficGeometry.Measure(collider);
            var width = box is var (bMin, bMax) ? bMax.X - bMin.X : Width;
            var length = box is var (lMin, lMax) ? lMax.Z - lMin.Z : 0;
            if (hull is var (hMin, hMax) && hMax.X - hMin.X is > 1.0f and < 3.6f and var hullWidth && width - hullWidth > 0.35) width = hullWidth + 0.1f;

            var (mass, wheelRadius, name, author) = Facts(car);

            // Lamps, and the lift, off the geometry drawn up close: a LOD B drops small meshes, and names taken from the full model would name meshes CSP cannot find
            var lights = recipe.Lights?.ToDictionary(kv => kv.Key, kv => kv.Value.ToList()) ?? TrafficGeometry.Lights(TrafficGeometry.FindLamps(near));
            if (lights.Values.Sum(l => l.Count) == 0) notes.Add("no lamps found: it drives unlit");
            if (recipe.Lights != null)
            {
                var have = near.Nodes.Select(n => n.Name).ToHashSet(StringComparer.Ordinal);
                var gone = recipe.Lights.Values.SelectMany(l => l).Where(m => !have.Contains(m)).ToList();
                if (gone.Count > 0) notes.Add($"the near model has no mesh {string.Join(", ", gone)} (the recipe's lamps)");
            }

            var lift = 0f;
            if (TrafficGeometry.Lowest(near) is var (low, lowMesh) && Math.Abs(low) > 0.02f)
            {
                if (near.RootNode?.NodeClass == Kn5NodeClass.Base)
                {
                    lift = -low;
                    notes.Add($"lifted {lift:0.000} m onto the road (lowest mesh: {lowMesh})");
                }
                else
                {
                    notes.Add($"needs a {-low:0.000} m lift but its root is no dummy; left as it is");
                }
            }

            // The near model's textures: the full model's, capped and compressed
            cancel.ThrowIfCancellationRequested();
            var (capped, compressed) = (0, 0);
            foreach (var texture in full.TexturesData.Keys.ToList())
            {
                cancel.ThrowIfCancellationRequested();
                var (bytes, change) = TrafficTextures.Process(full.TexturesData[texture], maxTexture, _texels);
                full.TexturesData[texture] = bytes;
                if (full.Textures.TryGetValue(texture, out var entry)) entry.Length = bytes.Length;
                if (change == TrafficTextures.Change.Capped) capped++;
                else if (change == TrafficTextures.Change.Compressed) compressed++;
            }
            notes.Add($"{full.TexturesData.Count} texture(s): {capped} capped, {compressed} compressed to DXT");
            if (!ReferenceEquals(near, full))
            {
                near.Textures.Clear();
                near.TexturesData.Clear();
                foreach (var (texture, entry) in full.Textures) near.Textures[texture] = entry;
                foreach (var (texture, bytes) in full.TexturesData) near.TexturesData[texture] = bytes;
            }

            if (lift != 0)
            {
                Lift(near, lift);
                Lift(far, lift);
                Lift(collider, lift);
            }
            foreach (var body in near.Nodes.Where(n => n.NodeClass == Kn5NodeClass.Base && n.Name == "BODY").ToList())
            {
                body.Name = "HULL";
                notes.Add("its BODY dummy renamed HULL (the traffic tool would reset its rotation)");
            }

            // Paint: the recipe's, else found on the near model; the far model must have the same materials
            IReadOnlyList<string> paint;
            if (recipe.Paint != null)
            {
                paint = recipe.Paint;
            }
            else
            {
                var found = TrafficPaint.Find(near, _texels);
                paint = found.Paint;
                notes.Add($"paint: {(found.Paint.Count == 0 ? "none" : string.Join(", ", found.Paint))} ({found.Why})");
            }
            var farMaterials = far.Materials.Values.Select(m => m.Name).ToHashSet(StringComparer.Ordinal);
            if (paint.Where(p => !farMaterials.Contains(p)).ToList() is { Count: > 0 } missing) notes.Add($"the far model has no material {string.Join(", ", missing)}");

            var id = recipe.Id;
            Directory.CreateDirectory(folder);
            var colours = TrafficPaint.OemColours(_acRoot, recipe.Color);
            var skinsFolder = id + "_skins";
            var variants = TrafficPaint.Harvest(near, car, paint, Path.Combine(folder, skinsFolder + ".tmp"), skinsFolder, colours, maxTexture, recipe.Skins, id, _texels, cancel);

            var definition = new JsonObject
            {
                ["main"] = id + ".kn5",
                ["lod"] = id + "_lod.kn5",
                ["collider"] = id + "_collider.kn5",
                ["color"] = recipe.Color,
                ["maxSpeed"] = recipe.MaxSpeed,
                ["dynamic"] = recipe.Dynamic,
                ["chance"] = recipe.Chance,
                ["dimensions"] = new JsonObject
                {
                    ["front"] = Front,
                    ["rear"] = Rear,
                    ["turningOffset"] = TurningOffset,
                    ["width"] = Math.Round(width, 3),
                    ["wheelRadius"] = Math.Round(wheelRadius ?? WheelRadius, 3),
                    ["fakeShadowX"] = Math.Round(width / 2 + 0.05, 3),
                    ["fakeShadowZ"] = Math.Round(length > 0 ? length / 2 + 0.15 : 2.25 + 0.15, 3)
                },
                ["lights"] = JsonSerializer.SerializeToNode(lights),
                ["physics"] = new JsonObject
                {
                    ["mass"] = recipe.Mass ?? mass ?? Mass,
                    ["width"] = Math.Round(Math.Max(width - 0.1, 0.5), 3),
                    ["length"] = PhysicsLength,
                    ["wheelsGripForce"] = GripForce,
                    ["suspensionTravel"] = SuspensionTravel,
                    ["suspensionForce"] = SuspensionForce,
                    ["suspensionDamping"] = SuspensionDamping
                },
                ["paint"] = JsonSerializer.SerializeToNode(paint)
            };
            if (paint.Count > 0) definition["paintSlots"] = JsonSerializer.SerializeToNode(variants.Slots);
            if (variants.Skins.Count > 0)
            {
                definition["paintTextures"] = JsonSerializer.SerializeToNode(variants.Textures);
                definition["skins"] = JsonSerializer.SerializeToNode(variants.Skins.Select(s => new { name = s.Name, textures = s.Textures }));
                notes.Add($"{variants.Skins.Count} colour variant(s): {variants.Real} skin(s) of the car's, {variants.Synthetic} tinted");
            }
            notes.AddRange(variants.Skipped.Take(6).Select(s => "skin left out: " + s));
            // Credits: where the model came from, and whose it is. Read by nothing in CSP; kept for the player and the game
            definition["source"] = new JsonObject
            {
                ["car"] = carId,
                ["name"] = name,
                ["author"] = author,
                ["converter"] = Version,
                ["maxTexture"] = maxTexture,
                ["fingerprint"] = Fingerprint(car, models)
            };

            // Everything under temporary names, then renamed: a cancel or a full disk leaves no half model
            cancel.ThrowIfCancellationRequested();
            var written = new List<(string Temporary, string Final)>();
            try
            {
                void Save(IKn5 model, string file)
                {
                    var temporary = Path.Combine(folder, file + ".tmp");
                    model.Save(temporary);
                    written.Add((temporary, Path.Combine(folder, file)));
                }
                Save(near, id + ".kn5");
                Save(far, id + "_lod.kn5");
                Save(collider, id + "_collider.kn5");
                var json = Path.Combine(folder, id + ".json.tmp");
                File.WriteAllText(json, definition.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                written.Add((json, Path.Combine(folder, id + ".json")));

                var skinsFinal = Path.Combine(folder, skinsFolder);
                if (Directory.Exists(skinsFinal)) Directory.Delete(skinsFinal, true);
                if (Directory.Exists(Path.Combine(folder, skinsFolder + ".tmp"))) Directory.Move(Path.Combine(folder, skinsFolder + ".tmp"), skinsFinal);
                // The definition last: a model is there when its .json is
                foreach (var (temporary, final) in written.OrderBy(w => w.Final.EndsWith(".json", StringComparison.OrdinalIgnoreCase))) File.Move(temporary, final, overwrite: true);
            }
            catch
            {
                foreach (var (temporary, _) in written) TryDelete(temporary);
                TryDeleteFolder(Path.Combine(folder, skinsFolder + ".tmp"));
                throw;
            }

            var total = new[] { ".kn5", "_lod.kn5", "_collider.kn5", ".json" }.Sum(s => new FileInfo(Path.Combine(folder, id + s)).Length)
                + (Directory.Exists(Path.Combine(folder, skinsFolder)) ? Directory.EnumerateFiles(Path.Combine(folder, skinsFolder)).Sum(f => new FileInfo(f).Length) : 0);
            return new Result(Path.Combine(folder, id + ".json"), null, carId, notes, total);
        }

        /// <summary>What a model was made from: the car, its model files' sizes and times, the converter</summary>
        public static string Fingerprint(string carFolder, Models models)
        {
            var parts = new[] { models.Full, models.LodB, models.LodC, models.LodD, models.Collider }.OfType<string>()
                .Select(f => new FileInfo(Path.Combine(carFolder, f))).Select(f => $"{f.Name}:{f.Length}:{f.LastWriteTimeUtc.Ticks}");
            return $"v{Version}|{Path.GetFileName(carFolder)}|{string.Join("|", parts)}";
        }

        /// <summary>The far model: the LOD C, else the LOD D, when it is a real far model</summary>
        private static (string? Path, string Label) Far(string car, Models models)
        {
            foreach (var (file, label) in new[] { (models.LodC, "LOD C"), (models.LodD, "LOD D") })
            {
                if (file == null) continue;
                var path = Path.Combine(car, file);
                if (new FileInfo(path).Length < FarMostBytes && Textured(path)) return (path, $"{label} ({file})");
            }
            return (null, "");
        }

        /// <summary>Whether a LOD carries real materials (a Content Manager LOD D of before 2026-09 is one mesh with none)</summary>
        private static bool Textured(string path)
        {
            try
            {
                var lod = Kn5.FromFile(path, SkippingTextureLoader.Instance);
                var materials = lod.Materials.Values.ToList();
                return materials.Count > 0 && materials.Count(m => m.TextureMappings is { Length: > 0 }) >= Math.Max(1, materials.Count / 2);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException or ArgumentException or NotSupportedException)
            {
                return false;
            }
        }

        /// <summary>The near model's geometry: the LOD B when it is the half-detail model its name says, else the full model</summary>
        private static (string Path, string Label) Near(string car, Models models, IKn5 full)
        {
            var fullPath = Path.Combine(car, models.Full!);
            if (models.LodB == null) return (fullPath, "the full model (no LOD B)");
            var path = Path.Combine(car, models.LodB);
            IKn5 lodB;
            try
            {
                lodB = Kn5.FromFile(path, SkippingTextureLoader.Instance);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException or ArgumentException or NotSupportedException)
            {
                return (fullPath, $"the full model ({models.LodB} does not read: {ex.Message})");
            }

            var vertices = TrafficGeometry.Vertices(lodB);
            if (lodB.Materials.Count == 0 || vertices < NearLeastVertices) return (fullPath, $"the full model ({models.LodB}: {vertices} vertices, {lodB.Materials.Count} materials)");
            if (models.LodC != null)
            {
                try
                {
                    var c = TrafficGeometry.Vertices(Kn5.FromFile(Path.Combine(car, models.LodC), SkippingTextureLoader.Instance));
                    // Half the far model's vertices means a far model, whatever its name; merely lighter than the LOD C is
                    // normal on hand-made LODs (the DS21's LOD C carries the interior its LOD B drops)
                    if (vertices < 0.5 * c) return (fullPath, $"the full model ({models.LodB} has {vertices} vertices, half the LOD C's {c})");
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException or ArgumentException or NotSupportedException)
                {
                }
            }
            var fullVertices = TrafficGeometry.Vertices(full);
            if (fullVertices > 0 && vertices >= fullVertices) return (fullPath, $"the full model ({models.LodB} is no lighter: {vertices} of {fullVertices} vertices)");
            return (path, $"{models.LodB} ({vertices} vertices)");
        }

        /// <summary>The car's mass and front wheel radius from its data (data.acd too), its name and author from its ui_car.json</summary>
        private static (double? Mass, double? WheelRadius, string? Name, string? Author) Facts(string car)
        {
            double? mass = null, radius = null;
            try
            {
                var data = AcCarDataReader.ForCar(car);
                if (data("car.ini") is { } carIni) mass = new IniText(carIni).GetNumber("BASIC", "TOTALMASS");
                if (data("tyres.ini") is { } tyres) radius = new IniText(tyres).GetNumber("FRONT", "RADIUS");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
            }

            string? name = null, author = null;
            try
            {
                var ui = Path.Combine(car, "ui", "ui_car.json");
                if (File.Exists(ui))
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(ui).TrimStart('﻿'), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
                    if (document.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        name = document.RootElement.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
                        author = document.RootElement.TryGetProperty("author", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() : null;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
            catch (JsonException)
            {
                // Kunos' own are often not strict JSON (raw line breaks and tabs in the description): the two fields by hand
                try
                {
                    var text = File.ReadAllText(Path.Combine(car, "ui", "ui_car.json"));
                    name = Regex.Match(text, @"""name""\s*:\s*""([^""]*)""") is { Success: true } n ? n.Groups[1].Value : null;
                    author = Regex.Match(text, @"""author""\s*:\s*""([^""]*)""") is { Success: true } a ? a.Groups[1].Value : null;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }
            return (mass is > 0 ? mass : null, radius is > 0.1 and < 1.5 ? radius : null, name, author);
        }

        /// <summary>The whole model raised by <paramref name="lift"/> metres: its root dummy's place, as the traffic tool draws from it</summary>
        private static void Lift(IKn5 model, float lift)
        {
            if (model.RootNode is not { NodeClass: Kn5NodeClass.Base } root) return;
            var m = root.Transform;
            m.M42 += lift;
            root.Transform = m;
        }

        private static void TryDelete(string file)
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        private static void TryDeleteFolder(string folder)
        {
            try
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
