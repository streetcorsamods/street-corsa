using StreetCorsa.Helpers;
using StreetCorsa.Models.GameState;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Services.Text;
using StreetCorsa.ViewModels;

namespace StreetCorsa.Screens.Garage
{
    /// <summary>The jobs the picked part can have done in the garage (<see cref="Rebuilding"/>), and doing one</summary>
    public partial class PartsWorkbenchViewModel
    {

        /// <summary>The jobs for the picked part, on the car or the stand, or on the shelf (<see cref="Rebuilding"/>)</summary>
        public BulkObservableCollection<RebuildOfferViewModel> Rebuilds { get; } = [];

        /// <summary>The part card shows the picked part's jobs</summary>
        public bool HasPartRebuilds => Rebuilds.Count > 0 && _selectedPart != null;

        /// <summary>The shelf shows the picked loose part's jobs</summary>
        public bool HasShelfRebuilds => Rebuilds.Count > 0 && _selectedShelfItem != null;

        /// <summary>
        /// The part the jobs are for, and whether it is off: a shelf part is, and so is the engine on the stand; a part on
        /// the car, or on another part, is not
        /// </summary>
        private (PartInstance Part, bool Loose)? RebuildTarget =>
            _selectedShelfItem is { } item ? (item.Part, true)
            : _selectedPart != null && SavedOf(_selectedPart) is { } saved ? (saved, _isStand && SelectedIsEngine)
            : null;

        private void UpdateRebuilds()
        {
            var offers = new List<RebuildOfferViewModel>();
            try
            {
                if (Catalog is { } catalog && RebuildTarget is var (part, loose))
                {
                    // Never under what the work adds to the part at the most a shop pays: a part bought worn, worked on
                    // and sold on is no money made
                    var money = _gameState.Player.Money;
                    var resale = Services.PartShops.PartShopRules.MostPaidFactor();
                    offers.AddRange(Rebuilding.Offers(catalog, part, loose, _gameState.Rules.PartPriceMultiplier, resale)
                        .Select(offer => new RebuildOfferViewModel(offer, part, money)));
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not tell what work the picked part can have done");
            }

            // The list told once, not once an offer
            Rebuilds.ReplaceAll(offers);
            OnPropertyChanged(nameof(HasPartRebuilds));
            OnPropertyChanged(nameof(HasShelfRebuilds));
        }

        /// <summary>Does the job on the picked part, pays for it and lets the clock run; the part stays picked</summary>
        private void Rebuild(RebuildOfferViewModel? offer)
        {
            if (IsWorking || offer is not { CanDo: true } || RebuildTarget is not var (part, _) || !ReferenceEquals(part, offer.Part)) return;
            if (_gameState.Player.Money < offer.Offer.Cost)
            {
                // The money changed since the jobs were listed: they say so now
                UpdateRebuilds();
                return;
            }

            var onCar = _car != null && !_isStand && _selectedShelfItem == null;
            var picked = _selectedPart;
            var shelfPart = _selectedShelfItem?.Part;
            var wearBefore = Unit.Clamp01(part.Wear, 1);

            // The loose part goes on the bench as it was: a tree of its own, before the job changes it
            var benchPart = shelfPart != null && Catalog is { } shelfCatalog ? PartTrees.ToInstalled(shelfCatalog, shelfPart)?.Root : null;

            // The work first: a job that fails to go on costs nothing. The player's own lands round what it can do
            var outcome = Rebuilding.Do(offer.Offer.Job, part, Random.Shared, Catalog);
            var show = new RebuildShow(offer.Offer.Job, offer.Name, wearBefore, outcome.Wear, offer.Offer.Minutes, _gameState.Date)
            {
                Verdict = outcome.Quality is { } quality ? GameText.Say($"rebuild.quality.{quality}") : null
            };
            _gameState.Player.Money -= offer.Offer.Cost;
            _logger.Information("{Job} on {Part} for ${Cost}: now {Wear:P0}", offer.Offer.Job.Id, part.DefinitionId, offer.Offer.Cost, part.Wear);

            // What the car's sheet and the gas station read off its parts
            if (onCar && Catalog is { } catalog) CarCondition.RefreshFigures(_car!, CarCondition.Groups(catalog));

            // A shelf part is no car's nor the stand's: only the shelf is made anew, and the same part picked again in it.
            // Otherwise the trees are, and the same part is picked again in them
            if (shelfPart != null)
            {
                FillShelf();
                Finish(1, offer.Offer.Minutes, carChanged: false, refresh: false);
                SelectedShelfItem = Shelf.FirstOrDefault(s => ReferenceEquals(s.Part, shelfPart));
            }
            else
            {
                Finish(1, offer.Offer.Minutes, carChanged: onCar);
                SelectedPart = InCurrentTree(picked);
            }

            UpdateRebuilds();

            var piece = benchPart != null ? new Controls.WorkPiece(benchPart, OnBench: true)
                : _selectedPart != null ? new Controls.WorkPiece(_selectedPart, OnBench: false)
                : null;
            ShowWork(show, piece);
        }
    }
}
