using StreetCorsa.Helpers;
using StreetCorsa.Models.GameState;
using StreetCorsa.Navigation;
using StreetCorsa.Services.Text;

namespace StreetCorsa.ViewModels
{
    /// <summary>
    /// The window's money card over a game's screens: the game's money, time and date, and whether the window's End Day
    /// button shows. Nothing tells of a change of money (it is paid in many places), so the window reads it again on a
    /// short timer and at once as the screen or a dialog changes; only what reads differently is told to the view.
    /// </summary>
    public sealed class GameStatusViewModel(Func<GameState?> game, Func<IScreen?> screen, Func<bool> canEndDay) : ObservableObject
    {
        private bool _isShown;

        /// <summary>A game is in play and its screen is on show</summary>
        public bool IsShown
        {
            get => _isShown;
            private set => SetProperty(ref _isShown, value);
        }

        private bool _isEndDayShown;

        /// <summary>The day can be ended from the screen on show with the window's own button</summary>
        public bool IsEndDayShown
        {
            get => _isEndDayShown;
            private set => SetProperty(ref _isEndDayShown, value);
        }

        private string _moneyText = string.Empty;

        public string MoneyText
        {
            get => _moneyText;
            private set => SetProperty(ref _moneyText, value);
        }

        private string _timeText = string.Empty;

        public string TimeText
        {
            get => _timeText;
            private set => SetProperty(ref _timeText, value);
        }

        private string _dateText = string.Empty;

        public string DateText
        {
            get => _dateText;
            private set => SetProperty(ref _dateText, value);
        }

        /// <summary>Reads the game again; the texts stay as they were while no game's screen is on show</summary>
        public void Refresh()
        {
            var state = game();
            IsShown = state != null && screen() is { InGame: true };
            IsEndDayShown = IsShown && canEndDay();
            if (!IsShown) return;

            MoneyText = Money.Text(state!.Player.Money);
            TimeText = GameText.Date(state.Date, "format.time");
            DateText = GameText.Date(state.Date, "format.date.long");
        }
    }
}
