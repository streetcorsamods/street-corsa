using StreetCorsa.Dialogs;
using StreetCorsa.Dialogs.Confirmation;
using StreetCorsa.Dialogs.Information;
using StreetCorsa.Logging;
using StreetCorsa.Models.GameState;
using StreetCorsa.Screens.UsedCarMarket;
using StreetCorsa.Services.Market;
using StreetCorsa.Services.Text;
using StreetCorsa.Helpers;

namespace StreetCorsa.Screens.Shared
{
    /// <summary>
    /// Buying a car on screen, the same wherever it is for sale (the paper's ads or a dealer's lot): the
    /// question with the car's details, the purchase, and what came of it. Every dialog goes through the
    /// dialog service's queue, so a save warning is read before the success message and not replaced by it.
    /// </summary>
    public sealed class PurchaseFlow
    {
        private readonly DialogService _dialogService;
        private readonly ICarPurchaseService _purchaseService;
        private readonly IAppLogger _logger;

        public PurchaseFlow(DialogService dialogService, ICarPurchaseService purchaseService, IAppLogger logger)
        {
            _dialogService = dialogService;
            _purchaseService = purchaseService;
            _logger = logger;
        }

        /// <summary>The question put to the player before a car is bought</summary>
        public static string ConfirmationMessage(UsedCarListingViewModel car, decimal bankroll)
        {
            var def = car.CarDefinition;
            return GameText.Say("purchase.confirm",
                ("car", $"{def.Brand} {def.Name}"),
                ("year", def.Year ?? 0),
                ("price", Money.Text(car.Listing.Price)),
                ("condition", car.ConditionLabel),
                ("mileage", car.Listing.Mileage),
                ("history", car.HistoryDisplay),
                ("seller", car.SellerLine),
                ("bankroll", Money.Text(bankroll)));
        }

        /// <summary>
        /// Asks, and buys when the player says yes. <paramref name="onFinished"/> runs after the result has
        /// been shown, for the screen to move on or reload; it is not called when the player said no, nor when
        /// the purchase failed with an error (nothing was bought then).
        /// </summary>
        public void Offer(GameState gameState, UsedCarListingViewModel car, Action<PurchaseResult> onFinished)
        {
            _logger.Information("Player attempting to purchase car {CarId} for ${Price}", car.CarDefinition.Id, car.Listing.Price);

            _dialogService.ShowDialog(new ConfirmationDialogViewModel(
                _dialogService,
                ConfirmationMessage(car, gameState.Player.Money),
                GameText.Say("purchase.confirm.title"),
                confirmed =>
                {
                    if (confirmed) Complete(gameState, car, onFinished);
                }));
        }

        private async void Complete(GameState gameState, UsedCarListingViewModel car, Action<PurchaseResult> onFinished)
        {
            PurchaseResult result;
            try
            {
                // The only await that can throw (the car's factory engine) comes before anything changes hands
                result = car.RivalAd is { } ad
                    ? await _purchaseService.PurchaseFromRivalAsync(gameState, ad, car.CarDefinition)
                    : await _purchaseService.PurchaseAsync(gameState, car.Listing, car.CarDefinition);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "The purchase of {CarId} failed", car.CarDefinition.Id);
                _dialogService.ShowDialog(new InformationDialogViewModel(
                    _dialogService,
                    GameText.Say("purchase.failed", ("error", ex.Message)),
                    GameText.Say("purchase.failed.title")));
                return;
            }

            try
            {
                if (result.Succeeded) Audio.Sfx.Play(Audio.Sfx.Purchase);
                foreach (var (title, message) in ResultDialogs(result))
                {
                    _dialogService.ShowDialog(new InformationDialogViewModel(_dialogService, message, title));
                }

                onFinished(result);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not finish the purchase of {CarId} on screen", car.CarDefinition.Id);
            }
        }

        /// <summary>What the player is told after a purchase, in the order they read it</summary>
        public static IReadOnlyList<(string Title, string Message)> ResultDialogs(PurchaseResult result)
        {
            if (!result.Succeeded)
            {
                var title = GameText.Say(result.Outcome == PurchaseOutcome.NotEnoughMoney ? "purchase.no_money.title" : "purchase.unavailable.title");
                return new[] { (title, result.Message) };
            }

            var dialogs = new List<(string, string)>();
            if (result.SaveFailed)
            {
                dialogs.Add((GameText.Say("purchase.save_failed.title"), GameText.Say("purchase.save_failed")));
            }

            dialogs.Add((GameText.Say("purchase.done.title"), result.Message));
            return dialogs;
        }
    }
}
