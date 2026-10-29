using StreetCorsa.Models.GameState;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;

namespace StreetCorsa.Tests;

/// <summary>A part's <c>fits_suspension</c>: a live axle under a car on one, an independent rear's differential not</summary>
public sealed class SuspensionFitTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private static PartDefinition Needing(string? suspension)
    {
        var part = new PartDefinition { Id = "x", Class = "rear_axle" };
        if (suspension != null) part.Specs["fits_suspension"] = suspension;
        return part;
    }

    [Fact]
    public void A_part_fits_the_suspension_it_names_and_independent_takes_all_but_a_live_axle()
    {
        Assert.True(RunningGear.FitsSuspension(Needing("AXLE"), "AXLE"));
        Assert.False(RunningGear.FitsSuspension(Needing("AXLE"), "DWB"));
        Assert.True(RunningGear.FitsSuspension(Needing("dwb"), "DWB"));
        Assert.True(RunningGear.FitsSuspension(Needing("independent"), "STRUT"));
        Assert.True(RunningGear.FitsSuspension(Needing("INDEPENDENT"), "DWB"));
        Assert.False(RunningGear.FitsSuspension(Needing("INDEPENDENT"), "axle"));

        // Nothing named, or a car whose data does not say: any
        Assert.True(RunningGear.FitsSuspension(Needing(null), "AXLE"));
        Assert.True(RunningGear.FitsSuspension(Needing("AXLE"), null));
    }

    [Fact]
    public void The_workbench_offers_a_live_axle_only_under_a_car_on_one()
    {
        _temp.Parts("parts/axles", """
            [
                { "id": "axles/live", "class": "rear_axle", "mass": 50, "specs": { "fits_suspension": "AXLE" } },
                { "id": "axles/irs", "class": "rear_axle", "mass": 30, "specs": { "fits_suspension": "INDEPENDENT" } },
                { "id": "axles/any", "class": "rear_axle", "mass": 40 }
              ]
            """);
        var catalog = PartsCatalog.Load(_temp.Combine("parts"));
        bool Offered(string id, bool liveAxle) =>
            Workbench.FindPlaces(catalog, [], new PartInstance(id), liveAxle, rearSuspension: liveAxle ? "AXLE" : "DWB").Any(p => p.ParentSlot == RearAxle.CarSlot);

        Assert.True(Offered("axles/live", liveAxle: true));
        Assert.False(Offered("axles/live", liveAxle: false));
        Assert.False(Offered("axles/irs", liveAxle: true));
        Assert.True(Offered("axles/irs", liveAxle: false));
        Assert.True(Offered("axles/any", liveAxle: true));
        Assert.True(Offered("axles/any", liveAxle: false));

        // A rear whose type the data does not give takes any, as the factory's housing is picked for it
        Assert.True(Workbench.FindPlaces(catalog, [], new PartInstance("axles/live"), liveAxle: false, rearSuspension: null).Any(p => p.ParentSlot == RearAxle.CarSlot));
    }
}
