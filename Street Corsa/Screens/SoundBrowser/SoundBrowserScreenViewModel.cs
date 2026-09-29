using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using StreetCorsa.Audio;
using StreetCorsa.Logging;
using StreetCorsa.Navigation;
using StreetCorsa.Services.Parts;
using StreetCorsa.ViewModels;

namespace StreetCorsa.Screens.SoundBrowser
{
    /// <summary>
    /// Every sound the game has or may have, to be heard one by one: the engine sounds (by make, then the AC cars' own
    /// by cylinders) and the sound folders (the candidates being chosen among, by folder). A file plays on the bus
    /// its folder belongs to, so the Settings sliders are heard on it; an engine starts and revs as in the garage. The
    /// candidates to keep are marked here (picks.json), and chosen from by hand.
    /// </summary>
    public class SoundBrowserScreenViewModel : BaseScreenViewModel
    {
        private readonly NavigationService _navigationService;
        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger("Audio");
        private readonly DispatcherTimer _clock;
        private readonly SoundPicks? _picks;

        private SoundCategory? _selectedCategory;
        private SoundItem? _selectedFile;
        private EngineItem? _selectedEngine;
        private EngineSpec? _engineSpec;
        private string _filter = "";
        private bool _autoPlay = true;
        private bool _loop;
        private string _status = "";
        private string _position = "";

        private PlayingSound? _playing;
        private SoundItem? _playingItem;

        public RelayCommand BackCommand { get; }
        public RelayCommand PlayCommand { get; }
        public RelayCommand StopCommand { get; }
        public RelayCommand KeepCommand { get; }
        public RelayCommand ShowFileCommand { get; }

        public IReadOnlyList<SoundCategory> Categories { get; }

        /// <summary>Where the sound files are read from; empty when there is no sound folder</summary>
        public string Root { get; }

        /// <param name="soundRoot">The sound folder; the source tree's, above the program, when not given</param>
        public SoundBrowserScreenViewModel(NavigationService navigationService, ICarPartsService parts, string? soundRoot = null)
        {
            _navigationService = navigationService;

            BackCommand = new RelayCommand(OnBack);
            PlayCommand = new RelayCommand(TogglePlay, () => _selectedFile != null);
            StopCommand = new RelayCommand(StopPlaying);
            KeepCommand = new RelayCommand(ToggleKeep, () => _selectedFile != null && _picks != null);
            ShowFileCommand = new RelayCommand(ShowFile, () => _selectedFile != null);

            _clock = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _clock.Tick += (_, _) => UpdatePlaying();

            Root = soundRoot ?? SoundFiles.FindDevRoot(AppContext.BaseDirectory) ?? "";
            // The engines first: a handful of categories, above the many folders of candidates
            var categories = new List<SoundCategory>(EngineCategories(parts));

            if (Root.Length > 0)
            {
                var candidates = Path.Combine(Root, "Candidates");
                _picks = LoadPicks(Path.Combine(Directory.Exists(candidates) ? candidates : Root, SoundPicks.FileName));
                categories.AddRange(SoundFiles.Scan(Root)
                    .GroupBy(f => FolderTitle(f.Category), StringComparer.OrdinalIgnoreCase)
                    .Select(g => new SoundCategory(g.Key,
                        g.Select(f => new SoundItem(f, _picks?.IsKept(f.RelativePath) == true)).ToList())));
            }

            Categories = categories;

            _status = Root.Length == 0 ? "No Assets\\Sound folder was found above the program's folder." : Summary();
        }

        public SoundCategory? SelectedCategory
        {
            get => _selectedCategory;
            set
            {
                if (!SetProperty(ref _selectedCategory, value)) return;
                StopPlaying();
                SelectedFile = null;
                SelectedEngine = null;
                OnPropertyChanged(nameof(Files));
                OnPropertyChanged(nameof(Engines));
                OnPropertyChanged(nameof(ShowsEngines));
            }
        }

        public bool ShowsEngines => _selectedCategory?.IsEngines == true;

        /// <summary>The category's sounds that match the filter</summary>
        public IReadOnlyList<SoundItem> Files =>
            _selectedCategory?.Files.Where(f => _filter.Length == 0 || f.Matches(_filter)).ToList() ?? [];

        public IReadOnlyList<EngineItem> Engines =>
            _selectedCategory?.Engines.Where(e => _filter.Length == 0 || e.Matches(_filter)).ToList() ?? [];

        public string Filter
        {
            get => _filter;
            set
            {
                if (!SetProperty(ref _filter, (value ?? "").Trim())) return;
                OnPropertyChanged(nameof(Files));
                OnPropertyChanged(nameof(Engines));
            }
        }

        public SoundItem? SelectedFile
        {
            get => _selectedFile;
            set
            {
                if (!SetProperty(ref _selectedFile, value)) return;
                if (value != null && _autoPlay) Play(value);
            }
        }

        public EngineItem? SelectedEngine
        {
            get => _selectedEngine;
            set
            {
                if (!SetProperty(ref _selectedEngine, value)) return;
                EngineSpec = value == null ? null : SpecFor(value);
            }
        }

        /// <summary>The engine under the panel's pedal</summary>
        public EngineSpec? EngineSpec
        {
            get => _engineSpec;
            private set => SetProperty(ref _engineSpec, value);
        }

        /// <summary>A sound picked in the list starts at once: arrows up and down go through a folder by ear</summary>
        public bool AutoPlay
        {
            get => _autoPlay;
            set => SetProperty(ref _autoPlay, value);
        }

        public bool Loop
        {
            get => _loop;
            set
            {
                if (SetProperty(ref _loop, value)) _playing?.SetLoop(value);
            }
        }

        public string Status
        {
            get => _status;
            private set => SetProperty(ref _status, value);
        }

        /// <summary>Where the playing sound is, and how long it is</summary>
        public string Position
        {
            get => _position;
            private set => SetProperty(ref _position, value);
        }

        public override void Exit()
        {
            StopPlaying();
            SelectedEngine = null;
            base.Exit();
        }

        private void OnBack()
        {
            StopPlaying();
            SelectedEngine = null;
            _navigationService.NavigateToMainMenu(MainMenu.MainMenuCard.Settings);
        }

        private void TogglePlay()
        {
            if (_selectedFile == null) return;
            if (_playingItem == _selectedFile && _playing?.IsPlaying == true) StopPlaying();
            else Play(_selectedFile);
        }

        /// <summary>The picked sound from its start, whether or not it plays already</summary>
        public void PlaySelected()
        {
            if (_selectedFile != null) Play(_selectedFile);
        }

        private void Play(SoundItem item)
        {
            StopPlaying();

            var playing = AudioHub.Shared.Play(item.File.Bus, item.File.FullPath, _loop);
            if (playing == null)
            {
                Status = $"{item.Title} could not be played (the log says why; FMOD needs the Assetto Corsa folder).";
                return;
            }

            _playing = playing;
            _playingItem = item;
            item.IsPlaying = true;
            if (playing.LengthMs > 0) item.Length = Time(playing.LengthMs);
            Status = $"Playing on the {item.File.Bus} bus: {item.File.RelativePath}";
            _clock.Start();
            UpdatePlaying();
        }

        private void StopPlaying()
        {
            _clock.Stop();
            _playing?.Stop();
            _playing = null;
            if (_playingItem != null) _playingItem.IsPlaying = false;
            _playingItem = null;
            Position = "";
        }

        private void UpdatePlaying()
        {
            if (_playing == null) return;
            if (!_playing.IsPlaying)
            {
                StopPlaying();
                return;
            }

            Position = _playing.LengthMs > 0 ? $"{Time(_playing.PositionMs)} / {Time(_playing.LengthMs)}" : Time(_playing.PositionMs);
        }

        private void ToggleKeep()
        {
            if (_selectedFile is not { } item || _picks == null) return;

            try
            {
                _picks.Set(item.File.RelativePath, !item.IsKept);
                item.IsKept = !item.IsKept;
                Status = Summary();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "The pick of {File} could not be saved", item.File.RelativePath);
                Status = $"The pick could not be saved: {ex.Message}";
            }
        }

        private void ShowFile()
        {
            if (_selectedFile == null) return;
            try
            {
                Process.Start("explorer.exe", $"/select,\"{_selectedFile.File.FullPath}\"");
            }
            catch (Exception ex)
            {
                _logger.Warning("Explorer could not show {File}: {Error}", _selectedFile.File.FullPath, ex.Message);
            }
        }

        private string Summary()
        {
            var files = Categories.Sum(c => c.Files.Count);
            var engines = Categories.Sum(c => c.Engines.Count);
            return $"{files} sounds, {engines} engine sounds" + (_picks != null ? $", {_picks.Count} kept" : "");
        }

        private SoundPicks? LoadPicks(string path)
        {
            try
            {
                return new SoundPicks(path);
            }
            catch (Exception ex)
            {
                // A pick list that cannot be read is not overwritten by a new one: keeping is off for now
                _logger.Error(ex, "The picks in {Path} could not be read", path);
                return null;
            }
        }

        /// <summary>
        /// "Candidates/tools/ratchet" as "tools › ratchet", the kept ones outside the candidates as they are. A pack's
        /// own "Audio" folder (Kenney's) says nothing: its sounds go with the pack's
        /// </summary>
        internal static string FolderTitle(string category)
        {
            var parts = category.Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
            if (parts.Count > 1 && parts[0].Equals("Candidates", StringComparison.OrdinalIgnoreCase)) parts.RemoveAt(0);
            if (parts.Count > 1 && parts[^1].Equals("Audio", StringComparison.OrdinalIgnoreCase)) parts.RemoveAt(parts.Count - 1);
            return parts.Count == 0 ? "(sound folder)" : string.Join(" › ", parts);
        }

        /// <summary>The library's engine sounds: the curated ones by make, the AC cars' own by how many cylinders</summary>
        private IEnumerable<SoundCategory> EngineCategories(ICarPartsService parts)
        {
            IReadOnlyList<Parts.Sounds.SoundEntry> all;
            try
            {
                if (!parts.IsAvailable) return [];
                all = parts.Sounds.All;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "The engine sounds could not be listed");
                return [];
            }

            return all
                .GroupBy(e => e.Curated
                    ? $"engines › {(e.Family.Length > 0 ? e.Family : "other")}"
                    : $"engines › AC cars › {(e.Cylinders is { } c ? $"{c} cylinders" : "unknown")}")
                .OrderBy(g => g.Key.Contains("AC cars") ? 1 : 0)
                .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => new SoundCategory(g.Key, g.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).Select(e => new EngineItem(e)).ToList()));
        }

        /// <summary>
        /// A stand-in engine to hear a bank on: no car's dyno, a plain curve under a limiter the bank's samples reach.
        /// The pitch follows the rpm, so what matters is the range.
        /// </summary>
        internal static EngineSpec SpecFor(EngineItem item)
        {
            var entry = item.Entry;
            var limiter = Math.Clamp((entry.RpmMax ?? 6500) - 300, 4500, 8000);
            const double peak = 450;
            return new EngineSpec(entry.Name, 800, limiter, entry.Cylinders ?? 8, 0.3,
                rpm => peak * (0.6 + 0.4 * Math.Sin(Math.Clamp(rpm / limiter, 0, 1) * Math.PI)), peak,
                entry.ToCarSound(), null);
        }

        private static string Time(uint ms) => TimeSpan.FromMilliseconds(ms) is var t && t.TotalHours >= 1
            ? t.ToString(@"h\:mm\:ss")
            : t.ToString(@"m\:ss\.f");
    }
}
