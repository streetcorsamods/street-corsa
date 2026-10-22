using StreetCorsa.Configuration;
using StreetCorsa.Dialogs;
using StreetCorsa.Dialogs.Confirmation;
using StreetCorsa.Dialogs.Information;
using StreetCorsa.Helpers;
using StreetCorsa.Logging;
using StreetCorsa.Models.GameState;
using StreetCorsa.Navigation;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Export;
using StreetCorsa.Screens.Shared;
using StreetCorsa.Services.Catalog;
using StreetCorsa.Services.Dealers;
using StreetCorsa.Services.Market;
using StreetCorsa.Services.Opponents;
using StreetCorsa.Services.Parts;
using StreetCorsa.Services.Storage;
using StreetCorsa.Services.Text;
using StreetCorsa.Services.Time;
using StreetCorsa.ViewModels;

namespace StreetCorsa.Screens.Newspaper
{
    /// <summary>The paper's two sections of classifieds</summary>
    public enum ClassifiedSection
    {
        Cars,
        Parts
    }

    /// <summary>
    /// The paper: the classifieds, nothing else (the race invitations and Street News are the racing hub's). Two sections,
    /// used cars and used parts, set as a period paper's ads and read a page at a time as the player scrolls. Every car
    /// for sale is there, the dealers', the racers' own and the player's own (somewhere among the rest, circled in pencil;
    /// clicked, the buyer who called); every used part ad, the racers' too. An ad is bought from here, and clicked to go
    /// and look at it.
    /// </summary>
    public class NewspaperScreenViewModel : BaseScreenViewModel
    {
        /// <summary>Ads added each time the player reaches the end of a section: a screen's worth and more, not a rule</summary>
        public const int AdsPerPage = 30;

        private readonly NavigationService _navigationService;
        private readonly DialogService _dialogService;
        private readonly Models.GameState.GameState _gameState;
        private readonly IContentCatalogRepository _catalogRepository;
        private readonly ICarProfileRepository _profileRepository;
        private readonly IUsedCarMarketService _marketService;
        private readonly ICarPartsService _partsService;
        private readonly INewspaperPartsService _partAdsService;
        private readonly IGameTimeService _timeService;
        private readonly IGameStateRepository _gameStateRepo;
        private readonly ICarSaleService _saleService;
        private readonly IAppLogger _logger;

        /// <summary>Ids of the parts that go somewhere on the car selected in the garage</summary>
        private HashSet<string> _fitting = new(StringComparer.OrdinalIgnoreCase);

        // An ad from before ads had words got them as it was shown: the save is worth writing
        private bool _wroteBlurbs;

        public RelayCommand BackCommand { get; }
        public RelayCommand<string> ShowSectionCommand { get; }
        public bool SkipEnterAnimation { get; }

        public NewspaperScreenViewModel(
            NavigationService navigationService,
            DialogService dialogService,
            Models.GameState.GameState gameState,
            IContentCatalogRepository catalogRepository,
            ICarProfileRepository profileRepository,
            IUsedCarMarketService marketService,
            ICarPartsService partsService,
            INewspaperPartsService partAdsService,
            IGameTimeService timeService,
            IGameStateRepository gameStateRepo,
            ICarSaleService saleService,
            bool skipAnimation = false)
        {
            _navigationService = navigationService;
            _dialogService = dialogService;
            _gameState = gameState;
            _catalogRepository = catalogRepository;
            _profileRepository = profileRepository;
            _marketService = marketService;
            _partsService = partsService;
            _partAdsService = partAdsService;
            _timeService = timeService;
            _gameStateRepo = gameStateRepo;
            _saleService = saleService;
            _logger = AppLoggerFactory.CreateLogger("Newspaper");
            SkipEnterAnimation = skipAnimation;

            BackCommand = new RelayCommand(OnBack);
            ShowSectionCommand = new RelayCommand<string>(section =>
            {
                if (Enum.TryParse<ClassifiedSection>(section, out var shown)) Section = shown;
            });

            // The ads are put together in Enter: the screen is always entered right after it is made
        }

        // ----- what the page shows -----

        private ClassifiedSection _section = ClassifiedSection.Cars;

        public ClassifiedSection Section
        {
            get => _section;
            set
            {
                if (!SetProperty(ref _section, value)) return;
                OnPropertyChanged(nameof(ShowCars));
                OnPropertyChanged(nameof(ShowParts));
            }
        }

        public bool ShowCars => _section == ClassifiedSection.Cars;
        public bool ShowParts => _section == ClassifiedSection.Parts;

        /// <summary>The paper's date line</summary>
        public string EditionDisplay => GameText.Date(_gameState.Date, "format.date.long");

        /// <summary>Every car for sale, a page at a time</summary>
        public AdPages<CarAdViewModel> CarAds { get; } = new(AdsPerPage);

        /// <summary>Every used part ad, a page at a time</summary>
        public AdPages<PartAdViewModel> PartAds { get; } = new(AdsPerPage);

        /// <summary>Every used engine kit, before the parts (roadmap step 40)</summary>
        public System.Collections.ObjectModel.ObservableCollection<KitAdViewModel> KitAds { get; } = [];

        public bool HasCarAds => CarAds.Count > 0;
        public bool HasPartAds => PartAds.Count > 0;

        /// <summary>The paper is out and the section has no ads: it says so</summary>
        public bool ShowNoCars => !IsLoading && !HasCarAds;
        public bool ShowNoParts => !IsLoading && !HasPartAds && KitAds.Count == 0;

        private bool _isLoading;

        /// <summary>The paper is being put together: the first market of a game gives every car an engine, which takes a few seconds</summary>
        public bool IsLoading
        {
            get => _isLoading;
            private set
            {
                if (!SetProperty(ref _isLoading, value)) return;
                OnPropertyChanged(nameof(ShowNoCars));
                OnPropertyChanged(nameof(ShowNoParts));
            }
        }

        /// <summary>The player scrolled to the end of the section on show: the next page of its ads</summary>
        public void ShowMore()
        {
            if (ShowCars) CarAds.ShowMore();
            else PartAds.ShowMore();
        }

        // ----- putting it together -----

        public override async void Enter()
        {
            base.Enter();

            IsLoading = true;
            try
            {
                await PrepareAsync();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not put the paper's ads together");
            }
            finally
            {
                IsLoading = false;
            }

            LoadAll();

            // Back from looking at a car, or the player put the paper down while it was put together: no time spent
            if (SkipEnterAnimation || !ReferenceEquals(_navigationService.CurrentScreen, this))
            {
                if (_wroteBlurbs) Save("the ads' words");
                return;
            }

            try
            {
                // Late in the evening that is the next morning, with another paper: the ads are read again, and the new
                // day is saved
                var spent = await _timeService.SpendTimeAsync(_gameState, GameAction.VisitNewspaper);
                if (spent.NewDayStarted) LoadAll();

                Save("the time for the newspaper");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not spend and save the time for the newspaper");
            }
        }

        /// <summary>
        /// The first market of a game and its first part ads (made with the game; here for a game from before, or one
        /// still being made), the part catalog and what fits the player's car: what takes a moment, off the UI thread
        /// where it can be. The game state itself is only changed on it.
        /// </summary>
        private async Task PrepareAsync()
        {
            await FirstMarket.EnsureAsync(_gameState, _marketService, _partAdsService);

            var car = SelectedCar;
            if (!await Task.Run(() => _partAdsService.IsAvailable)) return;

            if (car != null) await _partsService.EnsurePartsAsync(car);
            _fitting = await Task.Run(() => _partAdsService.FindFittingParts(car));
        }

        private Car? SelectedCar =>
            _gameState.Player.Cars.FirstOrDefault(c => c.InstanceId == _gameState.Player.SelectedCarInstanceId);

        private void LoadAll()
        {
            LoadCarAds();
            LoadPartAds();
            OnPropertyChanged(nameof(EditionDisplay));
        }

        private PartsCatalog? Catalog => _partsService.IsAvailable ? _partsService.Catalog : null;

        /// <summary>
        /// The cars in the paper, in no order: mostly private people's and the racers' own, a few of the dealers' (a car or
        /// so off each lot and what they keep out back, <see cref="PaperAds.Listings"/>), the player's own in among them
        /// </summary>
        private void LoadCarAds()
        {
            try
            {
                var catalog = Catalog;
                var ads = new List<CarAdViewModel>();

                // A make's factory build is read from the database: once an opening, however many of its cars are in
                var factories = new Dictionary<string, IReadOnlySet<string>?>(StringComparer.OrdinalIgnoreCase);
                IReadOnlySet<string>? Factory(string carDefinitionId)
                {
                    if (!factories.TryGetValue(carDefinitionId, out var factory)) factories[carDefinitionId] = factory = _marketService.FactoryParts(carDefinitionId);
                    return factory;
                }

                // The player's own first: offered to the AI ahead of everybody else's ads (its desk takes so many at once)
                var mine = new List<(Guid, CarAdViewModel)>();
                foreach (var sale in _gameState.NewspaperAds.PlayerCars)
                {
                    try
                    {
                        if (PlayerCarAd(sale, catalog, Factory) is { } ad) mine.Add((sale.AdId, ad));
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning("Could not show the player's ad {AdId}: {Error}", sale.AdId, ex.Message);
                    }
                }

                foreach (var listing in PaperAds.Listings(_marketService.GetAvailableListings(_gameState.UsedCarMarket ?? []),
                             Rules.RuleBook.Current.Market.DealerAdsInPaper))
                {
                    _wroteBlurbs |= AdBlurbs.Ensure(listing, catalog, () => Factory(listing.CarDefinitionId));
                    // Words still the game's own go to the player's AI; they show the next time the paper is opened
                    AdBlurbs.Polish(listing, catalog, () => Factory(listing.CarDefinitionId));
                    if (listing.IsPrivate)
                    {
                        var name = Services.Talk.TalkPeople.SellerName(_gameState, Services.Talk.TalkPeople.CarKey(listing));
                        if (CarAd(listing, null, name, privateSeller: name) is { } own) ads.Add(own);
                        continue;
                    }

                    var dealer = _gameState.DealerLocations?.FirstOrDefault(d => d.Id == listing.DealerLocation);
                    if (CarAd(listing, null, dealer?.Name ?? GameText.Say("market.unknown_dealer")) is { } ad) ads.Add(ad);
                }

                foreach (var offer in RivalCarAds.Live(_gameState))
                {
                    try
                    {
                        _wroteBlurbs |= AdBlurbs.Ensure(offer.Ad, offer.Car, catalog, () => Factory(offer.Car.DefinitionId));
                        AdBlurbs.Polish(offer.Ad, offer.Car, catalog, () => Factory(offer.Car.DefinitionId));
                        if (CarAd(RivalCarAds.AsListing(offer, _marketService), offer, UsedCarListingViewModel.PrivateSellerName(offer.Seller.Name)) is { } ad)
                            ads.Add(ad);
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning("Could not show {Rival}'s ad: {Error}", offer.Seller.Name, ex.Message);
                    }
                }

                CarAds.Fill(ClassifiedOrder.Mix(ClassifiedOrder.Shuffled(ads, a => a.Car.Listing.Id), mine));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not lay out the car ads");
                CarAds.Fill([]);
            }

            OnPropertyChanged(nameof(HasCarAds));
            OnPropertyChanged(nameof(ShowNoCars));
        }

        /// <summary>
        /// The player's own ad, set like the others: its words written the first time it is shown (and offered to the
        /// player's AI; what it writes shows the next time the paper opens), the car as it stands in the garage
        /// </summary>
        private CarAdViewModel? PlayerCarAd(CarSaleAd sale, PartsCatalog? catalog, Func<string, IReadOnlySet<string>?> factoryOf)
        {
            if (_gameState.Player.Cars.FirstOrDefault(c => c.InstanceId == sale.CarInstanceId) is not { } car) return null;

            IReadOnlySet<string>? Factory() => factoryOf(car.DefinitionId);
            _wroteBlurbs |= AdBlurbs.Ensure(sale, car, catalog, Factory, () => _marketService.DescribeEngine(car));
            AdBlurbs.Polish(sale, car, catalog, Factory);

            // A listing to show it by, never put in the market; the ad's own price and words
            var listing = _marketService.ListCar(car, sale.AskingPrice, string.Empty, sale.PostedDate, describeEngine: false);
            listing.Id = sale.AdId.ToString();
            listing.Price = sale.AskingPrice;
            listing.PowerHp = car.PowerHp;
            listing.EngineSummary = sale.EngineSummary;
            listing.IsModified = sale.IsModified;
            listing.Description = sale.Description;
            return CarAd(listing, null, _gameState.Player.Name, sale);
        }

        /// <param name="privateSeller">The private person selling it, when it is no dealer's, rival's or the player's</param>
        private CarAdViewModel? CarAd(UsedCarListing listing, RivalCarAds.Offer? rivalOffer, string seller, CarSaleAd? playerAd = null,
            string? privateSeller = null)
        {
            var definition = _catalogRepository.GetCar(listing.CarDefinitionId);
            var profile = _profileRepository.GetProfile(listing.CarDefinitionId);
            if (definition == null || profile == null)
            {
                _logger.Warning("The catalog lacks car {CarId} of ad {ListingId}", listing.CarDefinitionId, listing.Id);
                return null;
            }

            var car = new UsedCarListingViewModel
            {
                Listing = listing,
                CarDefinition = definition,
                CarProfile = profile,
                RivalAd = rivalOffer?.Ad,
                PrivateSeller = rivalOffer?.Seller.Name ?? (playerAd != null ? seller : privateSeller),
                DealerName = seller,
                CanAfford = _gameState.Player.Money >= listing.Price
            };

            CarAdViewModel? ad = null;
            ad = new CarAdViewModel(car, new RelayCommand(() =>
            {
                if (ad!.IsPlayerAd) OnOpenPlayerAd(ad);
                else OnLookAtCar(ad);
            }), playerAd, _gameState.Date);
            return ad;
        }

        /// <summary>Every used part ad the catalog knows the part of, in no order</summary>
        private void LoadPartAds()
        {
            try
            {
                var ads = new List<PartAdViewModel>();
                if (Catalog is { } catalog)
                {
                    foreach (var ad in _gameState.NewspaperAds.Parts)
                    {
                        if (catalog.Get(ad.Part.DefinitionId) is not { } definition) continue;

                        _wroteBlurbs |= AdBlurbs.Ensure(ad, catalog);
                        // Words still the game's own go to the player's AI; they show the next time the paper is opened
                        AdBlurbs.Polish(ad, catalog);
                        PartAdViewModel? row = null;
                        row = new PartAdViewModel(ad, definition, _fitting.Contains(definition.Id), new RelayCommand(() => OnLookAtPart(row!)));
                        ads.Add(row);
                    }
                }

                PartAds.Fill(ClassifiedOrder.Shuffled(ads, a => a.Ad.AdId.ToString("N")));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not lay out the part ads");
                PartAds.Fill([]);
            }

            LoadKitAds();

            OnPropertyChanged(nameof(HasPartAds));
            OnPropertyChanged(nameof(ShowNoParts));
        }

        /// <summary>Every used kit whose parts the catalog all knows, in no order</summary>
        private void LoadKitAds()
        {
            KitAds.Clear();
            try
            {
                if (Catalog is not { } catalog) return;

                var rows = (_gameState.NewspaperAds.Kits ?? [])
                    .Where(ad => ad.Parts.Count > 0 && ad.Parts.All(p => catalog.Get(p.DefinitionId) != null))
                    .Select(ad =>
                    {
                        KitAdViewModel? row = null;
                        row = new KitAdViewModel(ad, new RelayCommand(() => OnBuyKit(row!)));
                        return row;
                    })
                    .ToList();
                foreach (var row in ClassifiedOrder.Shuffled(rows, r => r.Ad.AdId.ToString("N"))) KitAds.Add(row);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not lay out the kit ads");
                KitAds.Clear();
            }
        }

        /// <summary>A used kit's ad clicked: bought once the player says yes, onto the shelf in its crate</summary>
        private void OnBuyKit(KitAdViewModel row)
        {
            _dialogService.ShowDialog(new ConfirmationDialogViewModel(_dialogService,
                GameText.Say("paper.kit.confirm", ("kit", row.Name), ("price", new Money(row.Ad.AskingPrice)), ("seller", row.Ad.SellerName)),
                GameText.Say("paper.kit.confirm.title"),
                yes =>
                {
                    if (!yes) return;

                    if (!_partAdsService.BuyKit(_gameState, row.Ad))
                    {
                        var gone = !(_gameState.NewspaperAds.Kits ?? []).Contains(row.Ad);
                        _dialogService.ShowDialog(new InformationDialogViewModel(_dialogService,
                            GameText.Say(gone ? "usedparts.ad_gone" : "market.part.no_money"),
                            GameText.Say(gone ? "usedparts.ad_gone.title" : "market.part.no_money.title")));
                        if (gone) KitAds.Remove(row);
                    }
                    else
                    {
                        Audio.Sfx.Play(Audio.Sfx.Purchase);
                        KitAds.Remove(row);
                        Save("a kit bought from the paper");
                    }

                    OnPropertyChanged(nameof(ShowNoParts));
                }));
        }

        // ----- buying and looking -----

        private void OnBuyPart(PartAdViewModel ad)
        {
            if (!_partAdsService.BuyUsed(_gameState, ad.Ad))
            {
                var gone = !_gameState.NewspaperAds.Parts.Contains(ad.Ad);
                _dialogService.ShowDialog(new InformationDialogViewModel(_dialogService,
                    GameText.Say(gone ? "usedparts.ad_gone" : "market.part.no_money"),
                    GameText.Say(gone ? "usedparts.ad_gone.title" : "market.part.no_money.title")));
                if (gone) PartAds.Remove(ad);
            }
            else
            {
                Audio.Sfx.Play(Audio.Sfx.Purchase);
                PartAds.Remove(ad);
                Save("a part bought from the paper");
            }

            OnPropertyChanged(nameof(HasPartAds));
            OnPropertyChanged(nameof(ShowNoParts));
        }

        /// <summary>Go and see the car (clicking its ad): a screen of its own, the car at the seller's curb or on the lot</summary>
        private void OnLookAtCar(CarAdViewModel ad)
        {
            _navigationService.Leave(this, "car_ad", _gameState, new FlowArgs { Car = ad.Car });
        }

        /// <summary>Have a look at the part (clicking its ad): the part alone in 3D, turned with the mouse, with Buy</summary>
        private void OnLookAtPart(PartAdViewModel ad)
        {
            _navigationService.ShowPartPreview(ad.Ad.Part, ad.Name, ad.PriceDisplay, () => OnBuyPart(ad),
                () => Services.Talk.TalkDesk.Shared.ForPart(_gameState, ad.Ad, ad.Name), ad.RefreshPrice);
        }

        // ----- the player's own ads -----

        /// <summary>
        /// The player's own ad clicked: the ad, the car, and the buyer who called, to take, turn down or (the player's AI
        /// on) call back
        /// </summary>
        private void OnOpenPlayerAd(CarAdViewModel row)
        {
            var sale = row.PlayerAd!;
            if (_gameState.Player.Cars.FirstOrDefault(c => c.InstanceId == sale.CarInstanceId) is not { } car)
            {
                CarAds.Remove(row);
                OnPropertyChanged(nameof(HasCarAds));
                OnPropertyChanged(nameof(ShowNoCars));
                return;
            }

            var preview = AcCarFolder.PreviewImage(AppSettings.Instance.CarsPath, car.DefinitionId, car.SkinId);
            _dialogService.ShowDialog(new Dialogs.PlayerAd.PlayerAdDialogViewModel(_dialogService, _gameState, row, car, preview,
                () => Services.Talk.TalkDesk.Shared.ForBuyer(_gameState, sale, row.Car),
                ad => OnAcceptOffer(ad, row),
                ad => OnDeclineOffer(ad, row),
                () =>
                {
                    // A call may have raised the offer, or sent the buyer away
                    row.RefreshOffer();
                    Save("a call to a buyer");
                }));
        }

        private void OnAcceptOffer(CarSaleAd ad, CarAdViewModel row)
        {
            if (ad.Offer is not { } offer) return;

            _dialogService.ShowDialog(new ConfirmationDialogViewModel(_dialogService,
                GameText.Say("paper.sell.confirm", ("car", row.Car.DisplayName), ("buyer", offer.BuyerName), ("amount", new Money(offer.Amount))),
                GameText.Say("sell.title"),
                yes =>
                {
                    if (yes) SellToBuyer(ad, row);
                }));
        }

        /// <summary>The offer taken up: the car goes to the buyer, its ad out of the paper, and the player is told how it went</summary>
        private async void SellToBuyer(CarSaleAd ad, CarAdViewModel row)
        {
            // Nothing may escape an async void, the telling included
            try
            {
                SaleResult result;
                try
                {
                    result = await _saleService.AcceptOfferAsync(_gameState, ad);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Could not sell the car");
                    result = new SaleResult(SaleOutcome.Refused, GameText.Say("sell.failed", ("error", ex.Message)));
                }

                if (!_gameState.NewspaperAds.PlayerCars.Contains(ad))
                {
                    CarAds.Remove(row);
                    OnPropertyChanged(nameof(HasCarAds));
                    OnPropertyChanged(nameof(ShowNoCars));
                }
                else row.RefreshOffer();

                var message = result.SaveFailed ? GameText.Say("sell.save_failed", ("message", result.Message)) : result.Message;
                _dialogService.ShowDialog(new InformationDialogViewModel(_dialogService, message,
                    GameText.Say(result.Succeeded ? "sell.sold.title" : "sell.no_sale.title")));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not show how the sale went");
            }
        }

        private void OnDeclineOffer(CarSaleAd ad, CarAdViewModel row)
        {
            _saleService.DeclineOffer(_gameState, ad);
            row.RefreshOffer();
        }

        private void OnBack()
        {
            _navigationService.Leave(this, "back", _gameState);
        }

        private void Save(string what)
        {
            try
            {
                if (!string.IsNullOrEmpty(_gameState.SaveName)) _gameStateRepo.Save(_gameState, _gameState.SaveName);
                _wroteBlurbs = false;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not save the game after {What}", what);
            }
        }
    }
}
