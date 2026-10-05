using StreetCorsa.ViewModels;

namespace StreetCorsa.Dialogs.Quit
{
    /// <summary>Where the player goes from the Quit dialog</summary>
    public enum QuitChoice
    {
        /// <summary>Nowhere: back to the game</summary>
        Stay,

        /// <summary>The main menu; the game stays saved</summary>
        MainMenu,

        /// <summary>Out of the game altogether</summary>
        Desktop
    }

    /// <summary>The way out of a game: to the main menu, out to the desktop, or back to the game</summary>
    public class QuitDialogViewModel : BaseDialogViewModel
    {
        private readonly Action<QuitChoice> _chosen;

        public RelayCommand MainMenuCommand { get; }
        public RelayCommand DesktopCommand { get; }
        public RelayCommand StayCommand { get; }

        /// <summary>Asked once at a time</summary>
        public override string? DuplicateKey => nameof(QuitDialogViewModel);

        public QuitDialogViewModel(DialogService dialogService, Action<QuitChoice> chosen)
            : base(dialogService)
        {
            _chosen = chosen;

            MainMenuCommand = new RelayCommand(() => Choose(QuitChoice.MainMenu));
            DesktopCommand = new RelayCommand(() => Choose(QuitChoice.Desktop));
            StayCommand = new RelayCommand(() => Choose(QuitChoice.Stay));
        }

        // Closed before the choice is acted on, as the other dialogs do: whatever it brings up comes after this one
        private void Choose(QuitChoice choice)
        {
            Close();
            _chosen(choice);
        }
    }
}
