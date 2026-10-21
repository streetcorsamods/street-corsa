using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StreetCorsa.Helpers;
using StreetCorsa.Mods;

namespace StreetCorsa.Parts;

/// <summary>
/// All parts of the mods (their parts folders and their mod.json's packs), with the fittings indexed: a slot says what
/// it fits and what it takes, the catalog answers what can be mounted on a given slot.
/// </summary>
public sealed class PartsCatalog
{
    private const int MaxAliasHops = 8;

    private readonly string _root;
    private readonly Dictionary<string, PartDefinition> _parts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _aliases = new(StringComparer.OrdinalIgnoreCase);

    private readonly Lazy<MatingIndex> _mating;

    private readonly Dictionary<string, string> _buildAliases = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _buildIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _problems = new();

    private PartsCatalog(string root)
    {
        _root = root;
        _mating = new Lazy<MatingIndex>(() => new MatingIndex(this));
    }

    public IReadOnlyDictionary<string, PartDefinition> Parts => _parts;

    /// <summary>The base game's parts folder: where the slot shifts are written</summary>
    public string Root => _root;

    /// <summary>Complete engines as part lists: what the cars left the factory with, rated builds, kits and the mods' builds</summary>
    public IReadOnlyList<EngineBuild> EngineBuilds { get; private set; } = Array.Empty<EngineBuild>();

    /// <summary>What has to come off an engine before what, and what only comes off on the stand</summary>
    public WorkOrder WorkOrder { get; internal set; } = WorkOrder.Empty;

    /// <summary>The work a part can have done on it to take its mileage off: kits and exchange units</summary>
    public PartRebuilds Rebuilds { get; internal set; } = PartRebuilds.Empty;

    /// <summary>A catalog without parts, for when the parts under the folder cannot be read</summary>
    public static PartsCatalog Empty(string root) => new(root);

    /// <summary>What could not be read, or was left out and why: one line each, for the log</summary>
    public IReadOnlyList<string> Problems => _problems;

    /// <summary>The base game's parts alone, from one folder (tests, tools)</summary>
    public static PartsCatalog Load(string root) =>
        Load(new[] { (new ModInfo { Id = ModIds.BaseId, Name = "Street Corsa" }, root) });

    /// <summary>
    /// The credits the packs' licences ask for (<see cref="PartPack.Credit"/>), each once, in load order: read off the
    /// pack files alone, for the credits screen, which has no call for the catalog. A pack that does not read is
    /// skipped; the catalog's load tells of it.
    /// </summary>
    public static IReadOnlyList<string> PackCredits(IEnumerable<(ModInfo Mod, string Dir)> roots)
    {
        var credits = new List<string>();
        foreach (var (mod, root) in roots)
        {
            if (Directory.Exists(root))
            {
                foreach (var file in Directory.EnumerateFiles(root, PartPack.FileName, SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                {
                    try
                    {
                        Add(JsonConvert.DeserializeObject<PackCredit>(File.ReadAllText(file))?.Credit);
                    }
                    catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
                    {
                    }
                }
            }

            try
            {
                if (mod.Section(ModSections.Packs) is { } packs)
                    foreach (var pack in JsonConvert.DeserializeObject<List<PackCredit?>>(packs) ?? new()) Add(pack?.Credit);
            }
            catch (Exception ex) when (IsBadSection(ex))
            {
            }
        }
        return credits;

        void Add(string? credit)
        {
            if (!string.IsNullOrWhiteSpace(credit) && !credits.Contains(credit.Trim(), StringComparer.OrdinalIgnoreCase)) credits.Add(credit.Trim());
        }
    }

    private sealed class PackCredit
    {
        [JsonProperty("credit")]
        public string? Credit { get; set; }
    }

    /// <summary>The parts of every mod that has any, in load order; the first folder is the base game's</summary>
    /// <remarks>
    /// Every mod's parts folder is laid out as the base game's: packs (a pack.json with its models) anywhere under
    /// it, and next to them <see cref="EngineBuild.FileName"/>, <see cref="PartPack.AliasesFileName"/> and
    /// <see cref="EngineBuild.AliasesFileName"/>, each about the mod's own parts and builds. Its mod.json may hold the
    /// same (<see cref="ModSections"/>: packs, builds, part and build aliases), read after the files; a pack of mod.json
    /// has its models in the parts folder under its id. A mod's ids get its namespace, and what its builds and aliases
    /// name is found in the mod first, then in the base game (<see cref="ModIds.Resolve"/>). Parts mate by fittings
    /// alone, which name no part.
    /// A pack that does not read is a pack that is not there, and a part without an id is no part: each is one line
    /// in <see cref="Problems"/>, and the rest of the catalog stands (one bad pack.json used to cost every car its parts).
    /// </remarks>
    public static PartsCatalog Load(IReadOnlyList<(ModInfo Mod, string Dir)> roots)
    {
        var baseRoot = roots.FirstOrDefault(r => r.Mod.IsBase).Dir ?? roots.FirstOrDefault().Dir ?? string.Empty;
        var catalog = new PartsCatalog(baseRoot);
        var registry = new ContentRegistry<PartDefinition>("part");
        var builds = new ContentRegistry<EngineBuild>("engine build");
        var aliases = new List<(ModInfo Mod, string Gone, string Current)>();
        var buildAliases = new List<(ModInfo Mod, string Gone, string Current)>();
        var models = new List<(ModInfo Mod, string Root, string Label, string Part, string Model)>();

        // The classes in use are the one source: what PartKinds.Is goes by (the installed mods' files, read once; a tool
        // or a test sets its own before it loads a catalog), so a class the game would not know is the one told of
        var classes = PartClasses.Current;

        foreach (var (mod, root) in roots)
        {
            var label = mod.IsBase ? string.Empty : mod.Id + "/";
            if (Directory.Exists(root))
            {
                foreach (var file in PackFiles(catalog, root, label))
                {
                    var packName = label + Path.GetRelativePath(root, file);
                    try
                    {
                        AddPack(JsonConvert.DeserializeObject<PartPack>(File.ReadAllText(file)), Path.GetDirectoryName(file), packName);
                    }
                    catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
                    {
                        catalog._problems.Add($"{packName}: {ex.Message}; its parts are left out");
                    }
                }

                ReadAliasFile(catalog, root, PartPack.AliasesFileName, label, mod, aliases, "parts that changed ids are not found by their old ones");
                ReadAliasFile(catalog, root, EngineBuild.AliasesFileName, label, mod, buildAliases, "builds that changed ids are not found by their old ones");
                ReadModels(catalog, root, label, mod, models);
                ReadBuilds(catalog, root, label, mod, builds);
            }

            // What the mod's mod.json holds of the same, after its files
            var manifest = $"{label}{ModSet.ManifestFileName}";
            foreach (var (pack, index) in SectionItems<PartPack>(catalog, mod, ModSections.Packs, "its parts are left out"))
            {
                var packName = $"{manifest} {ModSections.Packs}[{index}]";
                if (string.IsNullOrWhiteSpace(pack.Id))
                {
                    catalog._problems.Add($"{packName}: no id, which names the folder under the mod's parts folder its models are in; its parts are left out");
                    continue;
                }
                if (!PathNames.TryCombineUnder(root, pack.Id.Replace('/', Path.DirectorySeparatorChar), out var folder))
                {
                    catalog._problems.Add($"{packName}: the id \"{pack.Id}\" is no folder under the mod's parts folder, where its models are; its parts are left out");
                    continue;
                }

                AddPack(pack, folder, $"{packName} ({pack.Id})");
            }

            ReadAliases(catalog, Section<Dictionary<string, string>>(catalog, mod, ModSections.PartAliases, "parts that changed ids are not found by their old ones"),
                $"{manifest} {ModSections.PartAliases}", mod, aliases);
            ReadAliases(catalog, Section<Dictionary<string, string>>(catalog, mod, ModSections.BuildAliases, "builds that changed ids are not found by their old ones"),
                $"{manifest} {ModSections.BuildAliases}", mod, buildAliases);
            AddBuilds(catalog, SectionItems<EngineBuild>(catalog, mod, ModSections.Builds, "the build is left out").Select(b => (EngineBuild?)b.Item).ToList(),
                $"{manifest} {ModSections.Builds}", mod, builds);

            void AddPack(PartPack? pack, string? folder, string packName)
            {
                if (pack?.Parts == null) return;

                var olderParts = new List<string>();
                var olderFields = new List<string>();
                foreach (var part in pack.Parts)
                {
                    if (part == null) continue;

                    part.ModId = mod.Id;
                    part.LocalId = part.Id;
                    part.PackFolder = folder;
                    part.Make = string.IsNullOrWhiteSpace(pack.Make) ? null : pack.Make.Trim();
                    if (registry.Add(mod, part.Id, part.Replaces, part, packName) is { } id) part.Id = id;

                    // A mod written for the pack format of before, or a class of no file, loads but does nothing
                    if (part.OlderFormatFields.Concat(part.Slots.SelectMany(s => s.OlderFormatFields)).Distinct().ToList() is { Count: > 0 } older)
                    {
                        olderParts.Add(part.Id);
                        olderFields.AddRange(older.Except(olderFields));
                    }
                    if (!string.IsNullOrWhiteSpace(part.Class) && !classes.Parents.ContainsKey(part.Class)
                        && !part.Class.Equals(PartClasses.Part, StringComparison.OrdinalIgnoreCase))
                        catalog._problems.Add($"{packName}: {part.Id}: the class {part.Class} is in no {PartClasses.FileName}; the part counts as no kind of part");
                }

                // One line for the pack: an old pack of 500 parts would crowd every other mod's problems out
                if (olderParts.Count > 0)
                    catalog._problems.Add($"{packName}: {olderParts.Count} part(s), the first {olderParts[0]}: {string.Join(", ", olderFields)} are of the older pack format and not read; a part has a class, specs and needs, and a slot mounts by fits and takes (docs/modding.md)");
            }
        }

        catalog._problems.AddRange(registry.Problems);
        catalog._problems.AddRange(builds.Problems);
        foreach (var (id, part, _) in registry.Entries) catalog._parts[id] = part;

        // What the builds name, now that every id is known: a mod's own part first, then the base game's
        foreach (var (id, build, mod) in builds.Entries)
        {
            build.Id = id;
            if (mod.IsBase) continue;

            for (var i = 0; i < build.Parts.Count; i++) build.Parts[i] = Resolve(mod.Id, build.Parts[i]);
        }

        catalog.EngineBuilds = builds.All.ToList();
        catalog._buildIds.UnionWith(catalog.EngineBuilds.Select(b => b.Id));
        catalog.WorkOrder = WorkOrder.Load(roots.Select(r => (r.Mod.Id, r.Mod.IsBase, r.Dir)), catalog._problems);
        WorkOrder.CheckWays(catalog._parts.Values, catalog._problems);
        catalog.Rebuilds = PartRebuilds.Load(roots.Select(r => (r.Mod.Id, r.Mod.IsBase, r.Dir)), catalog._problems);
        catalog._problems.AddRange(catalog.Rebuilds.UnknownNames(
            catalog._parts.Values.Select(catalog.WorkOrder.KindOf).OfType<string>(),
            catalog._parts.Values.Select(PartKinds.GroupOf)));
        catalog.Rebuilds.CheckBecomes(catalog._parts.Values, catalog.Get, catalog._problems);

        // What each part does for the cooling, where its kind says: the cooling reads it off the part
        if (catalog.WorkOrder.TellsCooling)
        {
            foreach (var part in catalog._parts.Values)
            {
                part.CoolingRole = catalog.WorkOrder.CoolingOf(part);
                part.CoolingOwnFan = part.CoolingRole != null && catalog.WorkOrder.HasOwnFan(part);
            }
        }
        Logic.PartFigures.Read(catalog._parts.Values, catalog._problems);

        foreach (var (mod, gone, current) in aliases)
        {
            // An alias names a part that was: it has no entry to find, so it is the mod's own unless written in full
            var from = ModIds.IsQualified(gone) ? ModIds.Normalize(gone) : ModIds.Qualify(mod.Id, gone);
            catalog._aliases[from] = Resolve(mod.Id, current);
        }

        // A build alias names a build that was, as a part alias does a part
        foreach (var (mod, gone, current) in buildAliases)
        {
            var from = ModIds.IsQualified(gone) ? ModIds.Normalize(gone) : ModIds.Qualify(mod.Id, gone);
            catalog._buildAliases[from] = ModIds.Resolve(mod.Id, current, catalog._buildIds.Contains);
        }

        // Models a mod gives other parts, in load order: the latest mod's wins
        foreach (var (mod, root, label, key, model) in models)
        {
            if (!PathNames.TryCombineUnder(root, model, out var file) || !File.Exists(file))
            {
                catalog._problems.Add($"{label}{PartPack.ModelsFileName}: {key}: no model {model}; left out");
                continue;
            }

            // A key names a part as it was called too: the mod's own alias first, then the base game's, and a wildcard
            // the parts whose old ids it covers
            var prefix = key.EndsWith('*') ? key[..^1] : null;
            var exact = prefix == null ? catalog.CurrentId(ModIds.Resolve(mod.Id, key, id => catalog.Get(id) != null)) : null;
            var renamed = prefix == null ? null : catalog._aliases.Where(a => a.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(a => catalog.CurrentId(a.Key)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var matched = 0;
            foreach (var part in catalog._parts.Values.Where(p => prefix != null ? p.Id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || renamed!.Contains(p.Id)
                                                                                 : p.Id.Equals(exact, StringComparison.OrdinalIgnoreCase)))
            {
                part.Model = model;
                part.ModelFolder = root;
                matched++;
            }

            if (matched == 0) catalog._problems.Add($"{label}{PartPack.ModelsFileName}: {key} names no part; left out");
        }

        // Slots nudged into place in the garage, by full id: the file is the base game's. A part since renamed keeps its
        // shifts under its new id, which the file gets the next time it is written.
        catalog.Shifts = SlotShifts.Load(baseRoot);
        if (catalog.Shifts.Problem != null) catalog._problems.Add(catalog.Shifts.Problem);
        catalog.Shifts.Rename(catalog.CurrentId);
        catalog._problems.AddRange(catalog.Shifts.ApplyTo(catalog._parts)
            .Where(id => !ModIds.IsQualified(id))
            .Select(id => $"{SlotShifts.FileName}: {id} names no part; its shifts are kept but move nothing"));

        return catalog;

        string Resolve(string modId, string reference) => ModIds.Resolve(modId, reference, catalog._parts.ContainsKey);
    }

    /// <summary>
    /// Every pack.json under a mod's parts folder, by path. A folder the game may not read is passed over; one that
    /// cannot be listed at all costs that mod its packs, with a line in the problems, never the other mods theirs.
    /// </summary>
    private static List<string> PackFiles(PartsCatalog catalog, string root, string label)
    {
        try
        {
            // Hidden and system folders are read as they always were: only the inaccessible are passed over
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0 };
            return Directory.EnumerateFiles(root, PartPack.FileName, options).Order(StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            catalog._problems.Add($"{label}{PartPack.FileName}: the parts folder could not be read ({ex.Message}); its parts are left out");
            return new List<string>();
        }
    }

    /// <summary>
    /// A mod's file of old ids to current ones (<see cref="PartPack.AliasesFileName"/> for parts,
    /// <see cref="EngineBuild.AliasesFileName"/> for builds), each alias onto <paramref name="aliases"/>. An alias to
    /// nothing would leave a saved part (or a car's build) with no id at all: told and left out. A file that does not
    /// read is told with <paramref name="lostMessage"/>, what the game loses without it.
    /// </summary>
    private static void ReadAliasFile(PartsCatalog catalog, string root, string fileName, string label, ModInfo mod,
        List<(ModInfo, string, string)> aliases, string lostMessage)
    {
        var file = Path.Combine(root, fileName);
        try
        {
            if (!File.Exists(file)) return;

            ReadAliases(catalog, JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(file)), label + fileName, mod, aliases);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            catalog._problems.Add($"{label}{fileName}: {ex.Message}; {lostMessage}");
        }
    }

    private static void ReadAliases(PartsCatalog catalog, Dictionary<string, string>? read, string source, ModInfo mod, List<(ModInfo, string, string)> aliases)
    {
        foreach (var (gone, current) in read ?? new())
        {
            if (string.IsNullOrWhiteSpace(current)) catalog._problems.Add($"{source}: {gone} leads nowhere; left out");
            else aliases.Add((mod, gone, current));
        }
    }

    /// <summary>A section of the mod's mod.json read as <typeparamref name="T"/>; null when it has none, or when it does not read (told, with what is lost)</summary>
    private static T? Section<T>(PartsCatalog catalog, ModInfo mod, string name, string lostMessage) where T : class
    {
        if (mod.Section(name) is not { } json) return null;

        try
        {
            return JsonConvert.DeserializeObject<T>(json);
        }
        catch (Exception ex) when (IsBadSection(ex))
        {
            catalog._problems.Add($"{(mod.IsBase ? string.Empty : mod.Id + "/")}{ModSet.ManifestFileName} {name}: {ex.Message}; {lostMessage}");
            return null;
        }
    }

    /// <summary>
    /// The entries of a list section of the mod's mod.json, each read on its own with its place in the list: one that
    /// does not read is told, with what is lost, and the others stand (one typo in one pack must not cost the mod all of
    /// its packs). None when it has no such section; told, and none, when it is no list.
    /// </summary>
    private static List<(T Item, int Index)> SectionItems<T>(PartsCatalog catalog, ModInfo mod, string name, string lostMessage) where T : class
    {
        var read = new List<(T, int)>();
        if (mod.Section(name) is not { } json) return read;

        var source = $"{(mod.IsBase ? string.Empty : mod.Id + "/")}{ModSet.ManifestFileName} {name}";
        JArray list;
        try
        {
            if (JToken.Parse(json) is not JArray array)
            {
                catalog._problems.Add($"{source}: not a list ([ ... ]); nothing of it is read");
                return read;
            }
            list = array;
        }
        catch (Exception ex) when (IsBadSection(ex))
        {
            catalog._problems.Add($"{source}: {ex.Message}; nothing of it is read");
            return read;
        }

        for (var i = 0; i < list.Count; i++)
        {
            try
            {
                if (list[i].Type != JTokenType.Null && list[i].ToObject<T>() is { } item) read.Add((item, i));
            }
            catch (Exception ex) when (IsBadSection(ex))
            {
                catalog._problems.Add($"{source}[{i}]: {ex.Message}; {lostMessage}");
            }
        }

        return read;
    }

    /// <summary>What a section of somebody's mod.json throws when it is not what it should be (a text where a number goes, a number too big)</summary>
    private static bool IsBadSection(Exception ex) => ex is JsonException or ArgumentException or FormatException or OverflowException or InvalidCastException;

    private static void ReadModels(PartsCatalog catalog, string root, string label, ModInfo mod,
        List<(ModInfo, string, string, string, string)> models)
    {
        var modelsFile = Path.Combine(root, PartPack.ModelsFileName);
        try
        {
            if (!File.Exists(modelsFile)) return;

            var read = JsonConvert.DeserializeObject<ModelsFile>(File.ReadAllText(modelsFile));
            foreach (var (part, model) in read?.Models ?? new())
            {
                if (string.IsNullOrWhiteSpace(part) || string.IsNullOrWhiteSpace(model) || part.Trim() == "*")
                {
                    catalog._problems.Add($"{label}{PartPack.ModelsFileName}: \"{part}\": \"{model}\" names no part or no model; left out");
                    continue;
                }

                models.Add((mod, root, label, part.Trim(), model.Trim()));
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            catalog._problems.Add($"{label}{PartPack.ModelsFileName}: {ex.Message}; the parts keep their own models");
        }
    }

    /// <summary>A mod's <see cref="PartPack.ModelsFileName"/>: part id (or the start of one, ending in <c>*</c>) to model</summary>
    private sealed class ModelsFile
    {
        [JsonProperty("models")]
        public Dictionary<string, string>? Models { get; set; }
    }

    private static void ReadBuilds(PartsCatalog catalog, string root, string label, ModInfo mod, ContentRegistry<EngineBuild> builds)
    {
        var buildsFile = Path.Combine(root, EngineBuild.FileName);
        try
        {
            if (!File.Exists(buildsFile)) return;

            AddBuilds(catalog, JsonConvert.DeserializeObject<List<EngineBuild?>>(File.ReadAllText(buildsFile)), label + EngineBuild.FileName, mod, builds);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            catalog._problems.Add($"{label}{EngineBuild.FileName}: {ex.Message}; no engine builds from it");
        }
    }

    private static void AddBuilds(PartsCatalog catalog, List<EngineBuild?>? read, string source, ModInfo mod, ContentRegistry<EngineBuild> builds)
    {
        // A build without an id can't be found again (cars and saves name builds by it): left out, the rest stand
        var unnamed = 0;
        foreach (var build in read ?? new())
        {
            if (build == null || (string.IsNullOrWhiteSpace(build.Id) && string.IsNullOrWhiteSpace(build.Replaces)))
            {
                unnamed++;
                continue;
            }

            // What the builds a car left the factory with were called before
            if (string.Equals(build.Origin, EngineBuild.OriginCarBefore, StringComparison.OrdinalIgnoreCase))
            {
                catalog._problems.Add($"{source}: {build.Id}: the origin \"{build.Origin}\" is \"{EngineBuild.OriginCar}\" now; read as that");
                build.Origin = EngineBuild.OriginCar;
            }

            builds.Add(mod, build.Id, build.Replaces, build, source);
        }

        if (unnamed > 0) catalog._problems.Add($"{source}: {unnamed} build(s) without an id are left out");
    }

    /// <summary>Slots moved in the garage's placement mode since the packs were written; already applied to the parts</summary>
    public SlotShifts Shifts { get; private set; } = new();

    /// <summary>
    /// Moves a slot in its part's space, for good: the part is changed in place (every slot of that id, as a
    /// loaded shift does: a pack may declare an id twice) and the move is written next to the packs, applied on every
    /// load from then on.
    /// </summary>
    /// <returns>False when the part moved but the move could not be written</returns>
    public bool ShiftSlot(PartDefinition part, int slotId, float[] delta)
    {
        foreach (var slot in part.Slots.Where(s => s.Id == slotId))
        {
            for (var axis = 0; axis < 3; axis++) slot.Position[axis] += delta[axis];
        }

        return Shifts.Add(part.Id, slotId, delta);
    }

    /// <summary>A part by its id, or by the id it had in a pack that has since been replaced; null for no id at all</summary>
    public PartDefinition? Get(string? id) => id == null ? null : _parts.GetValueOrDefault(id) ?? _parts.GetValueOrDefault(CurrentId(id));

    /// <summary>
    /// The id an engine build goes by now: its own, or the one of the build that took its place. A build that is there
    /// goes by its own id, whatever an alias says (a later mod may bring it back)
    /// </summary>
    public string CurrentBuildId(string id)
    {
        for (var hops = 0; !_buildIds.Contains(id) && hops < MaxAliasHops && _buildAliases.TryGetValue(id, out var current); hops++) id = current;
        return id;
    }

    /// <summary>True when parts have changed ids: saves made before may hold the old ones</summary>
    public bool HasAliases => _aliases.Count > 0;

    /// <summary>
    /// The id a part goes by now: its own, unless the pack it came from was renamed or replaced by a later release
    /// since. Aliases chain when both happened (a pack renamed, then replaced under its new name), so they are
    /// followed until an id that is a part, or as far as they go.
    /// </summary>
    public string CurrentId(string id)
    {
        for (var hops = 0; !_parts.ContainsKey(id) && hops < MaxAliasHops && _aliases.TryGetValue(id, out var current); hops++)
        {
            id = current;
        }

        return id;
    }

    /// <summary>
    /// Whether a part goes on another by these slots: the child's slot fits a fitting the parent's slot takes. A
    /// carburettor base that fits "carb:4bbl" goes on any manifold pad that takes it, whatever mod either is from; one
    /// way only, as <see cref="FindMountable"/> looks, so a part never goes onto what goes on it.
    /// </summary>
    public static bool CanMate(PartSlot childSlot, PartSlot parentSlot) =>
        childSlot.Fits.Any(fitting => parentSlot.Takes.Contains(fitting, StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// The parts of a group that go on the parent's slot (<see cref="FindMountable"/>), each once whatever the case of its
    /// id, in the order of their ids; none for a part without the slot
    /// </summary>
    public List<PartDefinition> MountableOf(PartDefinition parent, int slotId, string group) =>
        parent.Slots.FirstOrDefault(s => s.Id == slotId) is { } slot ? MountableOf(parent, slot, group) : [];

    /// <inheritdoc cref="MountableOf(PartDefinition, int, string)"/>
    public List<PartDefinition> MountableOf(PartDefinition parent, PartSlot slot, string group) =>
        FindMountable(parent, slot).Select(m => m.Part).Where(p => PartKinds.GroupOf(p) == group)
            .DistinctBy(p => p.Id, StringComparer.OrdinalIgnoreCase).OrderBy(p => p.Id, StringComparer.Ordinal).ToList();

    /// <summary>The parts of a group, in the order of their ids</summary>
    public List<PartDefinition> InGroup(string group) =>
        _parts.Values.Where(p => PartKinds.GroupOf(p) == group).OrderBy(p => p.Id, StringComparer.Ordinal).ToList();

    /// <summary>
    /// Every part that goes on a slot, with the slot of its own it goes on by: those that fit what the slot takes. A
    /// block never goes on anything, and a part of a kind that never stacks never on its own kind (a head on a head's
    /// slot that shares its fitting); those are left out.
    /// </summary>
    public IReadOnlyList<(PartDefinition Part, PartSlot Slot)> FindMountable(PartDefinition parent, PartSlot slot)
    {
        var index = _mating.Value;
        var parentGroup = PartKinds.GroupOf(parent);
        var result = new List<(PartDefinition Part, PartSlot Slot)>();
        var seen = new HashSet<PartSlot>();

        foreach (var fitting in slot.Takes) Add(index.FittedBy.GetValueOrDefault(fitting));

        return result;

        void Add(List<(PartDefinition Part, PartSlot Slot)>? candidates)
        {
            if (candidates == null) return;

            foreach (var candidate in candidates)
            {
                if (ReferenceEquals(candidate.Part, parent) || !seen.Add(candidate.Slot)) continue;
                if (PartKinds.IsBlock(candidate.Part)) continue;
                if (PartKinds.NeverStacks(parentGroup) && PartKinds.GroupOf(candidate.Part) == parentGroup) continue;

                result.Add(candidate);
            }
        }
    }

    /// <summary><see cref="CanMate"/> turned around: from a fitting to the slots that fit it</summary>
    private sealed class MatingIndex
    {
        /// <summary>Slots by the fittings they mount by</summary>
        public Dictionary<string, List<(PartDefinition Part, PartSlot Slot)>> FittedBy { get; } = new(StringComparer.OrdinalIgnoreCase);

        public MatingIndex(PartsCatalog catalog)
        {
            foreach (var part in catalog._parts.Values)
            {
                foreach (var slot in part.Slots)
                {
                    foreach (var fitting in slot.Fits)
                    {
                        if (!FittedBy.TryGetValue(fitting, out var list)) FittedBy[fitting] = list = new();
                        list.Add((part, slot));
                    }
                }
            }
        }
    }

    /// <summary>Full path of the part's model, null if it has none</summary>
    /// <remarks>
    /// The model is named in pack.json and sits next to it; a part not loaded from a pack (made in a test) is looked
    /// for where its id says, under the parts folder. A path that leads out of that folder is no model.
    /// </remarks>
    public string? GetModelPath(PartDefinition part)
    {
        if (part.Model == null) return null;

        if ((part.ModelFolder ?? part.PackFolder) is { } folder)
            return PathNames.TryCombineUnder(folder, part.Model, out var own) ? own : null;

        var packEnd = part.Id.LastIndexOf('/');
        if (packEnd <= 0) return null;

        var relative = Path.Combine(part.Id[..packEnd].Replace('/', Path.DirectorySeparatorChar), part.Model);
        return PathNames.TryCombineUnder(_root, relative, out var path) ? path : null;
    }
}
