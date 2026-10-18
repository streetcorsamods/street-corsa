using StreetCorsa.Models.Catalog;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Services.Catalog;

namespace StreetCorsa.Tests;

public class CarProfileServiceTests
{
    private readonly MemoryCatalog _catalog = new();
    private readonly MemoryProfiles _profiles = new();
    private readonly CarProfileService _service;

    public CarProfileServiceTests()
    {
        TestLogging.SilenceLogging();
        _service = new CarProfileService(_catalog, _profiles);
    }

    private static CarDefinition Car(string? bhp, string? weight, string brand = "Plymouth", int? year = null,
        ContentSource source = ContentSource.Unknown, string id = "car") => new()
    {
        Id = id,
        Name = "Test",
        Brand = brand,
        Year = year,
        Source = source,
        ContentHash = "hash",
        Specs = bhp == null && weight == null ? null : new CarSpecsData { Bhp = bhp, Weight = weight }
    };

    /// <summary>The real parts, every car leaving the factory with the build whose id ends with what the car's name says</summary>
    private sealed class FactoryEngines : NullCarPartsService
    {
        private readonly CatalogParts _parts = new();

        public override StreetCorsa.Parts.PartsCatalog Catalog => _parts.Catalog;
        public override EngineBuildIndex Builds => _parts.Builds;
        public override bool IsAvailable => true;

        public override RatedBuild? GetStockBuild(CarDefinition car) =>
            Builds.Runnable.FirstOrDefault(b => b.Build.Id.EndsWith(car.Name, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("1400kg", 2500)] // the reference body
    [InlineData("1000kg", 4100)] // lighter: dearer
    [InlineData("2000kg", 1500)] // a heavy cruiser: cheaper
    [InlineData(null, 2500)] // no weight: the reference
    [InlineData("90kg", 2500)] // not believable: the same
    public void Without_parts_a_car_is_priced_by_its_body_the_lighter_the_dearer(string? weight, int expected)
    {
        Assert.Equal(expected, _service.CalculateBasePrice(Car("150bhp", weight)));
    }

    [Fact]
    public void Its_badge_year_and_source_do_nothing_to_its_price()
    {
        Assert.Equal(_service.CalculateBasePrice(Car("300bhp", "1600kg")),
            _service.CalculateBasePrice(Car("300bhp", "1600kg", "Ferrari", 2005, ContentSource.Kunos)));
    }

    [Fact]
    public void A_car_costs_its_body_and_its_factory_engines_parts()
    {
        var service = new CarProfileService(_catalog, _profiles, new FactoryEngines());
        var car = Car("275bhp", "1400kg");
        car.Name = "chrysler-la-340-275-hp";

        var price = service.PriceNew(car);
        var engine = new FactoryEngines().GetStockBuild(car)!;
        var catalog = new CatalogParts().Catalog;
        var parts = engine.Build.Parts.Sum(p => catalog.Get(p) is { } d ? PartPricing.NewPrice(d) : 0);

        Assert.Equal(2500m, price.Body);
        Assert.Equal(Math.Round((decimal)parts), price.Engine);
        Assert.Equal(Math.Round((2500m + price.Engine) / 100) * 100, price.BasePrice);
    }

    [Fact]
    public void A_stronger_factory_engine_makes_a_dearer_car_and_a_heavier_body_a_cheaper_one()
    {
        var service = new CarProfileService(_catalog, _profiles, new FactoryEngines());
        CarDefinition With(string build, string weight)
        {
            var car = Car("100bhp", weight);
            car.Name = build;
            return car;
        }

        var builds = new FactoryEngines().Builds.Runnable.OrderBy(b => b.PowerHp).ToList();
        var weak = builds.First(b => !b.Build.Aftermarket).Build.Id;
        var strong = builds.Last(b => !b.Build.Aftermarket).Build.Id;

        Assert.True(service.CalculateBasePrice(With(strong, "1400kg")) > service.CalculateBasePrice(With(weak, "1400kg")));
        Assert.True(service.CalculateBasePrice(With(strong, "2000kg")) < service.CalculateBasePrice(With(strong, "1400kg")));
    }

    [Fact]
    public void Precedence_tiers_by_hp_per_tonne()
    {
        Assert.Equal(0.5f, _service.CalculatePrecedence(Car("150bhp", "1000kg")), 3); // neither
        Assert.Equal(0.7f, _service.CalculatePrecedence(Car("60bhp", "1200kg")), 3); // economy
        Assert.Equal(0.3f, _service.CalculatePrecedence(Car("500bhp", "1000kg")), 3); // exotic
        Assert.Equal(0.5f, _service.CalculatePrecedence(Car(null, null)), 3); // no specs: no tier
    }

    [Fact]
    public async Task Every_profile_is_priced_again_at_the_start_and_how_common_a_hand_edited_one_is_left()
    {
        _catalog.UpsertCar(Car("150bhp", "1000kg", id: "generated"));
        _catalog.UpsertCar(Car("150bhp", "1000kg", id: "manual"));
        _profiles.UpsertProfile(new CarProfile
        {
            CarDefinitionId = "generated", BasePrice = 5000m, DealerPrecedence = 0.1f, Source = ProfileDataSource.Generated, DefinitionHash = "hash|specs2"
        });
        _profiles.UpsertProfile(new CarProfile
        {
            CarDefinitionId = "manual", BasePrice = 1234m, DealerPrecedence = 0.1f, Source = ProfileDataSource.Manual, DefinitionHash = "hash|specs2"
        });

        await _service.EnsureProfilesExistAsync();

        Assert.Equal(4100m, _profiles.Profiles["generated"].BasePrice);
        Assert.Equal(4141m, _profiles.Profiles["generated"].BodyPrice);
        Assert.Equal(0.5f, _profiles.Profiles["generated"].DealerPrecedence, 3);
        Assert.Equal(4100m, _profiles.Profiles["manual"].BasePrice);
        Assert.Equal(0.1f, _profiles.Profiles["manual"].DealerPrecedence, 3);
        Assert.Equal("hash|specs2", _profiles.Profiles["manual"].DefinitionHash);
    }
}
