using StreetCorsa.Configuration;
using StreetCorsa.Services.Ai;
using StreetCorsa.Services.Settings;
using StreetCorsa.Services.Text;
using StreetCorsa.Screens.Settings;

namespace StreetCorsa.Tests;

/// <summary>
/// The Settings card's one Save: what counts as typed and not saved (the AI's boxes, the AC folder) the way the Save
/// stores it, the folder and the AI written together or not at all, and the credits' sections
/// </summary>
[Collection(GlobalsCollection.Name)]
public sealed class SettingsSaveTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly string _inUse = AppSettings.Instance.AssettoCorsaPath;

    public void Dispose()
    {
        AppSettings.Instance.AssettoCorsaPath = _inUse;
        _temp.Dispose();
    }

    private GameSettingsService Service(string json = "{}")
    {
        var path = _temp.Combine("settings.json");
        File.WriteAllText(path, json);
        return new GameSettingsService(path, null, () => null, () => null);
    }

    private static AiSettingsSection Section(GameSettingsService service, List<string>? told = null) =>
        new(service, new FakeAiText((_, _) => Task.FromResult<string?>(null)), new NoImages(), m => told?.Add(m));

    private sealed class NoImages : IAiImages
    {
        public bool IsAvailable => false;
        public Task<byte[]?> PaintAsync(string use, TextValues values, CancellationToken cancellationToken = default) => Task.FromResult<byte[]?>(null);
        public Task<AiTestResult> TestAsync(string? endpoint, CancellationToken cancellationToken = default) => Task.FromResult(new AiTestResult(false, ""));
    }

    [Fact]
    public void A_box_emptied_or_at_its_usual_address_is_no_change()
    {
        var service = Service();
        var ai = Section(service);
        Assert.False(ai.HasChanges);

        // Saved as none, shown as the usual address: emptied, or typed back, the Save stores the same none
        ai.Endpoint = "";
        Assert.False(ai.HasChanges);
        ai.Endpoint = "  " + AiProviders.DefaultEndpoint(ai.Provider.Id) + " ";
        Assert.False(ai.HasChanges);
        ai.ComfyEndpoint = "";
        Assert.False(ai.HasChanges);
        ai.Model = "   ";
        Assert.False(ai.HasChanges);

        ai.Model = "llama3";
        Assert.True(ai.HasChanges);
    }

    [Fact]
    public void A_saved_usual_address_written_out_is_no_change_either()
    {
        var service = Service();
        service.Current.Ai.Endpoint = AiProviders.DefaultEndpoint(AiProviders.Normalize(service.Current.Ai.TextProvider));
        var ai = Section(service);

        ai.Endpoint = "";
        Assert.False(ai.HasChanges);
    }

    [Fact]
    public void A_key_counts_only_for_a_provider_that_takes_one()
    {
        var service = Service();
        var ai = Section(service);
        ai.Provider = ai.Providers.First(p => p.Id == AiProviders.Ollama);
        Assert.False(ai.HasChanges);

        ai.TypedKey = "sk-123";
        Assert.False(ai.HasChanges);

        ai.Provider = ai.Providers.First(p => p.Id == AiProviders.OpenAi);
        Assert.True(ai.HasChanges);
    }

    [Fact]
    public void A_key_for_an_address_it_may_not_go_to_prepares_nothing()
    {
        var service = Service();
        var told = new List<string>();
        var ai = Section(service, told);
        ai.Provider = ai.Providers.First(p => p.Id == AiProviders.OpenAi);
        ai.Endpoint = "http://example.com/v1";
        ai.TypedKey = "sk-123";

        Assert.Null(ai.Prepare());
        Assert.Single(told);
        Assert.Null(service.Current.Ai.ApiKeyProtected);
    }

    [Fact]
    public void What_is_prepared_is_what_the_boxes_show_and_no_change_once_saved()
    {
        var service = Service();
        var ai = Section(service);
        ai.Model = " llama3 ";
        ai.Endpoint = "";

        var typed = ai.Prepare();
        Assert.NotNull(typed);
        Assert.Equal("llama3", typed.Model);
        Assert.Null(typed.Endpoint);

        Assert.True(SettingsScreenViewModel.SaveTogether(service, typed, service.Current.AssettoCorsaPath, out var error));
        Assert.Null(error);
        ai.Saved(typed);
        Assert.False(ai.HasChanges);
        Assert.Contains("llama3", File.ReadAllText(_temp.Combine("settings.json")));
    }

    [Fact]
    public void A_failed_write_puts_back_both_the_ai_and_the_folder()
    {
        var service = Service();
        var savedAi = service.Current.Ai;
        var savedPath = service.Current.AssettoCorsaPath;

        // The file cannot be written over: a folder stands in its place
        var path = _temp.Combine("settings.json");
        File.Delete(path);
        Directory.CreateDirectory(path);

        var typed = savedAi.Clone();
        typed.Model = "llama3";
        Assert.False(SettingsScreenViewModel.SaveTogether(service, typed, @"D:\Elsewhere\AC", out _));
        Assert.Same(savedAi, service.Current.Ai);
        Assert.Equal(savedPath, service.Current.AssettoCorsaPath);
    }

    [Theory]
    [InlineData(@"C:\GAMES\Assetto Corsa", @"C:\GAMES\Assetto Corsa\")]
    [InlineData(@"C:\GAMES\Assetto Corsa", "C:/GAMES/Assetto Corsa")]
    [InlineData(@"C:\GAMES\Assetto Corsa", @"  c:\games\assetto corsa  ")]
    [InlineData(@"D:\AC", @"D:\AC\.\")]
    public void One_folder_written_two_ways_is_the_same_folder(string a, string b)
    {
        Assert.True(SettingsScreenViewModel.SameFolder(a, b));
    }

    [Fact]
    public void A_blank_folder_is_the_default_and_the_default_is_stored_as_none()
    {
        Assert.True(SettingsScreenViewModel.SameFolder("", AppSettings.DefaultAssettoCorsaPath));
        Assert.False(SettingsScreenViewModel.SameFolder(@"D:\AC", AppSettings.DefaultAssettoCorsaPath));

        Assert.Null(SettingsScreenViewModel.StoredFolder(""));
        Assert.Null(SettingsScreenViewModel.StoredFolder(AppSettings.DefaultAssettoCorsaPath + @"\"));
        Assert.Null(SettingsScreenViewModel.StoredFolder(AppSettings.DefaultAssettoCorsaPath.Replace('\\', '/')));
        Assert.Equal(@"D:\AC", SettingsScreenViewModel.StoredFolder(@" D:\AC "));
    }

    [Fact]
    public void A_credits_section_leaves_out_blank_lines_and_none_is_made_of_nothing()
    {
        var section = SettingsScreenViewModel.Section("settings.credits.heading.parts", ["Ann", "", "  ", "Ann", "Bob"]);
        Assert.NotNull(section);
        Assert.Equal(["Ann", "Bob"], section.Lines);

        Assert.Null(SettingsScreenViewModel.Section("settings.credits.heading.parts", ["", " "]));
    }
}
