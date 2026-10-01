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
    /// and mileage (km), and the parts worth a word, the most telling first
    /// </summary>
    public sealed record CarAdFacts(string? Engine, bool Modified, double Condition, int MileageKm, IReadOnlyList<AdStandout> Standouts) : AdFacts;

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
    /// up with one, on the thread that owns the game state (the UI thread), never at all if it does not. Improve itself
    /// may be called on a worker (the paper's part ads are made on one) and must return at once.
    /// </summary>
    public interface IAdWriter
    {
        void Improve(AdFacts facts, string written, Action<string> rewrite);
    }

    /// <summary>
    /// The few words of an ad in the paper's classifieds, written once when the ad is made and kept with it: a part
    /// worth naming ("Blown with a GMC 6-71"), "all original", a line of character ("Grandma's car, church on Sundays
    /// only"), now and then the condition; not every ad the same. The lines are the text pools <c>ads.car.*</c> and
    /// <c>ads.part.*</c> (the base game's <c>text\ads.json</c>), when to say which is here.
    ///
    /// The seam for a better writer: <see cref="Writer"/>. Every ad written here is offered to it after the built-in
    /// line is set, and what it writes back takes that line's place.
    /// </summary>
    public static partial class AdBlurbs
    {
        private static readonly IAppLogger Logger = AppLoggerFactory.CreateLogger("Market");

        /// <summary>The writer that may improve an ad after it is written (<see cref="IAdWriter"/>); none by default</summary>
        public static IAdWriter? Writer { get; set; }

        // The sorts of part a car's ad names, the most telling first: each is a pool ads.car.<kind>, with {part}. A
        // blower or a turbo is worth a word even as the factory fitted it; the rest only when somebody put it on.
        // Carburettors and injection are one sort: the scripts give both the same class.
        private static readonly string[] Ranked = ["blower", "turbo", "nitrous", "carbs", "heads", "cam", "intake", "headers"];
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
            string? engine, bool modified, double condition, int mileageKm)
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

            return new CarAdFacts(string.IsNullOrWhiteSpace(engine) ? null : engine, modified, condition, mileageKm,
                standouts.OrderBy(s => Array.IndexOf(Ranked, s.Kind)).ToList());
        }

        public static CarAdFacts FactsOf(UsedCarListing listing, PartsCatalog? catalog, IReadOnlySet<string>? factory) =>
            FactsOf(listing.Parts, catalog, factory, listing.EngineSummary, listing.IsModified, listing.Condition, listing.Mileage);

        public static CarAdFacts FactsOf(RivalCarAd ad, Car car, PartsCatalog? catalog, IReadOnlySet<string>? factory) =>
            FactsOf(car.Parts, catalog, factory, ad.EngineSummary, ad.IsModified, CarValuation.ConditionOf(car),
                double.IsFinite(car.OdometerKM) ? (int)Math.Clamp(car.OdometerKM, 0, int.MaxValue) : 0);

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

        /// <summary>The sort of standout a part is, by its script's classes; null for one not worth a word</summary>
        private static string? KindOf(PartDefinition part) =>
            PartKinds.Is(part, "SuperCharger") ? "blower"
            : PartKinds.Is(part, "TurboCharger") ? "turbo"
            : PartKinds.Is(part, "NOSInjectorSystem") ? "nitrous"
            : PartKinds.Is(part, "FuelInjectorSystem") ? "carbs"
            : PartKinds.Is(part, "CylinderHead") ? "heads"
            : PartKinds.Is(part, "Camshaft") ? "cam"
            : PartKinds.Is(part, "IntakeManifold") ? "intake"
            : PartKinds.Is(part, "ExhaustHeader") ? "headers"
            : null;

        // ----- the words -----

        /// <summary>A car's ad: one thing worth saying, picked with some weight to what stands out, now and then a word on its condition after it</summary>
        public static string Write(CarAdFacts facts, Random random)
        {
            var values = new TextValues { ["engine"] = facts.Engine, ["km"] = facts.MileageKm };
            var condition = facts.Condition >= MintCondition ? "ads.car.mint" : facts.Condition <= RoughCondition ? "ads.car.rough" : null;

            var choices = new List<(string Key, double Weight, AdStandout? Part)>();
            if (facts.Standouts.Count > 0) choices.Add(("ads.car." + facts.Standouts[0].Kind, 5, facts.Standouts[0]));
            if (facts.Standouts.Count > 1) choices.Add(("ads.car." + facts.Standouts[1].Kind, 2, facts.Standouts[1]));
            if (facts.Engine != null && facts.Modified && facts.Standouts.Count == 0) choices.Add(("ads.car.built", 3, null));
            if (facts.Engine != null && !facts.Modified) choices.Add(("ads.car.engine", 2, null));
            if (!facts.Modified) choices.Add(("ads.car.original", 3, null));
            if (condition != null) choices.Add((condition, condition == "ads.car.rough" ? 3 : 2, null));
            if (facts.MileageKm is > 0 and < LowMileageKm) choices.Add(("ads.car.low_miles", 1.5, null));
            choices.Add(("ads.car.character", 2.5, null));

            var (key, _, part) = Pick(choices, random);
            if (part != null) values["part"] = part.Part;
            var line = GameText.Installed.Pick(key, random, values);

            if (condition != null && key != condition && key != "ads.car.original" && random.NextDouble() < ConditionAside)
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

        /// <summary>The listing's words, written now and offered to the <see cref="Writer"/></summary>
        public static void Describe(UsedCarListing listing, PartsCatalog? catalog, IReadOnlySet<string>? factory, Random? random = null)
        {
            var facts = FactsOf(listing, catalog, factory);
            listing.Description = Write(facts, random ?? Random.Shared);
            Offer(facts, listing.Description, better => listing.Description = better);
        }

        public static void Describe(RivalCarAd ad, Car car, PartsCatalog? catalog, IReadOnlySet<string>? factory, Random? random = null)
        {
            var facts = FactsOf(ad, car, catalog, factory);
            ad.Description = Write(facts, random ?? Random.Shared);
            Offer(facts, ad.Description, better => ad.Description = better);
        }

        /// <summary>The part ad's words; none for a part the catalog does not know</summary>
        public static void Describe(PartAd ad, PartsCatalog catalog, Random? random = null)
        {
            if (FactsOf(ad, catalog) is not { } facts) return;
            ad.Description = Write(facts, random ?? Random.Shared);
            Offer(facts, ad.Description, better => ad.Description = better);
        }

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

        public static bool Ensure(PartAd ad, PartsCatalog catalog)
        {
            if (ad.Description != null) return false;
            Describe(ad, catalog);
            return ad.Description != null;
        }

        /// <summary>The ad to the better writer, if there is one; one that fails leaves the game's own words</summary>
        private static void Offer(AdFacts facts, string written, Action<string> rewrite)
        {
            if (Writer is not { } writer) return;

            try
            {
                writer.Improve(facts, written, better =>
                {
                    if (!string.IsNullOrWhiteSpace(better)) rewrite(better.Trim());
                });
            }
            catch (Exception ex)
            {
                Logger.Warning("The ad writer failed; the ad keeps its own words: {Error}", ex.Message);
            }
        }
    }
}
