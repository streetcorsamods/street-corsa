using StreetCorsa.Mods;
using StreetCorsa.Navigation;

namespace StreetCorsa.Tests;

/// <summary>
/// A made-up setting's places for the tests of the flow: the core has none of its own (the garage, its car lot, the racing
/// hub, the career, the race and the tools only), so a test that needs a diner, the street, the shops, the dealers, the
/// paper, a gas station or a paint booth gets them from this flow, laid over the core's as a mod's would be.
/// </summary>
internal static class TestPlaces
{
    /// <summary>The places' flow: every kind of place screen, wired to each other and to the garage, with their tiles</summary>
    public const string Flow = """
        {
          "screens": {
            "racing_hub": { "exits": { "streets": "diner_map" } },
            "diner_map": { "kind": "diner_map", "exits": { "back": "garage", "diner": "diner" } },
            "diner": { "kind": "diner", "exits": { "back": "garage", "cruise": "street", "map": "diner_map" } },
            "street": { "kind": "street", "exits": { "back": "garage", "diner": "diner" } },
            "gas_station": { "kind": "gas_station", "exits": { "back": "garage" } },
            "paint_booth": { "kind": "paint_booth", "exits": { "back": "garage" } },
            "newspaper": { "kind": "newspaper", "exits": { "back": "garage", "car_ad": "car_viewing" } },
            "car_viewing": { "kind": "car_viewing", "exits": { "back": "newspaper", "gone": "newspaper", "bought": "garage", "garage": "garage" } },
            "dealer_map": { "kind": "dealer_map", "exits": { "back": "garage", "lot": "dealer_lot" } },
            "dealer_lot": { "kind": "dealer_lot", "exits": { "back": "dealer_map", "bought": "garage" } },
            "part_shops_map": { "kind": "part_shops_map", "exits": { "back": "garage", "shop": "part_shop", "mail_order": "mail_order" } },
            "part_shop": { "kind": "part_shop", "exits": { "back": "part_shops_map" } },
            "mail_order": { "kind": "mail_order", "exits": { "back": "part_shops_map" } }
          },
          "tiles": {
            "garage": {
              "car_dealers": { "to": "dealer_map", "text": "garage.tile.car_dealers", "icon": "icons/car_dealer", "spot": "bottom_left", "order": 1, "size": "large", "padding": 9 },
              "part_dealers": { "to": "part_shops_map", "text": "garage.tile.part_dealers", "icon": "icons/parts_filled", "spot": "bottom_left", "order": 2, "size": "large" },
              "newspaper": { "to": "newspaper", "text": "garage.tile.newspaper", "icon": "icons/newspaper", "spot": "bottom_left", "order": 3, "size": "large" },
              "mail_order": { "to": "mail_order", "text": "garage.tile.mail_order", "icon": "icons/parts_filled", "spot": "bottom_left", "order": 4, "size": "large", "without": [ "partShops" ] },
              "gas_station": { "to": "gas_station", "text": "garage.tile.gas_station", "icon": "icons/fuel_pump", "spot": "bottom_right", "order": 1, "requires": [ "car_here" ] },
              "paint_booth": { "to": "paint_booth", "text": "garage.tile.paint_booth", "icon": "icons/paint_spray", "spot": "bottom_right", "order": 2, "requires": [ "car_here" ] }
            }
          }
        }
        """;

    /// <summary>The id of the mod the places come in</summary>
    public const string ModId = "places";

    /// <summary>
    /// A Content folder in <paramref name="temp"/> of the core's flow (from the source tree) and a mod with <see cref="Flow"/>
    /// (or <paramref name="flow"/>): the mods of a game that has every place
    /// </summary>
    public static ModSet Mods(TempDir temp, string? flow = null)
    {
        temp.File($"{ModIds.BaseId}/{ModSet.ManifestFileName}", $$"""{ "id": "{{ModIds.BaseId}}" }""");
        temp.File($"{ModIds.BaseId}/flow/flow.json", File.ReadAllText(Path.Combine(RepoPaths.BaseContent, "flow", "flow.json")));
        temp.File($"{ModId}/{ModSet.ManifestFileName}", $$"""{ "id": "{{ModId}}" }""");
        temp.File($"{ModId}/flow/flow.json", flow ?? Flow);
        return ModSet.Load(temp.Path);
    }

    /// <summary>The core's flow with every place laid over it</summary>
    public static FlowBook Book(TempDir temp) => FlowBook.Load(Mods(temp), problems: []);

    /// <summary>
    /// Plays <paramref name="mods"/> as the content in play until disposed (the core's again after): for a test of what
    /// the game in play has (<see cref="FlowBook.InPlay"/>). Only in <see cref="GlobalsCollection"/>.
    /// </summary>
    public static IDisposable Play(ModSet mods)
    {
        var before = ModSet.Installed;
        ModSet.Play(mods);
        return new Restore(before);
    }

    private sealed class Restore(ModSet before) : IDisposable
    {
        public void Dispose() => ModSet.Play(before);
    }
}
