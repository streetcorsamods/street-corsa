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
    /// on the engine stand in one go, a working day's job (the rules' <c>time.kitAssembly</c>), or straight into the car's
    /// empty engine bay (that and the engine's way in); Unpack leaves its parts loose on the shelf.
    /// </summary>
    public partial class PartsWorkbenchViewModel
    {
        private static int KitAssemblyMinutes => Rules.RuleBook.Current.Time.KitAssembly;

        public BulkObservableCollection<KitCrateViewModel> Crates { get; } = [];

        public bool HasCrates => Crates.Count > 0;

        private RelayCommand<KitCrateViewModel>? _assembleCrateCommand;
        private RelayCommand<KitCrateViewModel>? _assembleCrateInCarCommand;
        private RelayCommand<KitCrateViewModel>? _unpackCrateCommand;

        /// <summary>The crate's parts put together on the stand: the stand free, a working day</summary>
        public RelayCommand<KitCrateViewModel> AssembleCrateCommand =>
            _assembleCrateCommand ??= new RelayCommand<KitCrateViewModel>(AssembleCrate, crate => crate?.CanAssemble == true && !_isWorking);

        /// <summary>The crate's parts put together into the car's empty engine bay: the car on the floor, a working day</summary>
        public RelayCommand<KitCrateViewModel> AssembleCrateInCarCommand =>
            _assembleCrateInCarCommand ??= new RelayCommand<KitCrateViewModel>(AssembleCrateInCar, crate => crate?.CanAssembleInCar == true && !_isWorking);

        /// <summary>The crate opened: its parts loose on the shelf</summary>
        public RelayCommand<KitCrateViewModel> UnpackCrateCommand =>
            _unpackCrateCommand ??= new RelayCommand<KitCrateViewModel>(UnpackCrate, crate => crate != null && !_isWorking);

        /// <summary>The crates on the shelf, whole; the ids of their parts, which the shelf leaves out of its loose parts</summary>
        private HashSet<Guid> FillCrates(PartsCatalog? catalog)
        {
            var player = _gameState.Player;
            var crated = KitCrates.Crated(player);
            var stand = !HasStand ? KitBay.None : player.EngineStand == null ? KitBay.Free : KitBay.Taken;
            var bay = _car == null ? KitBay.None : _car.Engine != null ? KitBay.Taken : _car.IsLifted ? KitBay.Lifted : KitBay.Free;
            Crates.ReplaceAll(catalog == null
                ? []
                : player.Crates.Select(c => new KitCrateViewModel(c, KitCrates.PartsOf(player, c), catalog, stand, bay)));
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

        private void AssembleCrateInCar(KitCrateViewModel? row)
        {
            if (row is not { CanAssembleInCar: true } || _isWorking || Catalog is not { } catalog
                || _car is not { Engine: null, IsLifted: false } car) return;

            var player = _gameState.Player;
            var crated = KitCrates.PartsOf(player, row.Crate);
            if (KitCrates.Assemble(catalog, player, row.Crate) is not { } engine)
            {
                KitCrates.Unpack(player, row.Crate);
                _logger.Warning("The {Kit} kit makes no engine: unpacked onto the shelf", row.Crate.Name);
                Finish(1, carChanged: false, refresh: true);
                return;
            }

            // Put together whole, nothing off for a stand: in it goes as the stand's engine would
            Workbench.Mount(car.Parts, new MountPlace(null, PartInstance.CarEngineSlot, 0), engine);

            // What goes on the car rather than the engine (the battery) goes where it fits and the rules let it
            var specs = _parts.Specs(car.DefinitionId);
            var liveAxle = specs?.HasLiveAxle ?? true;
            var factory = _parts.FactoryRunningGear(car) is var (front, rear) ? front.All.Concat(rear.All).ToList() : null;
            var onCar = 0;
            foreach (var part in crated.Where(player.Parts.Contains).ToList())
            {
                var place = Workbench.FindPlaces(catalog, car.Parts, part, liveAxle, specs?.DrivesRear ?? true)
                    .FirstOrDefault(p => CarWork.WhyNotOn(catalog, car.Parts, part, p, liveAxle, factory, car.IsLifted) == null);
                if (place == null) continue;

                Workbench.Mount(car.Parts, place, part);
                player.Parts.Remove(part);
                onCar++;
            }

            _logger.Information("Put the {Kit} kit together into {Car} ({Count} part(s) on the engine, {OnCar} on the car)",
                row.Crate.Name, car.DefinitionId, engine.SelfAndDescendants().Count(), onCar);

            SelectedShelfItem = null;
            SelectedPart = null;
            IsCarView = true;
            Finish(1, KitAssemblyMinutes + EngineInOutMinutes);
        }

        private void UnpackCrate(KitCrateViewModel? row)
        {
            if (row == null || _isWorking || !KitCrates.Unpack(_gameState.Player, row.Crate)) return;

            _logger.Information("Unpacked the {Kit} kit onto the shelf", row.Crate.Name);
            Finish(row.PartCount, carChanged: false, refresh: false);
            FillShelf();
        }
    }

    /// <summary>Where a kit could be put together: there is none, it is free, another engine has it, the car is up on the lift</summary>
    public enum KitBay
    {
        None,
        Free,
        Taken,
        Lifted
    }

    /// <summary>An engine kit on the shelf, in its crate: what it is, what is in it, what it is worth</summary>
    public class KitCrateViewModel
    {
        public KitCrateViewModel(KitCrate crate, IReadOnlyCollection<PartInstance> parts, PartsCatalog catalog, KitBay stand, KitBay bay)
        {
            Crate = crate;
            PartCount = parts.Count;
            Name = PartOfferViewModel.KitName(crate.New ? crate.Size : "used", crate.Name);
            Detail = crate.New
                ? GameText.Say("workbench.crate.new", ("parts", parts.Count))
                : PartOfferViewModel.UsedKitDetail(parts, crate.Missing ?? []);
            WorthDisplay = Money.Text(PartPricing.Round(KitWork.WorthOf(catalog, parts)));
            var hasBlock = parts.Any(p => catalog.Get(p.DefinitionId) is { } d && PartKinds.IsBlock(d));
            CanAssemble = hasBlock && stand == KitBay.Free;
            CanAssembleInCar = hasBlock && bay == KitBay.Free;
            HasStand = stand != KitBay.None;
            HasCar = bay != KitBay.None;
            AssembleTip = !hasBlock
                ? GameText.Say("workbench.crate.no_block")
                : GameText.Say(stand == KitBay.Free ? "workbench.crate.assemble.tip" : "workbench.crate.stand_taken");
            AssembleInCarTip = !hasBlock
                ? GameText.Say("workbench.crate.no_block")
                : GameText.Say(bay switch
                {
                    KitBay.Free => "workbench.crate.in_car.tip",
                    KitBay.Lifted => "work.lift_hoist",
                    _ => "workbench.crate.bay_taken"
                });
        }

        public KitCrate Crate { get; }
        public int PartCount { get; }
        public string Name { get; }
        public string Detail { get; }
        public string WorthDisplay { get; }

        /// <summary>The stand is free and there is a block to bolt to it</summary>
        public bool CanAssemble { get; }

        /// <summary>The car in the bay has its engine bay empty and stands on the floor, and there is a block to put in it</summary>
        public bool CanAssembleInCar { get; }

        /// <summary>The game has an engine stand: its button is shown</summary>
        public bool HasStand { get; }

        /// <summary>There is a car in the bay: its button is shown</summary>
        public bool HasCar { get; }

        /// <summary>What Assemble does, or why it cannot</summary>
        public string AssembleTip { get; }

        /// <summary>What Assemble in the car does, or why it cannot</summary>
        public string AssembleInCarTip { get; }
    }
}
