using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using StreetCorsa.Audio;
using StreetCorsa.Configuration;
using StreetCorsa.Controls.Street;
using StreetCorsa.Dialogs;
using StreetCorsa.Dialogs.Information;
using StreetCorsa.Helpers;
using StreetCorsa.Logging;
using StreetCorsa.Looks;
using StreetCorsa.Models.GameState;
using StreetCorsa.Navigation;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Screens.Shared;
using StreetCorsa.Services.Catalog;
using StreetCorsa.Services.PaintBooth;
using StreetCorsa.Services.Parts;
using StreetCorsa.Services.Storage;
using StreetCorsa.Services.Street;
using StreetCorsa.Services.Text;
using StreetCorsa.Services.Time;
using StreetCorsa.ViewModels;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;

namespace StreetCorsa.Screens.PaintBooth
{
    /// <summary>
    /// The paint booth: the car rolls in from the garage (pushed in, one that will not run), and the player picks what it
    /// is painted, seeing it on the car as they pick: another of its make's colours (its mod's skins), or a colour of
    /// their own over one of them where its paint can take it (<see cref="CarPaint"/>). The job costs money and half a
    /// day (<see cref="Rules.RepairRules.ColourChangeCost"/>, <see cref="GameAction.PaintJob"/>): the painter goes round the
    /// car with his gun, the new colour going on coat by coat, the old paint's wear and the dirt gone with it. Then the
    /// screen fades home, where the car comes into the garage in its new colour. The player may leave without a job.
    /// </summary>
    public class PaintBoothScreenViewModel : DriveInScreenViewModel
    {
        /// <summary>The booth's fans, the shop beyond</summary>
        public override string? Ambience => "ambience.booth";

        // The coats the paint goes on in, over the painter's way round the car, and how long the car is admired after
        private const int Coats = 8;
        private static readonly TimeSpan Admire = TimeSpan.FromSeconds(2.5);

        // A colour picked by hand is put on the car once the hand has rested this long
        private static readonly TimeSpan PreviewDelay = TimeSpan.FromMilliseconds(350);

        /// <summary>The colours of the day a player is offered first, as the makers called theirs</summary>
        private static readonly (string Key, string Hex)[] PeriodColours =
        [
            ("booth.colour.red", "#9E1B1B"), ("booth.colour.orange", "#D9531E"), ("booth.colour.yellow", "#E8C21A"), ("booth.colour.lime", "#8DB33A"),
            ("booth.colour.green", "#1F5F3A"), ("booth.colour.turquoise", "#2A9DA3"), ("booth.colour.lightblue", "#5B8FC7"), ("booth.colour.blue", "#1E3F8A"),
            ("booth.colour.purple", "#5B2A6E"), ("booth.colour.pink", "#D98AA6"), ("booth.colour.cream", "#EDE3C8"), ("booth.colour.white", "#F2F0E6"),
            ("booth.colour.silver", "#A9ADB0"), ("booth.colour.gold", "#B8913A"), ("booth.colour.brown", "#5A3B26"), ("booth.colour.black", "#121214")
        ];

        private readonly NavigationService _navigationService;
        private readonly DialogService _dialogService;
        private readonly GameState _gameState;
        private readonly IContentCatalogRepository _catalogRepository;
        private readonly IGameTimeService _timeService;
        private readonly IGameStateRepository _gameStateRepo;
        private readonly ICarPartsService _partsService;
        private readonly IAppLogger _logger;
        private readonly DispatcherTimer _previewTimer;

        private Car? _car;
        private string _carDirectory = "";
        private PartsCatalog? _catalog;
        private bool _leaving;
        private int _tintCheck;

        public PaintBoothScreenViewModel(
            NavigationService navigationService,
            DialogService dialogService,
            GameState gameState,
            IContentCatalogRepository catalogRepository,
            IGameTimeService timeService,
            IGameStateRepository gameStateRepo,
            ICarPartsService partsService)
            : base(gameState, new DriveInStage(), AppLoggerFactory.CreateLogger("PaintBooth"))
        {
            _navigationService = navigationService;
            _dialogService = dialogService;
            _gameState = gameState;
            _catalogRepository = catalogRepository;
            _timeService = timeService;
            _gameStateRepo = gameStateRepo;
            _partsService = partsService;
            _logger = AppLoggerFactory.CreateLogger("PaintBooth");

            _previewTimer = new DispatcherTimer { Interval = PreviewDelay };
            _previewTimer.Tick += (_, _) =>
            {
                _previewTimer.Stop();
                Preview();
            };

            SprayCommand = new AsyncRelayCommand(SprayAsync, CanSpray, name: "Spray");
            LeaveCommand = new RelayCommand(Leave, () => !_leaving && !IsSpraying);
            ResetCommand = new RelayCommand(() => Pick(_car?.SkinId, PaintColour.Parse(_car?.PaintColour)), () => IsPicking);
            Stage.Arrived += OnArrived;
        }

        #region What the view shows

        public PaintBoothScene? Booth { get; private set; }

        public string Title => Booth?.Title ?? GameText.Say("booth.ui.title");

        /// <summary>Indoors, the same light at every hour</summary>
        public StreetLight Light => StreetLight.Day;

        public StreetCar? PlayerCar { get; private set; }

        public PaintJobShow Show { get; } = new();

        private string _statusText = "";

        public string StatusText
        {
            get => _statusText;
            private set => SetProperty(ref _statusText, value);
        }

        private bool _isPicking;

        /// <summary>The car stands in the booth and the player picks its colour</summary>
        public bool IsPicking
        {
            get => _isPicking;
            private set
            {
                if (SetProperty(ref _isPicking, value)) RaiseAll();
            }
        }

        private bool _isSpraying;

        public bool IsSpraying
        {
            get => _isSpraying;
            private set
            {
                if (SetProperty(ref _isSpraying, value)) RaiseAll();
            }
        }

        /// <summary>The car's mod's skins, the one picked marked</summary>
        public ObservableCollection<BoothSkin> Skins { get; } = [];

        /// <summary>The colours of the day, to start from</summary>
        public ObservableCollection<BoothSwatch> Swatches { get; } = [];

        private bool _canTint;

        /// <summary>The picked skin's paint can take a colour of the player's own</summary>
        public bool CanTint
        {
            get => _canTint;
            private set => SetProperty(ref _canTint, value);
        }

        private string _tintNote = "";

        /// <summary>Why it cannot, or that it is being looked at</summary>
        public string TintNote
        {
            get => _tintNote;
            private set => SetProperty(ref _tintNote, value);
        }

        private string? _skin;
        private PaintColour? _colour;

        /// <summary>The picked colour of the player's own; null for the skin's own colours</summary>
        public PaintColour? Colour => _colour;

        public bool HasColour => _colour != null;

        public SolidColorBrush ColourBrush => _colour is { } c ? new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B)) : Brushes.Transparent;

        // The colour by hand: hue (degrees), saturation and brightness (0..1)
        private double _hue = 210, _saturation = 0.7, _brightness = 0.6;

        public double Hue
        {
            get => _hue;
            set
            {
                if (SetProperty(ref _hue, value)) MixedByHand();
            }
        }

        public double Saturation
        {
            get => _saturation;
            set
            {
                if (SetProperty(ref _saturation, value)) MixedByHand();
            }
        }

        public double Brightness
        {
            get => _brightness;
            set
            {
                if (SetProperty(ref _brightness, value)) MixedByHand();
            }
        }

        /// <summary>What the job is, in words: the skin, and the colour over it</summary>
        public string ChoiceText => _car == null ? ""
            : _colour is { } c ? GameText.Say("booth.ui.choice.custom", ("skin", SkinName(_skin)), ("colour", c.Hex))
            : GameText.Say("booth.ui.choice.factory", ("skin", SkinName(_skin)));

        /// <summary>The picked job differs from the car as it is</summary>
        public bool IsChange => _car != null && (!string.Equals(_skin, _car.SkinId, StringComparison.OrdinalIgnoreCase) || _colour != PaintColour.Parse(_car.PaintColour));

        public decimal Price => _car == null ? 0
            : PartPricing.Round((double)(Rules.RuleBook.Current.Repairs.ColourChangeCost + (_colour != null ? Rules.RuleBook.Current.Repairs.CustomColourCost : 0))
                                * GameRules.Sane(_gameState.Rules.PartPriceMultiplier));

        public string PriceText => GameText.Say("booth.ui.price", ("cost", new Money(Price)), ("time", GameAction.PaintJob.GetTimeDescription()));

        public AsyncRelayCommand SprayCommand { get; }

        public RelayCommand LeaveCommand { get; }

        public RelayCommand ResetCommand { get; }

        #endregion

        #region Coming and going

        public override void Enter()
        {
            base.Enter();
            _logger.Information("Went to the paint booth");
            IsActive = true;

            _car = _gameState.Player.Cars.FirstOrDefault(c => c.InstanceId == _gameState.Player.SelectedCarInstanceId);
            var definition = _car == null ? null : _catalogRepository.GetCar(_car.DefinitionId);
            if (_car == null || definition == null)
            {
                SendHome(GameText.Say("booth.ui.no_car"), GameText.Say("booth.ui.no_car.title"));
                return;
            }

            if (_car.IsImpounded)
            {
                SendHome(GameText.Say("booth.ui.impounded"), GameText.Say("booth.ui.impounded.title"));
                return;
            }

            if (PaintFirst.WhyNot(_car) is { } why)
            {
                SendHome(why, PaintFirst.Title);
                return;
            }

            Booth = PaintBooths.Installed.FirstOrDefault();
            if (Booth == null)
            {
                SendHome(GameText.Say("booth.ui.none"), GameText.Say("booth.ui.none.title"));
                return;
            }

            _catalog = _partsService.IsAvailable ? _partsService.Catalog : null;
            _carDirectory = Path.Combine(AppSettings.Instance.CarsPath, _car.DefinitionId);
            _skin = string.IsNullOrEmpty(_car.SkinId) ? null : _car.SkinId;
            _colour = PaintColour.Parse(_car.PaintColour);

            foreach (var skin in definition.AvailableSkins ?? []) Skins.Add(new BoothSkin(skin, SkinName(skin), PreviewOf(skin), new RelayCommand(() => Pick(skin, null), () => IsPicking)));
            foreach (var (key, hex) in PeriodColours)
            {
                var colour = PaintColour.Parse(hex)!.Value;
                Swatches.Add(new BoothSwatch(GameText.Say(key), new SolidColorBrush(Color.FromRgb(colour.R, colour.G, colour.B)),
                    new RelayCommand(() => Pick(_skin, colour), () => IsPicking && CanTint)));
            }
            MarkPicked();

            Stage.Time = _gameState.Date;
            PlayerCar = new StreetCar(_carDirectory, _skin, _partsService.Shown(_car));
            OnPropertyChanged(nameof(Booth));
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(PlayerCar));
            PlayerEngine.Place(null, StreetStage.OwnEngineVolume);

            // A car that runs rolls in on its own; one that will not is pushed in, and stands there from the start
            var problems = DrivingHome.WhyWontRun(_car, _catalog);
            _ = StartOwnEngineAsync(_partsService, _catalogRepository, _car, running: problems.Count == 0);
            CheckTint();
            if (problems.Count == 0)
            {
                StatusText = GameText.Say("booth.ui.rolling_in");
                ComeInOnceShown(() => Booth?.HasModel == true, () => Stage.Arrive(PlayerEngine, false));
            }
            else
            {
                StatusText = GameText.Say("booth.ui.pushed_in");
                StandThereAtOnce();
                OnArrived();
            }
        }

        public override void Exit()
        {
            base.Exit();
            _logger.Information("Left the paint booth");
            _previewTimer.Stop();
            StopSprayLoop();
            EndVisit();
        }

        private void SendHome(string message, string title)
        {
            _leaving = true;
            _dialogService.ShowDialog(new InformationDialogViewModel(_dialogService, message, title));
            _navigationService.GoHomeLater(_gameState);
        }

        private void OnArrived()
        {
            if (!IsActive || _car == null || IsPicking) return;
            _ = Dispatcher.CurrentDispatcher.BeginInvoke(() => _navigationService.ScreenReady(this));

            // Nobody paints a running car
            if (PlayerEngine.IsRunning) PlayerEngine.Toggle();
            StatusText = GameText.Say("booth.ui.pick");
            IsPicking = true;
        }

        private void Leave()
        {
            if (_leaving || IsSpraying) return;
            _ = GoHomeAsync();
        }

        /// <summary>The curtain comes down and the player is home, where the car drives into the garage (or is simply there, pushed back in)</summary>
        private async Task GoHomeAsync()
        {
            _leaving = true;
            IsPicking = false;
            RaiseAll();
            try
            {
                Save();
                var home = new FlowArgs { Arrival = _car == null ? null : DrivingHome.Arrival(_gameState, OwnEngine, _catalog) };
                if (await _navigationService.LeaveAsync(this, "back", _gameState, home, first: CloseCurtain)) return;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not go home from the paint booth");
            }

            // The garage would not open: back in the booth to try again
            OpenCurtain?.Invoke();
            _leaving = false;
            IsPicking = !IsSpraying;
            StatusText = GameText.Say("booth.ui.still_here");
            RaiseAll();
        }

        #endregion

        #region Picking

        /// <summary>The player picks the skin <paramref name="skin"/>, painted <paramref name="colour"/> over it (null: in its own colours)</summary>
        private void Pick(string? skin, PaintColour? colour)
        {
            if (!IsPicking) return;
            var skinChanged = !string.Equals(skin, _skin, StringComparison.OrdinalIgnoreCase);
            _skin = skin;
            _colour = colour;
            if (colour is { } c) (_hue, _saturation, _brightness) = ToHsv(c);
            OnPropertyChanged(nameof(Hue));
            OnPropertyChanged(nameof(Saturation));
            OnPropertyChanged(nameof(Brightness));
            MarkPicked();
            if (skinChanged) CheckTint();
            _previewTimer.Stop();
            Preview();
        }

        /// <summary>A slider moved: the colour mixed by hand, put on the car once the hand rests</summary>
        private void MixedByHand()
        {
            if (!IsPicking || !CanTint) return;
            _colour = FromHsv(_hue, _saturation, _brightness);
            MarkPicked();
            _previewTimer.Stop();
            _previewTimer.Start();
        }

        /// <summary>The car as it would be painted, on the booth's car</summary>
        private void Preview()
        {
            if (_car == null) return;
            Show.Wear(_skin, ShownAs(_skin, _colour));
        }

        /// <summary>What the car shows painted so: its parts and how worn it looks, a fresh paint job taking the paint's wear</summary>
        private ShownSet? ShownAs(string? skin, PaintColour? colour, bool fresh = false)
        {
            if (_car == null) return null;
            var look = CarLooks.Of(_car);
            if (fresh && look != null) look = look with { Paint = 0, Dirt = 0 };
            return _partsService.Shown(_car.DefinitionId, _car.Parts, look, CornerHeights.Of(CarLooks.RideHeight(_car)), colour);
        }

        private void MarkPicked()
        {
            foreach (var skin in Skins) skin.IsPicked = _colour == null && string.Equals(skin.Id, _skin, StringComparison.OrdinalIgnoreCase);
            foreach (var swatch in Swatches) swatch.IsPicked = false;
            OnPropertyChanged(nameof(Colour));
            OnPropertyChanged(nameof(HasColour));
            OnPropertyChanged(nameof(ColourBrush));
            OnPropertyChanged(nameof(ChoiceText));
            OnPropertyChanged(nameof(IsChange));
            OnPropertyChanged(nameof(Price));
            OnPropertyChanged(nameof(PriceText));
            RaiseAll();
        }

        /// <summary>Whether the picked skin's paint can take a colour of the player's own: looked at off the UI thread, once a car and skin</summary>
        private async void CheckTint()
        {
            var check = ++_tintCheck;
            var (directory, skin) = (_carDirectory, _skin);
            CanTint = false;
            TintNote = GameText.Say("booth.ui.tint.looking");
            bool can;
            try
            {
                can = await Task.Run(() => CarPaint.CanTint(directory, skin));
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Could not look at the paint of {Car}", directory);
                can = false;
            }
            if (check != _tintCheck || !IsActive) return;

            CanTint = can;
            TintNote = can ? "" : GameText.Say("booth.ui.tint.cannot");
            if (!can && _colour != null)
            {
                // A colour of the player's own does not take on this skin: its own colours
                _colour = null;
                MarkPicked();
                Preview();
            }
            RaiseAll();
        }

        private string SkinName(string? skin)
        {
            if (string.IsNullOrEmpty(skin)) return GameText.Say("booth.ui.skin.own");
            try
            {
                var file = Path.Combine(_carDirectory, "skins", skin, "ui_skin.json");
                if (File.Exists(file))
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
                    if (document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("skinname", out var name)
                        && name.ValueKind == JsonValueKind.String && name.GetString()?.Trim() is { Length: > 0 } named
                        && !named.Equals("undefined", StringComparison.OrdinalIgnoreCase))
                        return named;
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                // A mod's file that cannot be read: the folder's name
            }
            return skin.Replace('_', ' ');
        }

        private ImageSource? PreviewOf(string skin)
        {
            var file = Path.Combine(_carDirectory, "skins", skin, "preview.jpg");
            if (!File.Exists(file)) return null;
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = 200;
                image.UriSource = new Uri(file);
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                return null;
            }
        }

        private static (double Hue, double Saturation, double Brightness) ToHsv(PaintColour colour)
        {
            var (r, g, b) = colour.Unit;
            var max = Math.Max(r, Math.Max(g, b));
            var min = Math.Min(r, Math.Min(g, b));
            var delta = max - min;
            double hue = delta == 0 ? 0 : max == r ? 60 * (((g - b) / delta % 6 + 6) % 6) : max == g ? 60 * ((b - r) / delta + 2) : 60 * ((r - g) / delta + 4);
            return (hue, max == 0 ? 0 : delta / max, max);
        }

        private static PaintColour FromHsv(double hue, double saturation, double brightness)
        {
            var c = brightness * saturation;
            var x = c * (1 - Math.Abs(hue / 60 % 2 - 1));
            var m = brightness - c;
            var (r, g, b) = (hue % 360) switch
            {
                < 60 => (c, x, 0d),
                < 120 => (x, c, 0d),
                < 180 => (0d, c, x),
                < 240 => (0d, x, c),
                < 300 => (x, 0d, c),
                _ => (c, 0d, x)
            };
            return PaintColour.Of((float)(r + m), (float)(g + m), (float)(b + m));
        }

        #endregion

        #region Spraying

        private bool CanSpray() => IsPicking && !_leaving && !IsSpraying && IsChange && _gameState.Player.Money >= Price && (_colour == null || CanTint);

        private PlayingSound? _sprayLoop;

        /// <summary>
        /// The job: paid, the car's colour changed and its paint's wear and dirt gone, the time spent; then the painter
        /// goes round it, the new colour going on coat by coat (all at once halfway round, for another skin), and the
        /// player is taken home once the car has been looked at a moment
        /// </summary>
        private async Task SprayAsync()
        {
            if (!CanSpray() || _car is not { } car) return;
            var price = Price;
            var (oldSkin, oldColour) = (string.IsNullOrEmpty(car.SkinId) ? null : car.SkinId, PaintColour.Parse(car.PaintColour));
            var (newSkin, newColour) = (_skin, _colour);
            _previewTimer.Stop();
            IsPicking = false;
            IsSpraying = true;

            try
            {
                _gameState.Player.Money -= price;
                car.SkinId = newSkin ?? car.SkinId;
                car.PaintColour = newColour?.Hex;
                car.PaintWear = 0;
                car.BodyDirt = 0;
                _logger.Information("Painted {Car}: {Skin} {Colour} for ${Price}", car.DefinitionId, newSkin, newColour?.Hex ?? "(its own colours)", price);
                Sfx.Play(Sfx.Purchase);
                StatusText = GameText.Say("booth.ui.spraying", ("choice", ChoiceText));

                // The car as it was before the job, the coats over it from there
                Show.Wear(oldSkin, ShownAs(oldSkin, oldColour));
                var sameSkin = string.Equals(oldSkin, newSkin, StringComparison.OrdinalIgnoreCase);
                var texels = sameSkin
                    ? Task.Run(() =>
                    {
                        var to = CarPaint.TexelsOf(_carDirectory, newSkin, newColour);
                        return (From: CarPaint.TexelsOf(_carDirectory, oldSkin, oldColour, to.Keys.ToList()), To: to);
                    })
                    : null;

                var time = TimeSpan.FromSeconds(Math.Clamp(Rules.RuleBook.Current.Repairs.SpraySeconds, 3, 120));
                var clock = System.Diagnostics.Stopwatch.StartNew();
                Show.Spray(time);
                _sprayLoop = AudioHub.Shared.PlayLoop("booth.spray", fadeSeconds: 0.6f);
                var spent = SpendAsync(GameAction.PaintJob);

                var (from, to) = texels == null ? (null, null) : await texels;
                var swapped = false;
                for (var coat = 1; coat <= Coats && IsActive; coat++)
                {
                    // The coats go on over the painter's way round the car, the last as he finishes it
                    var due = time * (0.12 + 0.8 * coat / Coats);
                    var wait = due - clock.Elapsed;
                    if (wait > TimeSpan.Zero) await Task.Delay(wait);
                    if (!IsActive) return;

                    if (from != null && to != null && to.Count > 0)
                    {
                        var share = (float)coat / Coats;
                        var textures = await Task.Run(() => to.Where(t => from.ContainsKey(t.Key))
                            .Select(t => (t.Key, CarPaint.Blend(from[t.Key], t.Value, share))).ToList());
                        if (!IsActive) return;
                        Show.PutCoat(textures);
                    }
                    else if (!swapped && coat >= Coats / 2)
                    {
                        // Another skin: under the spray, the car is the new one
                        swapped = true;
                        Show.Wear(newSkin, ShownAs(newSkin, newColour, fresh: true));
                    }
                }

                var left = time - clock.Elapsed;
                if (left > TimeSpan.Zero) await Task.Delay(left);
                if (!IsActive) return;

                StopSprayLoop();
                Show.StopSpraying();
                Show.Wear(newSkin, ShownAs(newSkin, newColour, fresh: true));
                IsSpraying = false;
                StatusText = GameText.Say("booth.ui.done", ("cost", new Money(price)));

                await spent;
                Save();
                await Task.Delay(Admire);
                if (IsActive) await GoHomeAsync();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "The paint job could not be shown through");
                StopSprayLoop();
                Show.StopSpraying();
                IsSpraying = false;
                Save();
                if (IsActive) await GoHomeAsync();
            }
        }

        private void StopSprayLoop()
        {
            AudioHub.Shared.StopLoop(_sprayLoop, 0.5f);
            _sprayLoop = null;
        }

        private async Task SpendAsync(GameAction action)
        {
            try
            {
                await _timeService.SpendTimeAsync(_gameState, action);
            }
            catch (Exception ex)
            {
                _logger.Warning("Could not spend the time in the paint booth: {Error}", ex.Message);
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
                _logger.Error(ex, "Could not save at the paint booth");
            }
        }

        private void RaiseAll()
        {
            RelayCommand.RaiseCanExecuteChanged();
            SprayCommand.RaiseCanExecuteChanged();
        }

        #endregion
    }

    /// <summary>One of the car's mod's skins, to be painted in</summary>
    public sealed class BoothSkin(string id, string name, ImageSource? preview, RelayCommand pick) : ObservableObject
    {
        public string Id { get; } = id;

        public string Name { get; } = name;

        public ImageSource? Preview { get; } = preview;

        public bool HasPreview => Preview != null;

        public RelayCommand Pick { get; } = pick;

        private bool _isPicked;

        public bool IsPicked
        {
            get => _isPicked;
            set => SetProperty(ref _isPicked, value);
        }
    }

    /// <summary>A colour of the day, to paint the car or to start a mix from</summary>
    public sealed class BoothSwatch(string name, SolidColorBrush brush, RelayCommand pick) : ObservableObject
    {
        public string Name { get; } = name;

        public SolidColorBrush Brush { get; } = brush;

        public RelayCommand Pick { get; } = pick;

        private bool _isPicked;

        public bool IsPicked
        {
            get => _isPicked;
            set => SetProperty(ref _isPicked, value);
        }
    }
}
