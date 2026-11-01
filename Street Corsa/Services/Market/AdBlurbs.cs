using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using StreetCorsa.Logging;
using StreetCorsa.Models.GameState;
using StreetCorsa.Parts;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Services.Market
{
    /// <summary>What an ad is written from: what the seller knows about the car or the part</summary>
    public abstract record AdFacts;

    /// <summary>
    /// A car's facts for its ad: what the seller says about the engine, whether it was worked on, its condition (0 to 1)
    /// and mileage (km), the parts worth a word, the most telling first, and whether it sits lowered
    /// </summary>
    public sealed record CarAdFacts(string? Engine, bool Modified, double Condition, int MileageKm, IReadOnlyList<AdStandout> Standouts,
        CarAdLooks? Looks = null, bool Lowered = false) : AdFacts;

    /// <summary>
    /// How a car for sale looks (each 0..1): what an honest ad cannot call shiny, and what a seller cannot deny. From how
    /// much each shows: the words' choice, not the game's figures (the price goes by the looks themselves).
    /// </summary>
    public sealed record CarAdLooks(double Rust, double PaintWear, double Cracks, double Dirt)
    {
        public bool Rusty => Rust >= 0.15;

        public bool BadlyRusty => Rust >= 0.5;

        public bool WornPaint => PaintWear >= 0.3;

        public bool Cracked => Cracks >= 0.01;

        public bool Dirty => Dirt >= 0.5;

        /// <summary>Anything a buyer sees at a glance</summary>
        public bool Shows => Rusty || WornPaint || Cracked || Dirty;

        public static CarAdLooks Of(UsedCarListing listing) => new(listing.Rust, listing.PaintWear, listing.GlassCracks, listing.BodyDirt);

        public static CarAdLooks Of(Car car) => new(car.Rust, car.PaintWear, car.GlassCracks, car.BodyDirt);
    }

    /// <summary>A part worth naming in a car's ad: what sort of thing it is (<see cref="AdBlurbs.StandoutKinds"/>) and its name</summary>
    public sealed record AdStandout(string Kind, string Part);

    /// <summary>
    /// A part's facts for its ad: its name and the player's word for its kind, whether it is a whole engine, a speed
    /// shop's sort of part, how worn it is (1 new, 0 worn out), and whether a racer pulled it off their car
    /// </summary>
    public sealed record PartAdFacts(string Part, string Kind, bool CompleteEngine, bool SpeedPart, double Wear, bool FromRacer) : AdFacts;

    /// <summary>
    /// Writes something better for an ad than the built-in line, in its own time: an AI, say. The game never waits on
    /// it. It is handed the ad's facts and what the game wrote, and calls <c>rewrite</c> with its own line if it comes
    /// up with one, on the thread that owns the game state (the UI thread), never at all if it does not; with null when
    /// it let the ad go unwritten (the AI switched off under its desk), so the ad may be offered again. Improve itself
    /// must return at once: true when it took the ad, false when it has its hands full (the ad waits for room).
    /// </summary>
    public interface IAdWriter
    {
        /// <summary>The writer is there to ask now (the player's AI is on)</summary>
        bool CanImprove { get; }

        /// <summary>Room for another ad now (<see cref="Improve"/> would take it): asked before an ad's facts are worked out</summary>
        bool HasRoom { get; }

        bool Improve(AdFacts facts, string written, Action<string?> rewrite);

        /// <summary>A writer that was full has room again (its ads written): the ads that waited are offered now</summary>
        event Action? RoomAgain;
    }

    /// <summary>
    /// The few words of an ad in the paper's classifieds, written once when the ad is made and kept with it: a part
    /// worth naming ("Blown with a GMC 6-71"), "all original", a line of character ("Grandma's car, church on Sundays
    /// only"), now and then the condition; not every ad the same. The lines are the text pools <c>ads.car.*</c> and
    /// <c>ads.part.*</c> (the core's <c>text\ads.json</c>), when to say which is here.
    ///
    /// The seam for a better writer: <see cref="Writer"/>. Every ad written here is offered to it as it is made (an ad
    /// still in the game's words, from while the writer was not there, when the paper opens: <c>Polish</c>). A writer
    /// with its hands full (a new game's market makes a hundred ads at once) is handed the rest as it has room again.
    /// What it writes back takes the built-in line's place, and the ad is not offered again.
    /// </summary>
    public static partial class AdBlurbs
    {
        private static readonly IAppLogger Logger = AppLoggerFactory.CreateLogger("Market");

        private static IAdWriter? _writer;

        /// <summary>The writer that may improve an ad after it is written (<see cref="IAdWriter"/>); none by default</summary>
        public static IAdWriter? Writer
        {
            get => _writer;
            set
            {
                if (ReferenceEquals(_writer, value)) return;
                if (_writer != null) _writer.RoomAgain -= OfferWaiting;
                _writer = value;
                if (value != null) value.RoomAgain += OfferWaiting;
            }
        }

        // The ads offered to the writer this session (in its hands, or waiting for room there), so one shown again (a page
        // back, the paper opened again) is not asked for twice while the first answer is still to come
        private static readonly ConditionalWeakTable<object, object> Offered = new();

        // The ads the writer had no room for, in the order they came, each with the words it was offered with and how its
        // facts are worked out: handed over as room comes. Weak: a game put away takes its ads with it.
        private static readonly List<(WeakReference<IAdWords> Ad, string Words, Func<AdFacts?> Facts)> Waiting = [];

        // The sorts of part a car's ad names, the most telling first: each is a pool ads.car.<kind>, with {part}. A
        // blower or a turbo is worth a word even as the factory fitted it; the rest only when somebody put it on.
        // Carburettors and injection are one sort, the fuel system (a setting's words say which its cars run).
        private static readonly string[] Ranked = ["blower", "turbo", "nitrous", "fuel", "heads", "cam", "intake", "headers"];
        public static IReadOnlyList<string> StandoutKinds => Ranked;
        private const int FactoryStandouts = 2;

        // When an ad says a word on the car's condition or mileage: the words' choice, not the game's figures (the price
        // goes by the car's condition, never by what its ad says)
        private const double MintCondition = 0.85;
        private const double RoughCondition = 0.45;
        private const int LowMileageKm = 45_000;
        private const double FreshWear = 0.8;
        private const double WornWear = 0.5;


        // How often a car's ad adds a word on its condition to what else it says, when there is one to say
        private const double ConditionAside = 0.4;

        // ----- the facts -----

        /// <summary>A car's facts, its parts looked up in <paramref name="catalog"/> (none named without one)</summary>
        /// <param name="factory">The ids of the parts of the car's factory engine (<see cref="IUsedCarMarketService.FactoryParts"/>):
        /// a part among them is what the car came with, no news. Null when the car's factory build is not known: then
        /// a worked-on car's parts all count.</param>
        public static CarAdFacts FactsOf(IReadOnlyList<PartInstance> parts, PartsCatalog? catalog, IReadOnlySet<string>? factory,
            string? engine, bool modified, double condition, int mileageKm, CarAdLooks? looks = null, bool? lowered = null)
        {
            var standouts = new List<AdStandout>();
            if (catalog != null)
            {
                foreach (var part in parts.SelectMany(p => p.SelfAndDescendants()))
                {
                    if (catalog.Get(part.DefinitionId) is not { } definition || KindOf(definition) is not { } kind) continue;
                    var always = Array.IndexOf(Ranked, kind) < FactoryStandouts;
                    if (!always && (!modified || factory?.Contains(definition.Id) == true)) continue;
                    if (standouts.Any(s => s.Kind == kind)) continue;
                    standouts.Add(new AdStandout(kind, NameOf(definition)));
                }
            }

            // What the seller says about the engine, without the catalog's notes in brackets on the block's name
            var said = string.IsNullOrWhiteSpace(engine) ? null : Bracketed().Replace(engine, string.Empty).Trim();
            return new CarAdFacts(string.IsNullOrEmpty(said) ? null : said, modified, condition, mileageKm,
                standouts.OrderBy(s => Array.IndexOf(Ranked, s.Kind)).ToList(), looks,
                lowered ?? (catalog != null && StreetCorsa.Parts.Cars.SetupLook.LoweredByParts(parts, catalog)));
        }

        public static CarAdFacts FactsOf(UsedCarListing listing, PartsCatalog? catalog, IReadOnlySet<string>? factory) =>
            FactsOf(listing.Parts, catalog, factory, listing.EngineSummary, listing.IsModified, listing.Condition, listing.Mileage,
                CarAdLooks.Of(listing));

        /// <summary>A rival's ad or the player's own: the car as it is, what the seller says about its engine, lowered by its setup too</summary>
        public static CarAdFacts FactsOf(ICarAdWords ad, Car car, PartsCatalog? catalog, IReadOnlySet<string>? factory) =>
            FactsOf(car.Parts, catalog, factory, ad.EngineSummary, ad.IsModified, CarValuation.ConditionOf(car),
                double.IsFinite(car.OdometerKM) ? (int)Math.Clamp(car.OdometerKM, 0, int.MaxValue) : 0, CarAdLooks.Of(car),
                catalog == null ? null : StreetCorsa.Parts.Cars.SetupLook.Of(car, catalog, Rules.RuleBook.Current.Tuning.Ranges()).Lowered);

        /// <summary>A part's facts; null when the catalog does not know it</summary>
        public static PartAdFacts? FactsOf(PartAd ad, PartsCatalog catalog)
        {
            if (catalog.Get(ad.Part.DefinitionId) is not { } definition) return null;

            var group = PartKinds.GroupOf(definition);
            return new PartAdFacts(
                NameOf(definition),
                PartKinds.GroupName(group),
                PartKinds.IsBlock(definition) && ad.Part.Children.Count > 0,
                PartGroups.Current.SoldBySpeedShops(group),
                ad.Part.Wear,
                ad.SellerRival != null);
        }

        /// <summary>A part as a seller says it: its name without what the catalog adds in brackets ("(L79)", "(stand-in model: ...)")</summary>
        public static string NameOf(PartDefinition part)
        {
            var name = part.DisplayName ?? part.Name;
            var bare = Bracketed().Replace(name, string.Empty).Trim();
            return bare.Length > 0 ? bare : name;
        }

        [GeneratedRegex(@"\s*\([^()]*\)")]
        private static partial Regex Bracketed();

        /// <summary>The sort of standout a part is, by its class; null for one not worth a word</summary>
        private static string? KindOf(PartDefinition part) =>
            PartKinds.Is(part, PartClasses.Supercharger) ? "blower"
            : PartKinds.Is(part, PartClasses.Turbocharger) ? "turbo"
            : PartKinds.Is(part, PartClasses.Nitrous) ? "nitrous"
            : PartKinds.Is(part, PartClasses.FuelSystem) ? "fuel"
            : PartKinds.Is(part, PartClasses.CylinderHead) ? "heads"
            : PartKinds.Is(part, PartClasses.Camshaft) ? "cam"
            : PartKinds.Is(part, PartClasses.IntakeManifold) ? "intake"
            : PartKinds.Is(part, PartClasses.ExhaustHeader) ? "headers"
            : null;

        // ----- the words -----

        /// <summary>A car's ad: one thing worth saying, picked with some weight to what stands out, now and then a word on its condition after it</summary>
        public static string Write(CarAdFacts facts, Random random)
        {
            var values = new TextValues { ["engine"] = facts.Engine, ["km"] = facts.MileageKm };
            // A car that shows its rust, worn paint, a cracked windscreen or its dirt is no mint car, whatever is under it
            var looks = facts.Looks;
            var condition = facts.Condition >= MintCondition && looks?.Shows != true ? "ads.car.mint" : facts.Condition <= RoughCondition ? "ads.car.rough" : null;

            var choices = new List<(string Key, double Weight, AdStandout? Part)>();
            if (facts.Standouts.Count > 0) choices.Add(("ads.car." + facts.Standouts[0].Kind, 5, facts.Standouts[0]));
            if (facts.Standouts.Count > 1) choices.Add(("ads.car." + facts.Standouts[1].Kind, 2, facts.Standouts[1]));
            if (facts.Engine != null && facts.Modified && facts.Standouts.Count == 0) choices.Add(("ads.car.built", 3, null));
            if (facts.Engine != null && !facts.Modified) choices.Add(("ads.car.engine", 2, null));
            if (!facts.Modified) choices.Add(("ads.car.original", 3, null));
            if (condition != null) choices.Add((condition, condition == "ads.car.rough" ? 3 : 2, null));
            if (facts.MileageKm is > 0 and < LowMileageKm) choices.Add(("ads.car.low_miles", 1.5, null));
            if (facts.Lowered) choices.Add(("ads.car.lowered", 2, null));
            // What the buyer will see anyway, said plainly
            if (looks?.Rusty == true) choices.Add(("ads.car.rust", 2, null));
            else if (looks?.WornPaint == true) choices.Add(("ads.car.paint", 1.5, null));
            if (looks?.Cracked == true) choices.Add(("ads.car.cracked_screen", 1.5, null));
            choices.Add(("ads.car.character", 2.5, null));

            var (key, _, part) = Pick(choices, random);
            if (part != null) values["part"] = part.Part;
            var line = GameText.Installed.Pick(key, random, values);

            // Not after "all original" or a line of character: those say enough, and a second "make offer" reads twice
            if (condition != null && key != condition && key != "ads.car.original" && key != "ads.car.character" && random.NextDouble() < ConditionAside)
                line = Join(line, GameText.Installed.Pick(condition, random, values));
            return line;
        }

        /// <summary>A part's ad: one thing worth saying about it</summary>
        public static string Write(PartAdFacts facts, Random random)
        {
            var choices = new List<(string Key, double Weight, AdStandout? Part)>();
            if (facts.CompleteEngine) choices.Add(("ads.part.engine", 5, null));
            if (facts.SpeedPart) choices.Add(("ads.part.speed", 3, null));
            if (facts.Wear >= FreshWear) choices.Add(("ads.part.fresh", 3, null));
            if (facts.Wear <= WornWear) choices.Add(("ads.part.worn", 3, null));
            if (facts.FromRacer) choices.Add(("ads.part.racer", 3, null));
            choices.Add(("ads.part.character", 2, null));

            return GameText.Installed.Pick(Pick(choices, random).Key, random, new TextValues { ["part"] = facts.Part, ["kind"] = facts.Kind });
        }

        private static (string Key, double Weight, AdStandout? Part) Pick(List<(string Key, double Weight, AdStandout? Part)> choices, Random random)
        {
            var roll = random.NextDouble() * choices.Sum(c => c.Weight);
            foreach (var choice in choices)
            {
                roll -= choice.Weight;
                if (roll < 0) return choice;
            }

            return choices[^1];
        }

        private static string Join(string first, string second) =>
            first.Length == 0 ? second : second.Length == 0 ? first : first + " " + second;

        // ----- onto the ads -----

        /// <summary>The listing's words, the game's own, written now, and offered to the <see cref="Writer"/></summary>
        public static void Describe(UsedCarListing listing, PartsCatalog? catalog, IReadOnlySet<string>? factory, Random? random = null)
        {
            var facts = FactsOf(listing, catalog, factory);
            listing.Description = Write(facts, random ?? Random.Shared);
            Offer(listing, () => facts);
        }

        /// <summary>A rival's ad's words, or the player's own, as a private seller would put them</summary>
        public static void Describe(ICarAdWords ad, Car car, PartsCatalog? catalog, IReadOnlySet<string>? factory, Random? random = null)
        {
            var facts = FactsOf(ad, car, catalog, factory);
            ad.Description = Write(facts, random ?? Random.Shared);
            Offer(ad, () => facts);
        }

        /// <summary>The part ad's words; none for a part the catalog does not know</summary>
        public static void Describe(PartAd ad, PartsCatalog catalog, Random? random = null)
        {
            if (FactsOf(ad, catalog) is not { } facts) return;
            ad.Description = Write(facts, random ?? Random.Shared);
            Offer(ad, () => facts);
        }

        /// <summary>
        /// An ad still in the game's own words (made while the AI was off, or before ads were offered to it) offered to the
        /// <see cref="Writer"/> now, once a session: the paper does it for every ad it holds as it opens. Its facts are
        /// worked out only once the writer has room for it (<paramref name="factory"/> reads the database).
        /// </summary>
        public static void Polish(UsedCarListing listing, PartsCatalog? catalog, Func<IReadOnlySet<string>?> factory) =>
            Offer(listing, () => FactsOf(listing, catalog, factory()));

        public static void Polish(ICarAdWords ad, Car car, PartsCatalog? catalog, Func<IReadOnlySet<string>?> factory) =>
            Offer(ad, () => FactsOf(ad, car, catalog, factory()));

        public static void Polish(PartAd ad, PartsCatalog catalog) => Offer(ad, () => FactsOf(ad, catalog));

        /// <summary>
        /// An ad from before ads had words gets them as it is shown, and keeps them; true when it was written now (the
        /// game is then worth saving)
        /// </summary>
        public static bool Ensure(UsedCarListing listing, PartsCatalog? catalog, Func<IReadOnlySet<string>?> factory)
        {
            if (listing.Description != null) return false;
            Describe(listing, catalog, factory());
            return true;
        }

        public static bool Ensure(RivalCarAd ad, Car car, PartsCatalog? catalog, Func<IReadOnlySet<string>?> factory)
        {
            if (ad.Description != null) return false;
            Describe(ad, car, catalog, factory());
            return true;
        }

        /// <summary>
        /// The player's own ad: its words written the first time, and again whenever the engine in the car is no longer
        /// the one they speak of (the player swapped it, or built it up), so the ad and the buyer who calls about it go
        /// by the car as it is. The AI's words go with the old engine.
        /// </summary>
        /// <param name="engine">What the seller says about the engine (<see cref="IUsedCarMarketService.DescribeEngine"/>):
        /// null when it cannot be told now (the parts not loaded), and the ad is left as it was</param>
        public static bool Ensure(CarSaleAd ad, Car car, PartsCatalog? catalog, Func<IReadOnlySet<string>?> factory, Func<EngineDescription?> engine)
        {
            var said = engine();
            if (ad.Description != null && (said == null || (said.Summary == ad.EngineSummary && said.IsModified == ad.IsModified))) return false;

            if (said != null)
            {
                ad.EngineSummary = said.Summary;
                ad.IsModified = said.IsModified;
            }

            // Written anew: offered to the AI again
            ad.AiPolished = false;
            Offered.Remove(ad);
            Describe(ad, car, catalog, factory());
            return true;
        }

        public static bool Ensure(PartAd ad, PartsCatalog catalog)
        {
            if (ad.Description != null) return false;
            Describe(ad, catalog);
            return ad.Description != null;
        }

        /// <summary>
        /// An ad with words of the game's own to the better writer, if there is one, once a session: into its hands when it
        /// has room, else put to wait for room (<see cref="OfferWaiting"/>). What it writes back takes the game's words'
        /// place for good.
        /// </summary>
        private static void Offer(IAdWords ad, Func<AdFacts?> facts)
        {
            if (ad.AiPolished || string.IsNullOrWhiteSpace(ad.Description) || Writer is not { CanImprove: true }) return;
            if (Offered.TryAdd(ad, ad) && !Hand(ad, facts)) Wait(ad, facts);
            OfferWaiting();
        }

        /// <summary>
        /// The ad into the writer's hands, if it has room: true when it took it (or there is nothing to ask it), false when
        /// it is full, nothing worked out then. The facts (a database read or two) are worked out only for a writer with
        /// room. One that fails leaves the game's own words, and is not asked for the ad again.
        /// </summary>
        private static bool Hand(IAdWords ad, Func<AdFacts?> facts)
        {
            if (Writer is not { CanImprove: true, HasRoom: true } writer) return false;
            if (ad.AiPolished || ad.Description is not { Length: > 0 } written) return true;

            try
            {
                if (facts() is not { } known) return true;
                return writer.Improve(known, written, better =>
                {
                    // Let go unwritten (the AI switched off under the writer's desk): it waits for room again
                    if (better == null) Wait(ad, facts);
                    else if (!string.IsNullOrWhiteSpace(better))
                    {
                        ad.Description = better.Trim();
                        ad.AiPolished = true;
                    }
                });
            }
            catch (Exception ex)
            {
                Logger.Warning("The ad writer failed; the ad keeps its own words: {Error}", ex.Message);
                return true;
            }
        }

        /// <summary>The ad waits for room at the writer, with the words it has now (written anew since, it is another ad)</summary>
        private static void Wait(IAdWords ad, Func<AdFacts?> facts)
        {
            if (ad.AiPolished || ad.Description is not { Length: > 0 } words) return;
            lock (Waiting) Waiting.Add((new WeakReference<IAdWords>(ad), words, facts));
        }

        /// <summary>The ads waiting for room at the writer forgotten: a game put away takes its ads with it (and a test its own)</summary>
        public static void Forget()
        {
            lock (Waiting) Waiting.Clear();
        }

        /// <summary>
        /// The ads that waited for room at the writer, into its hands now that it has some, the oldest first, until it is
        /// full again: the writer says when (<see cref="IAdWriter.RoomAgain"/>), and every ad offered tries too
        /// </summary>
        public static void OfferWaiting()
        {
            if (Writer is not { CanImprove: true, HasRoom: true }) return;

            List<(WeakReference<IAdWords> Ad, string Words, Func<AdFacts?> Facts)> waiting;
            lock (Waiting)
            {
                if (Waiting.Count == 0) return;
                waiting = [.. Waiting];
                Waiting.Clear();
            }

            var left = new List<(WeakReference<IAdWords> Ad, string Words, Func<AdFacts?> Facts)>();
            foreach (var entry in waiting)
            {
                // Gone with its game, written since, or its words changed under it (the player's own, written anew): done with
                if (!entry.Ad.TryGetTarget(out var ad) || ad.AiPolished || ad.Description != entry.Words) continue;

                // The writer full again: this one and the rest keep waiting, in their order
                if (left.Count > 0 || !Hand(ad, entry.Facts)) left.Add(entry);
            }

            lock (Waiting) Waiting.InsertRange(0, left);
        }
    }
}
