using StreetCorsa.ViewModels;

namespace StreetCorsa.Dialogs
{
    public abstract class BaseDialogViewModel : ObservableObject, IDialog
    {
        // The service that shows it: the one it closes itself through
        private readonly DialogService _dialogs;

        protected BaseDialogViewModel(DialogService dialogService) => _dialogs = dialogService;

        /// <summary>
        /// Closes this dialog, whether it is in front or still waiting (<see cref="DialogService.Withdraw"/>): never
        /// another that came in front of it meanwhile, as a late callback closing "whatever is up" would
        /// </summary>
        protected void Close() => _dialogs.Withdraw(this);

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
