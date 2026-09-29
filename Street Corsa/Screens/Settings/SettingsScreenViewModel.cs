using System.IO;
using System.Windows.Threading;
using StreetCorsa.Audio;
using StreetCorsa.Configuration;
using StreetCorsa.Dialogs;
using StreetCorsa.Dialogs.Information;
using StreetCorsa.Logging;
using StreetCorsa.Navigation;
using StreetCorsa.Services.Settings;
using StreetCorsa.ViewModels;

namespace StreetCorsa.Screens.Settings
{
    public class SettingsScreenViewModel : BaseScreenViewModel
    {
        private readonly NavigationService _navigationService;
        private readonly Action _back;
        private readonly DialogService _dialogService;
        private readonly GameSettingsService _settingsService;
        private readonly IAppLogger _logger;

        public RelayCommand BackCommand { get; }
        public RelayCommand SaveCommand { get; }
        public RelayCommand ResetCommand { get; }
        public RelayCommand OpenCatalogEditorCommand { get; }
        public RelayCommand BrowseAssettoCorsaFolderCommand { get; }

        public GameSettings Settings => _settingsService.Current;

        // Volumes are saved a moment after the last slider move, not on every step of a drag
        private static readonly TimeSpan AudioSaveDelay = TimeSpan.FromMilliseconds(600);
        private readonly DispatcherTimer _audioSave;

        /// <summary>The master volume and one for each kind of sound; heard as they move, saved a moment later</summary>
        public IReadOnlyList<VolumeSlider> Volumes { get; private set; } = [];

        /// <summary>The game goes quiet while another window has the focus</summary>
        public bool MuteInBackground
        {
            get => Settings.Audio.MuteInBackground;
            set
            {
                if (Settings.Audio.MuteInBackground == value) return;
                Settings.Audio.MuteInBackground = value;
                OnPropertyChanged();
                OnAudioChanged();
            }
        }

        private string _assettoCorsaFolder;

        /// <summary>
        /// The Assetto Corsa folder the game uses. Read once at start-up: a new one takes effect the next time
        /// the game starts, because the catalog and the car-data overlay are built on the folder it started with.
        /// </summary>
        public string AssettoCorsaFolder
        {
            get => _assettoCorsaFolder;
            set
            {
                if (SetProperty(ref _assettoCorsaFolder, value ?? string.Empty))
                    OnPropertyChanged(nameof(AssettoCorsaFolderHint));
            }
        }

        /// <summary>Whether the folder typed or picked looks like an Assetto Corsa install</summary>
        public string AssettoCorsaFolderHint => LooksLikeAssettoCorsa(AssettoCorsaFolder)
            ? string.Empty
            : "No Assetto Corsa installation found in this folder (it needs content\\cars and content\\tracks).";

        public SettingsScreenViewModel(
            NavigationService navigationService,
            DialogService dialogService,
            GameSettingsService settingsService,
            Action back)
        {
            _navigationService = navigationService;
            _back = back;
            _dialogService = dialogService;
            _settingsService = settingsService;
            _logger = AppLoggerFactory.CreateLogger("Settings");

            BackCommand = new RelayCommand(OnBack);
            SaveCommand = new RelayCommand(OnSave);
            ResetCommand = new RelayCommand(OnReset);
            OpenCatalogEditorCommand = new RelayCommand(OnOpenCatalogEditor);
            BrowseAssettoCorsaFolderCommand = new RelayCommand(OnBrowseAssettoCorsaFolder);

            _assettoCorsaFolder = SavedAssettoCorsaFolder;

            _audioSave = new DispatcherTimer { Interval = AudioSaveDelay };
            _audioSave.Tick += (_, _) => SaveAudio();
            Volumes = MakeVolumes();
        }

        private IReadOnlyList<VolumeSlider> MakeVolumes()
        {
            var audio = Settings.Audio;
            return
            [
                new VolumeSlider("Master", null, audio.Master, OnVolumeChanged),
                new VolumeSlider("Engines", AudioBus.Engine, audio.Engine, OnVolumeChanged),
                new VolumeSlider("Radio", AudioBus.Radio, audio.Radio, OnVolumeChanged),
                new VolumeSlider("Tools and parts", AudioBus.Effects, audio.Effects, OnVolumeChanged),
                new VolumeSlider("Surroundings", AudioBus.Ambience, audio.Ambience, OnVolumeChanged),
                new VolumeSlider("Menus and buttons", AudioBus.Interface, audio.Interface, OnVolumeChanged)
            ];
        }

        private void OnVolumeChanged(VolumeSlider slider)
        {
            if (slider.Bus is { } bus) Settings.Audio.Set(bus, slider.Value);
            else Settings.Audio.Master = slider.Value;
            OnAudioChanged();
        }

        /// <summary>Heard at once; on disk once the player has let go of the slider for a moment</summary>
        private void OnAudioChanged()
        {
            AudioHub.Shared.Apply(Settings.Audio);
            _audioSave.Stop();
            _audioSave.Start();
        }

        /// <summary>
        /// The volumes to disk, with whatever else the settings in use hold (the AC folder only changes there on Save).
        /// A volume that did not reach the disk is still heard: the service logs it, and the next move tries again.
        /// </summary>
        private void SaveAudio()
        {
            _audioSave.Stop();
            if (_settingsService.Save()) _logger.Information("Volumes saved");
        }

        /// <summary>The folder the settings name, or the default when they name none</summary>
        private string SavedAssettoCorsaFolder =>
            string.IsNullOrWhiteSpace(Settings.AssettoCorsaPath) ? AppSettings.DefaultAssettoCorsaPath : Settings.AssettoCorsaPath.Trim();

        public override void Enter()
        {
            base.Enter();
            _logger.Information("Entered settings screen");
        }

        private void OnBack()
        {
            if (_audioSave.IsEnabled) SaveAudio();
            _back();
        }

        private void OnBrowseAssettoCorsaFolder()
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Assetto Corsa folder",
                InitialDirectory = Directory.Exists(AssettoCorsaFolder) ? AssettoCorsaFolder : string.Empty
            };

            if (dialog.ShowDialog() == true)
            {
                AssettoCorsaFolder = dialog.FolderName;
            }
        }

        private void OnSave()
        {
            var folder = AssettoCorsaFolder.Trim();
            var savedPath = Settings.AssettoCorsaPath;

            try
            {
                // The default is stored as no folder at all, so a later default follows along
                Settings.AssettoCorsaPath = folder.Length == 0 ||
                    string.Equals(folder, AppSettings.DefaultAssettoCorsaPath, StringComparison.OrdinalIgnoreCase)
                    ? null
                    : folder;

                // The service logs why; a false here means nothing reached the disk
                if (!_settingsService.Save())
                {
                    // Not on disk, so not in use either: a later save of something else must not carry it along
                    Settings.AssettoCorsaPath = savedPath;
                    ShowNotSaved("The settings could not be saved. The details are in the log.");
                    return;
                }

                _logger.Information("Settings saved");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not save the settings");
                Settings.AssettoCorsaPath = savedPath;
                ShowNotSaved($"The settings could not be saved:\n\n{ex.Message}");
                return;
            }

            AssettoCorsaFolder = SavedAssettoCorsaFolder;
            ShowRestartNoticeIfFolderChanged();
        }

        /// <summary>
        /// The running game keeps the folder it started with: once a saved folder differs from it, the player
        /// hears that it takes a restart
        /// </summary>
        private void ShowRestartNoticeIfFolderChanged()
        {
            if (string.Equals(AssettoCorsaFolder, AppSettings.Instance.AssettoCorsaPath, StringComparison.OrdinalIgnoreCase))
                return;

            _dialogService.ShowDialog(new InformationDialogViewModel(
                _dialogService,
                $"The game will use the Assetto Corsa folder\n{AssettoCorsaFolder}\nthe next time it starts. Restart the game for the change to take effect.",
                "Restart Needed"));
        }

        private void ShowNotSaved(string message)
        {
            _dialogService.ShowDialog(new InformationDialogViewModel(_dialogService, message, "Settings Not Saved"));
        }

        private void OnReset()
        {
            try
            {
                if (!_settingsService.ResetToDefaults())
                {
                    ShowNotSaved("The settings could not be reset. The details are in the log.");
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not reset the settings");
                ShowNotSaved($"The settings could not be reset:\n\n{ex.Message}");
                return;
            }

            OnPropertyChanged(nameof(Settings));
            AssettoCorsaFolder = SavedAssettoCorsaFolder;

            // The defaults' volumes, heard and shown; the pending save of the old ones would put them back
            _audioSave.Stop();
            AudioHub.Shared.Apply(Settings.Audio);
            Volumes = MakeVolumes();
            OnPropertyChanged(nameof(Volumes));
            OnPropertyChanged(nameof(MuteInBackground));
            _logger.Information("Settings reset to defaults");

            // The defaults name the default folder: a player whose install is elsewhere needs to know
            ShowRestartNoticeIfFolderChanged();
        }

        private void OnOpenCatalogEditor()
        {
            _logger.Information("Navigating to car catalog editor");
            _navigationService.NavigateToCarCatalogEditor();
        }

        /// <summary>The same test the game makes of the install at start-up: a cars and a tracks folder</summary>
        public static bool LooksLikeAssettoCorsa(string? folder) =>
            !string.IsNullOrWhiteSpace(folder)
            && Directory.Exists(Path.Combine(folder, "content", "cars"))
            && Directory.Exists(Path.Combine(folder, "content", "tracks"));
    }
}
