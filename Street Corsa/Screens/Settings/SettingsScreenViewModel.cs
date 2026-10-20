using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using StreetCorsa.Audio;
using StreetCorsa.Configuration;
using StreetCorsa.Dialogs;
using StreetCorsa.Dialogs.Information;
using StreetCorsa.Logging;
using StreetCorsa.Navigation;
using StreetCorsa.Services.Settings;
using StreetCorsa.Services.Text;
using StreetCorsa.ViewModels;
using StreetCorsa.Helpers;

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
        public RelayCommand OpenSoundBrowserCommand { get; }
        public RelayCommand BrowseAssettoCorsaFolderCommand { get; }

        public GameSettings Settings => _settingsService.Current;

        /// <summary>
        /// The credits the licences of what the game plays and shows ask for: FMOD, and whoever made the garage's
        /// radio in use (a mod's own radio names its own maker)
        /// </summary>
        public string Credits { get; } = MakeCredits();

        private static string MakeCredits()
        {
            var credits = "Sound by FMOD Studio, Firelight Technologies Pty Ltd." + GameText.Say("settings.credits.icons");
            try
            {
                var mods = Mods.ModSet.Installed;
                var garage = AppSettings.Instance.GarageShowroomKn5;
                var radio = Services.Garage.GarageProps.Find(mods, "radio", garage);
                if (radio?.Credit is { Length: > 0 } credit) credits += GameText.Say("settings.credits.radio", ("credit", credit));

                // The garage's other props whose licence asks for their makers to be named (CC BY); a CC0 one asks nothing
                string[] ownPlaces = [Parts.WorkbenchLayout.PropId, Parts.LiftLayout.PropId, Parts.EngineStandLayout.PropId];
                var named = Services.Garage.GarageProps.FindAll(mods, garage, ["radio", .. ownPlaces])
                    .Concat(ownPlaces.Select(id => Services.Garage.GarageProps.Find(mods, id, garage, builtIn: true)))
                    .Select(p => p?.Credit)
                    .OfType<string>()
                    .Where(c => c.Length > 0 && !c.Contains("CC0", StringComparison.OrdinalIgnoreCase))
                    .Distinct()
                    .ToList();
                if (named.Count > 0) credits += GameText.Say("settings.credits.garage", ("credits", string.Join("; ", named)));

                // The gas stations' makers: a station's credit names everyone its licences ask named, as its file
                // has it (it may mention CC0 models among them, so it is not sifted as a prop's single maker is)
                var installed = Services.Station.StationScenes.Installed;
                var stations = installed.Select(s => s.Credit).Where(c => c.Length > 0).Distinct().ToList();
                if (stations.Count > 0) credits += GameText.Say("settings.credits.station", ("credits", string.Join("; ", stations)));

                // And the attendants', whichever actor each station stands at its pump
                var attendants = installed.Select(s => s.Attendant?.Prop).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(prop => Services.Station.ActorProps.Find(mods, prop)?.Credit)
                    .OfType<string>()
                    .Where(c => c.Length > 0 && !c.Contains("CC0", StringComparison.OrdinalIgnoreCase))
                    .Distinct()
                    .ToList();
                if (attendants.Count > 0) credits += GameText.Say("settings.credits.attendant", ("credit", string.Join("; ", attendants)));
            }
            catch (Exception)
            {
                // Only a line of thanks: the settings open without it
            }

            return credits;
        }

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

        /// <summary>The player's own AI: the text provider and ComfyUI</summary>
        public AiSettingsSection Ai { get; }

        /// <summary>The languages of the mods next to the game, the game's own first</summary>
        public IReadOnlyList<LanguageChoice> Languages { get; }

        /// <summary>A mod gives the game another language to speak: the picker shows</summary>
        public bool HasLanguages => Languages.Count > 1;

        private LanguageChoice? _language;

        /// <summary>
        /// The language the game speaks. Saved at once; the game reads its words once, at start-up, so it speaks the
        /// new one the next time it starts
        /// </summary>
        public LanguageChoice? Language
        {
            get => _language;
            set => Pick(ref _language, value, OnLanguageChanged);
        }

        /// <summary>The units the player can read: the game's own first, then imperial and metric</summary>
        public IReadOnlyList<UnitsChoice> UnitChoices { get; }

        private UnitsChoice? _units;

        /// <summary>The units the player reads. Saved and taken at once: what a screen writes next is in them</summary>
        public UnitsChoice? Units
        {
            get => _units;
            set => Pick(ref _units, value, OnUnitsChanged);
        }

        private bool OnUnitsChanged(UnitsChoice choice)
        {
            var saved = Settings.Units;
            Settings.Units = choice.Id;
            if (!_settingsService.Save())
            {
                Settings.Units = saved;
                ShowNotSaved(GameText.Say("settings.not_saved"));
                return false;
            }

            Locale.Units = UnitsIn(choice.Id);
            _logger.Information("Units: {Units}", Locale.Units);
            return true;
        }

        /// <summary>The units of a pick: its own, or the game's for none</summary>
        private static UnitSystem UnitsIn(string? picked) =>
            Locale.ParseUnits(picked) ?? Rules.GameFile.Installed.Locale.UnitSystem;

        private static IReadOnlyList<UnitsChoice> MakeUnits(string? picked, out UnitsChoice? selected)
        {
            var game = Rules.GameFile.Installed.Locale.UnitSystem;
            var choices = new List<UnitsChoice>
            {
                new(null, GameText.Say("settings.units.game", ("units", GameText.Say(UnitsKey(game))))),
                new("imperial", GameText.Say(UnitsKey(UnitSystem.Imperial))),
                new("metric", GameText.Say(UnitsKey(UnitSystem.Metric)))
            };
            var pick = Locale.ParseUnits(picked);
            selected = pick == null ? choices[0] : choices.First(c => Locale.ParseUnits(c.Id) == pick);
            return choices;
        }

        private static string UnitsKey(UnitSystem units) => units == UnitSystem.Imperial ? "settings.units.imperial" : "settings.units.metric";

        /// <summary>The choice to disk; false when it did not get there</summary>
        private bool OnLanguageChanged(LanguageChoice choice)
        {
            var saved = Settings.Language;
            Settings.Language = choice.IsGameLanguage ? null : choice.Id;
            if (!_settingsService.Save())
            {
                Settings.Language = saved;
                ShowNotSaved(GameText.Say("settings.not_saved"));
                return false;
            }

            _dialogService.ShowDialog(new InformationDialogViewModel(_dialogService,
                GameText.Say("settings.language.restart", ("language", choice.Name)), GameText.Say("settings.restart.title")));
            return true;
        }

        /// <summary>
        /// A picker's pick, saved by <paramref name="save"/>; one that did not get saved puts the picker back on what
        /// is, told once the picker is done with the pick, which it would otherwise keep showing
        /// </summary>
        private void Pick<T>(ref T? field, T? value, Func<T, bool> save, [CallerMemberName] string property = "") where T : class
        {
            if (value == null || ReferenceEquals(value, field)) return;
            var previous = field;
            field = value;
            OnPropertyChanged(property);
            if (previous == null || save(value)) return;

            field = previous;
            UiThread.Later(() => OnPropertyChanged(property), DispatcherPriority.Background);
        }

        private static IReadOnlyList<LanguageChoice> MakeLanguages(string? picked, out LanguageChoice? selected)
        {
            var languages = new List<LanguageChoice>();
            try
            {
                var own = true;
                foreach (var (id, name) in GameText.AvailableLanguages)
                {
                    languages.Add(new LanguageChoice(id, name, own));
                    own = false;
                }
            }
            catch (Exception)
            {
                // No list, no picker: the game speaks what it speaks
            }

            selected = languages.FirstOrDefault(l => string.Equals(l.Id, picked, StringComparison.OrdinalIgnoreCase))
                       ?? languages.FirstOrDefault();
            return languages;
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
            : GameText.Say("settings.ac_folder.not_ac");

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
            OpenSoundBrowserCommand = new RelayCommand(OnOpenSoundBrowser);
            BrowseAssettoCorsaFolderCommand = new RelayCommand(OnBrowseAssettoCorsaFolder);

            _assettoCorsaFolder = SavedAssettoCorsaFolder;
            Languages = MakeLanguages(Settings.Language, out _language);
            UnitChoices = MakeUnits(Settings.Units, out _units);

            Ai = new AiSettingsSection(settingsService, Services.Ai.AiHub.Shared.Text, Services.Ai.AiHub.Shared.Images, ShowNotSaved);

            _audioSave = new DispatcherTimer { Interval = AudioSaveDelay };
            _audioSave.Tick += (_, _) => SaveAudio();
            Volumes = MakeVolumes();
        }

        private IReadOnlyList<VolumeSlider> MakeVolumes()
        {
            var audio = Settings.Audio;
            return
            [
                new VolumeSlider(GameText.Say("settings.volume.master"), null, audio.Master, OnVolumeChanged),
                new VolumeSlider(GameText.Say("settings.volume.engines"), AudioBus.Engine, audio.Engine, OnVolumeChanged),
                new VolumeSlider(GameText.Say("settings.volume.radio"), AudioBus.Radio, audio.Radio, OnVolumeChanged),
                new VolumeSlider(GameText.Say("settings.volume.effects"), AudioBus.Effects, audio.Effects, OnVolumeChanged),
                new VolumeSlider(GameText.Say("settings.volume.ambience"), AudioBus.Ambience, audio.Ambience, OnVolumeChanged),
                new VolumeSlider(GameText.Say("settings.volume.interface"), AudioBus.Interface, audio.Interface, OnVolumeChanged)
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
                Title = GameText.Say("settings.ac_folder"),
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
                    ShowNotSaved(GameText.Say("settings.not_saved"));
                    return;
                }

                _logger.Information("Settings saved");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not save the settings");
                Settings.AssettoCorsaPath = savedPath;
                ShowNotSaved(GameText.Say("settings.save_failed", ("error", ex.Message)));
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
                GameText.Say("settings.ac_folder.restart", ("folder", AssettoCorsaFolder)),
                GameText.Say("settings.restart.title")));
        }

        private void ShowNotSaved(string message)
        {
            _dialogService.ShowDialog(new InformationDialogViewModel(_dialogService, message, GameText.Say("settings.not_saved.title")));
        }

        private void OnReset()
        {
            try
            {
                if (!_settingsService.ResetToDefaults())
                {
                    ShowNotSaved(GameText.Say("settings.not_reset"));
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not reset the settings");
                ShowNotSaved(GameText.Say("settings.reset_failed", ("error", ex.Message)));
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
            _language = Languages.FirstOrDefault();
            OnPropertyChanged(nameof(Language));
            _units = UnitChoices[0];
            Locale.Units = UnitsIn(null);
            OnPropertyChanged(nameof(Units));
            Ai.Reload();
            _logger.Information("Settings reset to defaults");

            // The defaults name the default folder: a player whose install is elsewhere needs to know
            ShowRestartNoticeIfFolderChanged();
        }

        private void OnOpenSoundBrowser()
        {
            if (_audioSave.IsEnabled) SaveAudio();
            _navigationService.GoKind(ScreenKinds.SoundBrowser, null);
        }

        private void OnOpenCatalogEditor()
        {
            _logger.Information("Navigating to car catalog editor");
            _navigationService.GoKind(ScreenKinds.CarCatalogEditor, null);
        }

        /// <summary>The same test the game makes of the install at start-up: a cars and a tracks folder</summary>
        public static bool LooksLikeAssettoCorsa(string? folder) =>
            !string.IsNullOrWhiteSpace(folder)
            && Directory.Exists(Path.Combine(folder, "content", "cars"))
            && Directory.Exists(Path.Combine(folder, "content", "tracks"));
    }
}
