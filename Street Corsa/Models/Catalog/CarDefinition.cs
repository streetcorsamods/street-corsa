using LiteDB;

namespace StreetCorsa.Models.Catalog
{
    /// <summary>
    /// Represents a car in the content catalog.
    /// This is the normalized, persistent representation of AC car data.
    /// Immutable identity - does NOT represent gameplay state.
    /// </summary>
    public class CarDefinition
    {
        /// <summary>
        /// Stable ID derived from AC folder name (e.g., "ks_porsche_911_gt3_rs")
        /// This is the immutable primary key
        /// </summary>
        [BsonId]
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Display name of the car
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Car manufacturer/brand
        /// </summary>
        public string Brand { get; set; } = string.Empty;

        /// <summary>
        /// Car description
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Tags from ui_car.json
        /// </summary>
        public List<string> Tags { get; set; } = [];

        /// <summary>
        /// Car class (e.g., "street", "race", "drift")
        /// </summary>
        public string Class { get; set; } = string.Empty;

        /// <summary>
        /// Country of origin
        /// </summary>
        public string? Country { get; set; }

        /// <summary>
        /// Original author/modder
        /// </summary>
        public string? Author { get; set; }

        /// <summary>
        /// The model year its ui_car.json gives, as stored (catalog.db's "Year")
        /// </summary>
        [BsonField("Year")]
        public int? UiYear { get; set; }

        /// <summary>
        /// The model year in the content in play (<see cref="YearIn"/> of <see cref="Parts.Cars.CarBook.Current"/>);
        /// never stored. Read-only: what is given is <see cref="UiYear"/>.
        /// </summary>
        [BsonIgnore]
        public int? Year => YearIn(Parts.Cars.CarBook.Current);

        // The last year worked out and what from: worked out again only when one of them is another
        private sealed record YearFrom(Parts.Cars.CarBook Book, string Id, string Name, string Brand, int? UiYear, int? Year);
        private YearFrom? _year;

        /// <summary>
        /// The model year as <paramref name="book"/> has it: its cars.json's for the car, else its ui_car.json's
        /// (<see cref="UiYear"/>), else one its name says (<see cref="Services.Catalog.CarYear"/>)
        /// </summary>
        public int? YearIn(Parts.Cars.CarBook book)
        {
            var last = _year;
            if (last != null && ReferenceEquals(last.Book, book) && ReferenceEquals(last.Id, Id) && ReferenceEquals(last.Name, Name)
                && ReferenceEquals(last.Brand, Brand) && last.UiYear == UiYear) return last.Year;

            var year = Services.Catalog.CarYear.Of(Id, Name, Brand, UiYear, book);
            _year = new YearFrom(book, Id, Name, Brand, UiYear, year);
            return year;
        }

        /// <summary>
        /// Mod version
        /// </summary>
        public string? Version { get; set; }

        /// <summary>
        /// Mod URL
        /// </summary>
        public string? Url { get; set; }

        /// <summary>
        /// Specifications (BHP, torque, weight, etc.)
        /// </summary>
        public CarSpecsData? Specs { get; set; }

        /// <summary>
        /// Available skins/liveries for this car (folder names from skins directory)
        /// </summary>
        public List<string> AvailableSkins { get; set; } = [];

        /// <summary>
        /// Source classification (Kunos, DLC, Mod, Unknown)
        /// </summary>
        public ContentSource Source { get; set; } = ContentSource.Unknown;

        /// <summary>
        /// Current status of the car
        /// </summary>
        public ContentStatus Status { get; set; } = ContentStatus.Active;

        /// <summary>
        /// Hash of ui_car.json for change detection
        /// </summary>
        public string ContentHash { get; set; } = string.Empty;

        /// <summary>
        /// Hash of preview image for visual change detection
        /// </summary>
        public string? PreviewHash { get; set; }

        /// <summary>
        /// When this entry was first imported
        /// </summary>
        public DateTime ImportedDate { get; set; }

        /// <summary>
        /// When this entry was last updated
        /// </summary>
        public DateTime LastUpdatedDate { get; set; }
    }

    /// <summary>
    /// Car specifications data
    /// </summary>
    public class CarSpecsData
    {
        public string? Bhp { get; set; }
        public string? Torque { get; set; }
        public string? Weight { get; set; }
        public string? TopSpeed { get; set; }
        public string? Acceleration { get; set; }
        public string? PwRatio { get; set; }
        public string? Drivetrain { get; set; }
    }

    /// <summary>
    /// Content source classification
    /// </summary>
    public enum ContentSource
    {
        Unknown,
        Kunos,      // Official Kunos content
        DLC,        // Official DLC
        Mod         // Community mod
    }

    /// <summary>
    /// Content status
    /// </summary>
    public enum ContentStatus
    {
        Active,     // Present in AC installation
        Legacy,     // Missing from AC but referenced by saves
        Broken      // Missing required files or invalid
    }
}
