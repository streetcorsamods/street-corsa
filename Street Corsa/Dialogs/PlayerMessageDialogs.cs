using StreetCorsa.Models.Race;

namespace StreetCorsa.Dialogs
{
    /// <summary>
    /// Shows a <see cref="PlayerMessage"/> the way it is meant to be seen: a timeslip as a timeslip, the game
    /// won as the victory screen, anything else as text. The dialog service queues them, one at a time.
    /// </summary>
    public static class PlayerMessageDialogs
    {
        /// <param name="toMainMenu">Where the victory screen's Main Menu button goes</param>
        /// <param name="jumpQueue">Shown at once, over the dialog up and ahead of the ones waiting (see <see cref="DialogService.ShowDialog(IDialog, bool)"/>)</param>
        public static void Show(DialogService dialogService, PlayerMessage message, Action toMainMenu, bool jumpQueue = false)
        {
            IDialog dialog;
            if (message.Timeslip is { } slip)
                dialog = new Timeslip.TimeslipDialogViewModel(dialogService, slip);
            else if (message.Victory is { } victory)
                dialog = new Victory.VictoryDialogViewModel(dialogService, victory, toMainMenu);
            else
                dialog = new Information.InformationDialogViewModel(dialogService, message.Text, message.Title);

            dialogService.ShowDialog(dialog, jumpQueue);
        }
    }
}
