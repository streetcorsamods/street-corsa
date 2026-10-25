using System.Numerics;
using System.Text.Json;
using StreetCorsa.Mods;

namespace StreetCorsa.Services.Street
{
    /// <summary>
    /// What a slot of a recipe takes: piece ids and tags, as one list (<c>"take": ["shop", "harbor_breakfast"]</c>); a word
    /// takes the piece of that id and every piece with that tag, so a mod's piece named as a tag (a folder "house") is one
    /// more of them, not all of them. A far choice and how near it starts (a palm's card past 45 m of the player's car, the
    /// palm itself nearer).
    /// </summary>
    public sealed record PieceChoice(IReadOnlyList<string> Take)
    {
        /// <summary>The pieces of the kit the choice takes</summary>
        public IReadOnlyList<StreetPiece> In(StreetKit kit) =>
            Take.Select(kit.Find).OfType<StreetPiece>()
                .Concat(kit.Tagged(Take))
                .DistinctBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();

        public override string ToString() => string.Join(", ", Take);
    }

    /// <summary>
    /// What every slot of a recipe says besides where it is: what it takes (and further off than <see cref="Near"/> of
    /// <see cref="NearAt"/>, <see cref="Far"/>), the chance it stays empty, how high its pieces stand, how much a piece is
    /// turned at random (degrees either way, 360 any way), its scale's range, the height a piece is scaled to (as made
    /// when not given), whether it keeps out of the clear areas (<see cref="Clear"/>) and off the footprints of what stands
    /// already (<see cref="Room"/>: a shop, a car; a lamp's arm over the parking lane or a palm's crown need not)
    /// </summary>
    public sealed record SlotRules(PieceChoice Choice, PieceChoice? Far, float Near, Vector2 NearAt, float Empty, float Y, float Turn,
        float ScaleMin, float ScaleMax, float? Height, bool Clear, bool Room)
    {
        /// <summary>
        /// The slot's name (<c>"name"</c>), unique among a recipe's slots: a diner's look's slot of a name takes the place
        /// of the street's, and its <c>drop</c> takes the street's of the names away (<see cref="StreetRecipe.Over"/>); empty
        /// for none
        /// </summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>
        /// Whether the slot is drawn again each day (<c>"daily": true</c>: the cars at the curbs), from the save's seed and the
        /// date, and not kept in the save (<see cref="StreetLayout.Daily"/>)
        /// </summary>
        public bool Daily { get; init; }

        /// <summary>
        /// Whether the file says <c>"daily"</c> at all: a diner's look's slot that does not keeps the daily of the street's
        /// slot whose place it takes (<see cref="StreetRecipe.Over"/>), so a look tweaking the curb cars leaves them daily
        /// </summary>
        public bool DailyGiven { get; init; }
    }

    /// <summary>
    /// A frontage filled side by side from <see cref="From"/> towards <see cref="To"/>: each piece's front on the line
    /// facing <see cref="Heading"/> (its middle on it, <see cref="Middle"/>: a car at the curb), a gap drawn between each and
    /// the next, the first ones taking <see cref="First"/> in turn; a slot left empty leaves its piece's width vacant
    /// </summary>
    public sealed record RecipeRow(Vector2 From, Vector2 To, float Heading, bool Middle, float GapMin, float GapMax,
        IReadOnlyList<PieceChoice> First, SlotRules Rules);

    /// <summary>
    /// Pieces every <see cref="Every"/> metres from <see cref="From"/> to <see cref="To"/>, facing <see cref="Heading"/>: one
    /// whose spot is taken goes <see cref="Nudge"/> metres on, once (0: it is left out)
    /// </summary>
    public sealed record RecipeRun(Vector2 From, Vector2 To, float Every, float Heading, float Nudge, SlotRules Rules);

    /// <summary>A piece at one spot (<see cref="At"/>: x and z), facing <see cref="Heading"/></summary>
    public sealed record RecipeSpot(Vector2 At, float Heading, SlotRules Rules);

    /// <summary>
    /// Where nothing is put (the player's spot, the way the car comes and goes, a crossing): a box (x and z from and to),
    /// for every piece or only those with any of <see cref="Only"/>'s tags
    /// </summary>
    public sealed record ClearArea(Vector2 Min, Vector2 Max, IReadOnlySet<string> Only)
    {
        public bool Holds(StreetPiece piece) => Only.Count == 0 || piece.Tags.Overlaps(Only);
    }

    /// <summary>The light a lamp post of the layout gives at night</summary>
    public sealed record LampLight(byte[] Color, float Brightness, float Range);

    /// <summary>
    /// How a street is laid out with the street kit (step 39; street.json's <c>"layout"</c>): slots, not objects. The rows
    /// are filled first, then the runs, then the spots, each in the order the file gives them; see <see cref="StreetLayout"/>.
    /// A diner's look gives one too (diners.json's <c>look.layout</c>), laid over its street's (<see cref="Over"/>).
    /// </summary>
    public sealed record StreetRecipe(IReadOnlyList<RecipeRow> Rows, IReadOnlyList<RecipeRun> Runs, IReadOnlyList<RecipeSpot> Spots,
        IReadOnlyList<ClearArea> Clear, LampLight Lamp)
    {
        /// <summary>
        /// How much more often a piece of a tag is picked against the others a slot takes (<c>"weights": {"industrial": 3}</c>):
        /// its weight times the factor of every tag it has; 0 leaves it out
        /// </summary>
        public IReadOnlyDictionary<string, float> Weights { get; init; } = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Whether the recipe's weights leave the piece out: a factor of 0 for one of its tags</summary>
        public bool Excludes(StreetPiece piece) => piece.Tags.Any(t => Weights.TryGetValue(t, out var factor) && factor == 0f);

        /// <summary>A piece's weight in this recipe: its own, times its tags' factors</summary>
        public float WeightOf(StreetPiece piece)
        {
            var weight = piece.Weight;
            foreach (var tag in piece.Tags)
                if (Weights.TryGetValue(tag, out var factor)) weight *= factor;
            return weight;
        }

        /// <summary>
        /// The names of the street's slots a diner's look takes away (<c>"drop"</c>; see <see cref="Over"/>): a block of
        /// shops a diner's yard takes the place of, the palms a corner has none of
        /// </summary>
        public IReadOnlySet<string> Drop { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The street's recipe with a diner's look's laid over it: the street's slots the look names in its <see cref="Drop"/>
        /// gone, those of a name one of the look's has taken by the look's in their place, the look's other rows, runs, spots
        /// and clear areas after the street's; its weights times the street's tag by tag, the street's lamp light; the
        /// fingerprint both of theirs
        /// </summary>
        public StreetRecipe Over(StreetRecipe look)
        {
            var weights = new Dictionary<string, float>(Weights, StringComparer.OrdinalIgnoreCase);
            foreach (var (tag, factor) in look.Weights) weights[tag] = weights.GetValueOrDefault(tag, 1f) * factor;
            return new StreetRecipe(Laid(Rows, look.Rows, r => r.Rules, (r, rules) => r with { Rules = rules }, look.Drop),
                Laid(Runs, look.Runs, r => r.Rules, (r, rules) => r with { Rules = rules }, look.Drop),
                Laid(Spots, look.Spots, s => s.Rules, (s, rules) => s with { Rules = rules }, look.Drop), Clear.Concat(look.Clear).ToList(), Lamp)
            {
                Weights = weights,
                Fingerprint = Helpers.StableHash.Fnv($"{Fingerprint}|{look.Fingerprint}").ToString("x8")
            };
        }

        /// <summary>
        /// What of a diner's look's names <see cref="Over"/> cannot lay over this street, a line each: a slot's name no slot of
        /// its kind (row, run, spot) of the street has (it is added beside the street's, not in place of one), a name two of
        /// the look's slots of a kind share (the last takes the place), a name to drop no slot of the street has (nothing
        /// goes). A typo, or a street's slot renamed since.
        /// </summary>
        public IReadOnlyList<string> NameProblems(StreetRecipe look)
        {
            var problems = new List<string>();
            void Check<T>(string kind, IReadOnlyList<T> street, IReadOnlyList<T> theirs, Func<T, string> name)
            {
                var own = street.Select(name).Where(n => n.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var group in theirs.Select(name).Where(n => n.Length > 0).GroupBy(n => n, StringComparer.OrdinalIgnoreCase))
                {
                    if (!own.Contains(group.Key)) problems.Add($"its {kind} \"{group.Key}\" is no {kind} of the street; added beside the street's");
                    if (group.Count() > 1) problems.Add($"{group.Count()} of its {kind}s are named \"{group.Key}\"; the last is laid");
                }
            }

            Check("row", Rows, look.Rows, r => r.Rules.Name);
            Check("run", Runs, look.Runs, r => r.Rules.Name);
            Check("spot", Spots, look.Spots, s => s.Rules.Name);
            var names = Rows.Select(r => r.Rules.Name).Concat(Runs.Select(r => r.Rules.Name)).Concat(Spots.Select(s => s.Rules.Name))
                .Where(n => n.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var drop in look.Drop.Where(d => !names.Contains(d)))
                problems.Add($"it drops \"{drop}\", which no slot of the street is named; nothing goes");
            return problems;
        }

        // The street's slots of a kind with the look's over them: dropped, put in place by name (daily as the street's one
        // when the look's does not say), or added after
        private static List<T> Laid<T>(IReadOnlyList<T> street, IReadOnlyList<T> look, Func<T, SlotRules> rulesOf, Func<T, SlotRules, T> withRules,
            IReadOnlySet<string> drop)
        {
            string NameOf(T slot) => rulesOf(slot).Name;
            var named = look.Where(s => NameOf(s).Length > 0).GroupBy(NameOf, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);
            var laid = new List<T>();
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var slot in street)
            {
                var own = NameOf(slot);
                if (own.Length > 0 && drop.Contains(own)) continue;
                if (own.Length > 0 && named.TryGetValue(own, out var theirs) && used.Add(own))
                    laid.Add(rulesOf(theirs).DailyGiven ? theirs : withRules(theirs, rulesOf(theirs) with { Daily = rulesOf(slot).Daily }));
                else laid.Add(slot);
            }

            laid.AddRange(look.Where(s => NameOf(s).Length == 0 || !used.Contains(NameOf(s))));
            return laid;
        }

        /// <summary>
        /// What the recipe was written as, as a short hash of its JSON (spacing aside): a save's layout of an older recipe
        /// is laid out again (<see cref="StreetLayouts"/>); empty for a recipe made in code
        /// </summary>
        public string Fingerprint { get; init; } = string.Empty;

        /// <summary>Whether any of the recipe's slots is drawn each day (<see cref="SlotRules.Daily"/>)</summary>
        public bool HasDaily { get; } = Rows.Any(r => r.Rules.Daily) || Runs.Any(r => r.Rules.Daily) || Spots.Any(s => s.Rules.Daily);

        /// <summary>Every word the recipe's slots take by (take, far, first), each once</summary>
        public IEnumerable<string> Words => WordsOf(_ => true);

        /// <summary>The words the recipe's kept slots take by (not its daily ones'), each once</summary>
        public IEnumerable<string> KeptWords => WordsOf(r => !r.Daily);

        private IEnumerable<string> WordsOf(Func<SlotRules, bool> of) =>
            Rows.Where(r => of(r.Rules)).SelectMany(r => r.First.Cast<PieceChoice?>().Append(r.Rules.Choice).Append(r.Rules.Far))
                .Concat(Runs.Where(r => of(r.Rules)).SelectMany(r => new[] { r.Rules.Choice, r.Rules.Far }))
                .Concat(Spots.Where(s => of(s.Rules)).SelectMany(s => new[] { s.Rules.Choice, s.Rules.Far }))
                .OfType<PieceChoice>()
                .SelectMany(c => c.Take)
                .Distinct(StringComparer.OrdinalIgnoreCase);

        /// <summary>The words of the recipe no piece of the kit is of or tagged with (a typo, a mod not installed), each once</summary>
        public IReadOnlyList<string> Unknown(StreetKit kit) => Words.Where(w => !kit.Knows(w)).ToList();

        private sealed record ChoiceJson(List<string?>? Take, List<string?>? Far, float? Near, float[]? NearAt, float? Empty, float? Y,
            float? Turn, float[]? Scale, float? Height, bool? Clear, bool? Room);

        private sealed record RowJson(float[]? From, float[]? To, float Heading, string? Align, float[]? Gap, List<string?>? First,
            List<string?>? Take, List<string?>? Far, float? Near, float[]? NearAt, float? Empty, float? Y, float? Turn, float[]? Scale,
            float? Height, bool? Clear, bool? Room, string? Name, bool? Daily);

        private sealed record RunJson(float[]? From, float[]? To, float Every, float Heading, float? Nudge,
            List<string?>? Take, List<string?>? Far, float? Near, float[]? NearAt, float? Empty, float? Y, float? Turn, float[]? Scale,
            float? Height, bool? Clear, bool? Room, string? Name, bool? Daily);

        private sealed record SpotJson(float[]? At, float Heading,
            List<string?>? Take, List<string?>? Far, float? Near, float[]? NearAt, float? Empty, float? Y, float? Turn, float[]? Scale,
            float? Height, bool? Clear, bool? Room, string? Name, bool? Daily);

        private sealed record ClearJson(float[]? Rect, List<string?>? Only);

        private sealed record LampJson(int[]? Color, float? Brightness, float? Range);

        private sealed record RecipeJson(List<RowJson?>? Rows, List<RunJson?>? Runs, List<SpotJson?>? Spots, List<ClearJson?>? Clear, LampJson? Lamp,
            Dictionary<string, float>? Weights, List<string?>? Drop);

        /// <summary>
        /// A recipe out of street.json's <c>"layout"</c> (or a diner's look's); null when there is none. A slot the game cannot
        /// use (no line, no take, numbers past what a float holds), or a weight that is no factor of 0 or more, is left out
        /// with a line in <paramref name="problems"/>, of <paramref name="whose"/> layout ("the street avenue").
        /// </summary>
        public static StreetRecipe? Read(JsonElement? layout, string whose, List<string> problems)
        {
            if (layout is not { ValueKind: JsonValueKind.Object } element) return null;

            RecipeJson? json;
            try
            {
                json = element.Deserialize<RecipeJson>(ModJson.Options);
            }
            catch (JsonException ex)
            {
                problems.Add($"{whose}'s layout does not read: {ex.Message}");
                return null;
            }

            if (json == null) return null;

            var rows = new List<RecipeRow>();
            foreach (var (row, n) in (json.Rows ?? []).Select((r, i) => (r, i)))
            {
                if (row == null || !Point(row.From, out var from) || !Point(row.To, out var to) || from == to || !float.IsFinite(row.Heading)
                    || !Rules(row.Take, row.Far, row.Near, row.NearAt, row.Empty, row.Y, row.Turn, row.Scale, row.Height, row.Clear ?? true, row.Room ?? true, out var rules))
                {
                    problems.Add($"{whose}'s row {n + 1} has no line from and to, a heading and a take; left out");
                    continue;
                }

                var gap = row.Gap is { Length: 2 } g && g.All(float.IsFinite) && g[0] >= 0f && g[1] >= g[0] ? g : [0.4f, 0.4f];
                var first = (row.First ?? []).Where(f => !string.IsNullOrWhiteSpace(f)).Select(f => new PieceChoice([f!.Trim()])).ToList();
                rows.Add(new RecipeRow(from, to, row.Heading, string.Equals(row.Align, "middle", StringComparison.OrdinalIgnoreCase),
                    gap[0], gap[1], first, rules with { Name = row.Name?.Trim() ?? string.Empty, Daily = row.Daily ?? false, DailyGiven = row.Daily != null }));
            }

            var runs = new List<RecipeRun>();
            foreach (var (run, n) in (json.Runs ?? []).Select((r, i) => (r, i)))
            {
                if (run == null || !Point(run.From, out var from) || !Point(run.To, out var to) || !float.IsFinite(run.Every) || run.Every <= 0.1f
                    || !float.IsFinite(run.Heading) || run.Nudge is { } nudge && !float.IsFinite(nudge)
                    || !Rules(run.Take, run.Far, run.Near, run.NearAt, run.Empty, run.Y, run.Turn, run.Scale, run.Height, run.Clear ?? true, run.Room ?? false, out var rules))
                {
                    problems.Add($"{whose}'s run {n + 1} has no line from and to, a spacing, a heading and a take; left out");
                    continue;
                }

                runs.Add(new RecipeRun(from, to, run.Every, run.Heading, run.Nudge ?? 0f, rules with { Name = run.Name?.Trim() ?? string.Empty, Daily = run.Daily ?? false, DailyGiven = run.Daily != null }));
            }

            var spots = new List<RecipeSpot>();
            foreach (var (spot, n) in (json.Spots ?? []).Select((s, i) => (s, i)))
            {
                if (spot == null || !Point(spot.At, out var at) || !float.IsFinite(spot.Heading)
                    || !Rules(spot.Take, spot.Far, spot.Near, spot.NearAt, spot.Empty, spot.Y, spot.Turn, spot.Scale, spot.Height, spot.Clear ?? false, spot.Room ?? false, out var rules))
                {
                    problems.Add($"{whose}'s spot {n + 1} has no place [x, z], heading and take; left out");
                    continue;
                }

                spots.Add(new RecipeSpot(at, spot.Heading, rules with { Name = spot.Name?.Trim() ?? string.Empty, Daily = spot.Daily ?? false, DailyGiven = spot.Daily != null }));
            }

            var clear = new List<ClearArea>();
            foreach (var (area, n) in (json.Clear ?? []).Select((c, i) => (c, i)))
            {
                if (area?.Rect is not { Length: 4 } rect || !rect.All(float.IsFinite))
                {
                    problems.Add($"{whose}'s clear area {n + 1} is no rect [x0, z0, x1, z1]; left out");
                    continue;
                }

                var a = new Vector2(rect[0], rect[1]);
                var b = new Vector2(rect[2], rect[3]);
                clear.Add(new ClearArea(Vector2.Min(a, b), Vector2.Max(a, b), Tags(area.Only)));
            }

            var lamp = json.Lamp;
            var light = new LampLight(lamp?.Color is { Length: 3 } colour ? colour.Select(c => (byte)Math.Clamp(c, 0, 255)).ToArray() : [255, 215, 160],
                lamp?.Brightness is { } brightness && float.IsFinite(brightness) && brightness >= 0f ? brightness : 0.9f,
                lamp?.Range is { } range && float.IsFinite(range) && range > 0f ? range : 20f);
            var weights = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            foreach (var (tag, factor) in json.Weights ?? [])
            {
                if (string.IsNullOrWhiteSpace(tag) || !float.IsFinite(factor) || factor < 0f)
                {
                    problems.Add($"{whose}'s weight {factor} for \"{tag}\" is no factor of 0 or more; left out");
                    continue;
                }

                weights[tag.Trim()] = factor;
            }

            return new StreetRecipe(rows, runs, spots, clear, light)
            {
                Weights = weights,
                Drop = Tags(json.Drop),
                Fingerprint = FingerprintOf(element)
            };

        }

        // How a save keeps a layout, in every fingerprint: one kept before its pieces said whether they keep room
        // (PlacedPiece.Room) is laid out again, from its seed, as it was
        private const string Keeping = "room";

        // Written again token by token, so spacing and line ends do not count
        private static string FingerprintOf(JsonElement layout)
        {
            using var buffer = new System.IO.MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer)) layout.WriteTo(writer);
            return Helpers.StableHash.Fnv($"{Keeping}|{System.Text.Encoding.UTF8.GetString(buffer.ToArray())}").ToString("x8");
        }

        private static bool Rules(List<string?>? take, List<string?>? far, float? near, float[]? nearAt, float? empty, float? y, float? turn,
            float[]? scale, float? height, bool clear, bool room, out SlotRules rules)
        {
            rules = null!;
            var choice = Choice(take);
            if (choice == null) return false;

            var numbers = new[] { near ?? 0f, empty ?? 0f, y ?? 0f, turn ?? 0f, height ?? 1f };
            if (!numbers.All(float.IsFinite) || scale is { } s && !s.All(float.IsFinite)) return false;
            var (low, high) = scale is { Length: 2 } range && range[0] > 0f && range[1] >= range[0] ? (range[0], range[1])
                : scale is { Length: 1 } one && one[0] > 0f ? (one[0], one[0]) : (1f, 1f);
            Point(nearAt, out var at);
            rules = new SlotRules(choice, Choice(far), near ?? 0f, at, Math.Clamp(empty ?? 0f, 0f, 1f), y ?? 0f, Math.Clamp(turn ?? 0f, 0f, 360f),
                low, high, height is > 0f ? height : null, clear, room);
            return true;
        }

        private static PieceChoice? Choice(List<string?>? take)
        {
            var words = (take ?? []).Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t!.Trim()).ToList();
            return words.Count == 0 ? null : new PieceChoice(words);
        }

        private static IReadOnlySet<string> Tags(List<string?>? tags) =>
            (tags ?? []).Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t!.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);

        private static bool Point(float[]? xz, out Vector2 point)
        {
            point = xz is { Length: 2 } p && p.All(float.IsFinite) ? new Vector2(p[0], p[1]) : default;
            return xz is { Length: 2 } q && q.All(float.IsFinite);
        }
    }
}
