using StreetCorsa.Mods;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;

namespace StreetCorsa.Tests;

/// <summary>
/// Touge Nights' engines of one family: the Celica's transverse 3S-GTE on its transaxle is never swapped for an engine
/// on a gearbox built along the car, nor the other way, while the AE86's 4A-GE and the Supra's 2JZ swap freely
/// </summary>
[Collection(GlobalsCollection.Name)]
public sealed class TougeNightsSwapTests
{
    [Fact]
    public void A_transverse_engine_is_never_swapped_for_one_built_along_the_car()
    {
        var mods = ModSet.Load(RepoPaths.Content, only: ["touge_nights"], setting: "touge_nights");
        var before = PartClasses.Current;
        try
        {
            PartClasses.Current = PartClasses.Load(mods.Folders(ContentTypes.Parts).Select(f => f.Dir));
            var catalog = PartsCatalog.Load(mods.ContentOf(ContentTypes.Parts).ToList(), mods);
            var builds = EngineBuildIndex.Create(catalog);
            RatedBuild Build(string block) => builds.Runnable.First(b => b.BlockId == $"engines/{block}/block");

            var (fourAge, threeS, twoJz) = (Build("4age"), Build("3sgte"), Build("2jzgte"));
            Assert.Equal(fourAge.Family, threeS.Family);
            Assert.False(EngineFactory.SameDrivetrain(catalog, threeS, twoJz));
            Assert.False(EngineFactory.SameDrivetrain(catalog, fourAge, threeS));
            Assert.True(EngineFactory.SameDrivetrain(catalog, fourAge, twoJz));
        }
        finally
        {
            PartClasses.Current = before;
        }
    }
}
