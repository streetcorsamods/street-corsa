using System.IO;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using AcTools.Kn5File;

namespace StreetCorsa.Services.Race.Traffic.Conversion
{
    /// <summary>
    /// Which materials of a traffic model are its paint, and how each takes a colour: ported from Race Explorer's
    /// <c>kn5paint.py</c> and <c>kn5skins.py</c> (2026-09-07).
    /// <para>
    /// CSP's traffic tool tints only <c>ksPerPixelMultiMap_damage_dirt</c>, the Kunos paint shader, so a Kunos car needs
    /// nothing. A mod car mostly paints with a plain multimap or per-pixel shader whose diffuse is the colour, and spawned
    /// in one baked colour (73 of the 99 models of the first fleet). The paint is the eligible material (a paint-capable
    /// shader, not named like glass, tyres, chrome, lamps or the interior in any of the modders' languages) with the
    /// largest exterior area and one-paint texels where its meshes sample its diffuse (a livery, two-tone or many-hued,
    /// stays as it is); with every other material sharing that texture.
    /// </para>
    /// <para>
    /// Most mods paint as Kunos does: the diffuse a grey occlusion map and the colour a tiled detail texture. Such a
    /// material takes its colour in <c>txDetail</c>, shading kept and no files. One whose diffuse IS the colour takes
    /// variants of it: the mod's own one-colour skins, else the body map recoloured, its luminance kept and its hue
    /// swapped for colours from CSP's OEM lists.
    /// </para>
    /// </summary>
    public static class TrafficPaint
    {
        /// <summary>Fewer usable skins of the mod than this, and tints fill up to <see cref="Target"/></summary>
        public const int LeastReal = 4;

        /// <summary>The colour variants a model ends up with (its own texture is one more)</summary>
        public const int Target = 8;

        /// <summary>The most skins of the mod kept (each is one texture a body map)</summary>
        public const int MostReal = 12;

        private static readonly HashSet<string> PaintShaders = new(StringComparer.Ordinal)
        {
            "ksPerPixelMultiMap", "ksPerPixelMultiMap_NMDetail", "ksPerPixelMultiMap_AT", "ksPerPixelMultiMap_AT_NMDetail",
            "ksPerPixelMultiMap_emissive", "ksPerPixelNM", "ksPerPixelNM_UVMult", "ksPerPixel", "ksPerPixelReflection", "ksPerPixelAT",
            "ksPerPixelAT_NM", "ksMultilayer", "ksMultilayer_fresnel_nm", "ksPerPixelSimpleRefl", "ksPerPixelMultiMap_damage",
            "ksPerPixelMultiMap_damage_dirt_sunspot"
        };

        // Never paint, whatever else the name says. (Not "hood", "chassis" or "ao": Forza conversions call the body
        // "cockpit_hooda_body", Russian mods "chasiss", and body_ao.dds is the usual name of a paint map.)
        private static readonly Regex HardNot = new(
            @"glass|glas|vetr|vidr|cristal|window|windscreen|windshield|tyre|tire|pneu|neum|gomm|rubber|wheel|rim\b|rims|llanta|cerch|" +
            @"chrome|chrom|crom|inox|light|lamp|farol|lanter|fanal|faro|luz|luce|led\b|lens|plate|targa|patente|placa|matricul|license|" +
            @"licence|mirror|specchio|espejo|grill|griglia|rejilla|exhaust|escape|scarico|brake|disc|caliper|shadow|ombra|sombra|dirt|" +
            @"damage|dust|wiper|antenna|interior|interno|cockpit_int|seat|sedil|asiento|dash|cruscot|tablero|carpet|rug\b|fabric|cloth|" +
            @"leather|cuero|pelle|headlin|belt|under|unter|sotto|bajo|baixo|floor|fondo|piso|engine|motor|dvig|mecan|mechan|susp|axle|" +
            @"shaft|number|num\b|decal|sticker|livery|logo|badge|emblem|backfac|steer|volante|pedal|console|tread|blur",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex DiffuseNot = new(
            @"glass|tread|tyre|tire|chrome|\brim|wheel|light|lamp|plate|interior|carpet|under|mirror|blank|grid|engine|dvig|leather|cloth|fabric",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Not paint unless the name also says so (a "black" body exists, a "black" bumper too)
        private static readonly Regex SoftNot = new(
            @"black|blk|nero|negro|noir|schwarz|dark|plastic|plast|trim|molding|moldura|metal|steel|alu|carbon|carbonio|fibra|misc|" +
            @"detail|normal|_nm\b|alpha|transp|blank|null|none|int_|inner|bolt|screw|nut|spring|cable|wire|hose|pipe|tank|handle|" +
            @"maniglia|manija|hinge|net\b|mesh|grid|chassis|chasis|chasiss|telaio|frame|bumper|paraurti|parachoque",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex PaintWords = new(
            @"paint|body|carrozz|carroc|karos|\black\b|(?<!b)lack\b|vernic|pintur|coach|shell|panel|bonnet|hood|cofano|capo|door|porta|" +
            @"puerta|tuer|fender|wing|parafang|guardab|roof|tetto|techo|dach|trunk|boot|baule|maletero|skin|\bext\b|ext_|_ext|exterior|" +
            @"carpaint|car_paint|\bcab\b|cabina|main|primary|color|colour|cor\b|lory|lorry",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // The inside of the car, even when its material is named blandly
        private static readonly Regex InteriorMesh = new(
            @"interior|\bint_|_int\b|seat|dash|steer|carpet|\brug\b|headlin|pedal|console|floor|under|engine|motor|mech_|chassis_gmt|" +
            @"door_.*_int|boot_.*_int|cockpit_(?!hood)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private const double DarkTrim = 30;        // mean luminance under this, on a material with no paint word: trim
        private const double PreferNamed = 0.30;   // a paint-named material this share of the top area wins over it
        private const double LeastShare = 0.12;    // a second paint material carries this share of the body's area
        private const double TwoTone = 0.2;        // a police black-and-white: this share dark and this share bright
        private const double ChromaSpread = 0.18;  // hue spread beyond this: decals, a livery
        private const double DetailChroma = 0.25;  // a diffuse less colourful than this is an occlusion map: txDetail carries the paint

        /// <summary>The colour of the texels a mesh uses, sampled where its vertices are</summary>
        public sealed record Stats(Vector3 Mean, double Luminance, double Chroma, double ChromaSpread, double Dark, double Bright)
        {
            /// <summary>Why these texels are a livery rather than one paint; null when they are one paint</summary>
            public string? Livery =>
                Dark > TwoTone && Bright > TwoTone ? $"two-tone ({Dark * 100:0}% dark, {Bright * 100:0}% bright)"
                : ChromaSpread > TrafficPaint.ChromaSpread ? $"several hues (chroma spread {ChromaSpread:0.00})" : null;
        }

        /// <summary>A material's share of the outside of the car</summary>
        private sealed class Surface
        {
            public double Area;
            public string Shader = "";
            public string Diffuse = "";
            public readonly List<Vector2> Uvs = [];
            public string Skip = "";
            public Stats? Stats;
            public bool Sampled;
        }

        /// <summary>The paint found: the materials, and why (for the log)</summary>
        public sealed record Found(IReadOnlyList<string> Paint, string Why);

        /// <summary>Whether a word is one the name checks take for what is never paint (glass, a seat, the underside): a car's own name may hold one</summary>
        public static bool IsNotPaintWord(string word) => HardNot.IsMatch(word) || DiffuseNot.IsMatch(word) || InteriorMesh.IsMatch(word);

        /// <summary>The model's paint materials (none when it paints with the Kunos shader, which the tool tints itself)</summary>
        /// <param name="carWords">The words of the car's own name that would read as a part (<see cref="TrafficGeometry.CarWords"/>)</param>
        public static Found Find(IKn5 model, ITexels texels, IReadOnlyList<string>? carWords = null)
        {
            if (model.Materials.Values.Any(m => m.ShaderName?.Contains("damage_dirt", StringComparison.Ordinal) == true))
                return new Found([], "the Kunos paint shader: the traffic tool tints it itself");

            var surfaces = Surfaces(model, carWords);
            if (surfaces.Count == 0) return new Found([], "no exterior meshes");

            foreach (var (name, s) in surfaces) s.Skip = Eligible(name, s, carWords);
            var ranked = surfaces.OrderByDescending(kv => kv.Value.Area).ToList();

            Stats? StatsOf(Surface s)
            {
                if (!s.Sampled)
                {
                    s.Sampled = true;
                    s.Stats = model.TexturesData.TryGetValue(s.Diffuse, out var bytes) ? Sample(bytes, s.Uvs, texels) : null;
                }
                return s.Stats;
            }

            // The largest eligible material of one paint is the body, unless one that says paint or body is nearly as
            // large: mods bury the body under a big black trim or "frame" material, and a name is a stronger sign
            (string Name, Surface Surface)? top = null;
            var topArea = 0.0;
            foreach (var (name, s) in ranked)
            {
                if (s.Skip.Length > 0) continue;
                var st = StatsOf(s);
                if (st == null)
                {
                    s.Skip = "texture unreadable";
                    continue;
                }
                if (st.Livery is { } livery)
                {
                    s.Skip = "livery: " + livery;
                    continue;
                }
                if (st.Luminance < DarkTrim && !Named(name, s))
                {
                    s.Skip = "dark, unnamed: trim";
                    continue;
                }
                if (top == null)
                {
                    top = (name, s);
                    topArea = s.Area;
                    if (Named(name, s)) break;
                    continue;
                }
                if (s.Area < topArea * PreferNamed) break;
                if (Named(name, s))
                {
                    top = (name, s);
                    break;
                }
            }
            if (top is not var (bodyName, body)) return new Found([], "no exterior material of one paint");

            var paint = new List<string> { bodyName };
            foreach (var (name, s) in ranked)
            {
                if (name == bodyName || s.Skip.Length > 0) continue;
                if (s.Diffuse.Equals(body.Diffuse, StringComparison.OrdinalIgnoreCase))
                {
                    paint.Add(name);
                    continue;
                }
                if (s.Area < body.Area * LeastShare || !Named(name, s) || SoftNot.IsMatch(name)) continue;
                if (StatsOf(s) is { Livery: null } st && st.Luminance >= DarkTrim) paint.Add(name);
            }
            return new Found(paint, $"body {bodyName} ({body.Shader}, {body.Area:0.0} m², diffuse {body.Diffuse})");
        }

        private static bool Named(string name, Surface s) => PaintWords.IsMatch(name) || PaintWords.IsMatch(s.Diffuse);

        private static string Eligible(string name, Surface s, IReadOnlyList<string>? carWords)
        {
            if (!PaintShaders.Contains(s.Shader)) return "shader";
            // Without the car's own name: a Thunderbird's "thunderbird_body" is not "under", a SEAT's "seat600_body" no seat
            if (HardNot.IsMatch(TrafficGeometry.Plain(name, carWords))) return "name";
            if (DiffuseNot.IsMatch(TrafficGeometry.Plain(s.Diffuse, carWords))) return "texture name";
            if (SoftNot.IsMatch(name) && !Named(name, s)) return "name (soft)";
            if (s.Diffuse.Length == 0) return "no diffuse";
            return "";
        }

        /// <summary>Each material's outside: its area over the meshes seen, and the UVs those meshes sample</summary>
        private static Dictionary<string, Surface> Surfaces(IKn5 model, IReadOnlyList<string>? carWords = null)
        {
            var surfaces = new Dictionary<string, Surface>(StringComparer.Ordinal);
            foreach (var mesh in TrafficGeometry.Meshes(model))
            {
                if (!mesh.Node.Active || !mesh.Node.IsRenderable || InteriorMesh.IsMatch(TrafficGeometry.Plain(mesh.Node.Name, carWords))
                    || mesh.Material is not { } material) continue;
                var name = material.Name ?? "";
                if (name.Length == 0) continue;
                if (!surfaces.TryGetValue(name, out var s))
                {
                    surfaces[name] = s = new Surface
                    {
                        Shader = material.ShaderName ?? "",
                        Diffuse = material.GetMappingByName("txDiffuse")?.Texture ?? ""
                    };
                }

                var vertices = mesh.Node.Vertices;
                var indices = mesh.Node.Indices ?? [];
                var positions = vertices.Select(v => Vector3.Transform(new Vector3(v.Position.X, v.Position.Y, v.Position.Z), mesh.World)).ToArray();
                for (var i = 0; i + 2 < indices.Length; i += 3)
                {
                    if (indices[i] >= positions.Length || indices[i + 1] >= positions.Length || indices[i + 2] >= positions.Length) continue;
                    var a = positions[indices[i]];
                    s.Area += Vector3.Cross(positions[indices[i + 1]] - a, positions[indices[i + 2]] - a).Length() / 2;
                }
                if (s.Uvs.Count < 6000)
                {
                    var step = Math.Max(1, vertices.Length / 2000);
                    for (var i = 0; i < vertices.Length; i += step) s.Uvs.Add(new Vector2(vertices[i].Tex.X, vertices[i].Tex.Y));
                }
            }
            return surfaces;
        }

        /// <summary>UVs over the whole texture, for a texture no mesh says where to look at</summary>
        private static List<Vector2> Grid()
        {
            const int n = 40;
            var uvs = new List<Vector2>(n * n);
            for (var i = 0; i < n; i++)
            for (var j = 0; j < n; j++) uvs.Add(new Vector2((i + 0.5f) / n, (j + 0.5f) / n));
            return uvs;
        }

        /// <summary>
        /// The colour of the texels a material's meshes use, sampled at their UVs (KN5 UVs are DirectX's, v down from the
        /// top, as a DDS is): a body map is a white car on a black background, and the background is never drawn
        /// </summary>
        public static Stats? Sample(byte[] texture, IReadOnlyList<Vector2> uvs, ITexels texels)
        {
            if (texels.Read(texture, 256) is not var (w, h, rgba)) return null;
            if (uvs.Count == 0) uvs = Grid();
            var step = Math.Max(1, uvs.Count / 1500);
            var samples = new List<(double R, double G, double B)>();
            for (var i = 0; i < uvs.Count; i += step)
            {
                var u = uvs[i].X - Math.Floor(uvs[i].X);
                var v = uvs[i].Y - Math.Floor(uvs[i].Y);
                var x = Math.Min(w - 1, (int)(u * w));
                var y = Math.Min(h - 1, (int)(v * h));
                var at = (y * w + x) * 4;
                samples.Add((rgba[at], rgba[at + 1], rgba[at + 2]));
            }
            if (samples.Count == 0) return null;

            var luminance = samples.Select(p => 0.299 * p.R + 0.587 * p.G + 0.114 * p.B).ToList();
            // Chroma: the distance from grey, scaled by brightness so a dark red and a bright red read alike; its spread
            // tells a livery (several hues) from one paint
            var chroma = samples.Select(p =>
            {
                var max = Math.Max(p.R, Math.Max(p.G, p.B));
                var min = Math.Min(p.R, Math.Min(p.G, p.B));
                return max > 8 ? (max - min) / max : 0;
            }).ToList();
            var meanChroma = chroma.Average();
            var spread = Math.Sqrt(chroma.Average(c => (c - meanChroma) * (c - meanChroma)));
            var mean = new Vector3((float)samples.Average(p => p.R), (float)samples.Average(p => p.G), (float)samples.Average(p => p.B));
            return new Stats(mean, luminance.Average(), Math.Round(meanChroma, 2), Math.Round(spread, 2),
                luminance.Count(l => l < 60) / (double)samples.Count, luminance.Count(l => l > 180) / (double)samples.Count);
        }

        /// <summary>One colour variant of a model: its name, and the file of each body map it swaps (relative to the model's folder)</summary>
        public sealed record Skin(string Name, Dictionary<string, string> Textures);

        /// <summary>How a model takes its colours: each paint material's slot, the body maps of those painted in their diffuse, the variants</summary>
        public sealed record Variants(Dictionary<string, string> Slots, Dictionary<string, string> Textures, List<Skin> Skins, int Real, int Synthetic, List<string> Skipped);

        /// <summary>
        /// The paint plan for one model, and its diffuse variants on disk, in <paramref name="outFolder"/> (named in the
        /// JSON as <paramref name="relative"/>/&lt;file&gt;): each paint material's slot; for those painted in their
        /// diffuse, the mod's own one-colour skins (from <paramref name="carFolder"/>'s <c>skins</c>), and tints of the
        /// body map when the mod has fewer than <see cref="LeastReal"/> (<paramref name="synthetic"/>: null as that says,
        /// 0 never, N that many)
        /// </summary>
        public static Variants Harvest(IKn5 model, string? carFolder, IReadOnlyList<string> paint, string outFolder, string relative, IReadOnlyList<Vector3> colours,
            int maxSide, int? synthetic, string seed, ITexels texels, CancellationToken cancel = default, IReadOnlyList<string>? carWords = null)
        {
            var result = new Variants([], [], [], 0, 0, []);
            // A file name of its own for every variant: two skins may come to one slug ("Red-1" and "red_1", or two long
            // names alike in their first 32 letters), and the second would be written over the first
            var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string Unique(string stem)
            {
                var name = stem;
                for (var n = 2; !written.Add(name); n++) name = $"{stem}_{n}";
                return name + ".dds";
            }

            if (Directory.Exists(outFolder)) Directory.Delete(outFolder, true);
            if (paint.Count == 0) return result;

            var surfaces = Surfaces(model, carWords);
            var diffuseMaterials = new Dictionary<string, (string Diffuse, List<Vector2> Uvs)>(StringComparer.Ordinal);
            foreach (var material in model.Materials.Values.Where(m => paint.Contains(m.Name)))
            {
                var diffuse = material.GetMappingByName("txDiffuse")?.Texture ?? "";
                var uvs = surfaces.TryGetValue(material.Name, out var s) ? s.Uvs : [];
                var chroma = model.TexturesData.TryGetValue(diffuse, out var bytes) ? Sample(bytes, uvs, texels)?.Chroma : null;
                var detailOn = material.GetMappingByName("txDetail") != null && (material.GetPropertyByName("useDetail")?.ValueA ?? 0) > 0
                    && material.ShaderName?.Contains("MultiMap", StringComparison.Ordinal) == true;
                var slot = detailOn && chroma < DetailChroma ? "txDetail" : "txDiffuse";
                result.Slots[material.Name] = slot;
                if (slot == "txDiffuse" && diffuse.Length > 0) diffuseMaterials[material.Name] = (diffuse, uvs);
            }
            if (diffuseMaterials.Count == 0) return result;

            foreach (var (material, (diffuse, _)) in diffuseMaterials) result.Textures[material] = diffuse;
            var wanted = diffuseMaterials.Values.Select(d => d.Diffuse).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList();
            var uvsOf = new Dictionary<string, List<Vector2>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (diffuse, uvs) in diffuseMaterials.Values)
            {
                if (!uvsOf.TryGetValue(diffuse, out var list)) uvsOf[diffuse] = list = [];
                list.AddRange(uvs);
            }
            var seen = wanted.Select(t => Digest(model.TexturesData.GetValueOrDefault(t) ?? [])).ToHashSet();
            Directory.CreateDirectory(outFolder);
            var real = 0;
            var tinted = 0;

            // 1. The mod's own skins
            var skins = carFolder == null ? null : Path.Combine(carFolder, "skins");
            if (skins != null && Directory.Exists(skins))
            {
                foreach (var folder in Directory.EnumerateDirectories(skins).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
                {
                    cancel.ThrowIfCancellationRequested();
                    if (real >= MostReal) break;
                    var skin = Path.GetFileName(folder);
                    var files = Directory.EnumerateFiles(folder).ToDictionary(f => Path.GetFileName(f), f => f, StringComparer.OrdinalIgnoreCase);
                    var entry = new Skin(skin, []);
                    var why = "";
                    foreach (var texture in wanted)
                    {
                        if (!files.TryGetValue(texture, out var file)) continue;
                        var bytes = File.ReadAllBytes(file);
                        var digest = Digest(bytes);
                        if (seen.Contains(digest))
                        {
                            why = "the same texture as one already kept";
                            continue;
                        }
                        if (Sample(bytes, uvsOf.GetValueOrDefault(texture) ?? [], texels) is not { } st)
                        {
                            why = $"{Path.GetFileName(file)} does not read";
                            continue;
                        }
                        if (st.Livery is { } livery)
                        {
                            why = "livery: " + livery;
                            continue;
                        }
                        if (texels.Read(bytes, maxSide) is not var (w, h, rgba))
                        {
                            why = $"{Path.GetFileName(file)} does not decode";
                            continue;
                        }
                        var name = Unique($"{Slug(skin)}_{Slug(Path.GetFileNameWithoutExtension(texture))}");
                        File.WriteAllBytes(Path.Combine(outFolder, name), TrafficTextures.EncodeDxt(rgba, w, h));
                        entry.Textures[texture] = relative + "/" + name;
                        seen.Add(digest);
                    }
                    if (entry.Textures.Count > 0)
                    {
                        result.Skins.Add(entry);
                        real++;
                    }
                    else if (why.Length > 0)
                    {
                        result.Skipped.Add($"{skin}: {why}");
                    }
                }
            }

            // 2. Tints of the model's own body map
            var count = synthetic ?? (real < LeastReal ? Math.Max(0, Target - real) : 0);
            if (count > 0)
            {
                var bases = new Dictionary<string, (int W, int H, byte[] Rgba, double Luminance)>(StringComparer.OrdinalIgnoreCase);
                foreach (var texture in wanted)
                {
                    if (!model.TexturesData.TryGetValue(texture, out var bytes) || texels.Read(bytes, maxSide) is not var (w, h, rgba))
                    {
                        result.Skipped.Add($"tint: {texture} does not decode");
                        continue;
                    }
                    var luminance = Sample(bytes, uvsOf.GetValueOrDefault(texture) ?? [], texels)?.Luminance ?? 128;
                    bases[texture] = (w, h, rgba, luminance);
                }
                if (bases.Count > 0)
                {
                    foreach (var colour in Palette(colours, count, seed))
                    {
                        cancel.ThrowIfCancellationRequested();
                        var tag = $"tint_{(int)colour.X:x2}{(int)colour.Y:x2}{(int)colour.Z:x2}";
                        var entry = new Skin(tag, []);
                        foreach (var (texture, (w, h, rgba, luminance)) in bases)
                        {
                            var name = Unique($"{tag}_{Slug(Path.GetFileNameWithoutExtension(texture))}");
                            File.WriteAllBytes(Path.Combine(outFolder, name), TrafficTextures.EncodeDxt(Tint(rgba, luminance, colour), w, h));
                            entry.Textures[texture] = relative + "/" + name;
                        }
                        result.Skins.Add(entry);
                        tinted++;
                    }
                }
            }

            if (result.Skins.Count == 0)
            {
                try
                {
                    Directory.Delete(outFolder);
                }
                catch (IOException)
                {
                }
            }
            return result with { Real = real, Synthetic = tinted };
        }

        /// <summary>
        /// The body map recoloured: its luminance against the paint's own drives the new colour, so shading, occlusion
        /// and badges stay; what is brighter than the paint (chrome, lamps drawn on the same map) adds a grey highlight
        /// </summary>
        public static byte[] Tint(byte[] rgba, double paintLuminance, Vector3 colour)
        {
            var output = (byte[])rgba.Clone();
            var reference = Math.Max(paintLuminance, 60);
            for (var i = 0; i < rgba.Length; i += 4)
            {
                var l = rgba[i] * 0.299 + rgba[i + 1] * 0.587 + rgba[i + 2] * 0.114;
                var b = Math.Clamp(l / reference, 0, 1);
                var highlight = Math.Clamp(l - reference, 0, 255) * 0.6;
                output[i] = (byte)Math.Clamp(colour.X * b + highlight, 0, 255);
                output[i + 1] = (byte)Math.Clamp(colour.Y * b + highlight, 0, 255);
                output[i + 2] = (byte)Math.Clamp(colour.Z * b + highlight, 0, 255);
            }
            return output;
        }

        /// <summary>
        /// The colours of CSP's OEM list (<c>extension\config\data_oem_colors_modern.txt</c> or <c>_vintage.txt</c>: a
        /// colour a line, as hex); a handful of its own when the list is not there
        /// </summary>
        public static List<Vector3> OemColours(string acRoot, string kind)
        {
            var file = Path.Combine(acRoot, "extension", "config", kind == "modern" ? "data_oem_colors_modern.txt" : "data_oem_colors_vintage.txt");
            var colours = new List<Vector3>();
            try
            {
                foreach (var line in File.ReadLines(file))
                {
                    var m = Regex.Match(line.Trim(), "^#?([0-9A-Fa-f]{6})");
                    if (!m.Success) continue;
                    var v = Convert.ToInt32(m.Groups[1].Value, 16);
                    colours.Add(new Vector3((v >> 16) & 255, (v >> 8) & 255, v & 255));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
            return colours.Count > 0 ? colours :
            [
                new(255, 255, 255), new(30, 30, 30), new(180, 20, 20), new(20, 40, 120), new(200, 180, 80), new(60, 110, 60), new(150, 150, 150), new(120, 70, 40)
            ];
        }

        /// <summary>
        /// <paramref name="n"/> colours spread over a slice of the list, chosen by the seed (the model's id), so every model
        /// has its own and a rebuild gives the same: one at random, then each the farthest from those already chosen
        /// </summary>
        public static List<Vector3> Palette(IReadOnlyList<Vector3> pool, int n, string seed)
        {
            var random = new Random(BitConverter.ToInt32(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(seed)), 0));
            var list = pool.ToList();
            if (list.Count > 80) list = list.OrderBy(_ => random.Next()).Take(80).ToList();
            if (list.Count == 0) return [];
            var chosen = new List<Vector3> { list[random.Next(list.Count)] };
            while (chosen.Count < Math.Min(n, list.Count))
                chosen.Add(list.MaxBy(c => chosen.Min(d => Vector3.DistanceSquared(c, d))));
            return chosen;
        }

        private static string Digest(byte[] bytes) => Convert.ToHexString(MD5.HashData(bytes));

        private static string Slug(string text)
        {
            var slug = Regex.Replace(text, "[^A-Za-z0-9]+", "_").Trim('_').ToLowerInvariant();
            return slug.Length == 0 ? "skin" : slug.Length > 32 ? slug[..32] : slug;
        }
    }
}
