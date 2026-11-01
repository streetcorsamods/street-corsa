using StreetCorsa.Audio;

namespace StreetCorsa.Tests;

/// <summary>Banks found silent are remembered by their bytes, in the user's data, and never measured again</summary>
[Collection(EngineLoudnessCollection.Name)]
public sealed class EngineSilentTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly string _was = EngineLoudness.DataFolder;

    public EngineSilentTests() => EngineLoudness.DataFolder = _temp.Path;

    public void Dispose()
    {
        EngineLoudness.DataFolder = _was;
        _temp.Dispose();
    }

    [Fact]
    public void A_silent_bank_is_remembered_across_starts_and_not_measured_again()
    {
        var engine = Guid.NewGuid();
        Assert.Null(EngineLoudness.Known("abc", engine));

        EngineLoudness.MarkSilent("abc", engine, "nowhere.bank");
        Assert.True(File.Exists(_temp.Combine("engine_silent.json")));

        // A new start reads it back
        EngineLoudness.DataFolder = _temp.Path;
        Assert.False(EngineLoudness.Known("abc", engine));
        Assert.Null(EngineLoudness.Known("abc", Guid.NewGuid()));

        // Known silent: answered without FMOD, which a test has none of
        Assert.False(EngineLoudness.Plays("nowhere.bank", engine, "abc"));
        Assert.Null(EngineLoudness.Measure("nowhere.bank", engine, "abc"));
    }

    [Fact]
    public void A_silent_list_that_does_not_read_is_listened_to_again()
    {
        _temp.File("engine_silent.json", "not json");
        EngineLoudness.DataFolder = _temp.Path;

        Assert.Null(EngineLoudness.Known("abc", Guid.NewGuid()));
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class EngineLoudnessCollection
{
    public const string Name = "EngineLoudness data folder";
}
