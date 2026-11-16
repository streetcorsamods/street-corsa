using System.Windows.Media;
using StreetCorsa.Helpers;
using StreetCorsa.Logging;
using StreetCorsa.Models.GameState;
using StreetCorsa.Screens.Shared;
using StreetCorsa.Services.Career;
using StreetCorsa.Services.Storage;
using StreetCorsa.Services.Text;
using StreetCorsa.ViewModels;

namespace StreetCorsa.Dialogs.Showroom
{
    /// <summary>
    /// Every car of the game on a card, by tier (<see cref="Services.Career.Showroom"/>): the open ones bright, the shut ones
    /// dimmed with the score their tier opens at, the ones in the garage marked. An open car is taken out to the garage
    /// here, one of each, once, as new: nothing is bought.
    /// </summary>
    public sealed class ShowroomDialogViewModel : BaseDialogViewModel
    {
        /// <summary>The tier picked to show all of them</summary>
        public const int AllTiers = 0;

        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger("Showroom");
        private readonly GameState _gameState;
        private readonly IGameStateRepository _repository;
        private readonly Func<List<ShowroomCar>> _list;
        private readonly Action? _carsChanged;
        private List<ShowroomCardViewModel> _all = [];
        private IReadOnlyList<ShowroomCardViewModel> _shown = [];
        private ShowroomCardViewModel? _selected;
        private int _tier = AllTiers;
        private string _searchText = string.Empty;
        private string _note = string.Empty;

        /// <param name="list">The cars as they stand now (<see cref="Services.Career.Showroom.Cars"/>)</param>
        /// <param name="carsChanged">Tells the screen under it a car came into the garage</param>
        public ShowroomDialogViewModel(DialogService dialogService, GameState gameState, IGameStateRepository repository, Func<List<ShowroomCar>> list,
            Action? carsChanged = null)
            : base(dialogService)
        {
            _carsChanged = carsChanged;
            _gameState = gameState;
            _repository = repository;
            _list = list;
            CloseCommand = new RelayCommand(() => Close());
            PickCommand = new RelayCommand<ShowroomCardViewModel>(card => Selected = card, card => card != null);
            TierCommand = new RelayCommand<TierFilter>(filter => Tier = filter?.Tier ?? AllTiers);
            TakeOutCommand = new RelayCommand(OnTakeOut, () => Selected is { Car.CanTake: true });
            Load();
        }

        public RelayCommand CloseCommand { get; }
        public RelayCommand<ShowroomCardViewModel> PickCommand { get; }
        public RelayCommand<TierFilter> TierCommand { get; }
        public RelayCommand TakeOutCommand { get; }

        /// <summary>The tiers to show, all of them first</summary>
        public IReadOnlyList<TierFilter> Tiers { get; private set; } = [];

        public IReadOnlyList<ShowroomCardViewModel> Cards
        {
            get => _shown;
            private set
            {
                _shown = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasCards));
                OnPropertyChanged(nameof(CountDisplay));
            }
        }

        public bool HasCards => Cards.Count > 0;

        public string CountDisplay => GameText.Say("showroom.count", ("open", _all.Count(c => c.Car.Open)), ("count", _all.Count));

        /// <summary>The banked score, as the tiers read it</summary>
        public string Banked => GameText.Say("challenges.banked",
            ("score", ChallengeCareer.CareerScore(_gameState.Career).ToString("N0", Locale.Culture)));

        public int Tier
        {
            get => _tier;
            set
            {
                if (_tier == value) return;
                _tier = value;
                foreach (var filter in Tiers) filter.IsSelected = filter.Tier == value;
                Refilter();
            }
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (_searchText == value) return;
                _searchText = value ?? string.Empty;
                OnPropertyChanged();
                Refilter();
            }
        }

        public ShowroomCardViewModel? Selected
        {
            get => _selected;
            set
            {
                if (ReferenceEquals(_selected, value)) return;
                if (_selected != null) _selected.IsSelected = false;
                _selected = value;
                if (_selected != null) _selected.IsSelected = true;
                OnPropertyChanged();
                Note = _selected?.Line ?? string.Empty;
                RelayCommand.RaiseCanExecuteChanged();
            }
        }

        /// <summary>The line by the button: the car picked, or what was just done</summary>
        public string Note
        {
            get => _note;
            private set
            {
                _note = value;
                OnPropertyChanged();
            }
        }

        private void Load()
        {
            var keep = Selected?.Car.Definition.Id;
            var score = ChallengeCareer.CareerScore(_gameState.Career);
            _all = _list().Select(car => new ShowroomCardViewModel(car, score)).ToList();
            Tiers = new[] { new TierFilter(AllTiers, GameText.Say("showroom.tier.all")) }
                .Concat(_all.Select(c => c.Car.Tier).Distinct().Order().Select(t => new TierFilter(t, GameText.Say("showroom.tier", ("tier", t)))))
                .ToList();
            foreach (var filter in Tiers) filter.IsSelected = filter.Tier == _tier;
            OnPropertyChanged(nameof(Tiers));
            OnPropertyChanged(nameof(Banked));
            _selected = null;
            Refilter();
            Selected = _all.FirstOrDefault(c => c.Car.Definition.Id == keep);
        }

        private void Refilter()
        {
            var query = _searchText.Trim();
            Cards = _all.Where(c => (_tier == AllTiers || c.Car.Tier == _tier)
                    && (query.Length == 0 || c.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                        || c.Car.Definition.Brand.Contains(query, StringComparison.CurrentCultureIgnoreCase)))
                .ToList();
            // A card the filter has hidden is not the one picked: Enter takes out what is in sight, or nothing
            if (_selected != null && !_shown.Contains(_selected)) Selected = null;
        }

        private void OnTakeOut()
        {
            if (Selected is not { } card) return;
            try
            {
                if (Services.Career.Showroom.TakeOut(_gameState, card.Car) == null) return;
                if (!string.IsNullOrEmpty(_gameState.SaveName)) _repository.Save(_gameState, _gameState.SaveName);
                Load();
                Note = GameText.Say("showroom.taken", ("car", card.Name));
                _carsChanged?.Invoke();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not take {Car} out of the showroom", card.Car.Definition.Id);
                Note = GameText.Say("showroom.failed", ("error", ex.Message));
            }
        }
    }

    /// <summary>One of the tier buttons over the cards</summary>
    public sealed class TierFilter(int tier, string label) : ObservableObject
    {
        private bool _isSelected;

        public int Tier { get; } = tier;
        public string Label { get; } = label;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>A car on its card: its picture, its name and tier, and whether it is open, shut or in the garage</summary>
    public sealed class ShowroomCardViewModel : ObservableObject
    {
        private const int PreviewDecodeWidth = 400;
        private ImageSource? _preview;
        private bool _previewRead;
        private bool _isSelected;

        public ShowroomCardViewModel(ShowroomCar car, double score)
        {
            Car = car;
            TierText = GameText.Say("showroom.tier", ("tier", car.Tier));
            Line = car.Owned ? GameText.Say("showroom.owned")
                : car.Taken ? GameText.Say("showroom.gone")
                : car.Open ? GameText.Say("showroom.open")
                : car.OpensAt is { } at ? GameText.Say("showroom.shut", ("score", at.ToString("N0", Locale.Culture)),
                    ("short", Math.Max(0, at - score).ToString("N0", Locale.Culture)))
                : GameText.Say("showroom.shut.later");
        }

        public ShowroomCar Car { get; }
        public string Name => string.IsNullOrWhiteSpace(Car.Definition.Name) ? Car.Definition.Id : Car.Definition.Name;
        public string TierText { get; }

        /// <summary>In the garage, open, or what opens it</summary>
        public string Line { get; }

        public bool IsOwned => Car.Owned;

        /// <summary>Shut ones dimmed, and one taken out before that is gone</summary>
        public double Opacity => Car.Open && (Car.Owned || !Car.Taken) ? 1 : 0.45;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                OnPropertyChanged();
            }
        }

        /// <summary>The car's picture, read the first time the card is shown</summary>
        public ImageSource? Preview
        {
            get
            {
                if (!_previewRead)
                {
                    _previewRead = true;
                    _ = ReadPreviewAsync();
                }

                return _preview;
            }
        }

        public bool HasNoPreview => _previewRead && _preview == null;

        private async Task ReadPreviewAsync()
        {
            var carsPath = Configuration.AppSettings.Instance.CarsPath;
            var id = Car.Definition.Id;
            _preview = await Thumbnails.LoadAsync(() => Parts.Export.AcCarFolder.PreviewImage(carsPath, id, Car.Definition.AvailableSkins.FirstOrDefault()),
                PreviewDecodeWidth, id);
            OnPropertyChanged(nameof(Preview));
            OnPropertyChanged(nameof(HasNoPreview));
        }
    }
}
