using StreetCorsa.Helpers;
using StreetCorsa.Models.GameState;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Screens.UsedParts;
using StreetCorsa.Services.Parts;
using StreetCorsa.Services.Text;
using StreetCorsa.ViewModels;

namespace StreetCorsa.Screens.Garage
{
    /// <summary>
    /// The engine kits on the shelf (roadmap step 40), each in its crate above the loose parts: Assemble puts it together
    /// on the engine stand in one go, a working day's job (the rules' <c>time.kitAssembly</c>); Unpack leaves its parts
    /// loose on the shelf.
    /// </summary>
    public partial class PartsWorkbenchViewModel
    {
        private static int KitAssemblyMinutes => Rules.RuleBook.Current.Time.KitAssembly;

        public BulkObservableCollection<KitCrateViewModel> Crates { get; } = [];

        public bool HasCrates => Crates.Count > 0;

        private RelayCommand<KitCrateViewModel>? _assembleCrateCommand;
        private RelayCommand<KitCrateViewModel>? _unpackCrateCommand;

        /// <summary>The crate's parts put together on the stand: the stand free, a working day</summary>
        public RelayCommand<KitCrateViewModel> AssembleCrateCommand =>
            _assembleCrateCommand ??= new RelayCommand<KitCrateViewModel>(AssembleCrate, crate => crate?.CanAssemble == true && !_isWorking);

        /// <summary>The crate opened: its parts loose on the shelf</summary>
        public RelayCommand<KitCrateViewModel> UnpackCrateCommand =>
            _unpackCrateCommand ??= new RelayCommand<KitCrateViewModel>(UnpackCrate, crate => crate != null && !_isWorking);

        /// <summary>The crates on the shelf, whole; the ids of their parts, which the shelf leaves out of its loose parts</summary>
        private HashSet<Guid> FillCrates(PartsCatalog? catalog)
        {
            var player = _gameState.Player;
            var crated = KitCrates.Crated(player);
            var standFree = HasStand && player.EngineStand == null;
            Crates.ReplaceAll(catalog == null
                ? []
                : player.Crates.Select(c => new KitCrateViewModel(c, KitCrates.PartsOf(player, c), catalog, standFree)));
            OnPropertyChanged(nameof(HasCrates));
            return catalog == null ? [] : crated;
        }

        private void AssembleCrate(KitCrateViewModel? row)
        {
            if (row is not { CanAssemble: true } || _isWorking || Catalog is not { } catalog || _gameState.Player.EngineStand != null) return;

            var player = _gameState.Player;
            if (KitCrates.Assemble(catalog, player, row.Crate) is not { } engine)
            {
                // No engine to be made of it (its block gone with a mod): its parts are loose for the player to see to
                KitCrates.Unpack(player, row.Crate);
                _logger.Warning("The {Kit} kit makes no engine: unpacked onto the shelf", row.Crate.Name);
                Finish(1, carChanged: false, refresh: true);
                return;
            }

            // Bolted to the stand by its bellhousing face: what is in the way comes off to the shelf
            var off = EngineWork.StripForStand(catalog, engine);
            player.Parts.AddRange(off);
            player.EngineStand = engine;
            _logger.Information("Put the {Kit} kit together on the engine stand ({Count} part(s) on it, {Off} off to bolt it on)",
                row.Crate.Name, engine.SelfAndDescendants().Count(), off.Count);

            SelectedShelfItem = null;
            IsStandView = true;
            Finish(1, KitAssemblyMinutes, carChanged: false);
        }

        private void UnpackCrate(KitCrateViewModel? row)
        {
            if (row == null || _isWorking || !KitCrates.Unpack(_gameState.Player, row.Crate)) return;

            _logger.Information("Unpacked the {Kit} kit onto the shelf", row.Crate.Name);
            Finish(row.PartCount, carChanged: false, refresh: false);
            FillShelf();
        }
    }

    /// <summary>An engine kit on the shelf, in its crate: what it is, what is in it, what it is worth</summary>
    public class KitCrateViewModel
    {
        public KitCrateViewModel(KitCrate crate, IReadOnlyCollection<PartInstance> parts, PartsCatalog catalog, bool standFree)
        {
            Crate = crate;
            PartCount = parts.Count;
            Name = PartOfferViewModel.KitName(crate.New ? crate.Size : "used", crate.Name);
            Detail = crate.New
                ? GameText.Say("workbench.crate.new", ("parts", parts.Count))
                : PartOfferViewModel.UsedKitDetail(parts, crate.Missing ?? []);
            WorthDisplay = Money.Text(PartPricing.Round(KitWork.WorthOf(catalog, parts)));
            CanAssemble = standFree && parts.Any(p => catalog.Get(p.DefinitionId) is { } d && PartKinds.IsBlock(d));
            AssembleTip = CanAssemble
                ? GameText.Say("workbench.crate.assemble.tip")
                : GameText.Say(standFree ? "workbench.crate.no_block" : "workbench.crate.stand_taken");
        }

        public KitCrate Crate { get; }
        public int PartCount { get; }
        public string Name { get; }
        public string Detail { get; }
        public string WorthDisplay { get; }

        /// <summary>The stand is free and there is a block to bolt to it</summary>
        public bool CanAssemble { get; }

        /// <summary>What Assemble does, or why it cannot</summary>
        public string AssembleTip { get; }
    }
}
