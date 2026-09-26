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
    }
}
