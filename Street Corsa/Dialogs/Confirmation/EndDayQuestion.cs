using StreetCorsa.Models.GameState;
using StreetCorsa.Services.Text;
using StreetCorsa.Services.Time;

namespace StreetCorsa.Dialogs.Confirmation
{
    /// <summary>
    /// The End Day question, the same wherever the day is ended from (the garage's calendar, the window's button): the
    /// hours left today, and the hour the game picks up tomorrow
    /// </summary>
    public static class EndDayQuestion
    {
        /// <summary>Asks; <paramref name="confirmed"/> runs only on a yes</summary>
        public static void Ask(DialogService dialogService, IGameTimeService timeService, GameState gameState, Action confirmed)
        {
            var left = timeService.GetRemainingMinutesToday(gameState);
            // The day's first hour is the rules': written as the game's clock writes the time (8:00 AM, 2:00 PM)
            var morning = GameText.Date(DateTime.Today.AddHours(timeService.DayStartHour), "format.time");
            var message = left > 0
                ? GameText.Say("garage.end_day.ask.left", ("hours", left / 60), ("minutes", left % 60), ("morning", morning))
                : GameText.Say("garage.end_day.ask", ("morning", morning));

            dialogService.ShowDialog(new ConfirmationDialogViewModel(
                dialogService, message, GameText.Say("garage.end_day.title"),
                yes =>
                {
                    if (yes) confirmed();
                }));
        }
    }
}
