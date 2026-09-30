using System.Collections.ObjectModel;
using StreetCorsa.Logging;
using StreetCorsa.Models.GameState;
using StreetCorsa.Navigation;
using StreetCorsa.Services.Catalog;
using StreetCorsa.Services.Parts;
using StreetCorsa.Services.Storage;

namespace StreetCorsa.Screens.UsedParts
{
    /// <summary>
    /// A page of parts to buy or sell: the newspaper's parts pages (<see cref="UsedPartsScreenViewModel"/>) and a part
    /// shop's counter (<c>PartShopScreenViewModel</c>). The tabs, what is on each and what the rows cost are the page's
    /// own; the filters by kind, name and fit, the cap on rows and the status line are here. Every tab starts
    /// unfiltered, with the kinds of what is on it.
    /// </summary>
    public abstract class PartsListScreenViewModel : BaseScreenViewModel
    {
        public const string AllGroups = "All parts";

        // A full page of rows, not the catalog's two thousand at once
        private const int MaxRows = 250;

        protected readonly GameState _gameState;
        protected readonly ICarPartsService _partsService;
        protected readonly IGameStateRepository _gameStateRepo;
        protected readonly IContentCatalogRepository _catalogRepo;
        protected readonly IAppLogger _logger;

        /// <summary>Ids of the parts that go somewhere on the car selected in the garage</summary>
        protected HashSet<string> _fitting = new(StringComparer.OrdinalIgnoreCase);

        protected PartsListScreenViewModel(GameState gameState, ICarPartsService partsService, IGameStateRepository gameStateRepo,
            IContentCatalogRepository catalogRepo, string logCategory, bool onlyFitting)
        {
            _gameState = gameState;
            _partsService = partsService;
            _gameStateRepo = gameStateRepo;
            _catalogRepo = catalogRepo;
            _logger = AppLoggerFactory.CreateLogger(logCategory);
            _onlyFitting = onlyFitting;
        }

        public ObservableCollection<PartOfferViewModel> Offers { get; } = [];
        public ObservableCollection<string> Groups { get; } = [];

        /// <summary>The player's own shelf, to sell from</summary>
        public abstract bool IsSellTab { get; }

        #region Filters

        private string _selectedGroup = AllGroups;

        public string SelectedGroup
        {
            get => _selectedGroup;
            set
            {
                if (SetProperty(ref _selectedGroup, value ?? AllGroups)) LoadOffers();
            }
        }

        private string _searchText = string.Empty;

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value ?? string.Empty)) LoadOffers();
            }
        }

        private bool _onlyFitting;

        /// <summary>Show only what goes somewhere on the car that is selected in the garage</summary>
        public bool OnlyFitting
        {
            get => _onlyFitting;
            set
            {
                if (SetProperty(ref _onlyFitting, value)) LoadOffers();
            }
        }

        public bool CanFilterByFit => SelectedCar != null && !IsSellTab;

        public string FitFilterLabel => SelectedCar == null ? "Only what fits my car" : $"Only what fits my {CarName}";

        private string _statusText = string.Empty;

        public string StatusText
        {
            get => _statusText;
            protected set => SetProperty(ref _statusText, value);
        }

        #endregion

        protected Car? SelectedCar =>
            _gameState.Player.Cars.FirstOrDefault(c => c.InstanceId == _gameState.Player.SelectedCarInstanceId);

        private string CarName => (SelectedCar == null ? null : _catalogRepo.GetCar(SelectedCar.DefinitionId))?.Name ?? "car";

        // ----- what the page lists -----

        /// <summary>False when there is nothing to list from (no parts catalog, no shop): the page says so</summary>
        protected abstract bool CanList { get; }

        /// <summary>Every row of the tab showing, unfiltered</summary>
        protected abstract IEnumerable<PartOfferViewModel> TabOffers();

        /// <summary>Whether the player can take the offer up: pay for it, or sell it here</summary>
        protected virtual bool CanTake(PartOfferViewModel offer) => IsSellTab || offer.Price <= _gameState.Player.Money;

        /// <summary>The status line when nothing matches, on a buying tab</summary>
        protected virtual string NothingText => "Nothing like that on offer.";

        /// <summary>Added to the status line: what else the page has to say</summary>
        protected virtual string StatusNote => string.Empty;

        /// <summary>
        /// Set while the page opens (<c>Enter</c>): the catalog is first loaded on a worker then, and a filter or a tab
        /// picked meanwhile must not load it again on the UI thread. <see cref="ShowFirst"/> lists the rows as they then
        /// stand.
        /// </summary>
        protected bool Entering { get; set; }

        /// <summary>A tab was picked: it starts unfiltered, with the kinds of what is on it</summary>
        protected void TabChanged()
        {
            _selectedGroup = AllGroups;
            _searchText = string.Empty;
            OnPropertyChanged(nameof(SelectedGroup));
            OnPropertyChanged(nameof(SearchText));
            OnPropertyChanged(nameof(CanFilterByFit));
            if (Entering) return;
            RefillGroups();
            LoadOffers();
        }

        /// <summary>After the page is opened: the fit filter's label, the kinds and the rows</summary>
        protected void ShowFirst()
        {
            Entering = false;
            OnPropertyChanged(nameof(CanFilterByFit));
            OnPropertyChanged(nameof(FitFilterLabel));
            RefillGroups();
            LoadOffers();
        }

        /// <summary>The kinds of what is on the tab showing, for the filter; a kind picked stays in the list</summary>
        private void RefillGroups()
        {
            try
            {
                var groups = CanList ? TabOffers().Select(o => o.Group).Distinct().OrderBy(g => g).ToList() : [];
                if (_selectedGroup != AllGroups && !groups.Contains(_selectedGroup)) groups.Add(_selectedGroup);

                Groups.Clear();
                Groups.Add(AllGroups);
                foreach (var group in groups) Groups.Add(group);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not list the kinds of part");
            }
        }

        /// <summary>
        /// The rows for the page, kind and search picked. Called from property setters bound to the view, so nothing
        /// may escape: a page that cannot be listed says so and stays empty.
        /// </summary>
        protected void LoadOffers()
        {
            if (Entering) return;

            try
            {
                FillOffers();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not list the parts");
                Offers.Clear();
                StatusText = "The parts could not be listed.";
            }
        }

        private void FillOffers()
        {
            Offers.Clear();
            if (!CanList)
            {
                StatusText = "No parts catalog is installed.";
                return;
            }

            var offers = TabOffers();
            if (_selectedGroup != AllGroups) offers = offers.Where(o => o.Group == _selectedGroup);
            if (_searchText.Length > 0) offers = offers.Where(o => o.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase));
            if (_onlyFitting && CanFilterByFit) offers = offers.Where(o => _fitting.Contains(o.Definition.Id));

            var matching = offers.ToList();
            foreach (var offer in matching.Take(MaxRows))
            {
                offer.Fits = _fitting.Contains(offer.Definition.Id);
                offer.CanAfford = CanTake(offer);
                Offers.Add(offer);
            }

            StatusText = matching.Count switch
            {
                0 when IsSellTab => "Your shelf is empty.",
                0 => NothingText,
                > MaxRows => $"Showing {MaxRows} of {matching.Count}: narrow it down by kind or name.",
                _ => $"{matching.Count} part{(matching.Count == 1 ? "" : "s")}"
            } + StatusNote;
        }

        /// <summary>The rows after a deal</summary>
        protected virtual void Refresh()
        {
            LoadOffers();
        }

        protected void Save(string what)
        {
            try
            {
                if (!string.IsNullOrEmpty(_gameState.SaveName)) _gameStateRepo.Save(_gameState, _gameState.SaveName);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to save game state after {What}", what);
            }
        }
    }
}
