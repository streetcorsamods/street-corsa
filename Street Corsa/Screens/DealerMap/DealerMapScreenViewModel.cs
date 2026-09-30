using System.Collections.ObjectModel;
using StreetCorsa.Dialogs;
using StreetCorsa.Dialogs.Confirmation;
using StreetCorsa.Dialogs.Information;
using StreetCorsa.Logging;
using StreetCorsa.Models.GameState;
using StreetCorsa.Navigation;
using StreetCorsa.Services.Dealers;
using StreetCorsa.Services.Market;
using StreetCorsa.Services.PartShops;
using StreetCorsa.Services.Text;
using StreetCorsa.Services.Time;
using StreetCorsa.ViewModels;

namespace StreetCorsa.Screens.DealerMap
{
    /// <summary>
    /// The city with the car dealers on it, or with the part shops (the garage has a button for each). Picking one drives
    /// out to its lot or its counter.
    /// </summary>
    public class DealerMapScreenViewModel : BaseScreenViewModel
    {
        /// <summary>
        /// The sheet the pins stand on when no mod has a map (or its picture does not read): the whole of it on screen,
        /// in a map's usual shape, so the pins at least keep their places against each other
        /// </summary>
        private const double BlankSheetWidth = 1600;
        private const double BlankSheetHeight = 1000;

        /// <summary>The map's picture, read once: it is a big one, and the map is opened over and over</summary>
        private static (string Path, System.Windows.Media.Imaging.BitmapSource Image)? _mapCache;

        private readonly NavigationService _navigationService;
        private readonly DialogService _dialogService;
        private readonly Models.GameState.GameState _gameState;
        private readonly IDealerCatalog _dealerCatalog;
        private readonly IUsedCarMarketService _marketService;
        private readonly IGameTimeService _timeService;
        private readonly IPartShopService _partShops;
        private readonly IAppLogger _logger;

        // A drive under way: the trip is paid for once, and the map is not left while the time is spent
        private bool _isDriving;

        public RelayCommand BackCommand { get; }
        public RelayCommand<DealerPinViewModel> VisitDealerCommand { get; }

        /// <summary>The part shops' map: their pins and their counters, no car lots</summary>
        public bool IsPartsMap { get; }

        public string Title => IsPartsMap ? "PARTS DEALERS" : "WHERE TO LOOK";

        public string Subtitle => IsPartsMap
            ? "Pick a shop and drive out to see what's on the shelves"
            : "Pick a lot and drive out to see what they have";

        public string EmptyText => IsPartsMap ? "No part shops could be found on the map." : "No dealers could be found on the map.";

        public string LoadingText => IsPartsMap ? "The shops are stocking their shelves..." : "The dealers are putting their cars out...";

        public ObservableCollection<DealerPinViewModel> Pins { get; } = [];

        /// <summary>
        /// The part of the map worth seeing (the map's anchor, see <see cref="DealerMapSpec"/>); the screen grows out from
        /// it to fill the window
        /// </summary>
        public System.Windows.Rect MapAnchor { get; }

        /// <summary>The map's picture: the last mod's (<see cref="IDealerCatalog.Map"/>); null when there is none to show</summary>
        public System.Windows.Media.ImageSource? MapImage { get; }

        /// <summary>The map's own size, so a crop measured in fractions can be given a shape</summary>
        public double MapImageWidth { get; }
        public double MapImageHeight { get; }

        private bool _isLoading;

        /// <summary>The market has never been filled and is being put together</summary>
        public bool IsLoading
        {
            get => _isLoading;
            private set
            {
                _isLoading = value;
                OnPropertyChanged(nameof(IsLoading));
            }
        }

        /// <summary>No dealer definitions: the map has nothing to put on itself</summary>
        public bool HasDealers => Pins.Count > 0;

        public DealerMapScreenViewModel(
            NavigationService navigationService,
            DialogService dialogService,
            Models.GameState.GameState gameState,
            IDealerCatalog dealerCatalog,
            IUsedCarMarketService marketService,
            IGameTimeService timeService,
            IPartShopService partShops,
            bool partShopsMap = false)
        {
            _navigationService = navigationService;
            _dialogService = dialogService;
            _gameState = gameState;
            _dealerCatalog = dealerCatalog;
            _marketService = marketService;
            _timeService = timeService;
            _partShops = partShops;
            IsPartsMap = partShopsMap;
            _logger = AppLoggerFactory.CreateLogger("DealerMap");

            (MapImage, MapAnchor, MapImageWidth, MapImageHeight) = LoadMap(dealerCatalog.Map);

            BackCommand = new RelayCommand(OnBack, () => !_isDriving);
            VisitDealerCommand = new RelayCommand<DealerPinViewModel>(OnVisitDealer, _ => !_isDriving);

            // The map is filled in Enter, after the screen before it has been left
        }

        /// <summary>The map's picture, read whole and frozen so the file is not held open, and kept for the next time</summary>
        private (System.Windows.Media.ImageSource?, System.Windows.Rect, double, double) LoadMap(DealerMapSpec? map)
        {
            if (map?.ImagePath is { } path)
            {
                if (_mapCache is { } cached && string.Equals(cached.Path, path, StringComparison.OrdinalIgnoreCase))
                {
                    return (cached.Image, map.Anchor, cached.Image.PixelWidth, cached.Image.PixelHeight);
                }

                try
                {
                    var image = new System.Windows.Media.Imaging.BitmapImage();
                    image.BeginInit();
                    image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    image.UriSource = new Uri(path, UriKind.Absolute);
                    image.EndInit();
                    image.Freeze();
                    if (image.PixelWidth > 0 && image.PixelHeight > 0)
                    {
                        _mapCache = (path, image);
                        return (image, map.Anchor, image.PixelWidth, image.PixelHeight);
                    }
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    _logger.Error(ex, "Could not read the dealer map {Path}", path);
                }
            }
            else
            {
                _logger.Warning("No mod has a dealer map: the pins stand on a blank sheet");
            }

            return (null, new DealerMapSpec().Anchor, BlankSheetWidth, BlankSheetHeight);
        }

        private async Task InitializeAsync()
        {
            if (IsPartsMap) await LoadShopsAsync();
            else await LoadDealersAsync();

            ShowPins(IsPartsMap ? ShopPins() : DealerPins());
        }

        /// <summary>The part shops, brought up to the mods' files, and stocked if no morning has got round to it yet</summary>
        private async Task LoadShopsAsync()
        {
            IsLoading = true;
            try
            {
                PartShopRoster.Sync(_gameState, _partShops.Catalog);
                await _partShops.RestockAsync(_gameState, _gameState.Date, Random.Shared, firstOnly: true);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not stock the part shops");
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>The dealers, brought up to the mods' files, and their lots stocked if the market needs it</summary>
        private async Task LoadDealersAsync()
        {
            // The dealers the save carries are whatever the files say today, each keeping how its business is going:
            // a save made before a dealer existed still gets it, and one dropped from the files goes away
            DealerRoster.Sync(_gameState, _dealerCatalog);
            if (_gameState.DealerLocations == null || _gameState.DealerLocations.Count == 0)
            {
                _gameState.DealerLocations = _marketService.GetDefaultDealers();
            }

            if (_gameState.UsedCarMarket == null || _gameState.UsedCarMarket.Count == 0 || IsMarketStale())
            {
                IsLoading = true;
                try
                {
                    // A lot in its closing-down sale restocks nothing: what it has left is what it sells, so it keeps it
                    var closing = DealerRoster.ClosingDown(_gameState).Select(d => d.Id).ToHashSet();
                    var onSale = (_gameState.UsedCarMarket ?? []).Where(l => !l.IsSold && closing.Contains(l.DealerLocation)).ToList();

                    var spawned = await _marketService.SpawnListingsAsync(
                        DealerRoster.Restocking(_gameState), _gameState.Date, _gameState.Rules.CarPriceMultiplier);
                    spawned.AddRange(onSale);
                    _gameState.UsedCarMarket = spawned;
                    _logger.Information("Spawned {Count} listings for the map, kept {OnSale} in closing-down sales",
                        spawned.Count - onSale.Count, onSale.Count);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Could not spawn the market");
                }
                finally
                {
                    IsLoading = false;
                }
            }
            else if (NeverStocked() is { Count: > 0 } newcomers)
            {
                // A dealer the save has never heard of (a mod put in since) gets a lot of its own; the other lots,
                // the player's and the rivals' cars among them, stay as they are
                IsLoading = true;
                try
                {
                    var fresh = await _marketService.SpawnListingsAsync(newcomers, _gameState.Date, _gameState.Rules.CarPriceMultiplier);
                    _gameState.UsedCarMarket.AddRange(fresh);
                    _logger.Information("Stocked {Count} cars on the lots of {Dealers}", fresh.Count, string.Join(", ", newcomers.Select(d => d.Name)));
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Could not stock the new dealers");
                }
                finally
                {
                    IsLoading = false;
                }
            }
        }

        /// <summary>
        /// Whether the market on the save was stocked for lots of another size: one carrying far more cars than it
        /// has room to stand. The cars for sale are then worth nothing to anybody, so they are thrown out and drawn again.
        /// </summary>
        private bool IsMarketStale()
        {
            var listings = _gameState.UsedCarMarket;
            if (listings == null || listings.Count == 0) return false;

            foreach (var dealer in _dealerCatalog.All)
            {
                var onTheLot = listings.Count(l => !l.IsSold && l.DealerLocation == dealer.Id);

                if (onTheLot > dealer.StockHigh * 2)
                {
                    _logger.Information("{Dealer} is carrying {Count} cars against a lot that holds {Room}; " +
                        "restocking the whole market", dealer.Name, onTheLot, dealer.StockHigh);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The dealers with nothing on the books: a save made before they existed. Sold listings count here: a lot
        /// the player has just bought out still has its sold cars on the books until the next refresh.
        /// </summary>
        private List<DealerLocation> NeverStocked()
        {
            var listings = _gameState.UsedCarMarket ?? [];
            return DealerRoster.Restocking(_gameState)
                .Where(d => !listings.Any(l => l.DealerLocation == d.Id))
                .ToList();
        }

        private void ShowPins(IEnumerable<DealerPinViewModel> pins)
        {
            Pins.Clear();
            foreach (var pin in pins) Pins.Add(pin);

            OnPropertyChanged(nameof(HasDealers));
            _logger.Information("Map showing {Count} {What}", Pins.Count, IsPartsMap ? "part shops" : "dealers");
        }

        /// <summary>The lots in business: open, or selling off their stock. The rest are closed, or not open yet.</summary>
        private IEnumerable<DealerPinViewModel> DealerPins()
        {
            var remainingToday = _timeService.GetRemainingMinutesToday(_gameState);
            foreach (var location in DealerRoster.Trading(_gameState))
            {
                if (_dealerCatalog.Get(location.Id) is not { } dealer) continue;
                var stock = _marketService.GetListingsByDealer(_gameState.UsedCarMarket ?? [], dealer.Id);

                yield return new DealerPinViewModel
                {
                    Id = dealer.Id,
                    Name = dealer.Name,
                    Region = dealer.Region,
                    Blurb = dealer.Blurb,

                    MapX = dealer.MapX,
                    MapY = dealer.MapY,

                    StockCount = stock.Count,
                    CheapestPrice = stock.Count > 0 ? stock.Min(l => l.Price) : 0m,
                    DearestPrice = stock.Count > 0 ? stock.Max(l => l.Price) : 0m,
                    TravelHours = dealer.TravelHours,
                    MinutesLeftToday = remainingToday,

                    ClosesOn = location.Status == DealerStatus.ClosingDown ? location.ClosesOn : null,
                    DaysLeft = DealerRules.DaysLeft(location, _gameState.Date),
                    Discount = DealerRules.DiscountOn(location, _gameState.Date),
                    IsNew = location.OpenedOn is { } opened && (_gameState.Date.Date - opened.Date).TotalDays < NewForDays
                };
            }
        }

        /// <summary>The part shops in business, as the lots are</summary>
        private IEnumerable<DealerPinViewModel> ShopPins()
        {
            var remainingToday = _timeService.GetRemainingMinutesToday(_gameState);
            foreach (var location in PartShopRoster.Trading(_gameState))
            {
                if (_partShops.Catalog.Get(location.Id) is not { } shop) continue;

                yield return new DealerPinViewModel
                {
                    Id = shop.Id,
                    Name = shop.Name,
                    Region = shop.Region,
                    Blurb = shop.Blurb,
                    IsPartShop = true,
                    KindLabel = GameText.Installed.Get($"partshop.kind.{shop.KindOf.ToString().ToLowerInvariant()}"),

                    MapX = shop.MapX,
                    MapY = shop.MapY,

                    StockCount = shop.KindOf == PartShopKind.Junkyard ? location.Yard.Count : location.Shelves.Sum(s => Math.Max(0, s.Count)),
                    TravelHours = shop.TravelHours,
                    MinutesLeftToday = remainingToday,

                    ClosesOn = location.Status == DealerStatus.ClosingDown ? location.ClosesOn : null,
                    DaysLeft = DealerRules.DaysLeft(location, _gameState.Date),
                    Discount = DealerRules.DiscountOn(location, _gameState.Date),
                    IsNew = location.OpenedOn is { } opened && (_gameState.Date.Date - opened.Date).TotalDays < NewForDays
                };
            }
        }

        /// <summary>A lot that opened during the game is marked new on the map for this long</summary>
        private const int NewForDays = 21;

        private void OnVisitDealer(DealerPinViewModel? pin)
        {
            if (pin == null || _isDriving) return;

            // A drive that does not fit in what is left of the day costs the rest of it: the player gets
            // there, looks round, and comes back tomorrow morning. That is a fair trade for a long trip to
            // a lot with something good on it, but it is not one to make by accident.
            // The last day of a sale, and too far to get there today: the doors will have shut by the morning
            if (!pin.FitsToday && pin.IsClosingDown && pin.DaysLeft == 0)
            {
                _dialogService.ShowDialog(new InformationDialogViewModel(
                    _dialogService,
                    GameText.Installed.Get("dealer.drive.too_late", new TextValues { ["dealer"] = pin.Name, ["time"] = _gameState.Date }),
                    GameText.Installed.Get("dealer.drive.too_late.title")));
                return;
            }

            if (!pin.FitsToday)
            {
                var message = $"{pin.Name} is {pin.TravelDisplay.ToLowerInvariant()}, and it is " +
                              $"{_gameState.Date:h:mm tt}." + Environment.NewLine + Environment.NewLine +
                              "You would not be back before the day is out. Drive over anyway?";

                _dialogService.ShowDialog(new ConfirmationDialogViewModel(
                    _dialogService,
                    message,
                    "A long way for a look",
                    confirmed =>
                    {
                        if (confirmed) DriveTo(pin);
                    }));
                return;
            }

            DriveTo(pin);
        }

        private async void DriveTo(DealerPinViewModel pin)
        {
            // One drive at a time: a second click while the time is spent would pay for the trip twice
            if (_isDriving) return;
            _isDriving = true;
            RelayCommand.RaiseCanExecuteChanged();

            try
            {
                _logger.Information("Driving out to {Dealer}: {Minutes} minutes there and back",
                    pin.Name, pin.TravelMinutes);

                try
                {
                    // The hours go before the lot is shown, so the clock on the way in is the time you arrived
                    await _timeService.SpendTimeAsync(_gameState, pin.TravelMinutes);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Could not spend the time for the drive to {Dealer}", pin.Name);
                }

                // Guarded by the navigation service: a lot that will not open is reported and the map stays
                if (pin.IsPartShop) _navigationService.NavigateToPartShop(_gameState, pin.Id);
                else _navigationService.NavigateToDealerLot(_gameState, pin.Id);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not drive out to {Dealer}", pin.Name);
            }
            finally
            {
                _isDriving = false;
                RelayCommand.RaiseCanExecuteChanged();
            }
        }

        private void OnBack()
        {
            _navigationService.NavigateToGarage(_gameState, skipAnimation: true);
        }

        public override async void Enter()
        {
            base.Enter();
            _logger.Information("Entered the dealer map");

            // The whole load in one guard: a market that fails to spawn or a pin that fails to build leaves an
            // empty map, not a dead game
            try
            {
                await InitializeAsync();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not put the dealers on the map");
                IsLoading = false;
            }
        }
    }

    /// <summary>One dealer's or part shop's pin on the map</summary>
    public class DealerPinViewModel
    {
        /// <summary>A part shop, not a car lot: its own pin, and its counter when picked</summary>
        public bool IsPartShop { get; set; }

        /// <summary>What sort of part shop ("Speed shop"); empty for a car lot</summary>
        public string KindLabel { get; set; } = string.Empty;

        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Region { get; set; } = string.Empty;
        public string Blurb { get; set; } = string.Empty;

        /// <summary>Where the dealer is on the whole map image, 0..1. MapPanel puts it on screen</summary>
        public double MapX { get; set; }
        public double MapY { get; set; }

        public int StockCount { get; set; }
        public decimal CheapestPrice { get; set; }
        public decimal DearestPrice { get; set; }
        public float TravelHours { get; set; }

        /// <summary>What is left of the working day when the map was drawn</summary>
        public int MinutesLeftToday { get; set; }

        /// <summary>The drive out and back, in minutes of game time</summary>
        public int TravelMinutes => Math.Max(15, (int)Math.Round(TravelHours * 60f));

        /// <summary>Whether there is enough of the day left to go and come back</summary>
        public bool FitsToday => TravelMinutes <= MinutesLeftToday;

        /// <summary>What the trip does to the day, for the card</summary>
        public string DayCostDisplay => FitsToday
            ? "You would be back before the day is out"
            : "You would not be back today";

        public string StockDisplay => IsPartShop
            ? StockCount == 1 ? "1 part" : $"{StockCount} parts"
            : StockCount == 1 ? "1 car" : $"{StockCount} cars";

        public string PriceDisplay => IsPartShop
            ? KindLabel
            : StockCount == 0
                ? "Nothing on the lot"
                : $"${CheapestPrice:N0} - ${DearestPrice:N0}";

        /// <summary>The last day of its closing-down sale; null when it is not closing down</summary>
        public DateTime? ClosesOn { get; set; }

        /// <summary>What its closing-down sale takes off today</summary>
        public decimal Discount { get; set; }

        /// <summary>The days before the sale's doors shut: 0 on its last day</summary>
        public int DaysLeft { get; set; }

        /// <summary>Opened during the game, not long ago</summary>
        public bool IsNew { get; set; }

        public bool IsClosingDown => ClosesOn != null;

        /// <summary>A word on the pin: the sale, or a lot that is new</summary>
        public string Notice => IsClosingDown ? Text("dealer.notice.closing") : IsNew ? Text("dealer.notice.new") : string.Empty;

        public bool HasNotice => Notice.Length > 0;

        /// <summary>The card's line on the sale</summary>
        public string NoticeDetail => IsClosingDown
            // The cut still to come, until it has come
            ? Text($"{Words}.detail.{(DaysLeft >= DealerRules.FinalDays ? "closing" : "closing_final")}")
            : IsNew ? Text($"{Words}.detail.new") : string.Empty;

        /// <summary>The card's words are a lot's or a shop's: "every car" or "everything"</summary>
        private string Words => IsPartShop ? "partshop" : "dealer";

        private string Text(string key) => GameText.Installed.Get(key, new TextValues
        {
            ["dealer"] = Name,
            ["discount"] = (int)Math.Round(Discount * 100),
            ["finalDays"] = DealerRules.FinalDays,
            ["day"] = ClosesOn
        });

        public string TravelDisplay => TravelHours <= 1f
            ? "About an hour away"
            : $"About {TravelHours:0.#} hours away";
    }
}
