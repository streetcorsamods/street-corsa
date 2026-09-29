using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using StreetCorsa.Configuration;
using StreetCorsa.Logging;
using StreetCorsa.ViewModels;

namespace StreetCorsa.Audio.Radio;

/// <summary>
/// The radio: the player's own music, out of any folders on the PC, on every screen but a race (the hub fades it out
/// and rests the mixer then) and on through screen changes. In the car (<see cref="Navigation.IScreen.InCar"/>) it
/// sounds like an AM car radio, with its static. Songs are read into memory before they play, so their files are never
/// held open; the next one is read while this one ends. Lives on the UI thread.
/// </summary>
public sealed class RadioPlayer : ObservableObject
{
    private static readonly Lazy<RadioPlayer> SharedInstance = new(() => new RadioPlayer());

    public static RadioPlayer Shared => SharedInstance.Value;

    // The next song is read in this long before this one ends
    private const uint PreloadMs = 8000;

    // A skip back this far into a song starts it over; nearer its start it goes to the one before
    private const uint RestartAfterMs = 3000;

    private static readonly TimeSpan RescanDelay = TimeSpan.FromSeconds(2);

    private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger("Radio");
    private readonly RadioQueue _queue;
    private readonly DispatcherTimer _clock;
    private readonly DispatcherTimer _rescanSoon;
    private readonly List<FileSystemWatcher> _watchers = [];
    private Dictionary<string, RadioSong> _byPath = new(StringComparer.OrdinalIgnoreCase);

    private RadioState _state = new();
    private string _statePath = "";
    private string _cachePath = "";
    private bool _started;

    private PlayingSound? _playing;
    private PlayingSound? _static;
    private (string Path, Task<SongSound?> Load)? _preload;
    private int _version;
    private int _failures;
    private bool _inCar;
    private CancellationTokenSource? _scanCancel;

    private IReadOnlyList<RadioSong> _songs = [];
    private RadioSong? _current;
    private bool _isOn;
    private bool _isPaused;
    private bool _isScanning;
    private string _status = "";
    private string _position = "";
    private string? _listName;

    private RadioPlayer()
    {
        _queue = new RadioQueue(artistOf: path => _byPath.TryGetValue(path, out var song) ? song.Artist : null);
        _clock = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _clock.Tick += (_, _) => Tick();
        _rescanSoon = new DispatcherTimer { Interval = RescanDelay };
        _rescanSoon.Tick += (_, _) =>
        {
            _rescanSoon.Stop();
            Rescan();
        };
    }

    /// <summary>A song started: its title for the "now playing" note</summary>
    public event Action<RadioSong>? SongStarted;

    public IReadOnlyList<RadioSong> Songs
    {
        get => _songs;
        private set => SetProperty(ref _songs, value);
    }

    public ObservableCollection<string> Folders { get; } = [];

    public ObservableCollection<RadioPlaylist> Playlists { get; } = [];

    public RadioSong? Current
    {
        get => _current;
        private set
        {
            if (SetProperty(ref _current, value)) OnPropertyChanged(nameof(NowPlaying));
        }
    }

    public string NowPlaying => _current?.Display ?? (_songs.Count == 0 ? "No music yet" : "Off");

    /// <summary>On: a song is playing or paused</summary>
    public bool IsOn
    {
        get => _isOn;
        private set
        {
            if (SetProperty(ref _isOn, value)) OnPropertyChanged(nameof(IsPlaying));
        }
    }

    public bool IsPaused
    {
        get => _isPaused;
        private set
        {
            if (SetProperty(ref _isPaused, value)) OnPropertyChanged(nameof(IsPlaying));
        }
    }

    public bool IsPlaying => _isOn && !_isPaused;

    public bool IsScanning
    {
        get => _isScanning;
        private set => SetProperty(ref _isScanning, value);
    }

    /// <summary>What the library is: how many songs, what could not be played, a folder that is gone</summary>
    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    /// <summary>"1:23 / 3:45" while on</summary>
    public string Position
    {
        get => _position;
        private set => SetProperty(ref _position, value);
    }

    /// <summary>The list playing; null for every song</summary>
    public string? ListName
    {
        get => _listName;
        private set => SetProperty(ref _listName, value);
    }

    public bool Shuffle
    {
        get => _state.Shuffle;
        set
        {
            if (_state.Shuffle == value) return;
            _state.Shuffle = value;
            _queue.SetShuffle(value);
            DropPreload();
            OnPropertyChanged();
            Save();
        }
    }

    public RadioRepeat Repeat
    {
        get => _state.Repeat;
        set
        {
            if (_state.Repeat == value) return;
            _state.Repeat = value;
            _queue.Repeat = value;
            DropPreload();
            OnPropertyChanged();
            Save();
        }
    }

    /// <summary>The radio comes on by itself at start-up if it was on when the game was left</summary>
    public bool AutoStart
    {
        get => _state.AutoStart;
        set
        {
            if (_state.AutoStart == value) return;
            _state.AutoStart = value;
            OnPropertyChanged();
            Save();
        }
    }

    /// <summary>Reads what was saved, lists the songs on a worker and, when asked to, comes on where it was left</summary>
    public void Start()
    {
        if (_started) return;
        _started = true;

        _statePath = Path.Combine(AppSettings.AppDataPath, RadioState.FileName);
        _cachePath = Path.Combine(AppSettings.AppDataPath, RadioLibrary.CacheFileName);
        try
        {
            _state = RadioState.Load(_statePath);
        }
        catch (Exception ex)
        {
            // Not overwritten until something changes: the player's lists may still be read by hand
            _logger.Error(ex, "The radio's settings could not be read from {Path}; starting afresh", _statePath);
            _state = new RadioState { Folders = [RadioState.DefaultFolder] };
        }

        _queue.Repeat = _state.Repeat;
        foreach (var folder in _state.Folders) Folders.Add(folder);
        foreach (var list in _state.Playlists) Playlists.Add(list);
        OnPropertyChanged(nameof(Shuffle));
        OnPropertyChanged(nameof(Repeat));
        OnPropertyChanged(nameof(AutoStart));

        // The default folder is made for the player to drop music into
        try
        {
            if (_state.Folders.Contains(RadioState.DefaultFolder, StringComparer.OrdinalIgnoreCase)) Directory.CreateDirectory(RadioState.DefaultFolder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Warning("The radio's folder {Folder} could not be made: {Error}", RadioState.DefaultFolder, ex.Message);
        }

        Rescan(resume: _state.AutoStart && _state.WasOn);
    }

    #region Library

    /// <summary>Lists the songs again, on a worker; the radio plays on meanwhile</summary>
    public void Rescan() => Rescan(resume: false);

    private async void Rescan(bool resume)
    {
        _scanCancel?.Cancel();
        var cancel = new CancellationTokenSource();
        _scanCancel = cancel;
        var folders = Folders.ToList();
        IsScanning = true;
        Status = "Looking for music...";
        WatchFolders(folders);

        try
        {
            var result = await Task.Run(() =>
            {
                // Tags are read through FMOD: wait for it (made at start-up, on a worker too)
                AudioHub.Shared.TryGetStudio();
                return RadioLibrary.Scan(folders, _cachePath, AudioHub.Shared.ReadSongInfo, cancel.Token);
            }, cancel.Token);

            if (cancel.IsCancellationRequested) return;
            _byPath = result.Songs.ToDictionary(s => s.Path, StringComparer.OrdinalIgnoreCase);
            Songs = result.Songs;
            if (_current != null && _byPath.TryGetValue(_current.Path, out var fresh)) Current = fresh;
            Status = Describe(result);
            OnPropertyChanged(nameof(NowPlaying));
            _logger.Information("Radio: {Songs} songs in {Folders} folder(s), {NotPlayable} it cannot play", result.Songs.Count, folders.Count, result.NotPlayable);

            if (resume) Resume();
        }
        catch (OperationCanceledException)
        {
            // A newer scan is on its way
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "The radio's music could not be listed");
            Status = "The music could not be listed: " + ex.Message;
        }
        finally
        {
            if (ReferenceEquals(_scanCancel, cancel)) IsScanning = false;
        }
    }

    private static string Describe(RadioLibrary.ScanResult result)
    {
        var parts = new List<string> { result.Songs.Count == 1 ? "1 song" : $"{result.Songs.Count} songs" };
        if (result.NotPlayable > 0) parts.Add($"{result.NotPlayable} file(s) in a format the radio cannot play (m4a, aac, opus, wma: convert them to mp3, ogg or flac)");
        if (result.MissingFolders.Count > 0) parts.Add("not found: " + string.Join(", ", result.MissingFolders));
        return string.Join(" · ", parts);
    }

    /// <summary>Music dropped into a folder, or taken out, is picked up a moment later</summary>
    private void WatchFolders(IReadOnlyList<string> folders)
    {
        foreach (var watcher in _watchers) watcher.Dispose();
        _watchers.Clear();

        foreach (var folder in folders.Where(Directory.Exists))
        {
            try
            {
                var watcher = new FileSystemWatcher(folder)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite
                };
                FileSystemEventHandler changed = (_, _) => ScheduleRescan();
                watcher.Created += changed;
                watcher.Deleted += changed;
                watcher.Changed += changed;
                watcher.Renamed += (_, _) => ScheduleRescan();
                watcher.Error += (_, _) => ScheduleRescan();
                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
            {
                _logger.Warning("The radio cannot watch {Folder} for new music: {Error}", folder, ex.Message);
            }
        }
    }

    // From the watcher's thread: the rescan is started on the UI thread, once things have settled
    private void ScheduleRescan() => _rescanSoon.Dispatcher.BeginInvoke(() =>
    {
        _rescanSoon.Stop();
        _rescanSoon.Start();
    });

    public void AddFolder(string folder)
    {
        folder = Path.GetFullPath(folder.Trim());
        if (Folders.Contains(folder, StringComparer.OrdinalIgnoreCase)) return;
        Folders.Add(folder);
        SaveFolders();
        Rescan();
    }

    public void RemoveFolder(string folder)
    {
        if (!Folders.Remove(folder)) return;
        SaveFolders();
        Rescan();
    }

    private void SaveFolders()
    {
        _state.Folders = Folders.ToList();
        Save();
    }

    #endregion

    #region Lists

    /// <summary>The songs of a list that are still there, in its order; every song for null</summary>
    public IReadOnlyList<RadioSong> SongsOf(string? listName)
    {
        if (listName == null) return _songs;
        var list = Playlists.FirstOrDefault(p => p.Name == listName);
        return list == null ? [] : list.Songs.Select(p => _byPath.GetValueOrDefault(p)).OfType<RadioSong>().ToList();
    }

    /// <summary>A new, empty list; its name made unique</summary>
    public RadioPlaylist CreatePlaylist(string name)
    {
        name = string.IsNullOrWhiteSpace(name) ? "My list" : name.Trim();
        var unique = name;
        for (var i = 2; Playlists.Any(p => p.Name.Equals(unique, StringComparison.OrdinalIgnoreCase)); i++) unique = $"{name} {i}";

        var list = new RadioPlaylist { Name = unique };
        Playlists.Add(list);
        SaveLists();
        return list;
    }

    public void RenamePlaylist(RadioPlaylist list, string name)
    {
        name = name.Trim();
        if (name.Length == 0 || Playlists.Any(p => p != list && p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) return;
        if (ListName == list.Name) ListName = name;
        list.Name = name;
        SaveLists();
    }

    public void DeletePlaylist(RadioPlaylist list)
    {
        if (!Playlists.Remove(list)) return;
        if (ListName == list.Name) ListName = null;
        SaveLists();
    }

    public void AddToPlaylist(RadioPlaylist list, IEnumerable<RadioSong> songs)
    {
        var added = false;
        foreach (var song in songs)
        {
            if (list.Songs.Contains(song.Path, StringComparer.OrdinalIgnoreCase)) continue;
            list.Songs.Add(song.Path);
            added = true;
        }

        if (added) SaveLists();
    }

    public void RemoveFromPlaylist(RadioPlaylist list, IEnumerable<RadioSong> songs)
    {
        var gone = songs.Select(s => s.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (list.Songs.RemoveAll(gone.Contains) > 0) SaveLists();
    }

    /// <summary>A song a place up (-1) or down (+1) its list</summary>
    public void MoveInPlaylist(RadioPlaylist list, RadioSong song, int by)
    {
        var from = list.Songs.FindIndex(p => string.Equals(p, song.Path, StringComparison.OrdinalIgnoreCase));
        var to = from + by;
        if (from < 0 || to < 0 || to >= list.Songs.Count) return;
        (list.Songs[from], list.Songs[to]) = (list.Songs[to], list.Songs[from]);
        SaveLists();
    }

    private void SaveLists()
    {
        _state.Playlists = Playlists.ToList();
        Save();
    }

    #endregion

    #region Playing

    /// <summary>Plays a list (null: every song), from <paramref name="from"/> or, shuffling, anywhere</summary>
    public void PlayList(string? listName, RadioSong? from = null, uint positionMs = 0)
    {
        var songs = SongsOf(listName);
        if (songs.Count == 0)
        {
            Status = listName == null ? $"No music yet: drop mp3, ogg or flac files into {Folders.FirstOrDefault() ?? RadioState.DefaultFolder}" : $"\"{listName}\" has no songs";
            return;
        }

        ListName = listName;
        _queue.Load(songs.Select(s => s.Path), from?.Path, _state.Shuffle);
        DropPreload();
        _failures = 0;
        PlayCurrent(positionMs);
    }

    /// <summary>On and off at the knob: a paused radio plays on, an off one starts its list</summary>
    public void TogglePlay()
    {
        AudioHub.Shared.PlayCue("radio.knob");
        if (!IsOn)
        {
            if (_queue.Current != null) PlayCurrent(0);
            else PlayList(ListName);
            return;
        }

        IsPaused = !IsPaused;
        _playing?.SetPaused(IsPaused);
        UpdateStatic();
        Save();
    }

    public void Next()
    {
        if (!IsOn && _queue.Current == null) return;
        _queue.Next(playedOut: false);
        _failures = 0;
        PlayCurrent(0);
    }

    public void Previous()
    {
        if (!IsOn && _queue.Current == null) return;
        if (_playing is { } playing && playing.PositionMs > RestartAfterMs)
        {
            PlayCurrent(0);
            return;
        }

        _queue.Previous();
        _failures = 0;
        PlayCurrent(0);
    }

    /// <summary>Off: the song stops, the list stays where it was</summary>
    public void Stop()
    {
        if (IsOn) AudioHub.Shared.PlayCue("radio.knob");
        _version++;
        ReleasePlaying();
        DropPreload();
        IsOn = false;
        IsPaused = false;
        Position = "";
        UpdateStatic();
        Save();
    }

    private void Resume()
    {
        if (_state.Song is { } song && _byPath.TryGetValue(song, out var from))
            PlayList(SongsOf(_state.List).Count > 0 ? _state.List : null, from, _state.PositionMs);
        else
            PlayList(SongsOf(_state.List).Count > 0 ? _state.List : null);
    }

    /// <summary>Plays the queue's song, out of the preload when it is there, read in on a worker when not</summary>
    private async void PlayCurrent(uint positionMs)
    {
        var version = ++_version;
        ReleasePlaying();

        if (_queue.Current is not { } path)
        {
            // The end of the list
            IsOn = false;
            IsPaused = false;
            Position = "";
            UpdateStatic();
            Save();
            return;
        }

        IsOn = true;
        IsPaused = false;
        Current = _byPath.GetValueOrDefault(path) ?? new RadioSong(path, RadioLibrary.TitleFromName(path), null, null, 0);

        SongSound? song;
        if (_preload is { } preload && string.Equals(preload.Path, path, StringComparison.OrdinalIgnoreCase))
        {
            _preload = null;
            song = await preload.Load;
        }
        else
        {
            DropPreload();
            song = await Task.Run(() => AudioHub.Shared.OpenSong(path));
        }

        if (version != _version)
        {
            song?.Release();
            return;
        }

        var playing = song == null ? null : AudioHub.Shared.PlaySong(song, positionMs);
        if (playing == null)
        {
            song?.Release();

            // Gone or unplayable: on to the next, but not round and round a list of nothing that plays
            if (++_failures >= Math.Max(1, _queue.Songs.Count))
            {
                _logger.Warning("Radio: none of the songs in the list can be played");
                Status = "None of these songs can be played (the log says why)";
                Stop();
                return;
            }

            _queue.Next(playedOut: false);
            PlayCurrent(0);
            return;
        }

        _failures = 0;
        _playing = playing;
        if (_inCar) AudioHub.Shared.PlayCue("radio.tune");
        UpdateStatic();
        _clock.Start();
        Tick();
        Save();

        try
        {
            SongStarted?.Invoke(Current);
        }
        catch (Exception ex)
        {
            _logger.Warning("Something that listens to the radio failed: {Error}", ex.Message);
        }
    }

    private void Tick()
    {
        if (_playing is not { } playing)
        {
            _clock.Stop();
            return;
        }

        if (!playing.IsPlaying)
        {
            // Played out
            _playing = null;
            _queue.Next(playedOut: true);
            PlayCurrent(0);
            return;
        }

        var at = playing.PositionMs;
        var length = playing.LengthMs;
        Position = length > 0 ? $"{Time(at)} / {Time(length)}" : Time(at);

        // The next song read in while this one ends, so it follows at once
        if (length > 0 && length - Math.Min(at, length) < PreloadMs && _preload == null && _queue.Upcoming is { } upcoming
            && !string.Equals(upcoming, _queue.Current, StringComparison.OrdinalIgnoreCase))
        {
            _preload = (upcoming, Task.Run(() => AudioHub.Shared.OpenSong(upcoming)));
        }
    }

    private void ReleasePlaying()
    {
        _playing?.Stop();
        _playing = null;
    }

    /// <summary>A song read in for nothing (skipped past, the order changed) is let go of once it is in</summary>
    private void DropPreload()
    {
        if (_preload is not { } preload) return;
        _preload = null;
        preload.Load.ContinueWith(t =>
        {
            if (t.IsCompletedSuccessfully) t.Result?.Release();
        }, TaskScheduler.Default);
    }

    #endregion

    #region The car

    /// <summary>In the car (the Cruise screen, the gas station) the radio is the car's: AM, and its static</summary>
    public void SetInCar(bool inCar)
    {
        if (_inCar == inCar) return;
        _inCar = inCar;
        AudioHub.Shared.SetAmRadio(inCar);
        UpdateStatic();
    }

    /// <summary>The static under the music: in the car, while a song plays</summary>
    private void UpdateStatic()
    {
        var wanted = _inCar && IsPlaying;
        if (wanted == (_static != null)) return;

        if (!wanted)
        {
            _static?.Stop();
            _static = null;
            return;
        }

        if (AudioHub.Shared.Cues.Get("radio.static") is not { Loop: true } cue) return;
        var sound = AudioHub.Shared.Play(cue.Bus, cue.Files[0], loop: true, paused: true);
        if (sound == null) return;
        sound.SetVolume(cue.Volume);
        sound.Resume();
        _static = sound;
    }

    #endregion

    /// <summary>What to come back to: the list, the song and how far in, and whether it was on. At exit too.</summary>
    public void Save()
    {
        if (!_started) return;
        _state.WasOn = IsOn;
        _state.List = ListName;
        _state.Song = _current?.Path;
        _state.PositionMs = _playing?.PositionMs ?? 0;

        try
        {
            _state.Save(_statePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Warning("The radio's settings could not be saved: {Error}", ex.Message);
        }
    }

    private static string Time(uint ms) => TimeSpan.FromMilliseconds(ms) is var t && t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
}
