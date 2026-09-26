using StreetCorsa.Services.Storage;

namespace StreetCorsa.Tests;

/// <summary>LegacyRename.Move on folders in a temp folder: the game's old folders going to their new names</summary>
public sealed class LegacyRenameTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly string _old;
    private readonly string _new;

    public LegacyRenameTests()
    {
        _old = _temp.Combine("StreetRodAC");
        _new = _temp.Combine("StreetCorsa");
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void The_whole_folder_moves_when_the_new_one_is_not_there()
    {
        _temp.File(Path.Combine("StreetRodAC", "Saves", "Career.db"), "save");
        _temp.File(Path.Combine("StreetRodAC", "settings.json"), "{}");

        var notes = LegacyRename.Move(_old, _new);

        Assert.Single(notes);
        Assert.False(Directory.Exists(_old));
        Assert.Equal("save", File.ReadAllText(Path.Combine(_new, "Saves", "Career.db")));
        Assert.Equal("{}", File.ReadAllText(Path.Combine(_new, "settings.json")));
    }

    [Fact]
    public void Into_a_new_folder_already_there_only_what_it_lacks_moves_and_nothing_is_overwritten()
    {
        // A start cut short: the logger made the new folder, the saves stayed behind
        _temp.File(Path.Combine("StreetRodAC", "Saves", "Career.db"), "save");
        _temp.File(Path.Combine("StreetRodAC", "settings.json"), "old");
        _temp.File(Path.Combine("StreetCorsa", "settings.json"), "new");
        _temp.File(Path.Combine("StreetCorsa", "Logs", "streetcorsa-20260926.log"), "log");

        LegacyRename.Move(_old, _new);

        Assert.Equal("save", File.ReadAllText(Path.Combine(_new, "Saves", "Career.db")));
        Assert.Equal("new", File.ReadAllText(Path.Combine(_new, "settings.json")));
        Assert.Equal("old", File.ReadAllText(Path.Combine(_old, "settings.json")));
        Assert.False(Directory.Exists(Path.Combine(_old, "Saves")));
    }

    [Fact]
    public void An_emptied_old_folder_goes()
    {
        _temp.File(Path.Combine("StreetRodAC", "a.json"), "a");
        Directory.CreateDirectory(_new);

        LegacyRename.Move(_old, _new);

        Assert.False(Directory.Exists(_old));
        Assert.True(File.Exists(Path.Combine(_new, "a.json")));
    }

    [Fact]
    public void Nothing_to_move_does_nothing()
    {
        Assert.Empty(LegacyRename.Move(_old, _new));
        Assert.False(Directory.Exists(_new));
    }
}
