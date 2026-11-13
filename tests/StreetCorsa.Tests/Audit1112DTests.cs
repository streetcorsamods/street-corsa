using System.IO.Compression;
using StreetCorsa.Mods;
using StreetCorsa.Parts.Logic;
using StreetCorsa.Rules;

namespace StreetCorsa.Tests;

/// <summary>
/// The 2026-11-12 tech audit's fixes in the parts, the rules and the mods: a zip's directory sized before it is read,
/// what a delete leaves named as gone, part speeds the parts add up to held to a part's own, a sunset within the day
/// </summary>
public sealed class Audit1112DTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private string Content => _temp.Combine("Content");

    private string Zip(string name, params (string Path, string Text)[] entries)
    {
        var path = _temp.Combine("zips", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (entry, text) in entries)
        {
            using var writer = new StreamWriter(zip.CreateEntry(entry).Open());
            writer.Write(text);
        }

        return path;
    }

    // ---- the zip's directory ----

    [Fact]
    public void A_zip_whose_end_record_claims_a_huge_directory_is_refused_before_it_is_read()
    {
        var zip = Zip("huge.zip", ("huge/mod.json", """{ "id": "huge" }"""));
        Assert.Null(ModImport.DirectoryTooBig(zip));

        // The end record is the last 22 bytes of a zip with no comment: its directory's size at 12
        var bytes = File.ReadAllBytes(zip);
        BitConverter.TryWriteBytes(bytes.AsSpan(bytes.Length - 22 + 12), 0x7FFF_FFFFu);
        File.WriteAllBytes(zip, bytes);

        Assert.NotNull(ModImport.DirectoryTooBig(zip));
        var refused = Assert.Single(ModImport.Plan([zip], Content).Refused);
        Assert.Equal(ImportRefusal.NotAZip, refused.Why);
        Assert.Contains("directory", refused.Detail);
    }

    [Fact]
    public void A_file_that_is_no_zip_is_left_to_the_zip_reader_to_refuse()
    {
        var notZip = _temp.File("zips/text.zip", "no zip at all");

        Assert.Null(ModImport.DirectoryTooBig(notZip));
        Assert.Equal(ImportRefusal.NotAZip, Assert.Single(ModImport.Plan([notZip], Content).Refused).Why);
    }

    // ---- what deletes leave ----

    [Fact]
    public void A_folder_set_aside_makes_the_replaced_folder_when_it_is_gone()
    {
        _temp.File("Content/addons/radio/mod.json", "{}");
        var to = Path.Combine(Content, ModImport.ReplacedFolder, "x");

        ModImport.MoveAside(Path.Combine(Content, "addons", "radio"), to);

        Assert.True(File.Exists(Path.Combine(to, "mod.json")));
    }

    [Fact]
    public void A_folder_set_aside_that_goes_is_renamed_gone_first_and_a_kept_one_is_never_swept()
    {
        _temp.File("Content/_replaced/old/file.txt", "x");
        _temp.File("Content/_replaced/kept/mod.json", "{}");
        var aside = Path.Combine(Content, ModImport.ReplacedFolder);

        ModImport.DeleteAside(Path.Combine(aside, "old"));
        ModImport.SweepReplaced(aside);

        Assert.False(Directory.Exists(Path.Combine(aside, "old")));
        Assert.False(Directory.Exists(Path.Combine(aside, "old" + ModImport.GoneSuffix)));
        Assert.True(File.Exists(Path.Combine(aside, "kept", "mod.json")));
    }

    // ---- part speeds ----

    private static DynoInputs Engine() => PartFigures.DataEngineBase with { MaxRpm = 6500, FloatRpm = 5000 };

    private static PartFigureSet Change(string name, FigureOp op, double value) => new() { Numbers = [new PartFigure(name, op, value)] };

    [Theory]
    [InlineData("max_rpm", FigureOp.Add, 1_000_000)]
    [InlineData("float_rpm", FigureOp.Times, 1000)]
    public void A_speed_added_or_multiplied_past_any_is_held_to_the_most_a_part_may_say(string name, FigureOp op, double value)
    {
        var inputs = PartFigures.Apply(Engine(), [Change(name, op, value)]);

        var speed = name == "max_rpm" ? inputs.MaxRpm : inputs.FloatRpm;
        Assert.InRange(speed, EngineDyno.LeastSpeed, EngineDyno.MaxRpm);
        Assert.True(EngineDyno.TopRpm(inputs) <= EngineDyno.MaxRpm);
    }

    [Fact]
    public void A_speed_taken_below_the_least_is_held_to_it_and_never_counts_as_none()
    {
        var inputs = PartFigures.Apply(Engine(), [Change("max_rpm", FigureOp.Add, -10_000)]);

        Assert.Equal(EngineDyno.LeastSpeed, inputs.MaxRpm);
        Assert.Equal(EngineDyno.LeastSpeed, EngineDyno.RedlineRpm(inputs));
    }

    // ---- the rules ----

    [Theory]
    [InlineData(23, 12, 1)]
    [InlineData(2, 12, 2)]
    public void A_sunset_swing_that_takes_the_sunset_out_of_the_day_is_held_to_it(double mean, double swing, double held)
    {
        var world = new WorldRules { MeanSunset = mean, SunsetSwing = swing };
        var problems = new List<string>();

        world.Check("world", problems);

        Assert.Equal(held, world.SunsetSwing);
        Assert.Contains(problems, p => p.Contains("sunsetSwing"));
    }

    [Fact]
    public void The_core_sunset_is_within_the_day()
    {
        var world = new WorldRules();
        var problems = new List<string>();

        world.Check("world", problems);

        Assert.Empty(problems);
    }

    [Fact]
    public void A_least_oil_bottle_over_the_sump_is_held_to_it()
    {
        var repair = new RepairRules { SumpLitres = 4, LeastOilLitres = 10 };
        var problems = new List<string>();

        repair.Check("repair", problems);

        Assert.Equal(4, repair.LeastOilLitres);
        Assert.Single(problems);
    }
}
