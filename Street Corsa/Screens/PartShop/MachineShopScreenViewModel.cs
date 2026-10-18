using System.Collections.ObjectModel;
using StreetCorsa.Audio;
using StreetCorsa.Controls;
using StreetCorsa.Dialogs;
using StreetCorsa.Dialogs.Information;
using StreetCorsa.Logging;
using StreetCorsa.Models.GameState;
using StreetCorsa.Navigation;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Screens.Shared;
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
    /// A machine shop the player drove out to from the part shops' map (roadmap step 30, phases 2 and 4), seen from inside
    /// the door: the boring bar, the crank grinder, the head bench, and on them what the player has left here and other
    /// customers' work. The counter lists what the player can bring (the garage's shelf, the engine on its stand) and has
    /// here, and for the one picked every job the shop does on it and the parts on it (<see cref="IMachineShopService"/>):
    /// the size it cuts to, the price, the days. Left here, a part is back in the garage on the morning it is ready. While
    /// the player's AI is on, the machinist talks them through it (<see cref="IShopCounter"/>): asked, they pick the work
    /// out of what the game offers, and the list shows it.
    /// </summary>
    public sealed class MachineShopScreenViewModel : BaseScreenViewModel, IShopCounter
    {
        public override string? Ambience => "ambience.machine_shop";

        /// <summary>Jobs the machinist is told about at most</summary>
        public const int MostPicks = 5;

        /// <summary>Whole engines run on the dyno for one ask at most, so an ask is answered in a moment</summary>
        public const int MostDynoRuns = 12;

        /// <summary>Other customers' work standing round the shop, besides the player's</summary>
        public const int OthersWork = 5;

        private readonly NavigationService _navigationService;
        private readonly DialogService _dialogService;
        private readonly GameState _gameState;
        private readonly string _shopId;
        private readonly IPartShopService _shops;
        private readonly ICarPartsService _partsService;
        private readonly IGameStateRepository _gameStateRepo;
        private readonly ShopScene? _scene;
        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger("MachineShop");

        // What a job does for a whole engine on the dyno, worked out once a visit per job, part and size
        private readonly Dictionary<(Guid Part, string Job, int Step), (double Before, double After)?> _power = new();

        public RelayCommand BackCommand { get; }
        public RelayCommand ToggleCounterCommand { get; }
        public RelayCommand<MachineJobViewModel> OrderCommand { get; }

        public MachineShopScreenViewModel(
            NavigationService navigationService,
            DialogService dialogService,
            GameState gameState,
            string shopId,
            IPartShopService shops,
            ICarPartsService partsService,
            IGameStateRepository gameStateRepo,
            ShopScene? scene = null)
        {
            _navigationService = navigationService;
            _dialogService = dialogService;
            _gameState = gameState;
            _shopId = shopId;
            _shops = shops;
            _partsService = partsService;
            _gameStateRepo = gameStateRepo;

            Definition = shops.Catalog.Get(shopId) ?? throw new InvalidOperationException($"No part shop \"{shopId}\"");
            var kind = Definition.KindOf.ToString().ToLowerInvariant();
            _scene = scene ?? ShopScenes.For(kind);

            BackCommand = new RelayCommand(OnBack);
            ToggleCounterCommand = new RelayCommand(() => IsCounterOpen = !IsCounterOpen);
            OrderCommand = new RelayCommand<MachineJobViewModel>(OnOrder);

            if (_scene != null)
            {
                var look = _scene.LookFor(Definition.Look, kind);
                var dressing = _scene.Dress(look, StreetLights.At(gameState.Date));
                Dressing = new LotDressing(dressing.Lighting, dressing.Sun, dressing.Pictures, dressing.Glow);
            }
        }

        public PartShopDefinition Definition { get; }

        private PartShopLocation? Shop => PartShopRoster.Find(_gameState, _shopId);

        private IMachineShopService Machine => _shops.Machine;

        public string ShopName => Definition.Name.ToUpperInvariant();
        public string Region => Definition.Region;
        public string Blurb => Definition.Blurb;
        public string KindLabel => GameText.Say("partshop.kind.machine");

        #region The room

        public string? SceneKn5 => _scene?.Kn5;

        public bool HasScene => _scene != null;

        public IReadOnlyList<ShopPlace>? Places => _scene?.Places;

        public ShopCamera? View => _scene?.Camera;

        public ShopRoom? Room => _scene?.Room;

        public LotDressing? Dressing { get; }

        private PartsCatalog? _catalog;

        public PartsCatalog? Catalog
        {
            get => _catalog;
            private set => SetProperty(ref _catalog, value);
        }

        private IReadOnlyList<ShopStockPart>? _stockShown;

        /// <summary>What stands on the machines: what the player has left here first, then other customers' work</summary>
        public IReadOnlyList<ShopStockPart>? StockShown
        {
            get => _stockShown;
            private set => SetProperty(ref _stockShown, value);
        }

        private void RefreshStockShown()
        {
            if (Catalog is not { } catalog)
            {
                StockShown = [];
                return;
            }

            var shown = (_gameState.Player.MachineWork ?? [])
                .Where(o => string.Equals(o.ShopId, _shopId, StringComparison.OrdinalIgnoreCase) && catalog.Get(o.Part.DefinitionId) != null)
                .Select(o => new ShopStockPart(catalog.Get(o.Part.DefinitionId)!, o.Part))
                .Concat(OthersWorkOf(catalog))
                .ToList();
            if (StockShown is { } before && before.Select(s => s.Tree).SequenceEqual(shown.Select(s => s.Tree))) return;
            StockShown = shown;
        }

        // Drawn once a visit: the same other work stands about while the player is here
        private List<ShopStockPart>? _othersWork;

        /// <summary>Blocks, heads and cranks of the day's other customers: the same few on the same day</summary>
        private List<ShopStockPart> OthersWorkOf(PartsCatalog catalog)
        {
            if (_othersWork != null) return _othersWork;

            // A hash that is the same in every run of the game, so the same day of the same save shows the same work
            var random = new Random((int)Helpers.StableHash.Of($"{_shopId.ToLowerInvariant()}|{_gameState.Date:yyyy-MM-dd}"));
            var kinds = catalog.Rebuilds.All.Where(j => j.IsMachine).SelectMany(j => j.Kinds).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var candidates = catalog.EngineBuilds.SelectMany(b => b.Parts).Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(catalog.Get).OfType<PartDefinition>()
                .Where(d => catalog.WorkOrder.KindOf(d) is { } k && kinds.Contains(k) && d.Model != null)
                .OrderBy(d => d.Id, StringComparer.Ordinal)
                .ToList();
            _othersWork = [];
            for (var i = 0; i < OthersWork && candidates.Count > 0; i++)
            {
                var pick = candidates[random.Next(candidates.Count)];
                candidates.Remove(pick);
                _othersWork.Add(new ShopStockPart(pick));
            }

            return _othersWork;
        }

        private bool _isCounterOpen;

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

        public bool ShowsCounterButton => HasScene && !IsCounterOpen;

        #endregion

        #region The counter

        /// <summary>What the player has here and can bring: the parts at the shop first, the stand's engine, the shelf</summary>
        public ObservableCollection<MachinePieceViewModel> Pieces { get; } = [];

        private MachinePieceViewModel? _selectedPiece;

        public MachinePieceViewModel? SelectedPiece
        {
            get => _selectedPiece;
            set
            {
                if (!SetProperty(ref _selectedPiece, value)) return;
                FillJobs();
            }
        }

        /// <summary>The work the shop does on the picked piece and the parts on it, repairs before speed work</summary>
        public ObservableCollection<MachineJobViewModel> Jobs { get; } = [];

        public bool HasPieces => Pieces.Count > 0;

        public bool HasJobs => Jobs.Count > 0;

        private string _statusText = string.Empty;
        public string StatusText
        {
            get => _statusText;
            private set => SetProperty(ref _statusText, value);
        }

        public string MoneyDisplay => GameText.Say("machine.money", ("money", new Helpers.Money(_gameState.Player.Money)));

        /// <summary>The closing-down sale, and what the player gets off as a regular</summary>
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

        private void FillPieces()
        {
            var picked = SelectedPiece?.Piece.Part;
            Pieces.Clear();
            if (Shop is { } shop && Catalog is { } catalog)
            {
                foreach (var piece in Machine.Pieces(_gameState, shop))
                    Pieces.Add(new MachinePieceViewModel(piece, catalog, _gameState.Date));
            }

            OnPropertyChanged(nameof(HasPieces));
            SelectedPiece = Pieces.FirstOrDefault(p => ReferenceEquals(p.Piece.Part, picked)) ?? Pieces.FirstOrDefault();
            if (SelectedPiece == null) FillJobs();
        }

        // Every job on what the player has, at the shop's prices: worked out off the UI thread after every change, as a
        // whole engine's go on the dyno for their price
        private List<MachineWork> _allWork = [];
        private Task _pricing = Task.CompletedTask;
        private int _pricingRun;

        private bool _isPricing;

        /// <summary>The work is being priced: the list shows what it had</summary>
        public bool IsPricing
        {
            get => _isPricing;
            private set => SetProperty(ref _isPricing, value);
        }

        private CancellationTokenSource? _pricingCancel;

        private void PriceWork() => _pricing = PriceWorkAsync();

        /// <summary>
        /// Prices every job on what the player has. The trees belong to the UI thread: the work is priced on copies of the
        /// pieces made here, at the shop's prices read here, and each job is put back on the player's own part after
        /// </summary>
        private async Task PriceWorkAsync()
        {
            var run = ++_pricingRun;
            _pricingCancel?.Cancel();
            var cancel = _pricingCancel = new CancellationTokenSource();
            var work = new List<MachineWork>();
            if (Shop is { } shop && Catalog != null && Machine.QuoteAt(_gameState, shop) is { } quote)
            {
                IsPricing = true;
                var pieces = Machine.Pieces(_gameState, shop);
                var copies = pieces.Select(p => (Piece: p, Copy: p with { Part = PartTrees.Clone(p.Part, keepIds: true) })).ToList();
                try
                {
                    var priced = await Task.Run(() => copies.Select(c =>
                    {
                        cancel.Token.ThrowIfCancellationRequested();
                        return (c.Piece, Work: Machine.Offers(quote, c.Copy));
                    }).ToList(), cancel.Token);
                    if (run != _pricingRun) return;

                    foreach (var (piece, offers) in priced)
                    {
                        var parts = piece.Part.SelfAndDescendants().ToDictionary(p => p.InstanceId);
                        foreach (var offer in offers)
                        {
                            if (parts.TryGetValue(offer.Part.InstanceId, out var part)) work.Add(new MachineWork(piece, part, offer.Offer));
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Could not tell what work the shop can do on the player's parts");
                }
            }

            if (run != _pricingRun) return;
            _allWork = work;
            IsPricing = false;
            FillJobs();
            Talk?.Talk.StockChanged();
        }

        private void FillJobs()
        {
            Jobs.Clear();
            if (SelectedPiece is { } piece && Catalog is { } catalog)
            {
                var money = _gameState.Player.Money;
                foreach (var work in _allWork.Where(w => ReferenceEquals(w.Piece.Part, piece.Piece.Part)))
                    Jobs.Add(new MachineJobViewModel(work, catalog, money, _picks.Contains(KeyOf(work))));
            }

            OnPropertyChanged(nameof(HasJobs));
        }

        private void OnOrder(MachineJobViewModel? job)
        {
            if (job is not { CanDo: true } || Shop is not { } shop) return;

            var order = Machine.Order(_gameState, shop, job.Work, Random.Shared);
            if (order == null)
            {
                var (message, title) = _gameState.Player.Money < job.Work.Offer.Cost
                    ? (GameText.Say("market.part.no_money"), GameText.Say("market.part.no_money.title"))
                    : (GameText.Say("machine.changed"), GameText.Say("machine.changed.title"));
                _dialogService.ShowDialog(new InformationDialogViewModel(_dialogService, message, title));
                Refresh();
                return;
            }

            Audio.Sfx.Play(Audio.Sfx.Purchase);
            TalkPeople.Happened(_gameState, new PersonEvent
            {
                Date = _gameState.Date,
                Kind = "machined",
                What = $"{job.Name} ({job.PartName})",
                Amount = job.Work.Offer.Cost
            }, TalkPeople.ShopKey(Definition.Id));
            Save();

            _picks.Clear();
            Refresh();
            _ = ShowWorkAsync(job.Work.Offer.Job, GameText.Say("machine.left", ("part", job.PartName), ("work", job.Name), ("day", order.ReadyOn)));
            // The piece is here now: it stays picked
            SelectedPiece = Pieces.FirstOrDefault(p => ReferenceEquals(p.Piece.Part, job.Work.Piece.Part)) ?? SelectedPiece;
        }

        private void Refresh()
        {
            OnPropertyChanged(nameof(Notice));
            OnPropertyChanged(nameof(HasNotice));
            OnPropertyChanged(nameof(MoneyDisplay));

            // What was priced is the parts as they were: none of it is offered, nor its power told, until they are priced again
            _allWork = [];
            _power.Clear();
            IsPricing = true;
            FillPieces();
            RefreshStockShown();
            PriceWork();
        }

        /// <summary>Real time each step of a job is shown as the machinist starts on it</summary>
        private const int ShowStepMs = 1100;

        private int _showRun;
        private PlayingSound? _showLoop;

        /// <summary>
        /// The machinist starts on the job just left: each of its steps' captions on the counter over its sound, a moment
        /// each, then <paramref name="done"/>. Only the showing: the job is ordered and paid for already.
        /// </summary>
        private async Task ShowWorkAsync(RebuildJob job, string done)
        {
            var run = ++_showRun;
            try
            {
                foreach (var step in job.Steps)
                {
                    if (run != _showRun) return;

                    StatusText = Rebuilding.CaptionOf(job, step);
                    AudioHub.Shared.StopLoop(_showLoop, 0.3f);
                    _showLoop = AudioHub.Shared.PlayLoop(string.IsNullOrWhiteSpace(step.Sound) ? Garage.RebuildShow.BenchSound : step.Sound!, 0.3f);
                    if (!string.IsNullOrWhiteSpace(step.Start)) Sfx.Play(step.Start!);
                    await Task.Delay(ShowStepMs);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning("The machinist's show stopped: {Error}", ex.Message);
            }
            finally
            {
                if (run == _showRun)
                {
                    AudioHub.Shared.StopLoop(_showLoop, 0.4f);
                    _showLoop = null;
                    StatusText = done;
                }
            }
        }

        private void EndShow()
        {
            _showRun++;
            AudioHub.Shared.StopLoop(_showLoop, 0.2f);
            _showLoop = null;
        }

        private void Save()
        {
            try
            {
                if (!string.IsNullOrEmpty(_gameState.SaveName)) _gameStateRepo.Save(_gameState, _gameState.SaveName);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to save game state after leaving work at the machine shop");
            }
        }

        #endregion

        #region The machinist

        private TalkViewModel? _talk;

        public TalkViewModel? Talk
        {
            get => _talk;
            private set
            {
                if (SetProperty(ref _talk, value)) OnPropertyChanged(nameof(HasTalk));
            }
        }

        public bool HasTalk => Talk != null;

        private void StartTalk()
        {
            if (Talk != null || Shop == null || !TalkDesk.Shared.IsAvailable) return;

            var talk = new TalkViewModel(TalkDesk.Shared.ForMachineShop(_gameState, Definition.Id, Definition.Clerk, this), Definition.Clerk?.PortraitFile)
            {
                IsOpen = IsCounterOpen
            };
            Talk = talk;
            _ = OpenTalkAsync(talk);
        }

        private async Task OpenTalkAsync(TalkViewModel talk)
        {
            await talk.OpenAsync();
            if (Talk != talk || talk.HasAnswered) return;

            _logger.Information("The machinist's AI did not answer: the counter goes on without the talk");
            talk.End();
            Talk = null;
        }

        // The jobs the machinist last picked out, by key: the list marks them
        private readonly HashSet<string> _picks = new(StringComparer.Ordinal);

        private static string KeyOf(MachineWork work) => $"{work.Part.InstanceId:N}:{work.Offer.Job.Id}:{work.Offer.Step}";

        /// <summary>Every job the shop could do on what the player has, at its prices as last worked out</summary>
        private List<MachineWork> AllWork() => _allWork.Where(w => w.Offer.CanDo).ToList();

        private string KindOf(MachineWork work) =>
            Catalog?.Get(work.Part.DefinitionId) is { } definition ? PartKinds.GroupName(PartKinds.GroupOf(definition)) : string.Empty;

        /// <summary>
        /// The kinds of part the shop works on among what the player has: read off the parts themselves, not the priced
        /// work, so an ask made while the work is still being priced knows them
        /// </summary>
        IReadOnlyList<string> IShopCounter.Kinds
        {
            get
            {
                if (Catalog is not { } catalog) return [];

                return Pieces.SelectMany(p => p.Piece.Part.SelfAndDescendants())
                    .Select(p => catalog.Get(p.DefinitionId)).OfType<PartDefinition>()
                    .Where(d => catalog.Rebuilds.For(d, catalog.WorkOrder.KindOf(d)).Any(j => j.IsMachine))
                    .Select(d => PartKinds.GroupName(PartKinds.GroupOf(d)))
                    .Where(k => k.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
            }
        }

        IEnumerable<string> IShopCounter.Facts()
        {
            yield return GameText.Say("chat.fact.machine_shop", ("shop", Definition.Name), ("region", Region), ("blurb", Blurb));
            if (Shop is not { } shop) yield break;

            if (shop.Status == DealerStatus.ClosingDown && shop.ClosesOn is { } last)
                yield return GameText.Say("chat.fact.shop_sale", ("discount", (int)Math.Round(DealerRules.DiscountOn(shop, _gameState.Date) * 100)), ("day", last));
            var regular = PartShopRules.RegularDiscountFor(shop.PlayerSpent);
            if (regular > 0) yield return GameText.Say("chat.fact.shop_regular", ("discount", (int)Math.Round(regular * 100)));
            yield return shop.PlayerSpent > 0 ? GameText.Say("chat.fact.shop_spent", ("spent", new Helpers.Money(shop.PlayerSpent))) : GameText.Say("chat.fact.shop_new_customer");

            var here = (_gameState.Player.MachineWork ?? []).Where(o => string.Equals(o.ShopId, _shopId, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var order in here)
                yield return GameText.Say("chat.fact.machine_here", ("part", NameOf(order.Part)), ("work", order.Work), ("day", order.ReadyOn));

            var brought = Machine.Pieces(_gameState, shop).Where(p => !p.AtShop).ToList();
            yield return brought.Count == 0
                ? GameText.Say("chat.fact.machine_nothing_brought")
                : GameText.Say("chat.fact.machine_brought", ("parts", string.Join(", ", brought.Take(12).Select(p => NameOf(p.Part)))));
        }

        private string NameOf(PartInstance part) =>
            Catalog?.Get(part.DefinitionId) is { } definition ? definition.DisplayName ?? definition.Name : part.DefinitionId;

        /// <summary>
        /// The machinist's search: of every job on what the player has, those of the kind of part asked about, under the
        /// price, by a name; the best few picked out (for power, what gains a whole engine most on the dyno, then the speed
        /// work; cheap, the cheapest; else repairs first, the cheapest) and marked in the list
        /// </summary>
        async Task<ClerkFindings> IShopCounter.FindAsync(ClerkSearch search, CancellationToken cancellationToken)
        {
            IsCounterOpen = true;
            await _pricing;
            var all = AllWork();
            var named = search.Words is { } words && all.Any(w => Matches(w, words)) ? words : null;
            var matching = all
                .Where(w => search.Kind == null || string.Equals(KindOf(w), search.Kind, StringComparison.OrdinalIgnoreCase))
                .Where(w => search.MaxPrice is not { } most || w.Offer.Cost <= most)
                .Where(w => named == null || Matches(w, named))
                .ToList();

            // A whole engine's work goes on the dyno, the cheapest first, so the machinist can say what it does
            if (Catalog is { } catalog && search.Want != ClerkSearch.Cheap)
            {
                // The engines are copied here, on the UI thread they belong to, and go on the dyno as copies
                var trying = matching.Where(w => IsEngine(catalog, w.Piece.Part) && !_power.ContainsKey(PowerKey(w)))
                    .OrderBy(w => w.Offer.Cost).Take(MostDynoRuns)
                    .Select(w => (Key: PowerKey(w), Work: w, Engine: PartTrees.Clone(w.Piece.Part, keepIds: true))).ToList();
                if (trying.Count > 0)
                {
                    var pricedAt = _pricingRun;
                    var found = await Task.Run(() => trying.Select(t => (t.Key, Gain: PowerWith(catalog, t.Work, t.Engine))).ToList(), cancellationToken);
                    // Not when the parts changed meanwhile: the gains are of the engine as it was
                    if (pricedAt == _pricingRun)
                    {
                        foreach (var (key, gain) in found) _power[key] = gain;
                    }
                }
            }

            double Gain(MachineWork w) => _power.GetValueOrDefault(PowerKey(w)) is { } p ? p.After - p.Before : 0;
            IEnumerable<MachineWork> ranked = search.Want switch
            {
                ClerkSearch.Power => matching.OrderByDescending(Gain).ThenByDescending(w => w.Offer.Job.Speed).ThenBy(w => w.Offer.Cost),
                ClerkSearch.Cheap => matching.OrderBy(w => w.Offer.Cost),
                _ => matching.OrderBy(w => w.Offer.Job.Speed).ThenBy(w => w.Offer.Cost)
            };

            var picks = ranked.Take(MostPicks).ToList();
            _picks.Clear();
            foreach (var pick in picks) _picks.Add(KeyOf(pick));

            // The first pick's piece shows, its jobs marked
            if (picks.FirstOrDefault() is { } first)
                SelectedPiece = Pieces.FirstOrDefault(p => ReferenceEquals(p.Piece.Part, first.Piece.Part)) ?? SelectedPiece;
            FillJobs();

            return new ClerkFindings(search with { Words = named, Fits = false }, matching.Count, picks.Select(PickOf).ToList(),
                search.Words != null && named == null ? search.Words : null);
        }

        ClerkFindings IShopCounter.Recheck(ClerkFindings found)
        {
            var now = AllWork().ToDictionary(KeyOf, StringComparer.Ordinal);
            var picks = found.Picks.Where(p => now.ContainsKey(p.Id)).Select(p => PickOf(now[p.Id])).ToList();
            return found with { Matching = Math.Max(picks.Count, found.Matching - (found.Picks.Count - picks.Count)), Picks = picks };
        }

        private bool Matches(MachineWork work, string words) =>
            NameOf(work.Part).Contains(words, StringComparison.OrdinalIgnoreCase) || work.Offer.Name.Contains(words, StringComparison.OrdinalIgnoreCase);

        private static (Guid, string, int) PowerKey(MachineWork work) => (work.Part.InstanceId, work.Offer.Job.Id, work.Offer.Step);

        private static bool IsEngine(PartsCatalog catalog, PartInstance part) =>
            catalog.Get(part.DefinitionId) is { } definition && PartKinds.GroupOf(definition) == PartGroups.Blocks;

        /// <summary>A pick as the machinist is told it: the job, the part it is on, the price, the days, what a whole engine gains</summary>
        private ClerkPick PickOf(MachineWork work)
        {
            var power = _power.GetValueOrDefault(PowerKey(work));
            return new ClerkPick(KeyOf(work), work.Offer.Name, NameOf(work.Part), work.Offer.Cost, work.Offer.Job.Speed, work.Offer.Days, power?.Before, power?.After);
        }

        /// <summary>What the job does for the whole engine on the dyno, measured on <paramref name="engine"/>, a copy of the piece nobody else has</summary>
        private (double Before, double After)? PowerWith(PartsCatalog catalog, MachineWork work, PartInstance engine)
        {
            try
            {
                if (EngineTuner.PowerOf(catalog, engine) is not { } before) return null;

                var part = engine.SelfAndDescendants().First(p => p.InstanceId == work.Part.InstanceId);
                Machining.Apply(catalog, work.Offer.Job, part, work.Offer.Job.AnySize ? work.Offer.Step : null);
                return EngineTuner.PowerOf(catalog, engine) is { } after ? (before, after) : null;
            }
            catch (Exception ex)
            {
                _logger.Warning("{Job} could not go on the dyno for the machinist: {Error}", work.Offer.Job.Id, ex.Message);
                return null;
            }
        }

        #endregion

        public override async void Enter()
        {
            base.Enter();
            _logger.Information("Walked into {Shop}", Definition.Name);
            try
            {
                if (await Task.Run(() => _shops.IsAvailable))
                {
                    PartShopRoster.Sync(_gameState, _shops.Catalog);
                    Catalog = await Task.Run(() => _partsService.IsAvailable ? _partsService.Catalog : null);
                }

                if (!ReferenceEquals(_navigationService.CurrentScreen, this)) return;

                Refresh();
                StartTalk();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not open {Shop}", Definition.Name);
                StatusText = GameText.Say("partshop.open_failed");
                StockShown ??= [];
            }
        }

        public override void Exit()
        {
            base.Exit();
            Talk?.End();

            // Nothing is priced, nor shown, once the player has gone: the parts are the garage's again
            _pricingRun++;
            _pricingCancel?.Cancel();
            EndShow();
        }

        private void OnBack()
        {
            if (_isCounterOpen && HasScene)
            {
                IsCounterOpen = false;
                return;
            }

            _navigationService.Leave(this, "back", _gameState);
        }
    }

    /// <summary>A piece the player has or can bring, as the counter lists it: its name, where it is, its shape and cuts</summary>
    public sealed class MachinePieceViewModel
    {
        public MachinePieceViewModel(MachinePiece piece, PartsCatalog catalog, DateTime today)
        {
            Piece = piece;
            var definition = catalog.Get(piece.Part.DefinitionId);
            Name = definition?.DisplayName ?? definition?.Name ?? piece.Part.DefinitionId;
            Group = definition == null ? string.Empty : PartKinds.GroupName(PartKinds.GroupOf(definition));
            Where = piece.Order is { } order
                ? GameText.Say(order.ReadyOn.Date <= today.Date.AddDays(1) ? "machine.where.here_tomorrow" : "machine.where.here", ("day", order.ReadyOn))
                : GameText.Say(piece.FromStand ? "machine.where.stand" : "machine.where.shelf");
            var cuts = Machining.Describe(catalog, piece.Part);
            Detail = string.Join("  ·  ", new[] { PartTrees.Describe(piece.Part) }.Concat(cuts));
        }

        public MachinePiece Piece { get; }
        public string Name { get; }
        public string Group { get; }
        public string Where { get; }
        public string Detail { get; }
        public bool AtShop => Piece.AtShop;
    }

    /// <summary>A job on a part of the picked piece, as the counter lists it: the work, the part, the price and the days, or why not</summary>
    public sealed class MachineJobViewModel
    {
        public MachineJobViewModel(MachineWork work, PartsCatalog catalog, decimal money, bool isPicked)
        {
            Work = work;
            var definition = catalog.Get(work.Part.DefinitionId);
            PartName = definition?.DisplayName ?? definition?.Name ?? work.Part.DefinitionId;
            CanAfford = work.Offer.CanDo && money >= work.Offer.Cost;
            IsPicked = isPicked;
        }

        public MachineWork Work { get; }

        public string Name => Work.Offer.Name;
        public string PartName { get; }
        public bool CanDo => Work.Offer.CanDo;
        public bool CanAfford { get; }
        public bool IsPicked { get; }
        public bool IsSpeed => Work.Offer.Job.Speed;

        public string PriceDisplay => CanDo ? Helpers.Money.Text(Work.Offer.Cost) : string.Empty;

        /// <summary>What it takes and brings, or why it cannot be done</summary>
        public string Detail => Work.Offer.WhyNot ?? string.Join("  ·  ", Lines());

        private IEnumerable<string> Lines()
        {
            yield return GameText.Say(Work.Offer.Days == 1 ? "machine.days.one" : "machine.days.other", ("count", Work.Offer.Days));
            if (Work.Offer.Job.Restores > 0) yield return GameText.Say("machine.after", ("condition", Work.Offer.After * 100));
            if (Work.Offer.Fitted.Count > 0) yield return GameText.Say("machine.fitted", ("parts", string.Join(", ", Work.Offer.Fitted)));
        }

        public string ActionLabel => GameText.Say("machine.leave_it");
    }
}
