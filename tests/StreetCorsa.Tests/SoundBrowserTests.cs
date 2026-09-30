using StreetCorsa.Audio;
using StreetCorsa.Parts.Sounds;
using StreetCorsa.Screens.SoundBrowser;

namespace StreetCorsa.Tests;

public class SoundFilesTests
{
    [Fact]
    public void Csv_fields_in_quotes_keep_their_commas_quotes_and_line_breaks()
    {
        var rows = SoundFiles.ParseCsv("a,b,c\r\n1,\"x, \"\"y\"\"\",\"two\nlines\"\n,last,\n");

        Assert.Equal(3, rows.Count);
        Assert.Equal(["a", "b", "c"], rows[0]);
        Assert.Equal(["1", "x, \"y\"", "two\nlines"], rows[1]);
        Assert.Equal(["", "last", ""], rows[2]);
    }

    [Fact]
    public void A_last_line_without_a_break_still_counts()
    {
        var rows = SoundFiles.ParseCsv("a,b\n1,2");
        Assert.Equal(["1", "2"], rows[1]);
    }

    [Theory]
    [InlineData("Candidates/ui/purchase/x.ogg", AudioBus.Interface)]
    [InlineData("Candidates/kenney/ui-audio/Audio/click1.ogg", AudioBus.Interface)]
    [InlineData("Candidates/kenney/interface-sounds/Audio/back_001.ogg", AudioBus.Interface)]
    [InlineData("Candidates/kenney/impact-sounds/Audio/impactMetal_000.ogg", AudioBus.Effects)]
    [InlineData("Candidates/ambience/junkyard/y.ogg", AudioBus.Ambience)]
    [InlineData("radio/static_tuning/z.ogg", AudioBus.Radio)]
    [InlineData("Candidates/tools/ratchet/r.ogg", AudioBus.Effects)]
    [InlineData("stray.mp3", AudioBus.Effects)]
    [InlineData(@"ui\click.wav", AudioBus.Interface)]
    public void A_sound_plays_on_its_folders_bus(string path, AudioBus bus) => Assert.Equal(bus, SoundFiles.BusFor(path));

    [Fact]
    public void A_scan_finds_the_sounds_and_what_the_credits_say_of_them()
    {
        var root = Directory.CreateTempSubdirectory("sc_sounds_");
        try
        {
            var ratchet = Directory.CreateDirectory(Path.Combine(root.FullName, "Candidates", "tools", "ratchet"));
            File.WriteAllBytes(Path.Combine(ratchet.FullName, "1_a_ratchet.ogg"), [0]);
            File.WriteAllBytes(Path.Combine(ratchet.FullName, "notes.txt"), [0]);
            var kenney = Directory.CreateDirectory(Path.Combine(root.FullName, "Candidates", "kenney", "ui-audio", "Audio"));
            File.WriteAllBytes(Path.Combine(kenney.FullName, "click1.ogg"), [0]);
            File.WriteAllText(Path.Combine(root.FullName, "Candidates", "credits.csv"),
                "category,file,id,title,user,license,duration,url\n" +
                "tools/ratchet,tools/ratchet/1_a_ratchet.ogg,1,\"Ratchet, fast\",someone,CC0 1.0,,https://freesound.org/s/1/\n");

            var files = SoundFiles.Scan(root.FullName);

            Assert.Equal(2, files.Count);
            var kenneyClick = files[0];
            Assert.Equal("Candidates/kenney/ui-audio/Audio", kenneyClick.Category);
            Assert.Equal("click1", kenneyClick.Title);
            Assert.Equal("Kenney", kenneyClick.Author);
            Assert.Equal(AudioBus.Interface, kenneyClick.Bus);

            var credited = files[1];
            Assert.Equal("Candidates/tools/ratchet/1_a_ratchet.ogg", credited.RelativePath);
            Assert.Equal("Ratchet, fast", credited.Title);
            Assert.Equal("someone", credited.Author);
            Assert.Equal("CC0 1.0", credited.License);
            Assert.Equal("https://freesound.org/s/1/", credited.Url);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [Fact]
    public void The_dev_root_is_found_above_the_program()
    {
        var root = Directory.CreateTempSubdirectory("sc_devroot_");
        try
        {
            var sound = Directory.CreateDirectory(Path.Combine(root.FullName, "Assets", "Sound"));
            var bin = Directory.CreateDirectory(Path.Combine(root.FullName, "bin", "Debug", "net10.0-windows"));

            Assert.Equal(sound.FullName, SoundFiles.FindDevRoot(bin.FullName));
        }
        finally
        {
            root.Delete(true);
        }
    }

    [Fact]
    public void Picks_are_written_at_once_and_read_back()
    {
        var root = Directory.CreateTempSubdirectory("sc_picks_");
        try
        {
            var path = Path.Combine(root.FullName, SoundPicks.FileName);
            var picks = new SoundPicks(path);
            picks.Set("Candidates/tools/ratchet/a.ogg", true);
            picks.Set("Candidates/ui/b.ogg", true);
            picks.Set("Candidates/ui/b.ogg", false);

            var again = new SoundPicks(path);
            Assert.True(again.IsKept("candidates/TOOLS/ratchet/a.ogg"));
            Assert.False(again.IsKept("Candidates/ui/b.ogg"));
            Assert.Equal(1, again.Count);
        }
        finally
        {
            root.Delete(true);
        }
    }
}

public class SoundBrowserTests
{
    [Theory]
    [InlineData("Candidates/tools/ratchet", "tools › ratchet")]
    [InlineData("Candidates/kenney/ui-audio/Audio", "kenney › ui-audio")]
    [InlineData("Candidates/kenney/ui-audio", "kenney › ui-audio")]
    [InlineData("ui/clicks", "ui › clicks")]
    [InlineData("Candidates", "Candidates")]
    [InlineData("", "(sound folder)")]
    public void A_folder_is_titled_without_the_candidates_and_a_packs_audio_folder(string category, string title) =>
        Assert.Equal(title, SoundBrowserScreenViewModel.FolderTitle(category));

    [Theory]
    [InlineData(null, 6200.0)]
    [InlineData(6000.0, 5700.0)]
    [InlineData(3000.0, 4500.0)]
    [InlineData(20000.0, 8000.0)]
    public void The_stand_in_engine_revs_to_what_its_bank_reaches(double? rpmMax, double limiter)
    {
        var entry = new SoundEntry { Id = "x", Name = "X", BankPath = "x.bank", GuidsText = "", DonorId = "x", RpmMax = rpmMax, Cylinders = 6 };

        var spec = SoundBrowserScreenViewModel.SpecFor(new EngineItem(entry));

        Assert.Equal(limiter, spec.LimiterRpm);
        Assert.Equal(6, spec.Cylinders);
        Assert.True(spec.Runs);
        Assert.Equal("x.bank", spec.Sound!.BankPath);
        Assert.InRange(spec.TorqueAt(spec.IdleRpm), 1, spec.PeakTorque);
    }
}
