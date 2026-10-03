using StreetCorsa.Services.Text;
using StreetCorsa.ViewModels;
using System;

namespace StreetCorsa.Dialogs.Confirmation
{
    public class ConfirmationDialogViewModel : BaseDialogViewModel
    {
        private readonly DialogService _dialogService;
        private readonly Action<bool> _resultCallback;
        private string _title;
        private string _message;

        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        public string Message
        {
            get => _message;
            set => SetProperty(ref _message, value);
        }

        /// <summary>
        /// What the question is about, when the same words can be asked of two things at once (two cars of one model): two
        /// questions about different things are both asked
        /// </summary>
        public string? About { get; init; }

        /// <summary>The same question, word for word and about the same thing, is only asked once at a time</summary>
        public override string? DuplicateKey => $"{Title}\n{Message}\n{About}";

        public RelayCommand YesCommand { get; }
        public RelayCommand NoCommand { get; }

        public ConfirmationDialogViewModel(
            DialogService dialogService,
            string message,
            string? title = null,
            Action<bool>? resultCallback = null)
        {
            _dialogService = dialogService;
            _resultCallback = resultCallback ?? (_ => { });
            _title = title ?? GameText.Say("dialog.title.confirm");
            _message = message;

            YesCommand = new RelayCommand(OnYes);
            NoCommand = new RelayCommand(OnNo);
        }

        // Closed before the callback runs: whatever the callback shows (an error, the next question) must
        // come up after this dialog has gone, not be closed along with it
        private void OnYes()
        {
            _dialogService.CloseDialog();
            _resultCallback(true);
        }

        private void OnNo()
        {
            _dialogService.CloseDialog();
            _resultCallback(false);
        }
    }
}
