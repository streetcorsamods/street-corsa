using System.Collections.ObjectModel;
using System.IO;
using StreetCorsa.Dialogs;
using StreetCorsa.Dialogs.Confirmation;
using StreetCorsa.Dialogs.Information;
using StreetCorsa.Logging;
using StreetCorsa.Mods;
using StreetCorsa.Navigation;
using StreetCorsa.Screens.MainMenu;
using StreetCorsa.Services.Text;
using StreetCorsa.ViewModels;

namespace StreetCorsa.Screens.ModManager
{
    /// <summary>A mod's row in the Mod Manager</summary>
    public sealed class ModRow(InstalledMod mod)
    {
        public InstalledMod Mod { get; } = mod;

        public string Name => Mod.Info?.Name is { Length: > 0 } name ? name : Mod.Id;

        /// <summary>Version and author, as far as its mod.json gives them</summary>
        public string Byline => string.Join("  ·  ", new[]
        {
            string.IsNullOrWhiteSpace(Mod.Info?.Version) ? null : GameText.Say("mods.version", ("version", Mod.Info!.Version)),
            string.IsNullOrWhiteSpace(Mod.Info?.Author) ? null : GameText.Say("mods.author", ("author", Mod.Info!.Author))
        }.Where(s => s != null));

        /// <summary>A whole game, or an add-on and the group it sits in</summary>
        public string Kind => Mod.IsSetting
            ? GameText.Say("mods.kind.setting")
            : GameText.Say("mods.kind.addon", ("group", Mod.Group.Length > 0 ? Mod.Group : GameText.Say("mods.kind.top")));

        /// <summary>The kind, then the byline when there is one</summary>
        public string Details => Byline.Length > 0 ? Kind + "  ·  " + Byline : Kind;

        public string Description => Mod.Info?.Description ?? string.Empty;

        public bool On => Mod.On;

        /// <summary>On, off, or on but left out by the game</summary>
        public string Status => !Mod.On ? GameText.Say("mods.status.off")
            : Mod.Loaded ? GameText.Say("mods.status.on")
            : GameText.Say("mods.status.left_out");

        public bool HasProblems => Mod.Problems.Count > 0;

        public string Problems => string.Join("\n", Mod.Problems);

        /// <summary>What the switch button says</summary>
        public string SwitchText => GameText.Say(Mod.On ? "mods.switch_off" : "mods.switch_on");
    }

    /// <summary>A pick of the type filter: every mod, the settings, or the add-ons of one group</summary>
    /// <param name="Id">"" for all, "setting", or "group:&lt;group&gt;"</param>
    public sealed record ModKind(string Id, string Label)
    {
        public const string All = "";
        public const string Setting = "setting";

        public static string OfGroup(string group) => "group:" + group;

        public bool Takes(InstalledMod mod) => Id switch
        {
            All => true,
            Setting => mod.IsSetting,
            _ => !mod.IsSetting && Id == OfGroup(mod.Group)
        };

        public override string ToString() => Label;
    }

    /// <summary>
    /// The Mod Manager card of the main screen: every mod in the Content folder (the core left out) with what it is and
    /// what the game makes of it; mods installed from zips, switched off and on, deleted. Outside any game: what is in
    /// play then is the core alone, so nothing listed here is in use.
    /// </summary>
    public class ModManagerScreenViewModel : BaseScreenViewModel
    {
        public override bool InGame => false;

        private readonly DialogService _dialogService;
        private readonly Action _back;
        private readonly string _root;
        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger("Mods");
        private string _errorMessage = string.Empty;
        private IReadOnlyList<ModRow> _all = [];
        private IReadOnlyList<ModKind> _kinds = [];
        private ModKind? _kind;
        private string _search = string.Empty;

        /// <summary>The mods the filters let through</summary>
        public ObservableCollection<ModRow> Mods { get; } = new();

        /// <summary>Any mod installed at all</summary>
        public bool HasMods => _all.Count > 0;

        /// <summary>Mods installed, but none the filters let through</summary>
        public bool NoMatch => _all.Count > 0 && Mods.Count == 0;

        /// <summary>The type filter's picks: every mod, the settings, then each group the add-ons are in</summary>
        public IReadOnlyList<ModKind> Kinds
        {
            get => _kinds;
            private set => SetProperty(ref _kinds, value);
        }

        public ModKind? Kind
        {
            get => _kind;
            set
            {
                if (SetProperty(ref _kind, value)) Filter();
            }
        }

        /// <summary>Words the name, id, author or description must hold</summary>
        public string Search
        {
            get => _search;
            set
            {
                if (SetProperty(ref _search, value ?? string.Empty)) Filter();
            }
        }

        public RelayCommand ClearSearchCommand { get; }

        public string ErrorMessage
        {
            get => _errorMessage;
            private set => SetProperty(ref _errorMessage, value);
        }

        /// <summary>The main screen's Install mods: the list is read again once it is done</summary>
        public ModInstallFlow Install { get; }

        public RelayCommand<ModRow> SwitchCommand { get; }
        public RelayCommand<ModRow> DeleteCommand { get; }
        public RelayCommand OpenContentFolderCommand { get; }
        public RelayCommand BackCommand { get; }

        /// <param name="install">The main screen's Install mods</param>
        /// <param name="back">Closes the card, back to the menu</param>
        /// <param name="root">The Content folder; the game's by default</param>
        public ModManagerScreenViewModel(DialogService dialogService, ModInstallFlow install, Action back, string? root = null)
        {
            _dialogService = dialogService;
            _back = back;
            _root = root ?? ModSet.DefaultRoot;
            Install = install;

            SwitchCommand = new RelayCommand<ModRow>(OnSwitch, r => r != null && !Install.IsBusy);
            DeleteCommand = new RelayCommand<ModRow>(OnDelete, r => r != null && !Install.IsBusy);
            OpenContentFolderCommand = new RelayCommand(OnOpenContentFolder);
            BackCommand = new RelayCommand(() => _back());
            ClearSearchCommand = new RelayCommand(() => Search = string.Empty);
        }

        public override void Enter()
        {
            base.Enter();
            Refresh();
        }

        /// <summary>The Content folder read again</summary>
        public void Refresh()
        {
            try
            {
                _all = global::StreetCorsa.Mods.ModManager.List(_root).Select(m => new ModRow(m)).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.Error(ex, "The mods in {Root} could not be listed", _root);
                ErrorMessage = GameText.Say("mods.list_failed", ("error", ex.Message));
                _all = [];
            }

            // The picks follow what is installed; the one picked stays when it still is there
            var kinds = KindsOf(_all.Select(r => r.Mod));
            var picked = _kind?.Id;
            Kinds = kinds;
            _kind = kinds.FirstOrDefault(k => k.Id == picked) ?? kinds[0];
            OnPropertyChanged(nameof(Kind));
            OnPropertyChanged(nameof(HasMods));
            Filter();
        }

        /// <summary>All mods, game settings, then a pick per group the add-ons sit in (engines, addons...)</summary>
        public static IReadOnlyList<ModKind> KindsOf(IEnumerable<InstalledMod> mods)
        {
            var list = new List<ModKind> { new(ModKind.All, GameText.Say("mods.filter.all")), new(ModKind.Setting, GameText.Say("mods.filter.settings")) };
            foreach (var group in mods.Where(m => !m.IsSetting).Select(m => m.Group).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
            {
                var label = group.ToLowerInvariant() switch
                {
                    "" => GameText.Say("mods.filter.top"),
                    "engines" => GameText.Say("mods.filter.engines"),
                    "addons" => GameText.Say("mods.filter.addons"),
                    _ => GameText.Say("mods.filter.group", ("group", group))
                };
                list.Add(new ModKind(ModKind.OfGroup(group), label));
            }

            return list;
        }

        /// <summary>True when every word of <paramref name="search"/> is in the mod's name, id, author or description</summary>
        public static bool Matches(ModRow row, string search)
        {
            var words = search.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (words.Length == 0) return true;
            var text = string.Join("\n", row.Name, row.Mod.Id, row.Mod.Info?.Author, row.Description, row.Mod.Group);
            return words.All(w => text.Contains(w, StringComparison.CurrentCultureIgnoreCase));
        }

        private void Filter()
        {
            Mods.Clear();
            foreach (var row in _all.Where(r => (_kind?.Takes(r.Mod) ?? true) && Matches(r, _search))) Mods.Add(row);
            OnPropertyChanged(nameof(NoMatch));
        }

        private void OnSwitch(ModRow? row)
        {
            if (row == null) return;
            try
            {
                var folder = global::StreetCorsa.Mods.ModManager.Switch(row.Mod, !row.On);
                _logger.Information("Mod {Id} switched {State}: {Folder}", row.Mod.Id, row.On ? "off" : "on", folder);
                ErrorMessage = string.Empty;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.Warning(ex, "Mod {Id} could not be switched", row.Mod.Id);
                ErrorMessage = GameText.Say("mods.switch_failed", ("name", row.Name), ("error", ex.Message));
            }

            Refresh();
        }

        private void OnDelete(ModRow? row)
        {
            if (row == null) return;
            _dialogService.ShowDialog(new ConfirmationDialogViewModel(_dialogService,
                GameText.Say("mods.delete.message", ("name", row.Name), ("folder", row.Mod.Folder)),
                GameText.Say("mods.delete.title"),
                yes =>
                {
                    if (!yes) return;
                    try
                    {
                        global::StreetCorsa.Mods.ModManager.Delete(row.Mod);
                        _logger.Information("Mod {Id} deleted: {Folder}", row.Mod.Id, row.Mod.Folder);
                        ErrorMessage = string.Empty;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        _logger.Warning(ex, "Mod {Id} could not be deleted", row.Mod.Id);
                        _dialogService.ShowDialog(new InformationDialogViewModel(_dialogService,
                            GameText.Say("mods.delete_failed", ("name", row.Name), ("error", ex.Message)), GameText.Say("mods.delete.title")));
                    }

                    Refresh();
                }));
        }

        private void OnOpenContentFolder() => Services.Content.ContentFolder.Open(_root, _logger);
    }
}
