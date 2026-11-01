using System.IO;
using StreetCorsa.Dialogs;
using StreetCorsa.Dialogs.Confirmation;
using StreetCorsa.Dialogs.Information;
using StreetCorsa.Logging;
using StreetCorsa.Mods;
using StreetCorsa.Services.Text;
using StreetCorsa.ViewModels;

namespace StreetCorsa.Screens.MainMenu
{
    /// <summary>
    /// Install mods: the player picks mod zips on the PC, the game says what is in them and what they replace, and
    /// unpacks them into the Content folder (<see cref="ModImport"/>). From the main screen, outside any game: the
    /// content in play then is the core alone, which no zip may replace.
    /// </summary>
    public sealed class ModInstallFlow : ObservableObject
    {
        private readonly DialogService _dialogService;
        private readonly Action<bool> _installed;
        private readonly Func<IReadOnlyList<string>?> _pick;
        private readonly string _root;
        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger("Mods");
        private bool _isBusy;

        /// <param name="installed">After mods went in; true when a setting was among them (New Game lists it now)</param>
        /// <param name="pick">The zips the player picks, null for none (tests); the file dialog by default</param>
        /// <param name="root">The Content folder; the game's by default</param>
        public ModInstallFlow(DialogService dialogService, Action<bool> installed, Func<IReadOnlyList<string>?>? pick = null, string? root = null)
        {
            _dialogService = dialogService;
            _installed = installed;
            _pick = pick ?? PickZips;
            _root = root ?? ModSet.DefaultRoot;
            Command = new RelayCommand(() => _ = RunAsync(), () => !IsBusy && !_dialogService.IsDialogOpen);
        }

        public RelayCommand Command { get; }

        /// <summary>Mods are being read or unpacked</summary>
        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetProperty(ref _isBusy, value)) RelayCommand.RaiseCanExecuteChanged();
            }
        }

        /// <summary>Picks, asks, installs and tells; never throws</summary>
        public async Task RunAsync()
        {
            if (IsBusy) return;
            var zips = _pick();
            if (zips == null || zips.Count == 0) return;

            IsBusy = true;
            try
            {
                var plan = await Task.Run(() => ModImport.Plan(zips, _root));
                _logger.Information("Mods picked: {Zips}; {Mods} to install, {Refused} refused",
                    string.Join(", ", zips.Select(Path.GetFileName)), plan.Mods.Count, plan.Refused.Count);

                if (plan.Mods.Count == 0)
                {
                    Tell(plan, null);
                    IsBusy = false;
                    return;
                }

                if (!plan.ReplacesAny)
                {
                    await InstallAsync(plan);
                    return;
                }

                var lines = plan.Mods.Where(m => m.Replaces.Count > 0).Select(m => "  " + GameText.Say(
                    m.IsOlder ? "menu.mods.replace.older" : "menu.mods.replace.line",
                    ("name", NameOf(m.Info)), ("installed", Shown(m.ReplacesVersion)), ("version", Shown(m.Info.Version))));
                _dialogService.ShowDialog(new ConfirmationDialogViewModel(_dialogService,
                    GameText.Say("menu.mods.replace", ("mods", string.Join("\n", lines))),
                    GameText.Say("menu.mods.replace.title"),
                    yes =>
                    {
                        if (yes) _ = InstallAsync(plan);
                        else IsBusy = false;
                    }));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "The mods could not be read");
                IsBusy = false;
                Show(GameText.Say("menu.mods.failed", ("error", ex.Message)));
            }
        }

        private async Task InstallAsync(ImportPlan plan)
        {
            try
            {
                IsBusy = true;
                var outcome = await Task.Run(() => ModImport.Install(plan, _root));
                foreach (var mod in outcome.Installed)
                    _logger.Information("Mod {Id} {Version} installed from {Zip} into {Folder}", mod.Info.Id, mod.Info.Version, mod.ZipPath, mod.Target);
                foreach (var (mod, error) in outcome.Failed)
                    _logger.Warning("Mod {Id} from {Zip} could not be installed: {Error}", mod.Info.Id, mod.ZipPath, error);

                Tell(plan, outcome);
                if (outcome.Installed.Count > 0) _installed(outcome.Installed.Any(m => m.Info.IsSetting));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "The mods could not be installed");
                Show(GameText.Say("menu.mods.failed", ("error", ex.Message)));
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>What went in, what needs more, what did not</summary>
        private void Tell(ImportPlan plan, ImportOutcome? outcome)
        {
            var parts = new List<string>();
            var installed = outcome?.Installed ?? [];
            if (installed.Count > 0)
            {
                parts.Add(GameText.Say("menu.mods.installed", ("mods", string.Join("\n", installed.Select(m =>
                    "  " + GameText.Say("menu.mods.line", ("name", NameOf(m.Info)), ("version", m.Info.Version.Trim())).TrimEnd())))));
                foreach (var mod in installed.Where(m => m.Missing.Count > 0))
                    parts.Add(GameText.Say("menu.mods.needs", ("name", NameOf(mod.Info)), ("mods", string.Join(", ", mod.Missing))));
                if (installed.Any(m => m.Info.IsSetting)) parts.Add(GameText.Say("menu.mods.new_game"));
            }
            else
            {
                parts.Add(GameText.Say("menu.mods.none"));
            }

            var not = plan.Refused.Select(Why)
                .Concat((outcome?.Failed ?? []).Select(f => GameText.Say("menu.mods.why.failed", ("mod", NameOf(f.Mod.Info)), ("error", f.Error))))
                .ToList();
            if (not.Count > 0) parts.Add(GameText.Say("menu.mods.not_installed", ("lines", string.Join("\n", not.Select(l => "  " + l)))));

            Show(string.Join("\n\n", parts));
        }

        private static string Why(ImportRefused refused)
        {
            var key = refused.Why switch
            {
                ImportRefusal.NotAZip => "menu.mods.why.not_a_zip",
                ImportRefusal.NoMod => "menu.mods.why.no_mod",
                ImportRefusal.Unreadable => "menu.mods.why.unreadable",
                ImportRefusal.BadId => "menu.mods.why.bad_id",
                ImportRefusal.Core => "menu.mods.why.core",
                ImportRefusal.NeedsNewerGame => "menu.mods.why.needs_newer_game",
                ImportRefusal.UnsafePath => "menu.mods.why.unsafe_path",
                ImportRefusal.Twice => "menu.mods.why.twice",
                _ => "menu.mods.why.no_room"
            };
            return GameText.Say(key,
                ("zip", Path.GetFileName(refused.ZipPath)), ("mod", refused.Mod ?? string.Empty), ("detail", refused.Detail));
        }

        private void Show(string text) =>
            _dialogService.ShowDialog(new InformationDialogViewModel(_dialogService, text, GameText.Say("menu.mods.title")));

        private static string NameOf(ModInfo info) => string.IsNullOrWhiteSpace(info.Name) ? info.Id : info.Name;

        private static string Shown(string version) => string.IsNullOrWhiteSpace(version) ? "?" : version;

        private static IReadOnlyList<string>? PickZips()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = GameText.Say("menu.mods.pick"),
                Filter = GameText.Say("menu.mods.filter") + " (*.zip)|*.zip",
                Multiselect = true,
                CheckFileExists = true
            };
            return dialog.ShowDialog() == true ? dialog.FileNames : null;
        }
    }
}
