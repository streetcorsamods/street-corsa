using StreetCorsa.ViewModels;

namespace StreetCorsa.Dialogs
{
    public abstract class BaseDialogViewModel : ObservableObject, IDialog
    {
        public virtual void OnOpened()
        {
        }

        public virtual void OnClosed()
        {
        }

        /// <inheritdoc cref="IDialog.DuplicateKey"/>
        public virtual string? DuplicateKey => null;
    }
}
