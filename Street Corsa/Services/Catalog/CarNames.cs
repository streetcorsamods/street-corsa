using StreetCorsa.Logging;
using StreetCorsa.Models.Catalog;

namespace StreetCorsa.Services.Catalog
{
    /// <summary>A car as people say it: "1969 Chevrolet Camaro"</summary>
    public static class CarNames
    {
        private static readonly IAppLogger Logger = AppLoggerFactory.CreateLogger(LogCategory.Catalog);

        /// <summary>Year, make and model; the folder name when the catalog doesn't know the car or can't be read</summary>
        public static string Of(IContentCatalogRepository catalog, string carDefinitionId)
        {
            try
            {
                if (catalog.GetCar(carDefinitionId) is { } definition) return Of(definition);
            }
            catch (Exception ex)
            {
                Logger.Warning("Could not look the {Car} up: {Error}", carDefinitionId, ex.Message);
            }

            return carDefinitionId;
        }

        /// <summary>
        /// <see cref="Of(IContentCatalogRepository, string)"/> with each car looked up once: for a review that names
        /// car after car. For that review only, on one thread.
        /// </summary>
        public static Func<string, string> Book(IContentCatalogRepository catalog)
        {
            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            return id => names.TryGetValue(id, out var name) ? name : names[id] = Of(catalog, id);
        }

        /// <summary>
        /// The model as people say it in passing: no make, and nothing from the year on, which in the catalog's names
        /// is the engine and gearbox ("Charger R/T 1969 440 Magnum 4-speed" is a Charger R/T). The whole name when
        /// that would leave nothing.
        /// </summary>
        public static string Short(CarDefinition definition)
        {
            var name = definition.Name.Trim();
            if (definition.Brand.Length > 0 && name.StartsWith(definition.Brand, StringComparison.OrdinalIgnoreCase))
                name = name[definition.Brand.Length..].Trim();

            var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var year = Array.FindIndex(words, w => w.Length == 4 && (w.StartsWith("19", StringComparison.Ordinal) || w.StartsWith("20", StringComparison.Ordinal)) && w.All(char.IsDigit));
            var kept = year > 0 ? string.Join(' ', words.Take(year)) : name;
            return kept.Length > 0 ? kept : definition.Name;
        }

        /// <summary>
        /// Make and model, the make said once: not again when the name holds it ("The Batmobile" of Batmobile, "Chevrolet
        /// Camaro" of Chevrolet) or starts with its initials ("AMC Javelin" of American Motor Company)
        /// </summary>
        public static string MakeAndModel(CarDefinition definition)
        {
            var brand = definition.Brand.Trim();
            if (brand.Length == 0 || definition.Name.Contains(brand, StringComparison.OrdinalIgnoreCase)) return definition.Name;

            var words = brand.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var first = definition.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
            return words.Length > 1 && string.Equals(first, string.Concat(words.Select(w => char.ToUpperInvariant(w[0]))), StringComparison.OrdinalIgnoreCase)
                ? definition.Name
                : $"{brand} {definition.Name}";
        }

        public static string Of(CarDefinition definition)
        {
            var name = MakeAndModel(definition);
            return definition.Year is { } year && !name.Contains(year.ToString()) ? $"{year} {name}" : name;
        }
    }
}
