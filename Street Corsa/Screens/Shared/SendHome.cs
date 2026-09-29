using System.Windows.Threading;
using StreetCorsa.Dialogs;
using StreetCorsa.Dialogs.Information;
using StreetCorsa.Models.GameState;
using StreetCorsa.Navigation;

namespace StreetCorsa.Screens.Shared
{
    /// <summary>
    /// A screen the player cannot stay on (no car to go in, nothing there): it says why and takes them back to the garage.
    /// Called from <c>Enter</c>, so the garage opens only once the navigation that brought the player here is over.
    /// </summary>
    public static class SendHome
    {
        public static void ToGarage(DialogService dialogService, NavigationService navigationService, GameState gameState, string message, string title)
        {
            dialogService.ShowDialog(new InformationDialogViewModel(dialogService, message, title));
            // Not from inside Enter: the navigation that brought the player here is still going on
            _ = Dispatcher.CurrentDispatcher.InvokeAsync(() => navigationService.NavigateToGarage(gameState));
        }
    }
}
