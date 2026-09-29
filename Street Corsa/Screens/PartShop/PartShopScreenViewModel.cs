using StreetCorsa.Dialogs;
using StreetCorsa.Dialogs.Information;
using StreetCorsa.Models.GameState;
using StreetCorsa.Navigation;
using StreetCorsa.Screens.UsedParts;
using StreetCorsa.Services.Catalog;
using StreetCorsa.Services.Dealers;
using StreetCorsa.Services.Parts;
using StreetCorsa.Services.PartShops;
using StreetCorsa.Services.Storage;
using StreetCorsa.Services.Text;
using StreetCorsa.ViewModels;

namespace StreetCorsa.Screens.PartShop
{
    /// <summary>
    /// A part shop the player drove out to from the map: what it has on its shelves (a junkyard: in its yard) at its
    /// prices today, and the player's shelf to sell from, at what this shop pays. A junkyard can be haggled with. The
    /// drive was paid for on the map; looking round costs nothing more.
    /// </summary>
    public class PartShopScreenViewModel : PartsListScreenViewModel
    {
        private readonly NavigationService _navigationService;
        private readonly DialogService _dialogService;
        private readonly string _shopId;
        private readonly IPartShopService _shops;
        private readonly INewspaperPartsService _paper;

        public RelayCommand BackCommand { get; }
        public RelayCommand<string> ShowTabCommand { get; }
        public RelayCommand<PartOfferViewModel> BuyCommand { get; }
        public RelayCommand<PartOfferViewModel> HaggleCommand { get; }

        public PartShopScreenViewModel(
            NavigationService navigationService,
            DialogService dialogService,
            GameState gameState,
            string shopId,
            IPartShopService shops,
            ICarPartsService partsService,
            INewspaperPartsService paper,
            IGameStateRepository gameStateRepo,
            IContentCatalogRepository catalogRepo)
            // Off at first: a shop is looked round
            : base(gameState, partsService, gameStateRepo, catalogRepo, "PartShop", onlyFitting: false)
        {
            _navigationService = navigationService;
            _dialogService = dialogService;
            _shopId = shopId;
            _shops = shops;
            _paper = paper;

            // A shop the map offered and the files have since lost cannot be shown: the navigation says so and stays put
            Definition = shops.Catalog.Get(shopId) ?? throw new InvalidOperationException($"No part shop \"{shopId}\"");

            BackCommand = new RelayCommand(OnBack);
            ShowTabCommand = new RelayCommand<string>(tab => SellTab = tab == "Sell");
            BuyCommand = new RelayCommand<PartOfferViewModel>(OnOfferAction);
            HaggleCommand = new RelayCommand<PartOfferViewModel>(OnHaggle);
        }

        public PartShopDefinition Definition { get; }

        private PartShopLocation? Shop => PartShopRoster.Find(_gameState, _shopId);

        public bool IsJunkyard => Definition.KindOf == PartShopKind.Junkyard;

        public string ShopName => Definition.Name.ToUpperInvariant();
        public string Region => Definition.Region;
        public string Blurb => Definition.Blurb;
        public string KindLabel => GameText.Installed.Get($"partshop.kind.{Definition.KindOf.ToString().ToLowerInvariant()}");

        public string BuyTabLabel => IsJunkyard ? "In the yard" : "On the shelves";
        public string SellTabLabel => $"Sell to {Definition.Name}";

        /// <summary>The closing-down sale, and what the player gets off as a regular: the line under the shop's name</summary>
        public string Notice
        {
            get
            {
                if (Shop is not { } shop) return string.Empty;

                var lines = new List<string>();
                if (shop.Status == DealerStatus.ClosingDown && shop.ClosesOn is { } last)
                {
                    var values = new TextValues { ["discount"] = (int)Math.Round(DealerRules.DiscountOn(shop, _gameState.Date) * 100), ["day"] = last };
                    lines.Add(GameText.Installed.Get(DealerRules.DaysLeft(shop, _gameState.Date) == 0 ? "partshop.lot.sale_last_day" : "partshop.lot.sale", values));
                }

                var regular = PartShopRules.RegularDiscountFor(shop.PlayerSpent);
                if (regular > 0)
                {
                    var key = regular >= PartShopRules.GoodCustomerDiscount ? "partshop.good_customer" : "partshop.regular";
                    lines.Add(GameText.Installed.Get(key, new TextValues { ["discount"] = (int)Math.Round(regular * 100) }));
                }

                return string.Join("   ·   ", lines);
            }
        }

        public bool HasNotice => Notice.Length > 0;

        private bool _isSellTab;

        /// <summary>The tab showing: the player's shelf when true, the shop's when false</summary>
        private bool SellTab
        {
            set
            {
                if (_isSellTab == value) return;

                _isSellTab = value;
                OnPropertyChanged(nameof(IsSellTab));
                OnPropertyChanged(nameof(IsBuyTab));
                TabChanged();
            }
        }

        public override bool IsSellTab => _isSellTab;

        public bool IsBuyTab => !_isSellTab;

        public override async void Enter()
        {
            base.Enter();
            _logger.Information("Walked into {Shop}", Definition.Name);

            Entering = true;
            try
            {
                var car = SelectedCar;
                if (await Task.Run(() => _shops.IsAvailable))
                {
                    // A shop the mornings have not got round to yet (a game just begun) is stocked as the player walks in
                    PartShopRoster.Sync(_gameState, _shops.Catalog);
                    await _shops.RestockAsync(_gameState, _gameState.Date, Random.Shared, firstOnly: true);

                    if (car != null) await _partsService.EnsurePartsAsync(car);
                    _fitting = await Task.Run(() => _paper.FindFittingParts(car));
                }

                OnPropertyChanged(nameof(Notice));
                OnPropertyChanged(nameof(HasNotice));
                ShowFirst();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not open {Shop}", Definition.Name);
                StatusText = "The shop could not be looked round.";
            }
            finally
            {
                Entering = false;
            }
        }

        protected override bool CanList => _shops.IsAvailable && Shop != null;

        protected override IEnumerable<PartOfferViewModel> TabOffers()
        {
            if (Shop is not { } shop) return [];

            var catalog = _partsService.Catalog;
            if (IsSellTab)
            {
                return _gameState.Player.Parts
                    .Where(p => catalog.Get(p.DefinitionId) != null)
                    .Select(p => PartOfferViewModel.ForSale(p, catalog.Get(p.DefinitionId)!, _shops.BuyingPrice(shop, p)));
            }

            if (IsJunkyard)
            {
                return shop.Yard
                    .Where(i => catalog.Get(i.Part.DefinitionId) != null)
                    .OrderBy(i => catalog.Get(i.Part.DefinitionId)!.DisplayName ?? catalog.Get(i.Part.DefinitionId)!.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(i => PartOfferViewModel.ForYard(i, catalog.Get(i.Part.DefinitionId)!, _shops.PriceOfUsed(_gameState, shop, i), i.AskingPrice));
            }

            return shop.Shelves
                .Where(s => s.Count > 0 && catalog.Get(s.DefinitionId) != null)
                .Select(s => (Shelf: s, Part: catalog.Get(s.DefinitionId)!))
                .OrderBy(s => s.Part.DisplayName ?? s.Part.Name, StringComparer.OrdinalIgnoreCase)
                .Select(s => PartOfferViewModel.ForShelf(s.Shelf, s.Part, _shops.PriceOfNew(_gameState, shop, s.Part),
                    _shops.MarkedPriceOfNew(_gameState, shop, s.Part)));
        }

        /// <summary>A shop that has shut since the page was drawn buys nothing</summary>
        protected override bool CanTake(PartOfferViewModel offer) =>
            IsSellTab ? Shop is { IsTrading: true } : base.CanTake(offer);

        protected override string NothingText =>
            Shop is { Status: DealerStatus.ClosingDown } ? "Cleaned out: nothing like that left." : "Nothing like that in today.";

        private void OnOfferAction(PartOfferViewModel? offer)
        {
            if (offer == null || Shop is not { } shop) return;

            var done = offer switch
            {
                { Owned: { } owned } => _shops.Sell(_gameState, shop, owned),
                { Ad: { } item } => _shops.BuyUsed(_gameState, shop, item),
                _ => _shops.BuyNew(_gameState, shop, offer.Definition)
            };

            if (!done)
            {
                var (message, title) = offer switch
                {
                    { Owned: not null } => ("That part is no longer on your shelf.", "Not Sold"),
                    { Ad: { } item } when !shop.Yard.Contains(item) => ("Somebody was quicker: that part has gone.", "Already Sold"),
                    { Shelf.Count: <= 0 } => ("That was the last one, and it has gone.", "Sold Out"),
                    _ => ("You don't have enough money for this part.", "Insufficient Funds")
                };
                _dialogService.ShowDialog(new InformationDialogViewModel(_dialogService, message, title));
            }
            else
            {
                Save("a parts deal");
            }

            Refresh();
        }

        private void OnHaggle(PartOfferViewModel? offer)
        {
            if (offer?.Ad is not { } item || Shop is not { } shop) return;

            var before = _shops.PriceOfUsed(_gameState, shop, item);
            var cameDown = _shops.Haggle(shop, item, Random.Shared);
            var after = _shops.PriceOfUsed(_gameState, shop, item);

            // Down to what the yard pays itself: the owner said yes, and nothing came off
            cameDown &= after < before;
            var values = new TextValues { ["part"] = offer.Name, ["price"] = after, ["was"] = before, ["shop"] = Definition.Name };
            var message = GameText.Installed.Pick(cameDown ? "partshop.haggle.yes" : "partshop.haggle.no", Random.Shared, values);
            _dialogService.ShowDialog(new InformationDialogViewModel(_dialogService, message,
                GameText.Installed.Get(cameDown ? "partshop.haggle.yes.title" : "partshop.haggle.no.title")));

            Save("haggling");
            Refresh();
        }

        protected override void Refresh()
        {
            OnPropertyChanged(nameof(Notice));
            OnPropertyChanged(nameof(HasNotice));
            base.Refresh();
        }

        private void OnBack() => _navigationService.NavigateToPartShopMap(_gameState);
    }
}
