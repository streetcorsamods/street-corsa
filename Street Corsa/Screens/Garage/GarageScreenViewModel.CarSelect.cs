using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using StreetCorsa.Configuration;
using StreetCorsa.Helpers;
using StreetCorsa.Models.Catalog;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Screens.Shared;
using StreetCorsa.Services.Career;
using StreetCorsa.Services.Text;
using StreetCorsa.ViewModels;

namespace StreetCorsa.Screens.Garage
{
    /// <summary>
    /// The garage laid out as a showroom to pick a car in (game.json's <c>garageLayout</c> <c>car_select</c>): the car
    /// turning on its own, its sheet at the right, and every car of the game on cards along the foot, the player's own
    /// first, then the showroom's by tier (<see cref="Showroom.Cars"/>), the shut ones dimmed with the score that opens
    /// them. A card of the player's own puts that car in the garage (<see cref="Services.CarLot.CarStorage.ToGarage"/>,
    /// as the car lot does, at once); one of the showroom's shows its car without taking it, to be taken out when it is
    /// open (<see cref="Showroom.TakeOut"/>).
    /// </summary>
    public partial class GarageScreenViewModel
    {
        private readonly Func<List<ShowroomCar>>? _showroomCars;
        private CarSelectCard? _pickedCard;
        private CarSheet? _sheet;
        private string _selectNote = string.Empty;

        /// <summary>The garage is the showroom to pick a car in, not the workshop</summary>
        public bool IsCarSelect { get; }

        public bool IsWorkshop => !IsCarSelect;

        /// <summary>Every car on its card: the player's own first, then the showroom's by tier and name</summary>
        public ObservableCollection<CarSelectCard> Strip { get; } = [];

        public RelayCommand<CarSelectCard> PickCardCommand { get; private set; } = null!;
        public RelayCommand TakeOutCommand { get; private set; } = null!;
        public AsyncRelayCommand DriveCommand { get; private set; } = null!;

        /// <summary>The card picked: the car on show</summary>
        public CarSelectCard? PickedCard
        {
            get => _pickedCard;
            private set
            {
                if (ReferenceEquals(_pickedCard, value)) return;
                if (_pickedCard != null) _pickedCard.IsSelected = false;
                _pickedCard = value;
                if (_pickedCard != null) _pickedCard.IsSelected = true;
                OnPropertyChanged(nameof(PickedCard));
                Sheet = value == null ? null : SheetOf(value);
                SelectNote = value?.Line ?? string.Empty;
                OnPropertyChanged(nameof(ViewCarDirectory));
                OnPropertyChanged(nameof(ViewSkinId));
                OnPropertyChanged(nameof(IsPickedOwned));
                OnPropertyChanged(nameof(IsPickedShowroom));
                RelayCommand.RaiseCanExecuteChanged();
            }
        }

        /// <summary>The picked car's sheet: its figures, its tier, its best run</summary>
        public CarSheet? Sheet
        {
            get => _sheet;
            private set
            {
                _sheet = value;
                OnPropertyChanged(nameof(Sheet));
            }
        }

        /// <summary>The line under the sheet's button: where the picked car stands, or what was just done</summary>
        public string SelectNote
        {
            get => _selectNote;
            private set
            {
                _selectNote = value;
                OnPropertyChanged(nameof(SelectNote));
            }
        }

        /// <summary>The picked car is the player's: it is the one in the garage, to drive</summary>
        public bool IsPickedOwned => PickedCard?.Owned != null;

        /// <summary>The picked car is the showroom's: to take out, or shut</summary>
        public bool IsPickedShowroom => PickedCard is { Owned: null };

        /// <summary>
        /// The car the 3D view stands up: the one in the garage, or, in the showroom layout, the showroom's car picked
        /// </summary>
        public string? ViewCarDirectory => PickedCard is { Owned: null } card
            ? Path.Combine(AppSettings.Instance.CarsPath, card.Definition.Id)
            : SelectedCar?.CarDirectory;

        public string? ViewSkinId => PickedCard is { Owned: null } card ? card.SkinId : SelectedCar?.SkinId;

        private void SetUpCarSelect()
        {
            PickCardCommand = new RelayCommand<CarSelectCard>(Pick, card => card != null && CanLeave() && !_launcher.IsExecutionLocked);
            TakeOutCommand = new RelayCommand(OnTakeOut, () => PickedCard is { CanTake: true } && CanLeave() && !_launcher.IsExecutionLocked);
            DriveCommand = new AsyncRelayCommand(OnDrive, () => IsPickedOwned && SelectedCarHere && CanLeave() && RacingHub != null, name: "Drive");
        }

        /// <summary>The racing hub the sheet's Drive opens, when the flow has one</summary>
        private string? RacingHub => _navigationService.Flow.FirstOfKind(Navigation.ScreenKinds.RacingHub);

        private Task OnDrive() => RacingHub is { } hub ? OnTileAsync(hub) : Task.CompletedTask;

        /// <summary>The cards made again from the cars as they stand, the car in the garage picked</summary>
        private void LoadStrip()
        {
            if (!IsCarSelect || _showroomCars == null) return;

            List<ShowroomCar> showroom;
            try
            {
                showroom = _showroomCars();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not list the showroom's cars");
                showroom = [];
            }

            var rules = Rules.RuleBook.Current.Unlocks;
            var score = ChallengeCareer.CareerScore(_gameState.Career);
            var byId = showroom.GroupBy(c => c.Definition.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var cards = new List<CarSelectCard>();
            foreach (var owned in Cars
                         .Select(car => (Car: car, Tier: byId.TryGetValue(car.CarDefinition.Id, out var s) ? s.Tier : Showroom.TierOf(car.CarDefinition, rules)))
                         .OrderBy(c => c.Tier)
                         .ThenBy(c => c.Car.DisplayName, StringComparer.CurrentCultureIgnoreCase))
            {
                cards.Add(CarSelectCard.Own(owned.Car, owned.Tier));
            }

            var ownedIds = Cars.Select(c => c.CarDefinition.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            cards.AddRange(showroom.Where(c => !ownedIds.Contains(c.Definition.Id)).Select(c => CarSelectCard.Shown(c, score)));

            var scale = CarSheet.Scale.Of(cards.Select(c => c.Definition));
            foreach (var card in cards) card.Scale = scale;

            var keep = PickedCard;
            _pickedCard = null;
            Strip.Clear();
            foreach (var card in cards) Strip.Add(card);

            // The car in the garage, or the showroom's car that was on show, or the first
            var pick = cards.FirstOrDefault(c => c.Owned != null && c.Owned.CarInstance.InstanceId == _gameState.Player.SelectedCarInstanceId)
                       ?? (keep is { Owned: null } ? cards.FirstOrDefault(c => c.Owned == null && c.Definition.Id == keep.Definition.Id) : null)
                       ?? cards.FirstOrDefault();
            foreach (var card in cards) card.IsInGarage = card.Owned != null && card.Owned.CarInstance.InstanceId == _gameState.Player.SelectedCarInstanceId;
            PickedCard = pick;
        }

        /// <summary>A card clicked: the player's car into the garage, or the showroom's car on show</summary>
        private void Pick(CarSelectCard? card)
        {
            if (card == null) return;
            try
            {
                if (card.Owned is { } owned)
                {
                    var fromShowroom = PickedCard is { Owned: null };
                    if (owned.CarInstance.InstanceId != _gameState.Player.SelectedCarInstanceId || !ReferenceEquals(SelectedCar, owned))
                    {
                        Services.CarLot.CarStorage.ToGarage(_gameState.Player, owned.CarInstance,
                            n => Services.CarLot.CarStorage.CapacityOf(Services.CarLot.WarehouseScenes.Installed, n));
                        if (!string.IsNullOrEmpty(_gameState.SaveName)) _gameStateRepo.Save(_gameState, _gameState.SaveName);
                        _logger.Information("Picked {Car} to drive", owned.CarDefinition.Id);
                        SelectedCar = owned;
                    }
                    else if (fromShowroom)
                    {
                        // Back from a showroom car to the one in the garage: its work and parts on show again
                        Workbench.SetCar(owned.CarInstance is { IsImpounded: false } car ? car : null);
                    }

                    foreach (var other in Strip) other.IsInGarage = ReferenceEquals(other, card);
                }
                else
                {
                    // Shown, not taken: the car in the garage is not worked on while another stands in its place
                    Workbench.SetCar(null);
                }

                PickedCard = card;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not pick {Car}", card.Definition.Id);
                SelectNote = GameText.Say("showroom.failed", ("error", ex.Message));
            }
        }

        private void OnTakeOut()
        {
            if (PickedCard is not { Showroom: { } car } card) return;
            try
            {
                if (Showroom.TakeOut(_gameState, car) == null) return;
                if (!string.IsNullOrEmpty(_gameState.SaveName)) _gameStateRepo.Save(_gameState, _gameState.SaveName);
                _logger.Information("Took {Car} out of the showroom", car.Definition.Id);
                // The cars anew: the one taken out is the player's now, in the garage, and picked
                _pickedCard = null;
                LoadPlayerCars();
                SelectNote = GameText.Say("showroom.taken", ("car", card.Name));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not take {Car} out of the showroom", car.Definition.Id);
                SelectNote = GameText.Say("showroom.failed", ("error", ex.Message));
            }
        }

        private CarSheet SheetOf(CarSelectCard card)
        {
            var id = card.Definition.Id;
            var best = (_gameState.Career.ScoredRuns ?? [])
                .Where(r => string.Equals(r.CarDefinitionId, id, StringComparison.OrdinalIgnoreCase))
                .Select(r => (double?)r.Score)
                .Max();
            return new CarSheet(card, best);
        }
    }

    /// <summary>
    /// A car on its card along the garage's foot: one of the player's, or one of the showroom's (open, shut, or taken
    /// out before and gone since)
    /// </summary>
    public sealed class CarSelectCard : ObservableObject
    {
        private const int PreviewDecodeWidth = 360;
        private ImageSource? _preview;
        private bool _previewRead;
        private bool _isSelected;
        private bool _isInGarage;

        private CarSelectCard(CarDefinition definition, int tier, CarDisplayViewModel? owned, ShowroomCar? showroom, string line, string skinId)
        {
            Definition = definition;
            Tier = tier;
            Owned = owned;
            Showroom = showroom;
            Line = line;
            SkinId = skinId;
            TierText = GameText.Say("showroom.tier", ("tier", tier));
        }

        /// <summary>The player's own car</summary>
        public static CarSelectCard Own(CarDisplayViewModel car, int tier) =>
            new(car.CarDefinition, tier, car, null, GameText.Say("showroom.owned"), car.SkinId);

        /// <summary>The showroom's car, not the player's</summary>
        public static CarSelectCard Shown(ShowroomCar car, double score) =>
            new(car.Definition, car.Tier, null, car,
                car.Taken ? GameText.Say("showroom.gone")
                : car.Open ? GameText.Say("showroom.open")
                : car.OpensAt is { } at ? GameText.Say("showroom.shut", ("score", at.ToString("N0", Locale.Culture)),
                    ("short", Math.Max(0, at - score).ToString("N0", Locale.Culture)))
                : GameText.Say("showroom.shut.later"),
                car.Definition.AvailableSkins.FirstOrDefault() ?? "default");

        public CarDefinition Definition { get; }
        public int Tier { get; }
        public string TierText { get; }

        /// <summary>The player's car, or null for the showroom's</summary>
        public CarDisplayViewModel? Owned { get; }

        /// <summary>The showroom's car, or null for the player's</summary>
        public ShowroomCar? Showroom { get; }

        public string SkinId { get; }

        /// <summary>Theirs, open, shut (and what opens it), or gone</summary>
        public string Line { get; }

        /// <summary>The showroom's car is open and there for the taking</summary>
        public bool CanTake => Showroom is { CanTake: true };

        /// <summary>Shut, or taken out before and gone since</summary>
        public bool IsShut => Showroom is { } car && !(car.Open && !car.Taken);

        public bool IsOwned => Owned != null;

        /// <summary>The tag over the picture: in the garage, the player's, the score that opens it, or gone</summary>
        public string Badge => IsInGarage ? GameText.Say("showroom.in_garage")
            : Owned != null ? GameText.Say("garage.select.yours")
            : Showroom is { Taken: true } ? GameText.Say("garage.select.gone")
            : Showroom is { Open: false, OpensAt: { } at } ? GameText.Say("garage.select.opens", ("score", at.ToString("N0", Locale.Culture)))
            : string.Empty;

        public bool HasBadge => Badge.Length > 0;

        public double Opacity => IsShut ? 0.45 : 1;

        /// <summary>The car's name, without its make when it starts with it ("Mazda MX5 ND" under "Mazda" is "MX5 ND")</summary>
        public string Name => NameOf(Definition);

        public static string NameOf(CarDefinition car)
        {
            var name = string.IsNullOrWhiteSpace(car.Name) ? car.Id : car.Name.Trim();
            var brand = car.Brand?.Trim() ?? string.Empty;
            return brand.Length > 0 && name.Length > brand.Length + 1 && name.StartsWith(brand + " ", StringComparison.CurrentCultureIgnoreCase)
                ? name[(brand.Length + 1)..].TrimStart()
                : name;
        }

        public string Brand => Definition.Brand;

        /// <summary>The highest figures of the cards: what the sheet's bars are measured against</summary>
        public CarSheet.Scale Scale { get; set; } = CarSheet.Scale.None;

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        /// <summary>The player's car that stands in the garage</summary>
        public bool IsInGarage
        {
            get => _isInGarage;
            set
            {
                if (!SetProperty(ref _isInGarage, value)) return;
                OnPropertyChanged(nameof(Badge));
                OnPropertyChanged(nameof(HasBadge));
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
            var carsPath = AppSettings.Instance.CarsPath;
            var id = Definition.Id;
            var skin = SkinId;
            var known = Owned?.PreviewImagePath;
            var first = Definition.AvailableSkins.FirstOrDefault();
            // The player's car's own picture, else its skin's, else the car's first skin's: a skin may come with none
            _preview = await Thumbnails.LoadAsync(() =>
                    !string.IsNullOrEmpty(known) && File.Exists(known) ? known
                    : Parts.Export.AcCarFolder.PreviewImage(carsPath, id, skin)
                      ?? (first != null ? Parts.Export.AcCarFolder.PreviewImage(carsPath, id, first) : null),
                PreviewDecodeWidth, id);
            OnPropertyChanged(nameof(Preview));
            OnPropertyChanged(nameof(HasNoPreview));
        }
    }

    /// <summary>
    /// The picked car's sheet at the right of the showroom: its name and year, its tier, its figures as its ui_car.json
    /// gives them with a bar each against the highest of the cards, and its best run for the score
    /// </summary>
    public sealed class CarSheet
    {
        public CarSheet(CarSelectCard card, double? best)
        {
            var car = card.Definition;
            Name = card.Name;
            Brand = car.Brand;
            Year = car.Year?.ToString(Locale.Culture) ?? string.Empty;
            TierText = card.TierText;
            var (hp, kg, kmh) = FiguresOf(car);
            var scale = card.Scale;
            Rows =
            [
                Row("garage.select.power", hp is { } p ? Measure.Power(p).ToString() : null, hp, scale.Power),
                Row("garage.select.weight", kg is { } w ? Measure.Mass(w).ToString("N0", null) : null, kg, scale.Weight),
                Row("garage.select.ratio", hp is { } ph && kg is { } wk ? Measure.PowerToWeight(ph / wk * 1000).ToString() : null,
                    hp / kg * 1000, scale.Ratio),
                Row("garage.select.top_speed", kmh is { } s ? Measure.Speed(s).ToString() : null, kmh, scale.Speed)
            ];
            Best = best is { } b
                ? GameText.Say("garage.select.best", ("score", b.ToString("N0", Locale.Culture)))
                : GameText.Say("garage.select.no_runs");
        }

        public string Name { get; }
        public string Brand { get; }
        public string Year { get; }
        public string TierText { get; }
        public IReadOnlyList<SheetRow> Rows { get; }
        public string Best { get; }

        private static SheetRow Row(string key, string? value, double? figure, double most) =>
            new(GameText.Say(key), value ?? GameText.Say("garage.select.unknown"),
                figure is { } f && most > 0 && double.IsFinite(f) ? Math.Clamp(f / most, 0.04, 1) : 0);

        /// <summary>A car's power (hp), weight (kg) and top speed (km/h) as its ui_car.json gives them, each when it does</summary>
        public static (double? Hp, double? Kg, double? Kmh) FiguresOf(CarDefinition car) =>
            (AcSpecs.ParsePower(car.Specs?.Bhp), AcSpecs.ParseWeight(car.Specs?.Weight), AcSpecs.ParseSpeed(car.Specs?.TopSpeed));

        /// <summary>The highest of each figure among the cards, the bars' full length</summary>
        public sealed record Scale(double Power, double Weight, double Ratio, double Speed)
        {
            public static readonly Scale None = new(0, 0, 0, 0);

            public static Scale Of(IEnumerable<CarDefinition> cars)
            {
                double power = 0, weight = 0, ratio = 0, speed = 0;
                foreach (var car in cars)
                {
                    var (hp, kg, kmh) = FiguresOf(car);
                    if (hp is { } p) power = Math.Max(power, p);
                    if (kg is { } w) weight = Math.Max(weight, w);
                    if (hp is { } ph && kg is { } wk && wk > 0) ratio = Math.Max(ratio, ph / wk * 1000);
                    if (kmh is { } s) speed = Math.Max(speed, s);
                }

                return new Scale(power, weight, ratio, speed);
            }
        }
    }

    /// <summary>One figure on the sheet: its name, its value in the player's units, and its bar's share of the full length</summary>
    public sealed record SheetRow(string Label, string Value, double Fraction);
}
