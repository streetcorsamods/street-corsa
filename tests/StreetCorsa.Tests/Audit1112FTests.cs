using System.Reflection;
using StreetCorsa.Installer;
using StreetCorsa.Tools;

namespace StreetCorsa.Tests;

/// <summary>
/// The 2026-11-12 audit's fixes, worker F: the bench's and the installer's test of an AC install are the game's on
/// both sides, and the kit's device names and the game's are the same list both ways
/// </summary>
public sealed class Audit1112FTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, false, false)]
    public void The_bench_and_the_installer_take_a_folder_for_ac_as_the_game_does(bool cars, bool tracks, bool system)
    {
        var folder = _temp.Combine("AC");
        Directory.CreateDirectory(folder);
        if (cars) Directory.CreateDirectory(Path.Combine(folder, "content", "cars"));
        if (tracks) Directory.CreateDirectory(Path.Combine(folder, "content", "tracks"));
        if (system) Directory.CreateDirectory(Path.Combine(folder, "system"));

        var game = Configuration.AppSettings.IsInstall(folder);
        Assert.Equal(cars && tracks, game);
        Assert.Equal(game, BenchOutput.HoldsAc(folder));
        Assert.Equal(game, InstallerChecks.IsInstall(folder, Directory.Exists));
    }

    [Fact]
    public void Every_device_the_kit_refuses_the_game_renames()
    {
        // The other way round from Audit1105GTests.The_kit_knows_every_device_the_game_does: each device the kit refuses
        // as a piece name the game's PathNames gives a '_' after too, so the two lists cannot drift apart either way
        static HashSet<string> Field(Type type, string name) =>
            (HashSet<string>)type.GetField(name, BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        var kit = Field(typeof(PreparedKit), "Devices");
        var game = Field(typeof(Helpers.PathNames), "Reserved");
        Assert.NotEmpty(kit);
        Assert.All(kit, device => Assert.Contains(device, game, StringComparer.OrdinalIgnoreCase));
        Assert.All(kit, device => Assert.False(PreparedKit.IsPlainName(device), device));
    }
}
