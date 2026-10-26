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

        // What each part does for the player's engine, worked out once a visit (the engine does not change in a shop): what
        // the engine makes now, the kinds of part on it (the only kinds the dyno can try), each part tried, by its row's key
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
            Clerk = new ClerkTalk(_logger, "clerk");
            Room = new ShopRoomViewModel(Definition, gameState.Date, Clerk, scene);

            BackCommand = new RelayCommand(OnBack);
            ShowTabCommand = new RelayCommand<string>(tab => ShowTab(tab == "Sell"));
            BuyCommand = new RelayCommand<PartOfferViewModel>(OnOfferAction);
            HaggleCommand = new RelayCommand<PartOfferViewModel>(OnHaggle);
            ToggleCounterCommand = new RelayCommand(() => Room.IsCounterOpen = !Room.IsCounterOpen);
        }

        public PartShopDefinition Definition { get; }

        /// <summary>The room, its counter and the stock standing in it</summary>
        public ShopRoomViewModel Room { get; }

        /// <summary>Whoever is behind the counter, talked to while the player's AI is on</summary>
        public ClerkTalk Clerk { get; }

        private PartsCatalog? Catalog => Room.Catalog;

        private PartShopLocation? Shop => PartShopRoster.Find(_gameState, _shopId);

        public bool IsJunkyard => Definition.KindOf == PartShopKind.Junkyard;

        public string ShopName => Definition.Name.ToUpperInvariant();
        public string Region => Definition.Region;
        public string Blurb => Definition.Blurb;
        public string KindLabel => GameText.Installed.Get($"partshop.kind.{Definition.KindOf.ToString().ToLowerInvariant()}");

        public string BuyTabLabel => GameText.Say(IsJunkyard ? "partshop.tab.yard" : "partshop.tab.shelves");
        public string SellTabLabel => GameText.Say("partshop.tab.sell", ("shop", Definition.Name));

        #region What stands in the room

        private void RefreshStockShown()
        {
            // Nothing to show is an empty shop, not one still to be stocked: the room comes up all the same
            if (Shop is not { } shop || Catalog is not { } catalog)
            {
                Room.StockShown = [];
                return;
            }

            List<ShopStockPart> shown = IsJunkyard
                ? shop.Yard.Where(i => catalog.Get(i.Part.DefinitionId) != null).Select(i => new ShopStockPart(catalog.Get(i.Part.DefinitionId)!, i.Part)).ToList()
                : shop.Shelves.Where(s => s.Count > 0 && catalog.Get(s.DefinitionId) != null).Select(s => new ShopStockPart(catalog.Get(s.DefinitionId)!)).ToList();

            // One of three on a shelf sold, or a part sold to the shop: the room looks the same and is not built again
            if (Room.StockShown is { } before && before.SequenceEqual(shown, StockLine.Instance)) return;
            Room.StockShown = shown;
        }

        /// <summary>The same thing standing in the room: the same part, the same piece of a yard</summary>
        private sealed class StockLine : IEqualityComparer<ShopStockPart>
        {
            public static StockLine Instance { get; } = new();

            public bool Equals(ShopStockPart? x, ShopStockPart? y) =>
                x != null && y != null && x.Part.Id == y.Part.Id && ReferenceEquals(x.Tree, y.Tree);

            public int GetHashCode(ShopStockPart obj) => obj.Part.Id.GetHashCode();
        }

        #endregion

        #region The clerk

        /// <summary>They look up as the player comes in: greeted before the counter is opened, the tab calls for the player</summary>
        private void StartTalk()
        {
            if (Shop == null) return;
            Clerk.Start(() => TalkDesk.Shared.ForShop(_gameState, Definition.Id, Definition.Clerk, this), Definition.Clerk?.PortraitFile, Room.IsCounterOpen);
        }

        IReadOnlyList<string> IShopCounter.Kinds => BuyOffers().Select(o => o.Group).Distinct().Order(StringComparer.OrdinalIgnoreCase).ToList();

        IEnumerable<string> IShopCounter.Facts()
        {
            yield return GameText.Say("chat.fact.shop", ("shop", Definition.Name), ("kind", KindLabel.ToLowerInvariant()), ("region", Region),
                ("blurb", Blurb));
            if (Shop is not { } shop) yield break;

            foreach (var fact in PartShopNotice.Facts(shop, _gameState.Date)) yield return fact;

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
            Room.IsCounterOpen = true;
            ShowTab(sell: false, list: false);

            // A name no part has is no filter (the clerk is told nothing goes by it), nor a fit with no car to fit
            var named = search.Words is { } words && BuyOffers().Any(o => o.Name.Contains(words, StringComparison.OrdinalIgnoreCase)) ? words : null;
            var fits = search.Fits && CanFilterByFit;
            SetFilters(search.Kind ?? AllGroups, named ?? string.Empty, fits, search.MaxPrice);
            var run = search with { Words = named, Fits = fits };

            // Rows of one thing (a part on two shelves) as one; a kit is its own thing, not its block (PartOfferViewModel.Key)
            var matching = Matching().GroupBy(o => o.Key, StringComparer.OrdinalIgnoreCase).Select(Cheapest).ToList();
            var engine = SelectedCar?.Engine is { } e ? PartTrees.Clone(e, keepIds: true) : null;
            if (engine != null && Catalog is { } catalog)
            {
                var (power, kinds) = _engine ??= (await Task.Run(() => PowerOf(catalog, engine), cancellationToken), EngineTuner.KindsOn(catalog, engine));

                // The ones that fit and take the place of a part on the engine, the cheapest first, through the dyno: what
                // they gain is what the clerk talks of (a kit is a whole engine: no part's place to take)
                var trying = matching.Select(m => m.Offer)
                    .Where(o => o.Fits && o.Kit == null && o.UsedKit == null && kinds.Contains(PartKinds.GroupOf(o.Definition)) && !_power.ContainsKey(o.Key))
                    .OrderBy(o => o.Price).Take(MostDynoRuns).ToList();
                if (power != null && trying.Count > 0)
                {
                    var found = await Task.Run(() => trying.ToDictionary(o => o.Key, o => PowerWith(catalog, engine, o.Definition, power)), cancellationToken);
                    foreach (var (key, gain) in found) _power[key] = gain;
                }
            }

            double Gain((PartOfferViewModel Offer, int InStock) m) => _power.GetValueOrDefault(m.Offer.Key) is { } p ? p.After - p.Before : 0;
            IEnumerable<(PartOfferViewModel Offer, int InStock)> ranked = search.Want switch
            {
                ClerkSearch.Power => matching.OrderByDescending(Gain).ThenByDescending(m => m.Offer.Fits).ThenBy(m => m.Offer.Price),
                ClerkSearch.Cheap => matching.OrderBy(m => m.Offer.Price).ThenByDescending(m => m.Offer.Fits),
                _ => matching.OrderByDescending(m => m.Offer.Fits).ThenByDescending(Gain).ThenBy(m => m.Offer.Price)
            };

            var picks = ranked.Take(MostPicks).ToList();
            Pick(picks.Select(m => m.Offer.Key).ToList());

            return new ClerkFindings(run, matching.Count, picks.Select(m => PickOf(m.Offer, m.InStock)).ToList(), search.Words != null && named == null ? search.Words : null);
        }

        ClerkFindings IShopCounter.Recheck(ClerkFindings found)
        {
            var now = BuyOffers().GroupBy(o => o.Key, StringComparer.OrdinalIgnoreCase).Select(Cheapest)
                .ToDictionary(m => m.Offer.Key, StringComparer.OrdinalIgnoreCase);
            foreach (var (offer, _) in now.Values) offer.Fits = _fitting.Contains(offer.Definition.Id);

            var picks = found.Picks.Where(p => now.ContainsKey(p.Id)).Select(p => PickOf(now[p.Id].Offer, now[p.Id].InStock)).ToList();
            return found with { Matching = Math.Max(picks.Count, found.Matching - (found.Picks.Count - picks.Count)), Picks = picks };
        }

        /// <summary>One thing's rows (a junkyard has one a piece): the cheapest, and how many there are</summary>
        private static (PartOfferViewModel Offer, int InStock) Cheapest(IEnumerable<PartOfferViewModel> same)
        {
            var rows = same.ToList();
            return (rows.MinBy(o => o.Price)!, rows.Sum(o => o.InStock));
        }

        private ClerkPick PickOf(PartOfferViewModel offer, int inStock)
        {
            var power = _power.GetValueOrDefault(offer.Key);
            return new ClerkPick(offer.Key, offer.Name, offer.Group, offer.Price, offer.Fits, inStock, power?.Before, power?.After);
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
        public string Notice => PartShopNotice.Text(Shop, _gameState.Date);

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

        public override async Task EnterAsync()
        {
            _logger.Information("Walked into {Shop}", Definition.Name);

            Entering = true;
            try
            {
                var car = SelectedCar;
                if (await Room.OpenAsync(_shops, _partsService, _gameState))
                {
                    // A shop the mornings have not got round to yet (a game just begun) is stocked as the player walks in
                    await _shops.RestockAsync(_gameState, _gameState.Date, Random.Shared, firstOnly: true);

                    if (car != null) await _partsService.EnsurePartsAsync(car);
                    _fitting = await Task.Run(() => _paper.FindFittingParts(car));
                    if (car != null && Catalog != null)
                        _carPower = await Task.Run(() => _partsService.Evaluate(car)?.Dyno?.MaxPowerHp);
                }

                // The player may have walked out meanwhile: no clerk is started for a shop already left
                if (!ReferenceEquals(_navigationService.CurrentScreen, this)) return;

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
                Room.StockShown ??= [];
            }
            finally
            {
                Entering = false;
            }
        }

        public override void Exit()
        {
            base.Exit();
            Clerk.End();
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
                // The engines taken apart whole first, then the loose parts
                return (shop.YardKits ?? [])
                    .Where(k => k.Parts.FirstOrDefault() is { } first && catalog.Get(first.DefinitionId) != null)
                    .OrderBy(k => k.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(k => PartOfferViewModel.ForYardKit(k, catalog.Get(k.Parts[0].DefinitionId)!, _shops.PriceOfUsedKit(_gameState, shop, k), k.AskingPrice))
                    .Concat(shop.Yard
                        .Where(i => catalog.Get(i.Part.DefinitionId) != null)
                        .OrderBy(i => catalog.Get(i.Part.DefinitionId)!.DisplayName ?? catalog.Get(i.Part.DefinitionId)!.Name, StringComparer.OrdinalIgnoreCase)
                        .Select(i => PartOfferViewModel.ForYard(i, catalog.Get(i.Part.DefinitionId)!, _shops.PriceOfUsed(_gameState, shop, i), i.AskingPrice)))
                    .ToList();
            }

            // The engine kits first, then the parts on the shelves; each kit's line looked up once
            var kits = (shop.Kits ?? [])
                .Where(k => k.Count > 0)
                .Select(k => (Kit: k, Line: _shops.LineOf(k)))
                .Where(k => k.Line?.Parts.FirstOrDefault(PartKinds.IsBlock) != null)
                .OrderBy(k => k.Line!.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(k => k.Line!.Parts.Count)
                .Select(k => PartOfferViewModel.ForKit(k.Kit, k.Line!, k.Line!.Parts.First(PartKinds.IsBlock), _shops.PriceOfKit(_gameState, shop, k.Line!),
                    _shops.MarkedPriceOfKit(_gameState, shop, k.Line!)));

            return kits.Concat(shop.Shelves
                .Where(s => s.Count > 0 && catalog.Get(s.DefinitionId) != null)
                .Select(s => (Shelf: s, Part: catalog.Get(s.DefinitionId)!))
                .OrderBy(s => s.Part.DisplayName ?? s.Part.Name, StringComparer.OrdinalIgnoreCase)
                .Select(s => PartOfferViewModel.ForShelf(s.Shelf, s.Part, _shops.PriceOfNew(_gameState, shop, s.Part),
                    _shops.MarkedPriceOfNew(_gameState, shop, s.Part))))
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
                { Kit: { } kit } => _shops.BuyKit(_gameState, shop, kit),
                { UsedKit: { } usedKit } => _shops.BuyUsedKit(_gameState, shop, usedKit),
                _ => _shops.BuyNew(_gameState, shop, offer.Definition)
            };

            if (!done)
            {
                var (message, title) = offer switch
                {
                    { Owned: not null } => (GameText.Say("market.part.not_owned"), GameText.Say("market.part.not_owned.title")),
                    { Ad: { } item } when !shop.Yard.Contains(item) => (GameText.Say("partshop.gone"), GameText.Say("partshop.gone.title")),
                    { UsedKit: { } usedKit } when !(shop.YardKits ?? []).Contains(usedKit) => (GameText.Say("partshop.gone"), GameText.Say("partshop.gone.title")),
                    { Shelf.Count: <= 0 } or { Kit.Count: <= 0 } => (GameText.Say("partshop.sold_out"), GameText.Say("partshop.sold_out.title")),
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
            Clerk.StockChanged();
        }

        private void OnBack()
        {
            // The counter first, then the shop
            if (Room.IsCounterOverRoom)
            {
                Room.IsCounterOpen = false;
                return;
            }

            _navigationService.Leave(this, "back", _gameState);
        }
    }
}
