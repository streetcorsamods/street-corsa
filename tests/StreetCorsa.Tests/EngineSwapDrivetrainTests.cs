using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;

namespace StreetCorsa.Tests;

/// <summary>
/// An engine swapped in keeps the drivetrain (<see cref="EngineFactory.SameDrivetrain"/>): an engine on a transaxle is
/// never swapped for one on a gearbox built along the car, nor the other way, while two on the same class swap freely
/// and one with no gearbox to tell by goes either way
/// </summary>
[Collection(GlobalsCollection.Name)]
public sealed class EngineSwapDrivetrainTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void A_transaxle_engine_is_never_swapped_for_one_built_along_the_car()
    {
        _temp.File("parts/" + PartClasses.FileName, """{ "classes": { "transaxle": "transmission" } }""");
        _temp.Parts("parts/boxes", """
            [
              { "id": "boxes/along", "class": "transmission", "specs": { "gears": [ 2.5, 1.5, 1.0 ], "reverse": 2.5, "final_drive": 3.5 } },
              { "id": "boxes/other_along", "class": "transmission", "specs": { "gears": [ 2.8, 1.6, 1.0 ], "reverse": 2.8, "final_drive": 3.7 } },
              { "id": "boxes/transaxle", "class": "transaxle", "specs": { "gears": [ 3.2, 1.9, 1.3, 1.0 ], "reverse": 3.0, "final_drive": 4.1 } }
            ]
            """);

        var before = PartClasses.Current;
        try
        {
            PartClasses.Current = PartClasses.Load([Path.Combine(RepoPaths.BaseContent, "parts"), _temp.Combine("parts")]);
            var catalog = PartsCatalog.Load(_temp.Combine("parts"));
            static RatedBuild On(params string[] parts) =>
                new(new EngineBuild { Id = string.Join("+", parts), Parts = [.. parts] }, "engines/x/block", "x", 100, 150, 2, 150);

            var (along, otherAlong, transaxle, none) = (On("boxes/along"), On("boxes/other_along"), On("boxes/transaxle"), On());
            Assert.False(EngineFactory.SameDrivetrain(catalog, transaxle, along));
            Assert.False(EngineFactory.SameDrivetrain(catalog, along, transaxle));
            Assert.True(EngineFactory.SameDrivetrain(catalog, along, otherAlong));
            Assert.True(EngineFactory.SameDrivetrain(catalog, transaxle, none));
            Assert.True(EngineFactory.SameDrivetrain(catalog, none, along));
        }
        finally
        {
            PartClasses.Current = before;
        }
    }
}
