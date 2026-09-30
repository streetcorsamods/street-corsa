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

        /// <inheritdoc cref="IScreen.InGame"/>
        public virtual bool InGame => true;

        /// <summary>The game's time of day as the screens' headers show it ("9:30 PM")</summary>
        protected static string HeaderTime(DateTime date) => date.ToString("h:mm tt");
    }
}
