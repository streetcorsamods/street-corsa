using System.Collections.ObjectModel;
using StreetCorsa.Dialogs;
using StreetCorsa.Dialogs.Information;
using StreetCorsa.Logging;
using StreetCorsa.Models.GameState;
using StreetCorsa.Navigation;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Services.Catalog;
using StreetCorsa.Services.Parts;
using StreetCorsa.Services.Storage;
using StreetCorsa.Services.Text;
using StreetCorsa.Services.Time;
using StreetCorsa.ViewModels;
using StreetCorsa.Helpers;

namespace StreetCorsa.Screens.UsedParts
{
    /// <summary>What the parts pages show</summary>
    public enum PartsShopTab
    {
        /// <summary>Used parts from the ads</summary>
        Used,

        /// <summary>New parts by mail order</summary>
        New,

        /// <summary>The player's own shelf, to sell from</summary>
        Sell
    }

    /// <summary>
    /// The parts pages of the newspaper: used parts from the ads, new ones by mail order, and selling what is
    /// on the shelf. Whatever is bought goes to the shelf in the garage.
    /// </summary>
    public class UsedPartsScreenViewModel : PartsListScreenViewModel
    {
        private readonly NavigationService _navigationService;
        private readonly DialogService _dialogService;
        private readonly INewspaperPartsService _shopService;
        private readonly IGameTimeService _timeService;

        public RelayCommand BackCommand { get; }
        public RelayCommand<string> ShowTabCommand { get; }
        public RelayCommand<PartOfferViewModel> BuyCommand { get; }

        public UsedPartsScreenViewModel(
            NavigationService navigationService,
            DialogService dialogService,
            Models.GameState.GameState gameState,
            ICarPartsService partsService,
            INewspaperPartsService shopService,
            IGameStateRepository gameStateRepo,
            IContentCatalogRepository catalogRepo,
            IGameTimeService timeService)
            : base(gameState, partsService, gameStateRepo, catalogRepo, "UsedParts", onlyFitting: true)
        {
            _navigationService = navigationService;
            _dialogService = dialogService;
            _shopService = shopService;
            _timeService = timeService;

            BackCommand = new RelayCommand(OnBack);
            ShowTabCommand = new RelayCommand<string>(tab => Tab = Enum.Parse<PartsShopTab>(tab ?? nameof(PartsShopTab.Used)));
            BuyCommand = new RelayCommand<PartOfferViewModel>(OnOfferAction);
        }

        private PartsShopTab _tab = PartsShopTab.Used;

        public PartsShopTab Tab
        {
            get => _tab;
            set
            {
                if (!SetProperty(ref _tab, value)) return;

                OnPropertyChanged(nameof(IsUsedTab));
                OnPropertyChanged(nameof(IsNewTab));
                OnPropertyChanged(nameof(IsSellTab));
                TabChanged();
            }
        }

        public bool IsUsedTab => _tab == PartsShopTab.Used;
        public bool IsNewTab => _tab == PartsShopTab.New;
        public override bool IsSellTab => _tab == PartsShopTab.Sell;

        public bool IsAvailable => _shopService.IsAvailable;

        public override async void Enter()
        {
            base.Enter();
            _logger.Information("Entered used parts screen");

            Entering = true;
            try
            {
                // Catalog, first ads and the fit check take a moment the first time: not on the UI thread.
                // The game state itself is only changed on it.
                var car = SelectedCar;
                if (await Task.Run(() => _shopService.IsAvailable))
                {
                    if (car != null) await _partsService.EnsurePartsAsync(car);
                    if (_gameState.NewspaperAds.Parts.Count == 0) await _shopService.RefreshAdsAsync(_gameState, _gameState.Date);
                    _fitting = await Task.Run(() => _shopService.FindFittingParts(car));
                }

                OnPropertyChanged(nameof(IsAvailable));
                ShowFirst();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not open the parts pages");
                StatusText = GameText.Say("usedparts.open_failed");
            }
            finally
            {
                Entering = false;
            }

            // The first look took a moment: a player who backed out meanwhile never stayed to spend the time
            if (!ReferenceEquals(_navigationService.CurrentScreen, this)) return;

            try
            {
                // Spend time for visiting the used parts shop (30 min)
                var spent = await _timeService.SpendTimeAsync(_gameState, GameAction.VisitUsedParts);

                // Late in the evening that is the next morning, with another paper
                if (spent.NewDayStarted) LoadOffers();

                // The time spent, and the new day it may end in, belong in the save
                Save("the time for the parts pages");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not spend and save the time for the parts pages");
            }
        }

        protected override bool CanList => _shopService.IsAvailable;

        protected override IEnumerable<PartOfferViewModel> TabOffers()
        {
            var catalog = _partsService.Catalog;
            return _tab switch
            {
                PartsShopTab.Used => _gameState.NewspaperAds.Parts
                    .Where(ad => catalog.Get(ad.Part.DefinitionId) != null)
                    .Select(ad => PartOfferViewModel.ForAd(ad, catalog.Get(ad.Part.DefinitionId)!)),
                PartsShopTab.New => _shopService.Assortment.Select(p => PartOfferViewModel.ForNew(p, _shopService.NewPrice(p, _gameState.Rules.PartPriceMultiplier))),
                _ => _gameState.Player.Parts
                    .Where(p => catalog.Get(p.DefinitionId) != null)
                    .Select(p => PartOfferViewModel.ForSale(p, catalog.Get(p.DefinitionId)!, _shopService.TradeInPrice(p)))
            };
        }

        /// <summary>What is ordered and not here yet: the mail order's page says so, so nothing is ordered twice by mistake</summary>
        protected override string StatusNote =>
            IsNewTab && _gameState.Player.PartOrders.Count is > 0 and var onTheWay
                ? GameText.Say(onTheWay == 1 ? "usedparts.on_the_way.one" : "usedparts.on_the_way.other", ("count", onTheWay))
                : string.Empty;

        private void OnOfferAction(PartOfferViewModel? offer)
        {
            if (offer == null) return;

            var done = offer switch
            {
                { Ad: { } ad } => _shopService.BuyUsed(_gameState, ad),
                { Owned: { } owned } => _shopService.Sell(_gameState, owned),
                _ => _shopService.BuyNew(_gameState, offer.Definition) != null
            };

            if (!done)
            {
                var (message, title) = offer switch
                {
                    { Owned: not null } => (GameText.Say("market.part.not_owned"), GameText.Say("market.part.not_owned.title")),
                    { Ad: { } ad } when !_gameState.NewspaperAds.Parts.Contains(ad) =>
                        (GameText.Say("usedparts.ad_gone"), GameText.Say("usedparts.ad_gone.title")),
                    _ => (GameText.Say("market.part.no_money"), GameText.Say("market.part.no_money.title"))
                };
                _dialogService.ShowDialog(new InformationDialogViewModel(_dialogService, message, title));
            }
            else
            {
                Audio.Sfx.Play(Audio.Sfx.Purchase);
                Save("a parts deal");
            }

            // Whatever went wrong, the rows are out of date
            Refresh();
        }

        private void OnBack()
        {
            _logger.Information("Navigating back to newspaper");
            _navigationService.NavigateToNewspaper(_gameState, skipAnimation: true);
        }

        public override void Exit()
        {
            base.Exit();
            _logger.Information("Exited used parts screen");
        }
    }

    /// <summary>
    /// One row of the parts pages and the part shops: a used part from an ad or a junkyard, a new part by mail or off a
    /// shop's shelf, or one of the player's own
    /// </summary>
    public class PartOfferViewModel
    {
        private PartOfferViewModel(PartDefinition definition, decimal price, string action)
        {
            Definition = definition;
            Price = price;
            ActionLabel = action;
            Name = definition.DisplayName ?? definition.Name;
            Group = PartKinds.GroupName(PartKinds.GroupOf(definition));
        }

        public PartDefinition Definition { get; }
        public PartAd? Ad { get; private init; }
        public PartInstance? Owned { get; private init; }

        /// <summary>A new part on a shop's shelf</summary>
        public ShopShelf? Shelf { get; private init; }

        /// <summary>What it cost before today's sale and the regulars' discount; null when nothing is off</summary>
        public decimal? WasPrice { get; private init; }

        public string WasPriceDisplay => WasPrice is { } was ? Money.Text(was) : string.Empty;
        public bool HasWasPrice => WasPrice != null;

        /// <summary>A junkyard's part the owner may still come down on</summary>
        public bool CanHaggle { get; private init; }

        public string Name { get; }
        public string Group { get; }
        public decimal Price { get; }
        public string PriceDisplay => Money.Text(Price);
        public string ActionLabel { get; }

        /// <summary>Condition, seller, what comes with it</summary>
        public string Detail { get; private init; } = GameText.Say("usedparts.detail.new");

        public bool Fits { get; set; }
        public bool CanAfford { get; set; }

        public static PartOfferViewModel ForNew(PartDefinition definition, decimal price) =>
            new(definition, price, GameText.Say("usedparts.action.order"))
            {
                Detail = GameText.Say("usedparts.detail.mail", ("days", NewspaperPartsService.MailOrderDays))
            };

        public static PartOfferViewModel ForAd(PartAd ad, PartDefinition definition) =>
            new(definition, ad.AskingPrice, GameText.Say("usedparts.action.buy"))
            {
                Ad = ad,
                Detail = GameText.Say("usedparts.detail.ad", ("part", PartTrees.Describe(ad.Part)), ("seller", ad.SellerName))
            };

        /// <summary>A new part on a shop's shelf, at the shop's price today</summary>
        public static PartOfferViewModel ForShelf(ShopShelf shelf, PartDefinition definition, decimal price, decimal? was) =>
            new(definition, price, GameText.Say("usedparts.action.buy"))
            {
                Shelf = shelf,
                WasPrice = was > price ? was : null,
                Detail = GameText.Say(shelf.Count == 1 ? "usedparts.detail.shelf.one" : "usedparts.detail.shelf.other", ("count", shelf.Count))
            };

        /// <summary>A used part in a junkyard's yard, at the yard's price today</summary>
        public static PartOfferViewModel ForYard(PartAd item, PartDefinition definition, decimal price, decimal? was) =>
            new(definition, price, GameText.Say("usedparts.action.buy"))
            {
                Ad = item,
                WasPrice = was > price ? was : null,
                CanHaggle = !item.Haggled,
                Detail = item.Haggled
                    ? GameText.Say("usedparts.detail.haggled", ("part", PartTrees.Describe(item.Part)))
                    : PartTrees.Describe(item.Part)
            };

        public static PartOfferViewModel ForSale(PartInstance part, PartDefinition definition, decimal price) =>
            new(definition, price, GameText.Say("usedparts.action.sell"))
            {
                Owned = part,
                Detail = PartTrees.Describe(part)
            };
    }
}
