using System.Windows.Media;
using StreetCorsa.Dialogs.CarInfo;
using StreetCorsa.Helpers;
using StreetCorsa.Models.GameState;
using StreetCorsa.Screens.Newspaper;
using StreetCorsa.Screens.Shared;
using StreetCorsa.Services.Market;
using StreetCorsa.Services.Talk;
using StreetCorsa.Services.Text;
using StreetCorsa.ViewModels;

namespace StreetCorsa.Dialogs.PlayerAd
{
    /// <summary>
    /// The player's own car in the paper, its ad clicked: the ad as printed, the car's picture and figures, how long the
    /// ad has run, and the buyer who called, if one has, with their offer to take or turn down. While the player's AI is
    /// on, the buyer can be called back to talk the offer up (the game decides how far they go: <see cref="Haggle.DecideAsk"/>);
    /// the window comes back when the call is over, with the offer as it then stands.
    /// </summary>
    public sealed class PlayerAdDialogViewModel : BaseDialogViewModel
    {
        private readonly DialogService _dialogService;
        private readonly GameState _game;
        private readonly Func<Conversation>? _talk;
        private readonly Action<CarSaleAd> _take;
        private readonly Action<CarSaleAd> _decline;
        private readonly Action? _changed;

        /// <param name="previewPath">The car's picture (its skin's, else its own); none, and the clipping stands alone</param>
        /// <param name="talk">The buyer, to call back; none when there is nobody to call</param>
        /// <param name="take">Taking the offer: the screen's own sale, run once this window has closed</param>
        /// <param name="decline">Turning the offer down: the screen's own, the window stays</param>
        /// <param name="changed">The offer changed (a call, turned down), for the screen behind</param>
        public PlayerAdDialogViewModel(DialogService dialogService, GameState game, CarAdViewModel ad, Car car, string? previewPath,
            Func<Conversation>? talk, Action<CarSaleAd> take, Action<CarSaleAd> decline, Action? changed = null)
        {
            _dialogService = dialogService;
            _game = game;
            _talk = talk;
            _take = take;
            _decline = decline;
            _changed = changed;
            Ad = ad;
            SaleAd = ad.PlayerAd ?? throw new ArgumentException("Not one of the player's ads", nameof(ad));
            Preview = string.IsNullOrEmpty(previewPath) ? null : Thumbnails.Load(previewPath, 640);
            Lines = LinesOf(SaleAd, car, ad.Car, game.Date);

            CloseCommand = new RelayCommand(() => _dialogService.CloseDialog());
            CallCommand = new RelayCommand(OnCall, () => CanCall);
            TakeCommand = new RelayCommand(OnTake, () => HasOffer);
            TurnDownCommand = new RelayCommand(OnTurnDown, () => HasOffer);
        }

        /// <summary>The ad as the paper prints it</summary>
        public CarAdViewModel Ad { get; }

        public CarSaleAd SaleAd { get; }

        public string Title => GameText.Say("paper.yours.title", ("car", Ad.Headline));

        public ImageSource? Preview { get; }

        public bool HasPreview => Preview != null;

        /// <summary>What the ad asks, how long it has run, and the car's shape, mileage and engine</summary>
        public IReadOnlyList<CarInfoLine> Lines { get; }

        /// <summary>A buyer is on the phone, their offer still good</summary>
        public bool HasOffer => SaleAd.Offer is { } offer && offer.Expires >= _game.Date && _game.NewspaperAds.PlayerCars.Contains(SaleAd);

        /// <summary>Who called and what they offer, until when; that nobody has called yet</summary>
        public string OfferLine => HasOffer
            ? GameText.Say("paper.ad.offer", ("buyer", CarSaleService.Capitalized(SaleAd.Offer!.BuyerName)),
                ("amount", new Money(SaleAd.Offer.Amount)), ("until", SaleAd.Offer.Expires))
            : GameText.Say("paper.ad.no_offer");

        /// <summary>The buyer can be called back while the player's AI is on</summary>
        public bool CanCall => HasOffer && _talk != null && TalkDesk.Shared.IsAvailable;

        public RelayCommand CloseCommand { get; }

        public RelayCommand CallCommand { get; }

        public RelayCommand TakeCommand { get; }

        public RelayCommand TurnDownCommand { get; }

        /// <summary>One window per ad</summary>
        public override string? DuplicateKey => $"player-ad:{SaleAd.AdId}";

        private static List<CarInfoLine> LinesOf(CarSaleAd ad, Car car, UsedCarListingViewModel shown, DateTime today)
        {
            var days = Math.Max(0, (int)(today.Date - ad.PostedDate.Date).TotalDays);
            var lines = new List<CarInfoLine>
            {
                new(GameText.Say("paper.yours.asking"), Money.Text(ad.AskingPrice)),
                new(GameText.Say("paper.yours.running"), GameText.Say(days == 1 ? "paper.yours.running.one" : "paper.yours.running.other",
                    ("count", days), ("until", ad.PostedDate.Date.AddDays(CarSaleService.AdDays)))),
                new(GameText.Say("carinfo.condition"), ConditionDisplay.Of(car).Sentence),
                new(GameText.Say("carinfo.mileage"), Measure.Distance(car.OdometerKM).ToString())
            };
            if (shown.EngineDisplay.Length > 0) lines.Add(new(GameText.Say("carinfo.engine"), shown.EngineDisplay));
            if (car.PowerHp is { } hp)
                lines.Add(new(GameText.Say("carinfo.power"), hp > 0 ? GameText.Say("carinfo.power.dyno", ("hp", $"{hp:0}")) : GameText.Say("carinfo.power.no_run")));
            return lines;
        }

        /// <summary>The call over this window; it comes back when the call is over, with the offer as it then is</summary>
        private void OnCall()
        {
            if (_talk == null || !CanCall) return;

            Talk.TalkDialogViewModel.Show(_dialogService, _talk(), () =>
            {
                Refresh();
                _changed?.Invoke();
            }, jumpQueue: true, buy: OnTake);
        }

        /// <summary>Closed first: the sale's own question and its answer come after this window, not under it</summary>
        private void OnTake()
        {
            if (!HasOffer) return;
            _dialogService.CloseDialog();
            _take(SaleAd);
        }

        private void OnTurnDown()
        {
            if (!HasOffer) return;
            _decline(SaleAd);
            Refresh();
            _changed?.Invoke();
        }

        private void Refresh()
        {
            OnPropertyChanged(nameof(HasOffer));
            OnPropertyChanged(nameof(OfferLine));
            OnPropertyChanged(nameof(CanCall));
            RelayCommand.RaiseCanExecuteChanged();
        }
    }
}
