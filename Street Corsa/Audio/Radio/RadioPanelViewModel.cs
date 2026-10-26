using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using StreetCorsa.Logging;
using StreetCorsa.Services.Settings;
using StreetCorsa.Services.Text;
using StreetCorsa.ViewModels;

namespace StreetCorsa.Audio.Radio;

/// <summary>A line of the lists column: every song, or one of the player's lists</summary>
public sealed class RadioListItem(RadioPlaylist? playlist, string title)
{
    public RadioPlaylist? Playlist { get; } = playlist;

    /// <summary>Null for every song</summary>
    public string? Name => Playlist?.Name;

    public string Title { get; } = title;
}

/// <summary>
/// The radio's panel, over any screen: the lists, the songs of the one picked, the folders the music is in, and the
/// knobs. One for the app (<see cref="Shared"/>); the garage's radio, the hotkey and the settings open it.
/// </summary>
public sealed class RadioPanelViewModel : ObservableObject
{
    private static readonly Lazy<RadioPanelViewModel> SharedInstance = new(() => new RadioPanelViewModel());

    public static RadioPanelViewModel Shared => SharedInstance.Value;

    /// <summary>Opens the panel, or closes it when it is open; for any button anywhere</summary>
    public static RelayCommand ToggleCommand { get; } = new(() => Shared.IsOpen = !Shared.IsOpen);

    private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger("Radio");
    private readonly DispatcherTimer _volumeSave;
    private bool _isOpen;
    private RadioListItem? _selectedList;
    private RadioSong? _selectedSong;
    private string _filter = "";
    private string _listNameText = "";
    private string? _selectedFolder;
    private RadioListItem? _addTarget;

    private RadioPanelViewModel()
    {
        Radio.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(RadioPlayer.Songs)) Refresh();
            if (e.PropertyName is nameof(RadioPlayer.Repeat)) OnPropertyChanged(nameof(RepeatText));
        };
        Radio.Playlists.CollectionChanged += (_, _) => Refresh();

        PlayPauseCommand = new RelayCommand(Radio.TogglePlay);
        NextCommand = new RelayCommand(Radio.Next);
        PreviousCommand = new RelayCommand(Radio.Previous);
        StopCommand = new RelayCommand(Radio.Stop);
        CloseCommand = new RelayCommand(() => IsOpen = false);
        CycleRepeatCommand = new RelayCommand(() => Radio.Repeat = Radio.Repeat switch
        {
            RadioRepeat.Off => RadioRepeat.All,
            RadioRepeat.All => RadioRepeat.One,
            _ => RadioRepeat.Off
        });
        PlayListCommand = new RelayCommand(() => Radio.PlayList(_selectedList?.Name));
        PlaySongCommand = new RelayCommand(PlaySelected, () => _selectedSong != null);
        NewListCommand = new RelayCommand(NewList);
        RenameListCommand = new RelayCommand(RenameList, () => _selectedList?.Playlist != null);
        DeleteListCommand = new RelayCommand(DeleteList, () => _selectedList?.Playlist != null);
        AddToListCommand = new RelayCommand(AddToList, () => _selectedSong != null && _addTarget?.Playlist != null);
        RemoveFromListCommand = new RelayCommand(RemoveFromList, () => _selectedSong != null && _selectedList?.Playlist != null);
        MoveUpCommand = new RelayCommand(() => Move(-1), () => _selectedSong != null && _selectedList?.Playlist != null);
        MoveDownCommand = new RelayCommand(() => Move(1), () => _selectedSong != null && _selectedList?.Playlist != null);
        AddFolderCommand = new RelayCommand(AddFolder);
        RemoveFolderCommand = new RelayCommand(() => { if (_selectedFolder != null) Radio.RemoveFolder(_selectedFolder); }, () => _selectedFolder != null);
        OpenFolderCommand = new RelayCommand(OpenFolder);
        RescanCommand = new RelayCommand(Radio.Rescan);

        _volumeSave = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _volumeSave.Tick += (_, _) =>
        {
            _volumeSave.Stop();
            Settings?.Save();
        };

        Refresh();
    }

    /// <summary>The game's settings, for the radio's volume: the same slider as on the Settings card (set at start-up)</summary>
    public static GameSettingsService? Settings { get; set; }

    public RadioPlayer Radio => RadioPlayer.Shared;

    /// <summary>The Radio volume of the settings: heard at once, saved a moment after the knob stops</summary>
    public double Volume
    {
        get => Settings?.Current.Audio.Radio ?? 0.8;
        set
        {
            if (Settings is not { } settings) return;
            value = Math.Clamp(value, 0.0, 1.0);
            if (Math.Abs(settings.Current.Audio.Radio - value) < 1e-6) return;
            settings.Current.Audio.Radio = value;
            AudioHub.Shared.Apply(settings.Current.Audio);
            OnPropertyChanged();
            _volumeSave.Stop();
            _volumeSave.Start();
        }
    }

    public RelayCommand PlayPauseCommand { get; }
    public RelayCommand NextCommand { get; }
    public RelayCommand PreviousCommand { get; }
    public RelayCommand StopCommand { get; }
    public RelayCommand CloseCommand { get; }
    public RelayCommand CycleRepeatCommand { get; }
    public RelayCommand PlayListCommand { get; }
    public RelayCommand PlaySongCommand { get; }
    public RelayCommand NewListCommand { get; }
    public RelayCommand RenameListCommand { get; }
    public RelayCommand DeleteListCommand { get; }
    public RelayCommand AddToListCommand { get; }
    public RelayCommand RemoveFromListCommand { get; }
    public RelayCommand MoveUpCommand { get; }
    public RelayCommand MoveDownCommand { get; }
    public RelayCommand AddFolderCommand { get; }
    public RelayCommand RemoveFolderCommand { get; }
    public RelayCommand OpenFolderCommand { get; }
    public RelayCommand RescanCommand { get; }

    public bool IsOpen
    {
        get => _isOpen;
        set
        {
            if (!SetProperty(ref _isOpen, value)) return;
            Dialogs.ModalState.OverlayOpen = value;
            if (!value) return;
            Refresh();
            OnPropertyChanged(nameof(Volume));
        }
    }

    public string RepeatText => Radio.Repeat switch
    {
        RadioRepeat.All => GameText.Say("radio.repeat.list"),
        RadioRepeat.One => GameText.Say("radio.repeat.song"),
        _ => GameText.Say("radio.repeat.off")
    };

    public IReadOnlyList<RadioListItem> Lists { get; private set; } = [];

    /// <summary>The lists a song can be added to</summary>
    public IReadOnlyList<RadioListItem> Playlists => Lists.Where(l => l.Playlist != null).ToList();

    public RadioListItem? SelectedList
    {
        get => _selectedList;
        set
        {
            if (!SetProperty(ref _selectedList, value)) return;
            ListNameText = value?.Playlist?.Name ?? "";
            OnPropertyChanged(nameof(Songs));
            OnPropertyChanged(nameof(IsPlaylist));
            RelayCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsPlaylist => _selectedList?.Playlist != null;

    /// <summary>The picked list's songs that match the filter</summary>
    public IReadOnlyList<RadioSong> Songs => Radio.SongsOf(_selectedList?.Name)
        .Where(s => _filter.Length == 0 || s.Display.Contains(_filter, StringComparison.CurrentCultureIgnoreCase)
                    || (s.Album?.Contains(_filter, StringComparison.CurrentCultureIgnoreCase) ?? false))
        .ToList();

    public RadioSong? SelectedSong
    {
        get => _selectedSong;
        set
        {
            if (SetProperty(ref _selectedSong, value)) RelayCommand.RaiseCanExecuteChanged();
        }
    }

    public string Filter
    {
        get => _filter;
        set
        {
            if (SetProperty(ref _filter, (value ?? "").Trim())) OnPropertyChanged(nameof(Songs));
        }
    }

    /// <summary>The name for a new list, or the picked list's new name</summary>
    public string ListNameText
    {
        get => _listNameText;
        set => SetProperty(ref _listNameText, value ?? "");
    }

    public RadioListItem? AddTarget
    {
        get => _addTarget;
        set => SetProperty(ref _addTarget, value);
    }

    public string? SelectedFolder
    {
        get => _selectedFolder;
        set => SetProperty(ref _selectedFolder, value);
    }

    /// <summary>The lists anew, keeping the one picked where it is still there</summary>
    private void Refresh()
    {
        var picked = _selectedList?.Name;
        Lists = [new RadioListItem(null, GameText.Say("radio.all_songs", ("count", Radio.Songs.Count))), .. Radio.Playlists.Select(p => new RadioListItem(p, $"{p.Name} ({p.Songs.Count})"))];
        OnPropertyChanged(nameof(Lists));
        OnPropertyChanged(nameof(Playlists));

        _selectedList = Lists.FirstOrDefault(l => l.Name == picked) ?? Lists[0];
        OnPropertyChanged(nameof(SelectedList));
        OnPropertyChanged(nameof(IsPlaylist));
        OnPropertyChanged(nameof(Songs));

        var target = _addTarget?.Name;
        _addTarget = Playlists.FirstOrDefault(l => l.Name == target) ?? Playlists.FirstOrDefault();
        OnPropertyChanged(nameof(AddTarget));
    }

    /// <summary>The picked song now, and the picked list on from it</summary>
    public void PlaySelected()
    {
        if (_selectedSong is { } song) Radio.PlayList(_selectedList?.Name, song);
    }

    private void NewList()
    {
        var list = Radio.CreatePlaylist(_listNameText);
        Refresh();
        SelectedList = Lists.FirstOrDefault(l => l.Playlist == list);
    }

    private void RenameList()
    {
        if (_selectedList?.Playlist is not { } list) return;
        Radio.RenamePlaylist(list, _listNameText);
        Refresh();
    }

    private void DeleteList()
    {
        if (_selectedList?.Playlist is { } list) Radio.DeletePlaylist(list);
    }

    private void AddToList()
    {
        if (_selectedSong is not { } song || _addTarget?.Playlist is not { } list) return;
        Radio.AddToPlaylist(list, [song]);
        Refresh();
    }

    private void RemoveFromList()
    {
        if (_selectedSong is not { } song || _selectedList?.Playlist is not { } list) return;
        Radio.RemoveFromPlaylist(list, [song]);
        Refresh();
    }

    private void Move(int by)
    {
        if (_selectedSong is not { } song || _selectedList?.Playlist is not { } list) return;
        Radio.MoveInPlaylist(list, song, by);
        OnPropertyChanged(nameof(Songs));
        SelectedSong = song;
    }

    /// <summary>Any folder on the PC; its subfolders come with it</summary>
    private void AddFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = GameText.Say("radio.folder_dialog"), Multiselect = true };
        if (dialog.ShowDialog() != true) return;
        foreach (var folder in dialog.FolderNames) Radio.AddFolder(folder);
    }

    private void OpenFolder()
    {
        var folder = _selectedFolder ?? Radio.Folders.FirstOrDefault() ?? RadioState.DefaultFolder;
        try
        {
            Directory.CreateDirectory(folder);
            // Explorer by its full path: a bare name is looked for in the game's own folder first, which the player can write to
            var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            Process.Start(new ProcessStartInfo(explorer, $"\"{folder}\"") { UseShellExecute = false });
        }
        catch (Exception ex)
        {
            _logger.Warning("The radio's folder {Folder} could not be opened: {Error}", folder, ex.Message);
        }
    }
}
