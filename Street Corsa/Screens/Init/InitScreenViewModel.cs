using StreetCorsa.Dialogs;
using StreetCorsa.Navigation;
using StreetCorsa.ViewModels;

namespace StreetCorsa.Screens.Init
{
    public class InitScreenViewModel : BaseScreenViewModel
    {
        private readonly NavigationService _navigationService;
        private readonly DialogService _dialogService;

        public RelayCommand ProceedCommand { get; }

        public InitScreenViewModel(NavigationService navigationService, DialogService dialogService)
        {
            _navigationService = navigationService;
            _dialogService = dialogService;
            ProceedCommand = new RelayCommand(OnProceed);
        }

        private void OnProceed()
        {
            _navigationService.NavigateToMainMenu();
        }

        public override void Enter()
        {
            base.Enter();
        }

        public override void Exit()
        {
            base.Exit();
        }
    }
}
