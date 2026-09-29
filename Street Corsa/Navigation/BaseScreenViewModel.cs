using StreetCorsa.ViewModels;

namespace StreetCorsa.Navigation
{
    public abstract class BaseScreenViewModel : ObservableObject, IScreen
    {
        public virtual void Enter()
        {
        }

        public virtual void Exit()
        {
        }

        /// <inheritdoc cref="IScreen.Resume"/>
        public virtual void Resume()
        {
        }

        /// <inheritdoc cref="IScreen.Ambience"/>
        public virtual string? Ambience => null;

        /// <inheritdoc cref="IScreen.InCar"/>
        public virtual bool InCar => false;

        /// <summary>The player's money as the screens' headers show it, whole dollars ("$12,500")</summary>
        protected static string Bankroll(decimal money) => $"${money:N0}";

        /// <summary>The game's date as the screens' headers show it ("Monday, June 3, 1963")</summary>
        protected static string HeaderDate(DateTime date) => date.ToString("dddd, MMMM d, yyyy");

        /// <summary>The game's time of day as the screens' headers show it ("9:30 PM")</summary>
        protected static string HeaderTime(DateTime date) => date.ToString("h:mm tt");
    }
}
