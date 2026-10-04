using StreetCorsa.Controls;
using StreetCorsa.Dialogs;
using StreetCorsa.Dialogs.Information;
using StreetCorsa.Models.GameState;
using StreetCorsa.Navigation;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Screens.Shared;
using StreetCorsa.Screens.UsedParts;
using StreetCorsa.Services.Catalog;
using StreetCorsa.Services.Dealers;
using StreetCorsa.Services.Parts;
using StreetCorsa.Services.PartShops;
using StreetCorsa.Services.Storage;
using StreetCorsa.Services.Street;
using StreetCorsa.Services.Talk;
using StreetCorsa.Services.Text;
using StreetCorsa.ViewModels;

namespace StreetCorsa.Screens.PartShop
{
    /// <summary>
    /// A part shop the player drove out to from the map, seen from inside the door: its own stock on its shelves, in its
    /// rack and on its stand. The counter (one window over the shop) has what it has (a junkyard: in its yard) at its
    /// prices today, the player's shelf to sell from at what this shop pays, and, while the player's AI is on, whoever is
    /// behind the counter: asked for something, they find it in the stock (<see cref="IShopCounter"/>), the list shows
    /// what matches with their picks first, and they talk the player through those. A junkyard can be haggled with. The
    /// drive was paid for on the map; looking round costs nothing more.
    /// </summary>
    public class PartShopScreenViewModel : PartsListScreenViewModel, IShopCounter
    {
        /// <summary>The sound of the place</summary>
        public override string? Ambience => IsJunkyard ? "ambience.junkyard" : "ambience.shop";

        /// <summary>Picks the clerk is told about at most</summary>
        public const int MostPicks = 5;

        /// <summary>Parts tried on the dyno for one ask at most: the cheapest that fit, so an ask is answered in a moment</summary>
        public const int MostDynoRuns = 40;

        private readonly NavigationService _navigationService;
        private readonly DialogService _dialogService;
        private readonly string _shopId;
        private readonly IPartShopService _shops;
        private readonly INewspaperPartsService _paper;
        private readonly ShopScene? _scene;

        // What each part does for the player's engine, worked out once a visit (the engine does not change in a shop)
        private readonly Dictionary<string, (double Before, double After)?> _power = new(StringComparer.OrdinalIgnoreCase);
        private double? _carPower;

        public RelayCommand BackCommand { get; }
        public RelayCommand<string> ShowTabCommand { get; }
        public RelayCommand<PartOfferViewModel> BuyCommand { get; }
        public RelayCommand<PartOfferViewModel> HaggleCommand { get; }
        public RelayCommand ToggleCounterCommand { get; }

        public PartShopScreenViewModel(
            NavigationService navigationService,
            DialogService dialogService,
            GameState gameState,
            string shopId,
            IPartShopService shops,
            ICarPartsService partsService,
            INewspaperPartsService paper,
            IGameStateRepository gameStateRepo,
            IContentCatalogRepository catalogRepo,
            ShopScene? scene = null)
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
            _scene = scene ?? ShopScenes.Current;

            BackCommand = new RelayCommand(OnBack);
            ShowTabCommand = new RelayCommand<string>(tab => SellTab = tab == "Sell");
            BuyCommand = new RelayCommand<PartOfferViewModel>(OnOfferAction);
            HaggleCommand = new RelayCommand<PartOfferViewModel>(OnHaggle);
            ToggleCounterCommand = new RelayCommand(() => IsCounterOpen = !IsCounterOpen);

            if (_scene != null)
            {
                var look = _scene.LookFor(Definition.Look, Definition.KindOf.ToString().ToLowerInvariant());
                var dressing = _scene.Dress(look, StreetLights.At(gameState.Date));
                Dressing = new LotDressing(dressing.Lighting, dressing.Sun, dressing.Pictures, dressing.Glow);
            }
        }

        public PartShopDefinition Definition { get; }

        private PartShopLocation? Shop => PartShopRoster.Find(_gameState, _shopId);

        public bool IsJunkyard => Definition.KindOf == PartShopKind.Junkyard;

        public string ShopName => Definition.Name.ToUpperInvariant();
        public string Region => Definition.Region;
        public string Blurb => Definition.Blurb;
        public string KindLabel => GameText.Installed.Get($"partshop.kind.{Definition.KindOf.ToString().ToLowerInvariant()}");

        public string BuyTabLabel => GameText.Say(IsJunkyard ? "partshop.tab.yard" : "partshop.tab.shelves");
        public string SellTabLabel => GameText.Say("partshop.tab.sell", ("shop", Definition.Name));

        #region The room

        /// <summary>The shop's room, when a scene is installed; none, and the counter is the screen</summary>
        public string? SceneKn5 => _scene?.Kn5;

        public bool HasScene => _scene != null;

        public IReadOnlyList<ShopPlace>? Places => _scene?.Places;

        public ShopCamera? View => _scene?.Camera;

        public ShopRoom? Room => _scene?.Room;

        /// <summary>The room dressed for this kind of shop at this hour</summary>
        public LotDressing? Dressing { get; }

        public PartsCatalog? Catalog => _partsService.IsAvailable ? _partsService.Catalog : null;

        private IReadOnlyList<ShopStockPart>? _stockShown;

        /// <summary>What stands in the room: a part of each line on the shelves, every part in the yard</summary>
        public IReadOnlyList<ShopStockPart>? StockShown
        {
            get => _stockShown;
            private set => SetProperty(ref _stockShown, value);
        }

        private void RefreshStockShown()
        {
            // Nothing to show is an empty shop, not one still to be stocked: the room comes up all the same
            if (Shop is not { } shop || Catalog is not { } catalog)
            {
                StockShown = [];
                return;
            }

            StockShown = IsJunkyard
                ? shop.Yard.Where(i => catalog.Get(i.Part.DefinitionId) != null).Select(i => new ShopStockPart(catalog.Get(i.Part.DefinitionId)!, i.Part)).ToList()
                : shop.Shelves.Where(s => s.Count > 0 && catalog.Get(s.DefinitionId) != null).Select(s => new ShopStockPart(catalog.Get(s.DefinitionId)!)).ToList();
        }

        private bool _isCounterOpen;

        /// <summary>The counter is up over the room: the list and the clerk; without a scene it always is</summary>
        public bool IsCounterOpen
        {
            get => _isCounterOpen || !HasScene;
            set
            {
                if (!SetProperty(ref _isCounterOpen, value)) return;
                OnPropertyChanged(nameof(ShowsCounterButton));
                if (Talk != null) Talk.IsOpen = value;
            }
        }

        /// <summary>The way up to the counter, while it is not open</summary>
        public bool ShowsCounterButton => HasScene && !IsCounterOpen;

        #endregion

        #region The clerk

        private TalkViewModel? _talk;

        /// <summary>Whoever is behind the counter, talked to while the player's AI is on</summary>
        public TalkViewModel? Talk
        {
            get => _talk;
            private set
            {
                if (SetProperty(ref _talk, value)) OnPropertyChanged(nameof(HasTalk));
            }
        }

        public bool HasTalk => Talk != null;

        /// <summary>They look up as the player comes in: greeted before the counter is opened, the tab calls for the player</summary>
        private void StartTalk()
        {
            if (Talk != null || Shop == null || !TalkDesk.Shared.IsAvailable) return;

            var talk = new TalkViewModel(TalkDesk.Shared.ForShop(_gameState, Definition.Id, Definition.Clerk, this), Definition.Clerk?.PortraitFile)
            {
                IsOpen = IsCounterOpen
            };
            Talk = talk;
            _ = OpenTalkAsync(talk);
        }

        /// <summary>A clerk who never says a word (the AI is on in Settings but not answering): the counter goes on without them</summary>
        private async Task OpenTalkAsync(TalkViewModel talk)
        {
            await talk.OpenAsync();
            if (Talk != talk || talk.HasAnswered) return;

            _logger.Information("The clerk's AI did not answer: the counter goes on without the talk");
            talk.End();
            Talk = null;
        }

        IReadOnlyList<string> IShopCounter.Kinds => BuyOffers().Select(o => o.Group).Distinct().Order(StringComparer.OrdinalIgnoreCase).ToList();

        IEnumerable<string> IShopCounter.Facts()
        {
            yield return GameText.Say("chat.fact.shop", ("shop", Definition.Name), ("kind", KindLabel.ToLowerInvariant()), ("region", Region),
                ("blurb", Blurb));
            if (Shop is not { } shop) yield break;

            if (shop.Status == DealerStatus.ClosingDown && shop.ClosesOn is { } last)
                yield return GameText.Say("chat.fact.shop_sale", ("discount", (int)Math.Round(DealerRules.DiscountOn(shop, _gameState.Date) * 100)), ("day", last));

            var regular = PartShopRules.RegularDiscountFor(shop.PlayerSpent);
            if (regular > 0) yield return GameText.Say("chat.fact.shop_regular", ("discount", (int)Math.Round(regular * 100)));
            if (shop.PlayerSpent > 0) yield return GameText.Say("chat.fact.shop_spent", ("spent", new Helpers.Money(shop.PlayerSpent)));
            else yield return GameText.Say("chat.fact.shop_new_customer");

            var offers = BuyOffers();
            yield return offers.Count == 0
                ? GameText.Say("chat.fact.shop_empty")
                : GameText.Say("chat.fact.shop_stock", ("count", offers.Count), ("kinds", string.Join(", ", offers.Select(o => o.Group).Distinct())));

            if (SelectedCar is not { } car) yield return GameText.Say("chat.fact.shop_no_car");
            else
                yield return GameText.Say("chat.fact.shop_car", ("car", _catalogRepo.GetCar(car.DefinitionId)?.Name ?? car.DefinitionId),
                    ("power", _carPower is { } hp ? GameText.Say("chat.fact.shop_car_power", ("power", (int)Math.Round(hp))) : string.Empty));
        }

        /// <summary>
        /// The clerk's search: the counter opens on the shop's own stock, filtered as the player asked (the filters show it),
        /// and the best few of what matches are picked out, first in the list. The best: for power, what gains the player's
        /// engine most on the dyno; cheap, the cheapest; else what fits the player's car, what it gains, then the price.
        /// </summary>
        async Task<ClerkFindings> IShopCounter.FindAsync(ClerkSearch search, CancellationToken cancellationToken)
        {
            IsCounterOpen = true;
            SellTab = false;
            SelectedGroup = search.Kind ?? AllGroups;
            OnlyFitting = search.Fits && CanFilterByFit;
            SearchText = search.Words is { } words && BuyOffers().Any(o => o.Name.Contains(words, StringComparison.OrdinalIgnoreCase)) ? words : string.Empty;
            MaxPrice = search.MaxPrice;
            Pick([]);

            var matching = Matching().DistinctBy(o => o.Definition.Id, StringComparer.OrdinalIgnoreCase).ToList();
            var engine = SelectedCar?.Engine is { } e ? PartTrees.Clone(e, keepIds: true) : null;
            var catalog = Catalog;
            if (engine != null && catalog != null)
            {
                // The ones that fit, the cheapest first, through the dyno: what they gain is what the clerk talks of
                var trying = matching.Where(o => o.Fits && !_power.ContainsKey(o.Definition.Id)).OrderBy(o => o.Price).Take(MostDynoRuns)
                    .Select(o => o.Definition).ToList();
                var found = await Task.Run(() => trying.ToDictionary(p => p.Id, p => PowerWith(catalog, engine, p)), cancellationToken);
                foreach (var (id, power) in found) _power[id] = power;
            }

            double Gain(PartOfferViewModel o) => _power.GetValueOrDefault(o.Definition.Id) is { } p ? p.After - p.Before : 0;
            IEnumerable<PartOfferViewModel> ranked = search.Want switch
            {
                ClerkSearch.Power => matching.OrderByDescending(Gain).ThenByDescending(o => o.Fits).ThenBy(o => o.Price),
                ClerkSearch.Cheap => matching.OrderBy(o => o.Price).ThenByDescending(o => o.Fits),
                _ => matching.OrderByDescending(o => o.Fits).ThenByDescending(Gain).ThenBy(o => o.Price)
            };

            var picks = ranked.Take(MostPicks).ToList();
            Pick(picks.Select(o => o.Definition.Id).ToList());

            return new ClerkFindings(search, matching.Count, picks.Select(o =>
            {
                var power = _power.GetValueOrDefault(o.Definition.Id);
                return new ClerkPick(o.Definition.Id, o.Name, o.Group, o.Price, o.Fits, o.InStock, power?.Before, power?.After);
            }).ToList());
        }

        private (double Before, double After)? PowerWith(PartsCatalog catalog, PartInstance engine, PartDefinition part)
        {
            try
            {
                return EngineTuner.PowerWith(catalog, engine, part);
            }
            catch (Exception ex)
            {
                // A part whose script faults is told of without a dyno figure
                _logger.Warning("{Part} could not go on the dyno for the clerk: {Error}", part.Id, ex.Message);
                return null;
            }
        }

        #endregion

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
                    if (car != null && _partsService.IsAvailable)
                        _carPower = await Task.Run(() => _partsService.Evaluate(car)?.Dyno?.MaxPowerHp);
                }

                OnPropertyChanged(nameof(Notice));
                OnPropertyChanged(nameof(HasNotice));
                OnPropertyChanged(nameof(Catalog));
                ShowFirst();
                RefreshStockShown();
                StartTalk();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not open {Shop}", Definition.Name);
                StatusText = GameText.Say("partshop.open_failed");
                StockShown ??= [];
            }
            finally
            {
                Entering = false;
            }
        }

        public override void Exit()
        {
            base.Exit();
            Talk?.End();
        }

        protected override bool CanList => _shops.IsAvailable && Shop != null;

        protected override IEnumerable<PartOfferViewModel> TabOffers() => IsSellTab ? SellOffers() : BuyOffers();

        /// <summary>What the shop has, at its prices today: its shelves, or a junkyard's yard</summary>
        private List<PartOfferViewModel> BuyOffers()
        {
            if (Shop is not { } shop || !_partsService.IsAvailable) return [];

            var catalog = _partsService.Catalog;
            if (IsJunkyard)
            {
                return shop.Yard
                    .Where(i => catalog.Get(i.Part.DefinitionId) != null)
                    .OrderBy(i => catalog.Get(i.Part.DefinitionId)!.DisplayName ?? catalog.Get(i.Part.DefinitionId)!.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(i => PartOfferViewModel.ForYard(i, catalog.Get(i.Part.DefinitionId)!, _shops.PriceOfUsed(_gameState, shop, i), i.AskingPrice))
                    .ToList();
            }

            return shop.Shelves
                .Where(s => s.Count > 0 && catalog.Get(s.DefinitionId) != null)
                .Select(s => (Shelf: s, Part: catalog.Get(s.DefinitionId)!))
                .OrderBy(s => s.Part.DisplayName ?? s.Part.Name, StringComparer.OrdinalIgnoreCase)
                .Select(s => PartOfferViewModel.ForShelf(s.Shelf, s.Part, _shops.PriceOfNew(_gameState, shop, s.Part),
                    _shops.MarkedPriceOfNew(_gameState, shop, s.Part)))
                .ToList();
        }

        /// <summary>The player's shelf, at what this shop pays</summary>
        private IEnumerable<PartOfferViewModel> SellOffers()
        {
            if (Shop is not { } shop) return [];

            var catalog = _partsService.Catalog;
            return _gameState.Player.Parts
                .Where(p => catalog.Get(p.DefinitionId) != null)
                .Select(p => PartOfferViewModel.ForSale(p, catalog.Get(p.DefinitionId)!, _shops.BuyingPrice(shop, p)));
        }

        /// <summary>A shop that has shut since the page was drawn buys nothing</summary>
        protected override bool CanTake(PartOfferViewModel offer) =>
            IsSellTab ? Shop is { IsTrading: true } : base.CanTake(offer);

        protected override string NothingText =>
            GameText.Say(Shop is { Status: DealerStatus.ClosingDown } ? "partshop.nothing.cleaned_out" : "partshop.nothing");

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
                    { Owned: not null } => (GameText.Say("market.part.not_owned"), GameText.Say("market.part.not_owned.title")),
                    { Ad: { } item } when !shop.Yard.Contains(item) => (GameText.Say("partshop.gone"), GameText.Say("partshop.gone.title")),
                    { Shelf.Count: <= 0 } => (GameText.Say("partshop.sold_out"), GameText.Say("partshop.sold_out.title")),
                    _ => (GameText.Say("market.part.no_money"), GameText.Say("market.part.no_money.title"))
                };
                _dialogService.ShowDialog(new InformationDialogViewModel(_dialogService, message, title));
            }
            else
            {
                Audio.Sfx.Play(Audio.Sfx.Purchase);
                // The clerk remembers who bought what over the counter, once they have talked to the player
                TalkPeople.Happened(_gameState, new PersonEvent
                {
                    Date = _gameState.Date,
                    Kind = offer.Owned != null ? "sold" : "bought",
                    What = offer.Name,
                    Amount = offer.Price
                }, TalkPeople.ShopKey(Definition.Id));
                Save("a parts deal");
                RefreshStockShown();
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

        private void OnBack()
        {
            // The counter first, then the shop
            if (_isCounterOpen && HasScene)
            {
                IsCounterOpen = false;
                return;
            }

            _navigationService.Leave(this, "back", _gameState);
        }
    }
}
