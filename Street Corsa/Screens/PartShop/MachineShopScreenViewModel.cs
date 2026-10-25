using System.Globalization;
using StreetCorsa.Dialogs;
using StreetCorsa.Dialogs.Information;
using StreetCorsa.Logging;
using StreetCorsa.Models.GameState;
using StreetCorsa.Navigation;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Screens.Shared;
using StreetCorsa.Services.Parts;
using StreetCorsa.Services.PartShops;
using StreetCorsa.Services.Storage;
using StreetCorsa.Services.Talk;
using StreetCorsa.Services.Text;
using StreetCorsa.ViewModels;

namespace StreetCorsa.Screens.PartShop
{
    /// <summary>
    /// A machine shop the player drove out to from the part shops' map, seen from inside
    /// the door: the boring bar, the crank grinder, the head bench, and on them what the player has left here and other
    /// customers' work (<see cref="Room"/>). The counter lists what the player can bring (the garage's shelf, the engine
    /// on its stand) and has here, and for the one picked every job the shop does on it and the parts on it
    /// (<see cref="IMachineShopService"/>): the size it cuts to, the price, the days. Left here, a part is back in the
    /// garage on the morning it is ready. While the player's AI is on, the machinist talks them through it
    /// (<see cref="Clerk"/>, <see cref="IShopCounter"/> in the other half of this class): asked, they pick the work out
    /// of what the game offers, and the list shows it.
    /// </summary>
    public sealed partial class MachineShopScreenViewModel : BaseScreenViewModel, IShopCounter
    {
        public override string? Ambience => "ambience.machine_shop";

        /// <summary>Jobs the machinist is told about at most (the rules' <c>partShops.mostPicks</c>)</summary>
        private static int MostPicks => Rules.RuleBook.Current.PartShops.MostPicks;

        /// <summary>Whole engines run on the dyno for one ask at most, so an ask is answered in a moment (<c>partShops.machineDynoRuns</c>)</summary>
        private static int MostDynoRuns => Rules.RuleBook.Current.PartShops.MachineDynoRuns;

        /// <summary>Other customers' work standing round the shop, besides the player's (<c>partShops.othersWork</c>)</summary>
        private static int OthersWork => Rules.RuleBook.Current.PartShops.OthersWork;

        private readonly NavigationService _navigationService;
        private readonly DialogService _dialogService;
        private readonly GameState _gameState;
        private readonly string _shopId;
        private readonly IPartShopService _shops;
        private readonly ICarPartsService _partsService;
        private readonly IGameStateRepository _gameStateRepo;
        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger("MachineShop");

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
            Clerk = new ClerkTalk(_logger, "machinist");
            Room = new ShopRoomViewModel(Definition, gameState.Date, Clerk, scene);

            BackCommand = new RelayCommand(OnBack);
            ToggleCounterCommand = new RelayCommand(() => Room.IsCounterOpen = !Room.IsCounterOpen);
            OrderCommand = new RelayCommand<MachineJobViewModel>(OnOrder);
        }

        public PartShopDefinition Definition { get; }

        /// <summary>The room, its counter and what stands on the machines</summary>
        public ShopRoomViewModel Room { get; }

        /// <summary>The machinist, while the player's AI is on</summary>
        public ClerkTalk Clerk { get; }

        private PartShopLocation? Shop => PartShopRoster.Find(_gameState, _shopId);

        private IMachineShopService Machine => _shops.Machine;

        private PartsCatalog? Catalog => Room.Catalog;

        public string ShopName => Definition.Name.ToUpperInvariant();
        public string Region => Definition.Region;
        public string Blurb => Definition.Blurb;
        public string KindLabel => GameText.Say("partshop.kind.machine");

        #region What stands on the machines

        private void RefreshStockShown()
        {
            if (Catalog is not { } catalog)
            {
                Room.StockShown = [];
                return;
            }

            var shown = (_gameState.Player.MachineWork ?? [])
                .Where(o => string.Equals(o.ShopId, _shopId, StringComparison.OrdinalIgnoreCase) && catalog.Get(o.Part.DefinitionId) != null)
                .Select(o => new Controls.ShopStockPart(catalog.Get(o.Part.DefinitionId)!, o.Part))
                .Concat(OthersWorkOf(catalog))
                .ToList();
            if (Room.StockShown is { } before && before.Select(s => s.Tree).SequenceEqual(shown.Select(s => s.Tree))) return;
            Room.StockShown = shown;
        }

        // Drawn once a visit: the same other work stands about while the player is here
        private List<Controls.ShopStockPart>? _othersWork;

        /// <summary>Blocks, heads and cranks of the day's other customers: the same few on the same day</summary>
        private List<Controls.ShopStockPart> OthersWorkOf(PartsCatalog catalog)
        {
            if (_othersWork != null) return _othersWork;

            // A hash that is the same in every run of the game, on every machine, so the same day of the same save shows
            // the same work: the day's number, not the date as the machine's calendar writes it
            var day = DateOnly.FromDateTime(_gameState.Date).DayNumber.ToString(CultureInfo.InvariantCulture);
            var random = new Random((int)Helpers.StableHash.Of($"{_shopId.ToLowerInvariant()}|{day}"));
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
                _othersWork.Add(new Controls.ShopStockPart(pick));
            }

            return _othersWork;
        }

        #endregion

        #region The counter

        /// <summary>What the player has here and can bring: the parts at the shop first, the stand's engine, the shelf</summary>
        public BulkObservableCollection<MachinePieceViewModel> Pieces { get; } = [];

        private MachinePieceViewModel? _selectedPiece;

        // The pieces are being listed again: the list's own change of selection is taken, the jobs listed once after
        private bool _refilling;

        public MachinePieceViewModel? SelectedPiece
        {
            get => _selectedPiece;
            set
            {
                if (!SetProperty(ref _selectedPiece, value) || _refilling) return;
                FillJobs();
            }
        }

        /// <summary>The work the shop does on the picked piece and the parts on it, repairs before speed work</summary>
        public BulkObservableCollection<MachineJobViewModel> Jobs { get; } = [];

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
        public string Notice => PartShopNotice.Text(Shop, _gameState.Date);

        public bool HasNotice => Notice.Length > 0;

        /// <summary>The pieces as they are now, the one picked kept (else the first), and the jobs of the one picked listed once</summary>
        private void FillPieces()
        {
            var picked = SelectedPiece?.Piece.Part;
            var pieces = Shop is { } shop && Catalog is { } catalog
                ? Machine.Pieces(_gameState, shop).Select(p => new MachinePieceViewModel(p, catalog, _gameState.Date)).ToList()
                : [];
            var next = pieces.FirstOrDefault(p => ReferenceEquals(p.Piece.Part, picked)) ?? pieces.FirstOrDefault();

            // The list lets go of a piece that is not in it any more (the view model it had is replaced): taken as the
            // list's, not as a pick
            _refilling = true;
            try
            {
                Pieces.ReplaceAll(pieces);
            }
            finally
            {
                _refilling = false;
            }

            OnPropertyChanged(nameof(HasPieces));
            if (ReferenceEquals(_selectedPiece, next)) FillJobs();
            else SelectedPiece = next;
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
        /// pieces made here, at the shop's prices read here, and each job is put back on the player's own part after.
        /// Whatever fails (a saved tree the catalog can no longer copy) is logged, and the counter stops saying "pricing".
        /// </summary>
        private async Task PriceWorkAsync()
        {
            var run = ++_pricingRun;
            EndPricing();
            var cancel = _pricingCancel = new CancellationTokenSource();
            var work = new List<MachineWork>();
            try
            {
                if (Shop is { } shop && Catalog != null && Machine.QuoteAt(_gameState, shop) is { } quote)
                {
                    IsPricing = true;
                    var pieces = Machine.Pieces(_gameState, shop);
                    var copies = pieces.Select(p => (Piece: p, Copy: p with { Part = PartTrees.Clone(p.Part, keepIds: true) })).ToList();
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
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not tell what work the shop can do on the player's parts");
            }
            finally
            {
                // Not for a run another has taken over: that one says when the pricing is done, and holds the cancellation
                if (run == _pricingRun)
                {
                    IsPricing = false;
                    EndPricing();
                }
            }

            if (run != _pricingRun) return;
            _allWork = work;
            FillJobs();
            Clerk.StockChanged();
        }

        /// <summary>The pricing on its way is dropped (one done is let go of), and its cancellation with it</summary>
        private void EndPricing()
        {
            if (_pricingCancel is not { } cancel) return;

            _pricingCancel = null;
            cancel.Cancel();
            cancel.Dispose();
        }

        private void FillJobs()
        {
            var jobs = SelectedPiece is { } piece && Catalog is { } catalog
                ? _allWork.Where(w => ReferenceEquals(w.Piece.Part, piece.Piece.Part))
                    .Select(w => new MachineJobViewModel(w, catalog, _gameState.Player.Money, _picks.Contains(KeyOf(w)))).ToList()
                : [];
            Jobs.ReplaceAll(jobs);
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
            _ = ShowWorkAsync(job.Work.Offer.Job, job.Name, GameText.Say("machine.left", ("part", job.PartName), ("work", job.Name), ("day", order.ReadyOn)));
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

        private readonly JobShowPlayer _show = new();

        /// <summary>
        /// The machinist starts on the job just left: each of its steps' captions on the counter over its sound, a moment
        /// each, then <paramref name="done"/>. Only the showing: the job is ordered and paid for already.
        /// </summary>
        private async Task ShowWorkAsync(RebuildJob job, string name, string done)
        {
            var run = _show.Begin();
            try
            {
                foreach (var (caption, sound, start) in Garage.RebuildShow.StepsOf(job, name))
                {
                    if (!_show.IsCurrent(run)) return;

                    StatusText = caption;
                    _show.Step(sound, start);
                    await Task.Delay(ShowStepMs);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "The machinist's show stopped");
            }
            finally
            {
                if (_show.IsCurrent(run))
                {
                    _show.Stop(0.4f);
                    StatusText = done;
                }
            }
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

        public override async void Enter()
        {
            base.Enter();
            _logger.Information("Walked into {Shop}", Definition.Name);
            try
            {
                await Room.OpenAsync(_shops, _partsService, _gameState);
                if (!ReferenceEquals(_navigationService.CurrentScreen, this)) return;

                Refresh();
                if (Shop != null)
                    Clerk.Start(() => TalkDesk.Shared.ForMachineShop(_gameState, Definition.Id, Definition.Clerk, this), Definition.Clerk?.PortraitFile, Room.IsCounterOpen);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not open {Shop}", Definition.Name);
                StatusText = GameText.Say("partshop.open_failed");
                Room.StockShown ??= [];
            }
        }

        public override void Exit()
        {
            base.Exit();
            Clerk.End();

            // Nothing is priced, nor shown, once the player has gone: the parts are the garage's again
            _pricingRun++;
            EndPricing();
            _show.Cancel(0.2f);
        }

        private void OnBack()
        {
            if (Room.IsCounterOverRoom)
            {
                Room.IsCounterOpen = false;
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
