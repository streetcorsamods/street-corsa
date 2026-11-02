using StreetCorsa.Installer;
using StreetCorsa.Services;

namespace StreetCorsa.Tests;

/// <summary>
/// Races need a CSP whose Lua has what the race mode calls: on 0.1.80-preview218 (build 2363) sc_race failed to load
/// (no ac.onCarCollision) and every race came back empty, a forfeit (2026-11-01, a clean AC). The game now refuses such a
/// race before it starts, and the installer's checks page says the CSP is too old.
/// </summary>
public sealed class CspVersionTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    // The manifests' [VERSION] as CSP writes them (the two installs this was found on), with what goes before it
    private const string Old = "; For Content Manager or other launchers\r\n\r\n[ℹ]\r\nFULLNAME=Information\r\n\r\n[VERSION]\r\n; hidden\r\n\r\nSHADERS_PATCH=0.1.80-preview218\r\nSHADERS_PATCH_BUILD=2363\r\n\r\n[FEATURES]\r\n; hidden\r\nCSP_TEST_ONLINE=1\r\n";
    private const string Played = "[ℹ]\r\nFULLNAME=Information\r\n\r\n[VERSION]\r\n; hidden\r\nSHADERS_PATCH=0.2.11\r\nSHADERS_PATCH_BUILD=3465\r\n";

    private string AcRoot(string? manifest)
    {
        var ac = _temp.Combine("ac");
        Directory.CreateDirectory(Path.Combine(ac, "extension"));
        if (manifest != null) _temp.File(Path.Combine("ac", "extension", "config", "data_manifest.ini"), manifest);
        return ac;
    }

    private string ModeSource()
    {
        foreach (var name in new[] { "mode.lua", "car_parts.lua" })
            _temp.File(Path.Combine("build", "sc_race", name), "-- " + name);
        return _temp.Combine("build", "sc_race");
    }

    [Fact]
    public void The_version_is_read_from_csps_manifest()
    {
        Assert.Equal(new CspVersion("0.1.80-preview218", 2363), CspVersion.Read(AcRoot(Old)));
        Assert.True(CspVersion.Read(AcRoot(Old))!.TooOld);
    }

    [Fact]
    public void The_build_the_races_are_played_on_is_new_enough()
    {
        var csp = CspVersion.Read(AcRoot(Played))!;
        Assert.Equal(new CspVersion("0.2.11", 3465), csp);
        Assert.False(csp.TooOld);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("[VERSION]\r\nSHADERS_PATCH=0.2.11\r\n")]
    [InlineData("[VERSION]\r\nSHADERS_PATCH_BUILD=soon\r\n")]
    public void A_manifest_missing_or_without_a_build_tells_nothing(string? manifest) =>
        Assert.Null(CspVersion.Read(AcRoot(manifest)));

    [Theory]
    [InlineData("[VERSION]\r\nSHADERS_PATCH=0.1.80-preview218 ; note\r\nSHADERS_PATCH_BUILD=2363 ; note\r\n")]
    [InlineData("[VERSION]\r\nSHADERS_PATCH=0.1.80-preview218 // note\r\nSHADERS_PATCH_BUILD=2363 // note\r\n")]
    public void The_installer_reads_a_commented_manifest_as_the_game_does(string manifest)
    {
        var game = CspVersion.Read(AcRoot(manifest))!;
        var installer = InstallerChecks.CspOf(manifest)!.Value;

        Assert.Equal(new CspVersion("0.1.80-preview218", 2363), game);
        Assert.Equal((game.Name, game.Build), installer);
    }

    [Fact]
    public void One_check_tells_no_csp_too_old_and_unknown_apart()
    {
        Assert.True(CspVersion.Check(_temp.Combine("no-ac")).Missing);
        // One AC folder, its manifest written in turn: none first
        Assert.True(CspVersion.Check(AcRoot(null)).Unknown);
        Assert.True(CspVersion.Check(AcRoot(Old)).TooOld);
        var played = CspVersion.Check(AcRoot(Played));
        Assert.False(played.TooOld || played.Missing || played.Unknown);
    }

    [Fact]
    public void A_build_with_no_name_is_called_by_its_number() =>
        Assert.Equal(new CspVersion("build 3500", 3500), CspVersion.Read(AcRoot("[VERSION]\r\nSHADERS_PATCH_BUILD=3500\r\n")));

    [Fact]
    public void A_race_on_a_csp_too_old_is_refused_and_its_mode_not_put_in()
    {
        var ac = AcRoot(Old);

        var why = new ScRaceMode(ModeSource()).Install(ac);

        Assert.NotNull(why);
        Assert.False(Directory.Exists(ScRaceMode.InstalledFolder(ac)));
    }

    [Theory]
    [InlineData(Played)]
    [InlineData(null)]
    public void A_race_goes_on_the_played_csp_and_on_one_whose_version_is_not_known(string? manifest)
    {
        var ac = AcRoot(manifest);

        Assert.Null(new ScRaceMode(ModeSource()).Install(ac));
        Assert.True(File.Exists(Path.Combine(ScRaceMode.InstalledFolder(ac), "mode.lua")));
    }

    [Fact]
    public void The_installer_reads_the_manifest_as_the_game_does_against_the_same_minimum()
    {
        Assert.Equal(("0.1.80-preview218", 2363), InstallerChecks.CspOf(Old));
        Assert.Equal(("0.2.11", 3465), InstallerChecks.CspOf(Played));
        Assert.Equal(("build 3500", 3500), InstallerChecks.CspOf("[VERSION]\nSHADERS_PATCH_BUILD=3500\n"));
        Assert.Null(InstallerChecks.CspOf("[FEATURES]\nSHADERS_PATCH_BUILD=3500\n"));
        Assert.Null(InstallerChecks.CspOf(null));

        Assert.Equal(CspVersion.MinimumBuild, InstallerChecks.CspMinimumBuild);
    }
}
