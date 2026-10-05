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

        /// <summary>Parts tried on the dyno for one ask at most: the cheapest that fit and take the place of a part on the engine, so an ask is answered in a moment</summary>
        public const int MostDynoRuns = 40;

        private readonly NavigationService _navigationService;
        private readonly DialogService _dialogService;
        private readonly string _shopId;
        private readonly IPartShopService _shops;
        private readonly INewspaperPartsService _paper;
        private readonly ShopScene? _scene;

        // What each part does for the player's engine, worked out once a visit (the engine does not change in a shop): what
        // the engine makes now, the kinds of part on it (the only kinds the dyno can try), each part tried
        private readonly Dictionary<string, (double Before, double After)?> _power = new(StringComparer.OrdinalIgnoreCase);
        private double? _carPower;
        private (double? Power, HashSet<string> Kinds)? _engine;

        // The shop's stock at its prices, priced once until a deal changes it (a search reads it many times)
        private List<PartOfferViewModel>? _buyOffers;

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
            ShowTabCommand = new RelayCommand<string>(tab => ShowTab(tab == "Sell"));
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

        private PartsCatalog? _catalog;

        /// <summary>The parts catalog, once <c>Enter</c> has loaded it on a worker (never loaded by the view's binding)</summary>
        public PartsCatalog? Catalog
        {
            get => _catalog;
            private set => SetProperty(ref _catalog, value);
        }

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

            List<ShopStockPart> shown = IsJunkyard
                ? shop.Yard.Where(i => catalog.Get(i.Part.DefinitionId) != null).Select(i => new ShopStockPart(catalog.Get(i.Part.DefinitionId)!, i.Part)).ToList()
                : shop.Shelves.Where(s => s.Count > 0 && catalog.Get(s.DefinitionId) != null).Select(s => new ShopStockPart(catalog.Get(s.DefinitionId)!)).ToList();

            // One of three on a shelf sold, or a part sold to the shop: the room looks the same and is not built again
            if (StockShown is { } before && before.SequenceEqual(shown, StockLine.Instance)) return;
            StockShown = shown;
        }

        /// <summary>The same thing standing in the room: the same part, the same piece of a yard</summary>
        private sealed class StockLine : IEqualityComparer<ShopStockPart>
        {
            public static StockLine Instance { get; } = new();

            public bool Equals(ShopStockPart? x, ShopStockPart? y) =>
                x != null && y != null && x.Part.Id == y.Part.Id && ReferenceEquals(x.Tree, y.Tree);

            public int GetHashCode(ShopStockPart obj) => obj.Part.Id.GetHashCode();
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
        /// engine most on the dyno; cheap, the cheapest; else what fits the player's car, what it gains, then the price. A
        /// part in the yard more than once is told of at its cheapest, with how many there are.
        /// </summary>
        async Task<ClerkFindings> IShopCounter.FindAsync(ClerkSearch search, CancellationToken cancellationToken)
        {
            IsCounterOpen = true;
            ShowTab(sell: false, list: false);

            // A name no part has is no filter (the clerk is told nothing goes by it), nor a fit with no car to fit
            var named = search.Words is { } words && BuyOffers().Any(o => o.Name.Contains(words, StringComparison.OrdinalIgnoreCase)) ? words : null;
            var fits = search.Fits && CanFilterByFit;
            SetFilters(search.Kind ?? AllGroups, named ?? string.Empty, fits, search.MaxPrice);
            var run = search with { Words = named, Fits = fits };

            var matching = Matching().GroupBy(o => o.Definition.Id, StringComparer.OrdinalIgnoreCase).Select(Cheapest).ToList();
            var engine = SelectedCar?.Engine is { } e ? PartTrees.Clone(e, keepIds: true) : null;
            if (engine != null && Catalog is { } catalog)
            {
                var (power, kinds) = _engine ??= (await Task.Run(() => PowerOf(catalog, engine), cancellationToken), EngineTuner.KindsOn(catalog, engine));

                // The ones that fit and take the place of a part on the engine, the cheapest first, through the dyno: what
                // they gain is what the clerk talks of
                var trying = matching.Select(m => m.Offer)
                    .Where(o => o.Fits && kinds.Contains(PartKinds.GroupOf(o.Definition)) && !_power.ContainsKey(o.Definition.Id))
                    .OrderBy(o => o.Price).Take(MostDynoRuns).Select(o => o.Definition).ToList();
                if (power != null && trying.Count > 0)
                {
                    var found = await Task.Run(() => trying.ToDictionary(p => p.Id, p => PowerWith(catalog, engine, p, power)), cancellationToken);
                    foreach (var (id, gain) in found) _power[id] = gain;
                }
            }

            double Gain((PartOfferViewModel Offer, int InStock) m) => _power.GetValueOrDefault(m.Offer.Definition.Id) is { } p ? p.After - p.Before : 0;
            IEnumerable<(PartOfferViewModel Offer, int InStock)> ranked = search.Want switch
            {
                ClerkSearch.Power => matching.OrderByDescending(Gain).ThenByDescending(m => m.Offer.Fits).ThenBy(m => m.Offer.Price),
                ClerkSearch.Cheap => matching.OrderBy(m => m.Offer.Price).ThenByDescending(m => m.Offer.Fits),
                _ => matching.OrderByDescending(m => m.Offer.Fits).ThenByDescending(Gain).ThenBy(m => m.Offer.Price)
            };

            var picks = ranked.Take(MostPicks).ToList();
            Pick(picks.Select(m => m.Offer.Definition.Id).ToList());

            return new ClerkFindings(run, matching.Count, picks.Select(m => PickOf(m.Offer, m.InStock)).ToList(), search.Words != null && named == null ? search.Words : null);
        }

        ClerkFindings IShopCounter.Recheck(ClerkFindings found)
        {
            var now = BuyOffers().GroupBy(o => o.Definition.Id, StringComparer.OrdinalIgnoreCase).Select(Cheapest)
                .ToDictionary(m => m.Offer.Definition.Id, StringComparer.OrdinalIgnoreCase);
            foreach (var (offer, _) in now.Values) offer.Fits = _fitting.Contains(offer.Definition.Id);

            var picks = found.Picks.Where(p => now.ContainsKey(p.Id)).Select(p => PickOf(now[p.Id].Offer, now[p.Id].InStock)).ToList();
            return found with { Matching = Math.Max(picks.Count, found.Matching - (found.Picks.Count - picks.Count)), Picks = picks };
        }

        /// <summary>A part's rows (a junkyard has one a piece): the cheapest, and how many there are</summary>
        private static (PartOfferViewModel Offer, int InStock) Cheapest(IEnumerable<PartOfferViewModel> same)
        {
            var rows = same.ToList();
            return (rows.MinBy(o => o.Price)!, rows.Sum(o => o.InStock));
        }

        private ClerkPick PickOf(PartOfferViewModel offer, int inStock)
        {
            var power = _power.GetValueOrDefault(offer.Definition.Id);
            return new ClerkPick(offer.Definition.Id, offer.Name, offer.Group, offer.Price, offer.Fits, inStock, power?.Before, power?.After);
        }

        private double? PowerOf(PartsCatalog catalog, PartInstance engine)
        {
            try
            {
                return EngineTuner.PowerOf(catalog, engine);
            }
            catch (Exception ex)
            {
                // An engine whose scripts fault: the clerk talks of no dyno figures
                _logger.Warning("The player's engine could not go on the dyno for the clerk: {Error}", ex.Message);
                return null;
            }
        }

        private (double Before, double After)? PowerWith(PartsCatalog catalog, PartInstance engine, PartDefinition part, double? powerNow)
        {
            try
            {
                return EngineTuner.PowerWith(catalog, engine, part, powerNow);
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

        /// <summary>The tab showing: the player's shelf when <paramref name="sell"/>, the shop's else; listed unless <paramref name="list"/> is false</summary>
        private void ShowTab(bool sell, bool list = true)
        {
            if (_isSellTab == sell) return;

            _isSellTab = sell;
            OnPropertyChanged(nameof(IsSellTab));
            OnPropertyChanged(nameof(IsBuyTab));
            TabChanged(list);
        }

        public override bool IsSellTab => _isSellTab;

        public bool IsBuyTab => !_isSellTab;

        /// <summary>The car the player drives, the one picked in the garage else their first: the one the clerk is told of</summary>
        protected override Car? SelectedCar => TalkFacts.CarDriven(_gameState);

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
                    Catalog = await Task.Run(() => _partsService.IsAvailable ? _partsService.Catalog : null);
                    if (car != null && Catalog != null)
                        _carPower = await Task.Run(() => _partsService.Evaluate(car)?.Dyno?.MaxPowerHp);
                }

                OnPropertyChanged(nameof(Notice));
                OnPropertyChanged(nameof(HasNotice));
                _buyOffers = null;
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

        /// <summary>What the shop has, at its prices today: its shelves, or a junkyard's yard; priced again after a deal</summary>
        private List<PartOfferViewModel> BuyOffers() => _buyOffers ??= PriceStock();

        private List<PartOfferViewModel> PriceStock()
        {
            if (Shop is not { } shop || Catalog is not { } catalog) return [];

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
            if (Shop is not { } shop || Catalog is not { } catalog) return [];

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
            _buyOffers = null;
            base.Refresh();
            // The clerk talks of what is left at its prices now (a regular's discount, a haggled price, the last one sold)
            Talk?.Talk.StockChanged();
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
