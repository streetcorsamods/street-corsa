using System.Collections.ObjectModel;
using System.Diagnostics;
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

        public ObservableCollection<ModRow> Mods { get; } = new();

        public bool HasMods => Mods.Count > 0;

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
                var mods = global::StreetCorsa.Mods.ModManager.List(_root);
                Mods.Clear();
                foreach (var mod in mods) Mods.Add(new ModRow(mod));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.Error(ex, "The mods in {Root} could not be listed", _root);
                ErrorMessage = GameText.Say("mods.list_failed", ("error", ex.Message));
            }

            OnPropertyChanged(nameof(HasMods));
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

        private void OnOpenContentFolder()
        {
            try
            {
                Directory.CreateDirectory(_root);
                Process.Start(new ProcessStartInfo { FileName = _root, UseShellExecute = true });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            {
                _logger.Warning(ex, "The Content folder {Folder} could not be opened", _root);
            }
        }
    }
}
