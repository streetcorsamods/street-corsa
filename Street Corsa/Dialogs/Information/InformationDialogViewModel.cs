using StreetCorsa.Services.Text;
using StreetCorsa.ViewModels;
using System;

namespace StreetCorsa.Dialogs.Information
{
    public class InformationDialogViewModel : BaseDialogViewModel
    {
        private readonly Action? _okCallback;
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
        /// The same message, word for word, is only shown once at a time; one with something to do on OK is
        /// always shown, so no callback is dropped
        /// </summary>
        public override string? DuplicateKey => _okCallback == null ? $"{Title}\n{Message}" : null;

        public RelayCommand OkCommand { get; }

        public InformationDialogViewModel(
            DialogService dialogService,
            string message,
            string? title = null,
            Action? okCallback = null)
            : base(dialogService)
        {
            _okCallback = okCallback;
            _title = title ?? GameText.Say("dialog.title.information");
            _message = message;

            OkCommand = new RelayCommand(OnOk);
        }

        private void OnOk()
        {
            // Closed first, so a dialog the callback shows is not closed along with this one
            Close();
            _okCallback?.Invoke();
        }
    }
}
