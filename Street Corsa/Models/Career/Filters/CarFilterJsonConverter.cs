using System.Text.Json;
using System.Text.Json.Serialization;
using StreetCorsa.Mods;

namespace StreetCorsa.Models.Career.Filters
{
    /// <summary>
    /// A car filter from a mod's file: an object with its "type" (brand, origin, years or decade, power, value, all,
    /// any) and the fields of that filter. Events' entry requirements, the kind of car a dealer deals in and the kind a
    /// racer drives are all written this way.
    /// </summary>
    public sealed class CarFilterJsonConverter : JsonConverter<ICarFilter>
    {
        // The filters' own fields read as everything in a mod's files is, enums by name
        private static readonly JsonSerializerOptions FilterOptions = new(ModJson.Options) { Converters = { new JsonStringEnumConverter() } };

        public override ICarFilter? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            return Read(document.RootElement);
        }

        /// <summary>A filter from its JSON; null for none (<c>null</c>)</summary>
        /// <exception cref="JsonException">It is no object, or its type is none of the filters</exception>
        public static ICarFilter? Read(JsonElement element)
        {
            if (element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return null;
            if (element.ValueKind != JsonValueKind.Object)
                throw new JsonException($"{element.GetRawText()} is no filter: write one as {{ \"type\": \"origin\", \"origin\": \"European\" }}");

            var type = ModJson.Text(element, "type")?.ToLowerInvariant();
            return type switch
            {
                "brand" => element.Deserialize<BrandFilter>(FilterOptions),
                "origin" => element.Deserialize<OriginFilter>(FilterOptions),
                "years" or "decade" => element.Deserialize<DecadeFilter>(FilterOptions),
                "power" => element.Deserialize<PowerFilter>(FilterOptions),
                "value" => element.Deserialize<ValueFilter>(FilterOptions),
                "all" or "any" => new CompositeCarFilter(type == "all",
                    (ModJson.TryGet(element, "filters", out var list) && list.ValueKind == JsonValueKind.Array
                        ? list.EnumerateArray().Select(Read).OfType<ICarFilter>()
                        : []).ToArray()),
                _ => throw new JsonException($"\"{type}\" is no car filter (brand, origin, years, power, value, all, any)")
            };
        }

        public override void Write(Utf8JsonWriter writer, ICarFilter value, JsonSerializerOptions options) =>
            throw new NotSupportedException("Car filters are read from the mods, never written");
    }
}
