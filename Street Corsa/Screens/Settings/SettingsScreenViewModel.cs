using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using StreetCorsa.Audio;
using StreetCorsa.Configuration;
using StreetCorsa.Dialogs;
using StreetCorsa.Dialogs.Confirmation;
using StreetCorsa.Dialogs.Information;
using StreetCorsa.Logging;
using StreetCorsa.Navigation;
using StreetCorsa.Services.Parts;
using StreetCorsa.Services.Settings;
using StreetCorsa.Services.Text;
using StreetCorsa.ViewModels;
using StreetCorsa.Helpers;

namespace StreetCorsa.Screens.Settings
{
    public class SettingsScreenViewModel : BaseScreenViewModel, IMenuCard
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
        /// The credits the licences of what the game plays and shows ask for, a section each, on the credits screen
        /// (<see cref="ShowingCredits"/>): FMOD, the icons' makers (<see cref="IconCredits"/>), whoever made the garage's
        /// radio in use (a mod's own radio names its own maker), the garage props, gas stations, attendants, streets and
        /// street pieces, the music and sounds that are not CC0 (sfx/credits.csv); the parts' makers once the parts catalog
        /// is loaded (<see cref="AddPartCredits"/>). Every mod installed is credited, in play or not: Settings opens on the
        /// main screen's core, and the settings and add-ons it does not play bring other people's work too. Read the first
        /// time the credits are shown, not each time Settings opens (every mod's stations, streets, kit and parts).
        /// </summary>
        public ObservableCollection<CreditSection> Credits { get; } = [];
        private bool _creditsRead;

        /// <summary>The credits screen is up in the card in the settings' place</summary>
        public bool ShowingCredits
        {
            get => _showingCredits;
            private set => SetProperty(ref _showingCredits, value);
        }
        private bool _showingCredits;

        public RelayCommand ShowCreditsCommand { get; }
        public RelayCommand HideCreditsCommand { get; }

        private void ShowCredits()
        {
            if (!_creditsRead)
            {
                _creditsRead = true;
                foreach (var section in MakeCredits()) Credits.Add(section);
                AddPartCredits();
            }

            ShowingCredits = true;
        }

        /// <summary>
        /// A section of the credits under the heading <paramref name="key"/> (settings.credits.heading.*): its lines once
        /// each, the blank ones left out; null when none is left
        /// </summary>
        internal static CreditSection? Section(string key, IEnumerable<string> lines)
        {
            var list = lines.Where(l => l.Trim().Length > 0).Distinct().ToList();
            return list.Count > 0 ? new CreditSection(GameText.Say(key), list) : null;
        }

        private static List<CreditSection> MakeCredits()
        {
            // FMOD's and the icons' credits come out of the game itself (IconCredits), never a text pool a translation or a
            // mod could change: only their headings are words of the game
            var credits = new List<CreditSection>
            {
                new(GameText.Say("settings.credits.heading.fmod"), ["Sound by FMOD Studio, Firelight Technologies Pty Ltd."])
            };
            void Add(string key, IEnumerable<string> lines)
            {
                if (Section("settings.credits.heading." + key, lines) is { } section) credits.Add(section);
            }

            Add("icons", IconCredits.Entries(IconCredits.Load()));
            try
            {
                var mods = Mods.ModSet.Everything;

                // The garage of each setting (and of the content in play): its radio, and its other props
                var garages = new[] { AppSettings.Instance.GarageShowroomKn5 }
                    .Concat(Mods.ModSet.Available.Settings.Select(s => AppSettings.Instance.GarageKn5Of(Mods.ModSet.PlayedIn(s.Id))))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                Add("radio", garages.Select(garage => Services.Garage.GarageProps.Find(mods, "radio", garage)?.Credit).OfType<string>());

                // The other props whose licence asks for their makers to be named (CC BY), every model the mods ship
                // whichever garage places it, if any (the core's hoist is placed by a setting only); a CC0 one asks nothing
                Add("garage", Services.Garage.GarageProps.Credits(mods, ["radio"])
                    .Where(c => !c.Contains("CC0", StringComparison.OrdinalIgnoreCase)));

                // The gas stations' makers: a station's credit names everyone its licences ask named, as its file
                // has it (it may mention CC0 models among them, so it is not sifted as a prop's single maker is)
                var installed = Services.Station.StationScenes.LoadAll(mods);
                Add("station", installed.Select(s => s.Credit));

                // And the attendants', whichever actor each station stands at its pump
                Add("attendant", installed.Select(s => s.Attendant?.Prop).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(prop => Services.Station.ActorProps.Find(mods, prop)?.Credit)
                    .OfType<string>()
                    .Where(c => !c.Contains("CC0", StringComparison.OrdinalIgnoreCase)));

                // The streets' makers (a street's credit names everyone its licences ask named, as the stations' do)
                Add("street", Services.Street.StreetScenes.LoadAll(mods).Select(s => s.Credit));

                // And the street kit's pieces the streets are laid out with (a piece's credit names its models' makers); the
                // CC0 ones ask nothing
                Add("streetkit", Services.Street.StreetKit.Load(mods, []).NamedCredits);

                // The music's and sounds' makers their licences ask named (sfx/credits.csv); the CC0 ones and the game's own ask nothing
                Add("sound", SoundFiles.NamedCredits(mods));
            }
            catch (Exception)
            {
                // Only thanks: the settings open without them
            }

            return credits;
        }

        /// <summary>
        /// The parts whose models are other people's (the running gear's axles, brakes and springs), off every mod's parts:
        /// it is loaded behind the UI thread, the credits showing the others till then
        /// </summary>
        private async void AddPartCredits()
        {
            try
            {
                // The parts of every mod installed, not only the content in play's
                var credits = await Task.Run(() => Parts.PartsCatalog.Load(Mods.ModSet.Everything.Folders(Mods.ContentTypes.Parts).ToList()).Credits());
                if (Section("settings.credits.heading.parts", credits) is { } section) Credits.Add(section);
            }
            catch (Exception ex)
            {
                // Only thanks: the credits stand without them
                _logger.Warning(ex, "The parts' credits could not be read");
            }
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
        /// The Assetto Corsa folder the game uses. A new one saved is put in use at once when it can be (no AC running,
        /// nothing of a race to put back in the old folder; see <see cref="NavigationService.SwitchAcFolder"/>), its cars
        /// in the next game started or loaded; otherwise it takes effect the next time the game starts.
        /// </summary>
        public string AssettoCorsaFolder
        {
            get => _assettoCorsaFolder;
            set
            {
                if (!SetProperty(ref _assettoCorsaFolder, value ?? string.Empty)) return;
                OnPropertyChanged(nameof(AssettoCorsaFolderHint));
                RelayCommand.RaiseCanExecuteChanged();
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
            SaveCommand = new RelayCommand(OnSave, () => CanSave);
            ResetCommand = new RelayCommand(OnReset);
            OpenCatalogEditorCommand = new RelayCommand(OnOpenCatalogEditor);
            OpenSoundBrowserCommand = new RelayCommand(OnOpenSoundBrowser);
            BrowseAssettoCorsaFolderCommand = new RelayCommand(OnBrowseAssettoCorsaFolder);
            ShowCreditsCommand = new RelayCommand(ShowCredits);
            HideCreditsCommand = new RelayCommand(() => ShowingCredits = false);

            _assettoCorsaFolder = SavedAssettoCorsaFolder;
            // Saved before but not in use yet (AC ran when it was saved): Save puts it in use once it can
            _folderWaiting = !SameFolder(SavedAssettoCorsaFolder, AppSettings.Instance.AssettoCorsaPath) && LooksLikeAssettoCorsa(SavedAssettoCorsaFolder);
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

        public override void Exit()
        {
            // However the card goes (another card opened, the game started): the volumes moved last are kept
            if (_audioSave.IsEnabled) SaveAudio();
            base.Exit();
        }

        private void OnBack() => Leave(_back);

        /// <inheritdoc/>
        public void StepBack()
        {
            if (ShowingCredits) ShowingCredits = false;
            else OnBack();
        }

        /// <summary>
        /// The card goes: at once, unless something typed is not saved (the folder, the AI's boxes), when the player is
        /// asked first whether to leave it unsaved
        /// </summary>
        public void Leave(Action go)
        {
            if (_audioSave.IsEnabled) SaveAudio();
            if (!HasChanges)
            {
                go();
                return;
            }

            _dialogService.ShowDialog(new ConfirmationDialogViewModel(_dialogService,
                GameText.Say("settings.unsaved"), GameText.Say("settings.unsaved.title"),
                leave => { if (leave) go(); }));
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

        /// <summary>
        /// Something typed is not saved yet: the Assetto Corsa folder, or the AI's provider, address, model, key or ComfyUI
        /// address (the rest of the card is saved as it changes)
        /// </summary>
        public bool HasChanges => FolderChanged || Ai.HasChanges;

        /// <summary>
        /// The Save has something to do: what is typed, or a folder saved while it could not go in use (AC ran), which a
        /// Save tries again to put in use
        /// </summary>
        public bool CanSave => HasChanges || _folderWaiting;
        private bool _folderWaiting;

        private bool FolderChanged => !SameFolder(AssettoCorsaFolder, SavedAssettoCorsaFolder);

        /// <summary>A folder as the game compares it: in full, without a trailing slash, the default for a blank one</summary>
        internal static string FolderKey(string? folder)
        {
            var key = (folder ?? string.Empty).Trim();
            if (key.Length == 0) key = AppSettings.DefaultAssettoCorsaPath;
            try
            {
                key = Path.GetFullPath(key);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                // Not a path Windows takes: compared as typed
            }

            return key.TrimEnd('\\', '/');
        }

        internal static bool SameFolder(string? a, string? b) =>
            string.Equals(FolderKey(a), FolderKey(b), StringComparison.OrdinalIgnoreCase);

        /// <summary>A folder as the settings keep it: none for the default (so a later default follows along), else as typed</summary>
        internal static string? StoredFolder(string? typed) =>
            SameFolder(typed, AppSettings.DefaultAssettoCorsaPath) ? null : typed!.Trim();

        /// <summary>
        /// The card's one Save: the AI section's typed settings and the folder, written together in one go (both or
        /// neither); an AI refused (a key for an address it may not go to) saves nothing. Then a saved folder not in use is
        /// put in use when it can be, last, as it may tell the player it takes a restart.
        /// </summary>
        private void OnSave()
        {
            AiSettings? ai = null;
            if (Ai.HasChanges && (ai = Ai.Prepare()) == null)
            {
                // The section told the player why
                return;
            }

            var folderChanged = FolderChanged;
            if (ai != null || folderChanged)
            {
                var stored = folderChanged ? StoredFolder(AssettoCorsaFolder) : Settings.AssettoCorsaPath;
                if (!SaveTogether(_settingsService, ai, stored, out var error))
                {
                    if (error != null) _logger.Error(error, "Could not save the settings");
                    ShowNotSaved(error == null ? GameText.Say("settings.not_saved") : GameText.Say("settings.save_failed", ("error", error.Message)));
                    return;
                }

                _logger.Information("Settings saved");
                if (ai != null) Ai.Saved(ai);
                if (folderChanged) AssettoCorsaFolder = SavedAssettoCorsaFolder;
            }

            ShowRestartNoticeIfFolderChanged();
            RelayCommand.RaiseCanExecuteChanged();
        }

        /// <summary>
        /// The AI's settings (when <paramref name="ai"/> is not null) and the AC folder into the settings and to disk in
        /// one write; on a failure both are put back as they were, so nothing half saved stays in use. <paramref name="error"/>
        /// is what was thrown, if anything (a false without one: the service logged why).
        /// </summary>
        internal static bool SaveTogether(GameSettingsService service, AiSettings? ai, string? folder, out Exception? error)
        {
            var settings = service.Current;
            var savedAi = settings.Ai;
            var savedPath = settings.AssettoCorsaPath;
            error = null;
            try
            {
                if (ai != null) settings.Ai = ai;
                settings.AssettoCorsaPath = folder;
                if (service.Save()) return true;
            }
            catch (Exception ex)
            {
                error = ex;
            }

            // Not on disk, so not in use either: a later save of something else must not carry it along
            settings.Ai = savedAi;
            settings.AssettoCorsaPath = savedPath;
            return false;
        }

        /// <summary>
        /// A saved folder other than the one in use is put in use now when it can be (no AC running, nothing of a race
        /// to put back in the old one), and the player hears that its cars are in the next game; otherwise that it
        /// takes a restart, or that it is no AC folder at all
        /// </summary>
        private void ShowRestartNoticeIfFolderChanged()
        {
            var folder = SavedAssettoCorsaFolder;
            _folderWaiting = false;
            if (SameFolder(folder, AppSettings.Instance.AssettoCorsaPath)) return;

            var result = _navigationService.SwitchAcFolder(folder);
            // AC ran, or a race left something to put back: the Save stays on to try again once it is closed
            _folderWaiting = result == AcFolderSwitch.Later;
            var (message, title) = result switch
            {
                AcFolderSwitch.Switched => ("settings.ac_folder.switched", "settings.ac_folder.switched.title"),
                AcFolderSwitch.NotAnInstall => ("settings.ac_folder.not_install", "settings.restart.title"),
                _ => ("settings.ac_folder.restart", "settings.restart.title")
            };
            _dialogService.ShowDialog(new InformationDialogViewModel(
                _dialogService,
                GameText.Say(message, ("folder", folder)),
                GameText.Say(title)));
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
            RelayCommand.RaiseCanExecuteChanged();
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
