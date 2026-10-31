using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Parts.Export;

namespace StreetCorsa.Tests;

/// <summary>
/// A car's factory shocks by the parts' <c>came_in</c>, as its springs and brakes: a live rear axle gets the tube shock
/// that came on one, not the front's strut (the Touge Nights AE86, 2026-10-30)
/// </summary>
public sealed class FactoryShocksTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private PartsCatalog Catalog()
    {
        _temp.Parts("parts/gear/test", """
            [
                { "id": "gear/test/rim", "class": "rim", "specs": { "offset": 0.0, "wheel_radius": 0.1905 } },
                { "id": "gear/test/tyre", "class": "tyre", "specs": { "friction": 1.0, "radius": 0.32, "tyre_width": 205, "wheel_radius": 190.5 } },
                { "id": "gear/test/brake", "class": "disc_brake", "specs": { "force": 1000, "radius": 0.1 } },
                { "id": "gear/test/spring", "class": "spring", "specs": { "force": 30000 } },
                { "id": "gear/test/strut", "class": "shock", "specs": { "damping": 3000 } },
                { "id": "gear/test/rear_tube", "class": "shock", "specs": { "damping": 1500 },
                  "came_in": { "axle": "rear", "suspension": [ "AXLE" ] } }
              ]
            """);
        return PartsCatalog.Load(_temp.Combine("parts"));
    }

    private static AcCarSpecs Specs(string rear) => new()
    {
        Traction = "RWD", TotalMass = 950, FrontWeightShare = 0.53,
        Front = new AcAxleSpecs { TyreWidth = 0.205, TyreRadius = 0.32, BrakeTorque = 1000, SpringRate = 30000, DampBump = 3000, DampRebound = 3000, CornerLoad = 250, SuspensionType = "STRUT" },
        Rear = new AcAxleSpecs { TyreWidth = 0.205, TyreRadius = 0.32, BrakeTorque = 600, SpringRate = 25000, DampBump = 3000, DampRebound = 3000, CornerLoad = 220, SuspensionType = rear }
    };

    private static readonly CarFacts Trueno = new("toyota", "toyota", 1983, "Japan");

    [Fact]
    public void A_live_rear_axle_gets_the_shock_that_came_on_one()
    {
        var (front, rear) = RunningGearFactory.Choose(Catalog(), Specs("AXLE"), Trueno)!.Value;

        Assert.Equal("gear/test/strut", front.Shock.Id);
        Assert.Equal("gear/test/rear_tube", rear.Shock.Id);
    }

    [Fact]
    public void Without_a_shock_for_the_car_the_plain_one_nearest_goes_on()
    {
        var catalog = Catalog();

        Assert.Equal("gear/test/strut", RunningGearFactory.Choose(catalog, Specs("STRUT"), Trueno)!.Value.Rear.Shock.Id);
        Assert.Equal("gear/test/strut", RunningGearFactory.Choose(catalog, Specs("AXLE"))!.Value.Rear.Shock.Id);
    }
}
