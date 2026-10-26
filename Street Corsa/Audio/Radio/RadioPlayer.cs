using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using StreetCorsa.Configuration;
using StreetCorsa.Logging;
using StreetCorsa.Services.Text;
using StreetCorsa.ViewModels;

namespace StreetCorsa.Audio.Radio;

/// <summary>
/// The radio: the player's own music, out of any folders on the PC, on every screen and on through screen changes; in a
/// race it goes off, and the player's keys (<see cref="RaceKeys"/>) put it on over AC. In the car (<see cref="Navigation.IScreen.InCar"/>) it
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

    // How long the radio takes to come up when it is switched on, and to die away when it is switched off or paused
    private const float PowerFadeSeconds = 0.25f;

    // Where the song was when the radio went off: switched on again, it goes on from there
    private uint _resumeAtMs;

    // A pause waits for its fade out before it holds the song; a play in between calls it off
    private int _pauseVersion;

    // On when the last session ended: it comes on again once a game is loaded, not at the main menu
    private bool _restoreOnGame;
    private bool _gameLoaded;

    // In a race: its keys listened for, and how the radio was before, to put back unless the player changed it
    private RaceKeys? _raceKeys;
    private bool _racing;
    private bool _onBeforeRace;
    private bool _screenInCar;
    private bool _changedInRace;
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

    public string NowPlaying => _current?.Display ?? (_songs.Count == 0 ? GameText.Say("radio.no_music_yet") : GameText.Say("radio.off"));

    /// <summary>On: a song is playing or paused</summary>
    public bool IsOn
    {
        get => _isOn;
        private set
        {
            if (!SetProperty(ref _isOn, value)) return;
            OnPropertyChanged(nameof(IsPlaying));
            AudioHub.Shared.SetRadioActive(IsPlaying);
        }
    }

    public bool IsPaused
    {
        get => _isPaused;
        private set
        {
            if (!SetProperty(ref _isPaused, value)) return;
            OnPropertyChanged(nameof(IsPlaying));
            AudioHub.Shared.SetRadioActive(IsPlaying);
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

    /// <summary>How far into the song, 0..1: the dial's needle</summary>
    public double PositionFraction
    {
        get => _positionFraction;
        private set => SetProperty(ref _positionFraction, value);
    }

    private double _positionFraction;

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

    /// <summary>
    /// Reads what was saved and lists the songs on a worker; the radio waits off, set at the song and place it was left
    /// at, and comes on again when a game is loaded if it was on when the last session ended (<see cref="GameLoaded"/>)
    /// </summary>
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

        // The default folder is made for the player to drop music into
        try
        {
            if (_state.Folders.Contains(RadioState.DefaultFolder, StringComparer.OrdinalIgnoreCase)) Directory.CreateDirectory(RadioState.DefaultFolder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Warning("The radio's folder {Folder} could not be made: {Error}", RadioState.DefaultFolder, ex.Message);
        }

        var dispatcher = Dispatcher.CurrentDispatcher;
        _raceKeys = new RaceKeys(action => dispatcher.BeginInvoke(action));
        _restoreOnGame = _state.WasOn;
        Rescan(resume: false);
    }

    #region Library

    /// <summary>Lists the songs again, on a worker; the radio plays on meanwhile</summary>
    public void Rescan() => Rescan(resume: false);

    /// <summary>
    /// The app is closing: a listing under way is called off (keeping what it read so far), so it is out of FMOD before
    /// FMOD goes. Any thread.
    /// </summary>
    public void StopScanning()
    {
        try
        {
            Volatile.Read(ref _scanCancel)?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Over already
        }
    }

    private async void Rescan(bool resume)
    {
        // The listing before is called off; it lets go of its own source once it is out
        StopScanning();
        var cancel = new CancellationTokenSource();
        _scanCancel = cancel;
        var folders = Folders.ToList();
        IsScanning = true;
        Status = GameText.Say("radio.looking");

        try
        {
            // Asked off the UI thread: a folder on a network or USB drive that has gone takes the share's timeout to say so
            var existing = await Task.Run(() => folders.Where(Directory.Exists).ToList(), cancel.Token);
            if (cancel.IsCancellationRequested) return;
            WatchFolders(existing);

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
            _logger.Information("Radio: {Songs} songs in {Folders} folder(s), {NotPlayable} it cannot play{Capped}", result.Songs.Count, folders.Count, result.NotPlayable,
                result.Capped ? $" (cut short at {RadioLibrary.MostSongs})" : "");
            if (result.Songs.Count > RadioLibrary.MostWatched && _watchers.Count > 0)
            {
                _logger.Information("Radio: a library of {Songs} songs is not watched for changes; \"Look again\" lists new music", result.Songs.Count);
                StopWatching();
            }

            if (resume) Resume();
            else if (!IsOn) Prime();
            if (_gameLoaded) RestoreForGame();
        }
        catch (OperationCanceledException)
        {
            // A newer scan is on its way
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "The radio's music could not be listed");
            Status = GameText.Say("radio.list_failed", ("error", ex.Message));
        }
        finally
        {
            if (ReferenceEquals(_scanCancel, cancel)) IsScanning = false;
            cancel.Dispose();
        }
    }

    private static string Describe(RadioLibrary.ScanResult result)
    {
        var parts = new List<string>
        {
            result.Songs.Count == 1
                ? GameText.Say("radio.found.one", ("count", result.Songs.Count))
                : GameText.Say("radio.found.other", ("count", result.Songs.Count))
        };
        if (result.Capped) parts.Add(GameText.Say("radio.capped", ("count", result.Songs.Count)));
        if (result.NotPlayable > 0) parts.Add(GameText.Say("radio.not_playable", ("count", result.NotPlayable)));
        if (result.MissingFolders.Count > 0) parts.Add(GameText.Say("radio.not_found", ("folders", string.Join(", ", result.MissingFolders))));
        return string.Join(" · ", parts);
    }

    /// <summary>
    /// Music dropped into a folder, or taken out, is picked up a moment later. Not over a library of more than
    /// <see cref="RadioLibrary.MostWatched"/> songs: a watcher on a tree that size fires on every write anywhere in it,
    /// and each firing is a scan of all of it (the "Look again" button lists new music there). The folders are those
    /// found to be there.
    /// </summary>
    private void WatchFolders(IReadOnlyList<string> folders)
    {
        StopWatching();
        if (_songs.Count > RadioLibrary.MostWatched) return;

        foreach (var folder in folders)
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

    private void StopWatching()
    {
        foreach (var watcher in _watchers) watcher.Dispose();
        _watchers.Clear();
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
        if (RadioLibrary.IsRoot(folder))
        {
            // A whole drive or share would be listed end to end and watched for every write anywhere on it: a worker and
            // the disk busy for the session. The player is told to pick the music folder in it
            _logger.Information("Radio: {Folder} is a whole drive or share, not taken as a music folder", folder);
            Status = GameText.Say("radio.folder_refused", ("folder", folder));
            return;
        }

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
        name = string.IsNullOrWhiteSpace(name) ? GameText.Say("radio.new_list") : name.Trim();
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
    public void PlayList(string? listName, RadioSong? from = null, uint positionMs = 0, bool fadeIn = false)
    {
        // The player's own hand on the radio: what they do now stands, the last session's state is not put back over it
        _restoreOnGame = false;
        var songs = SongsOf(listName);
        if (songs.Count == 0)
        {
            Status = listName == null
                ? GameText.Say("radio.empty", ("folder", Folders.FirstOrDefault() ?? RadioState.DefaultFolder))
                : GameText.Say("radio.list_empty", ("list", listName));
            return;
        }

        ListName = listName;
        _queue.Load(songs.Select(s => s.Path), from?.Path, _state.Shuffle);
        DropPreload();
        _failures = 0;
        PlayCurrent(positionMs, fadeIn || !IsOn);
    }

    /// <summary>
    /// The power knob (R, the car radio's left knob): off where it was, song and place remembered and saved, or on
    /// again from there, the sound coming up and dying away over a moment rather than cut
    /// </summary>
    public void TogglePower()
    {
        _changedInRace |= _racing;
        // The player's own hand on the radio: what they do now stands, the last session's state is not put back over it
        _restoreOnGame = false;
        if (IsOn) Stop();
        else PowerOn();
    }

    private void PowerOn()
    {
        AudioHub.Shared.PlayCue("radio.knob");
        if (_queue.Current != null) PlayCurrent(_resumeAtMs, fadeIn: true);
        else PlayList(ListName, positionMs: 0, fadeIn: true);
    }

    /// <summary>Play and pause (the panel's button, the media key): an off radio comes on; a paused song goes on where it was</summary>
    public void TogglePlay()
    {
        _changedInRace |= _racing;
        // The player's own hand on the radio: what they do now stands, the last session's state is not put back over it
        _restoreOnGame = false;
        if (!IsOn)
        {
            PowerOn();
            return;
        }

        AudioHub.Shared.PlayCue("radio.knob");
        IsPaused = !IsPaused;
        var version = ++_pauseVersion;
        if (_playing is { } playing)
        {
            if (IsPaused)
            {
                // Dies away, then holds
                playing.Fade(1f, 0f, PowerFadeSeconds, AudioHub.Shared.MixRate, stopAtEnd: false);
                After(PowerFadeSeconds, () =>
                {
                    if (version == _pauseVersion && ReferenceEquals(_playing, playing)) playing.SetPaused(true);
                });
            }
            else
            {
                playing.Fade(0f, 1f, PowerFadeSeconds, AudioHub.Shared.MixRate, stopAtEnd: false);
                playing.SetPaused(false);
            }
        }

        UpdateStatic();
        Save();
    }

    /// <summary>Something done a moment from now, on the UI thread</summary>
    private static void After(float seconds, Action action)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds + 0.05) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            action();
        };
        timer.Start();
    }

    public void Next()
    {
        if (!IsOn && _queue.Current == null) return;
        _changedInRace |= _racing;
        var fadeIn = !IsOn;
        _queue.Next(playedOut: false);
        _failures = 0;
        PlayCurrent(0, fadeIn);
    }

    public void Previous()
    {
        if (!IsOn && _queue.Current == null) return;
        _changedInRace |= _racing;
        if (_playing is { } playing && playing.PositionMs > RestartAfterMs)
        {
            PlayCurrent(0);
            return;
        }

        var fadeIn = !IsOn;
        _queue.Previous();
        _failures = 0;
        PlayCurrent(0, fadeIn);
    }

    /// <summary>
    /// Off: the song dies away and stops, the list and the place in the song stay where they were (saved at once), for
    /// the radio to go on from when it is switched on again
    /// </summary>
    public void Stop()
    {
        if (!IsOn) return;
        AudioHub.Shared.PlayCue("radio.knob");
        _version++;
        _pauseVersion++;

        if (_playing is { } playing)
        {
            _resumeAtMs = playing.PositionMs;
            _playing = null;
            if (IsPaused)
            {
                // Silent already
                playing.Stop();
            }
            else
            {
                playing.Fade(1f, 0f, PowerFadeSeconds, AudioHub.Shared.MixRate, stopAtEnd: true);
                After(PowerFadeSeconds, playing.Stop);
            }
        }

        DropPreload();
        IsOn = false;
        IsPaused = false;
        Position = "";
        UpdateStatic();
        Save();
    }

    /// <summary>
    /// Off, but ready where the last session left it: the list and the song set, the place in it kept, for the power
    /// knob to go on from there
    /// </summary>
    private void Prime()
    {
        if (_state.Song is not { } path || !_byPath.TryGetValue(path, out var song)) return;

        var list = SongsOf(_state.List).Count > 0 ? _state.List : null;
        ListName = list;
        _queue.Load(SongsOf(list).Select(s => s.Path), path, _state.Shuffle);
        Current = song;
        _resumeAtMs = _state.PositionMs;
    }

    /// <summary>
    /// A game was loaded (or a new one started): the radio comes on as it was left last session, once. Before the songs
    /// are listed it waits for them.
    /// </summary>
    public void GameLoaded()
    {
        _gameLoaded = true;
        if (!IsScanning) RestoreForGame();
    }

    private void RestoreForGame()
    {
        if (!_restoreOnGame) return;
        _restoreOnGame = false;
        if (!IsOn && _songs.Count > 0) PowerOn();
    }

    private void Resume()
    {
        if (_state.Song is { } song && _byPath.TryGetValue(song, out var from))
            PlayList(SongsOf(_state.List).Count > 0 ? _state.List : null, from, _state.PositionMs);
        else
            PlayList(SongsOf(_state.List).Count > 0 ? _state.List : null);
    }

    /// <summary>Plays the queue's song, out of the preload when it is there, read in on a worker when not</summary>
    private async void PlayCurrent(uint positionMs, bool fadeIn = false)
    {
        var version = ++_version;
        _pauseVersion++;
        ReleasePlaying();

        if (_queue.Current is not { } path)
        {
            // The end of the list
            _resumeAtMs = 0;
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
        try
        {
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
        }
        catch (Exception ex)
        {
            // Nothing may leave an async void: a song that could not be read in is one that cannot be played
            _logger.Warning("Radio: the song {File} could not be read in: {Error}", path, ex.Message);
            song = null;
        }

        if (version != _version)
        {
            song?.Release();
            return;
        }

        var playing = song == null ? null : AudioHub.Shared.PlaySong(song, positionMs, fadeIn ? PowerFadeSeconds : 0f);
        if (playing == null)
        {
            song?.Release();

            // Gone or unplayable: on to the next, but not round and round a list of nothing that plays
            if (++_failures >= Math.Max(1, _queue.Songs.Count))
            {
                _logger.Warning("Radio: none of the songs in the list can be played");
                Status = GameText.Say("radio.none_playable");
                Stop();
                return;
            }

            _queue.Next(playedOut: false);
            PlayCurrent(0);
            return;
        }

        _failures = 0;
        _resumeAtMs = 0;
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
        PositionFraction = length > 0 ? Math.Clamp(at / (double)length, 0, 1) : 0;

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
            // Seen here, or it would surface as an unobserved exception at the next collection, far from its cause
            else if (t.Exception is { } failed) _logger.Warning("Radio: the next song {File} could not be read ahead: {Error}", preload.Path, failed.GetBaseException().Message);
        }, TaskScheduler.Default);
    }

    #endregion

    #region The race

    /// <summary>
    /// Assetto Corsa starts or is over. At the start of a race the radio goes off (fading); with
    /// <paramref name="keepRadio"/> (a free run, a test-and-tune) it plays on. Either way its keys are listened for over
    /// AC, the radio sounding as the car's, AM. At the end the keys are let go of, the screen's sound comes back, and the
    /// radio is as it was before, unless the player switched it in AC: then as they left it. UI thread.
    /// </summary>
    public void SetRacing(bool racing, bool keepRadio = false)
    {
        if (_racing == racing) return;

        if (racing)
        {
            _onBeforeRace = IsOn && !IsPaused;
            _changedInRace = false;
            _screenInCar = _inCar;
            if (IsOn && !keepRadio) Stop();
            _racing = true;
            ApplyInCar(true);
            _raceKeys?.Start();
            return;
        }

        _racing = false;
        _raceKeys?.Stop();
        ApplyInCar(_screenInCar);
        if (!_changedInRace && _onBeforeRace && !IsOn) PowerOn();
    }

    #endregion

    #region The car

    /// <summary>
    /// In the car (the Cruise screen, the gas station) the radio is the car's: AM, and its static, fading in over
    /// <see cref="AudioHub.AmFadeSeconds"/>. The car's screens say so once the player sits in it (their scene is up);
    /// leaving for any other screen takes it out at once.
    /// </summary>
    public void SetInCar(bool inCar)
    {
        // In a race the radio is the car's whatever screen is behind it; the screen's own comes back after
        if (_racing)
        {
            _screenInCar = inCar;
            return;
        }

        ApplyInCar(inCar);
    }

    private void ApplyInCar(bool inCar)
    {
        if (_inCar == inCar) return;
        _inCar = inCar;
        AudioHub.Shared.SetAmRadio(inCar);
        UpdateStatic(AudioHub.AmFadeSeconds);
    }

    /// <summary>The static under the music: in the car, while a song plays. It comes and goes over <paramref name="fadeSeconds"/>.</summary>
    private void UpdateStatic(float fadeSeconds = PowerFadeSeconds)
    {
        var wanted = _inCar && IsPlaying;
        if (wanted == (_static != null)) return;

        if (!wanted)
        {
            // The static dies away with the song
            if (_static is { } leaving)
            {
                // From where its fade-in got to: leaving the car while it still comes in does not burst it to full
                leaving.FadeFromHere(0f, fadeSeconds, AudioHub.Shared.MixRate, stopAtEnd: true);
                After(fadeSeconds, leaving.Stop);
            }
            _static = null;
            return;
        }

        if (AudioHub.Shared.Cues.Get("radio.static") is not { Loop: true } cue) return;
        var sound = AudioHub.Shared.Play(cue.Bus, cue.Files[0], loop: true, paused: true);
        if (sound == null) return;
        sound.SetVolume(cue.Volume);
        sound.Fade(0f, 1f, fadeSeconds, AudioHub.Shared.MixRate, stopAtEnd: false);
        sound.Resume();
        _static = sound;
    }

    #endregion

    /// <summary>What to come back to: the list, the song and how far in, and whether it was on. At exit too.</summary>
    public void Save()
    {
        if (!_started) return;
        // Left at the main menu before a game was loaded, it is still to come on at the next one
        _state.WasOn = IsOn || _restoreOnGame;
        _state.List = ListName;
        _state.Song = _current?.Path;
        _state.PositionMs = _playing?.PositionMs ?? _resumeAtMs;

        try
        {
            _state.Save(_statePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Warning("The radio's settings could not be saved: {Error}", ex.Message);
        }
    }

    private static string Time(uint ms) => RadioTime.Format(ms);
}
