using System.Reflection;
using StreetCorsa.Installer;
using StreetCorsa.Tools;

namespace StreetCorsa.Tests;

/// <summary>
/// The 2026-11-05 audit's fixes, worker G: the bench never writes into an AC install (by name, by what it holds, through
/// a junction), a kit's pieces are never named after a device, and the uninstall's wait for the game's clean-up is tied
/// to the game's own wait for AC
/// </summary>
public sealed class Audit1105GTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    /// <summary>An AC install of the test's own: content\cars and content\tracks, the game's test of one</summary>
    private string FakeAc()
    {
        var ac = _temp.Combine("AC");
        Directory.CreateDirectory(Path.Combine(ac, "content", "cars", "chevy", "data"));
        Directory.CreateDirectory(Path.Combine(ac, "content", "tracks"));
        Directory.CreateDirectory(Path.Combine(ac, "content", "sfx"));
        Directory.CreateDirectory(Path.Combine(ac, "system"));
        return ac;
    }

    // ----- the bench's output -----

    [Fact]
    public void The_bench_never_writes_at_or_under_a_folder_that_holds_ac()
    {
        var ac = FakeAc();
        var car = _temp.Combine("cars_unpacked", "chevy");
        Assert.True(Configuration.AppSettings.IsInstall(ac), "the bench's rule is the game's");

        Assert.NotNull(BenchOutput.Refusal(ac, car));
        Assert.NotNull(BenchOutput.Refusal(Path.Combine(ac, "content"), car));
        Assert.NotNull(BenchOutput.Refusal(Path.Combine(ac, "content", "sfx"), car));
        Assert.NotNull(BenchOutput.Refusal(Path.Combine(ac, "system"), car));
        Assert.NotNull(BenchOutput.Refusal(Path.Combine(ac, "apps", "new", "folder"), car));
        Assert.Contains("Assetto Corsa install", BenchOutput.Refusal(Path.Combine(ac, "system"), car));

        // Beside it is not in it
        Assert.Null(BenchOutput.Refusal(_temp.Combine("AC_out"), car));
        Assert.Null(BenchOutput.Refusal(_temp.Combine("out", "chevy"), car));
    }

    [Fact]
    public void A_junction_into_ac_or_into_the_car_read_is_refused_where_it_leads()
    {
        var ac = FakeAc();
        var car = _temp.Combine("cars_unpacked", "chevy");
        Directory.CreateDirectory(car);

        var intoCar = _temp.Combine("links", "into_car");
        Junction.Make(intoCar, Path.Combine(ac, "content", "cars", "chevy"));
        Assert.NotNull(BenchOutput.Refusal(intoCar, car));
        Assert.NotNull(BenchOutput.Refusal(Path.Combine(intoCar, "data"), car));

        var intoSystem = _temp.Combine("links", "into_system");
        Junction.Make(intoSystem, Path.Combine(ac, "system"));
        Assert.NotNull(BenchOutput.Refusal(Path.Combine(intoSystem, "new"), car));

        var intoRead = _temp.Combine("links", "into_read");
        Junction.Make(intoRead, car);
        Assert.NotNull(BenchOutput.Refusal(intoRead, car));

        // A junction to a plain folder is a plain folder
        var plain = _temp.Combine("plain");
        Directory.CreateDirectory(plain);
        var intoPlain = _temp.Combine("links", "into_plain");
        Junction.Make(intoPlain, plain);
        Assert.Null(BenchOutput.Refusal(Path.Combine(intoPlain, "out"), car));
        Assert.Equal(Path.Combine(plain, "out"), BenchOutput.Resolved(Path.Combine(intoPlain, "out")), ignoreCase: true);
    }

    // ----- a kit's piece names -----

    [Theory]
    [InlineData("CON")]
    [InlineData("nul")]
    [InlineData("Aux")]
    [InlineData("PRN")]
    [InlineData("COM1")]
    [InlineData("lpt9")]
    [InlineData("CONIN$")]
    [InlineData("con.house")]
    [InlineData("NUL .x")]
    [InlineData("house.")]
    [InlineData("house..")]
    [InlineData("house ")]
    public void A_piece_named_after_a_device_or_ending_in_a_dot_or_space_is_no_plain_name(string id) =>
        Assert.False(PreparedKit.IsPlainName(id));

    [Theory]
    [InlineData("console")]
    [InlineData("com10")]
    [InlineData("house.v2")]
    [InlineData("nul_porch")]
    public void A_name_that_only_starts_like_a_device_is_plain(string id) => Assert.True(PreparedKit.IsPlainName(id));

    [Fact]
    public void The_kit_knows_every_device_the_game_does()
    {
        // The game's PathNames gives a device name a '_' after it: each of those is no plain name to the kit
        var reserved = (HashSet<string>)typeof(Helpers.PathNames).GetField("Reserved", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        Assert.NotEmpty(reserved);
        Assert.All(reserved, name => Assert.False(PreparedKit.IsPlainName(name), name));
    }

    // ----- the uninstall's wait -----

    [Fact]
    public void The_uninstall_gives_the_clean_up_many_times_the_games_own_wait_for_ac()
    {
        // The game's clean-up waits AcExitWait for AC to close before it tells the installer; the installer's limit on
        // the whole clean-up must leave room for that wait many times over, or a slow AC reads as a hung clean-up
        var wait = (TimeSpan)typeof(Services.AcUninstall).GetField("AcExitWait", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        Assert.True(wait > TimeSpan.Zero);
        Assert.True(InstallerChecks.RestoreTimeout >= wait * 10, $"the installer gives {InstallerChecks.RestoreTimeout}, the game waits {wait} for AC");
    }
}
