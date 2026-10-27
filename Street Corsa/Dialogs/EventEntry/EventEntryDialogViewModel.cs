using StreetCorsa.Models.Career.Events;
using StreetCorsa.Models.GameState;
using StreetCorsa.Models.Race;
using StreetCorsa.Dialogs.RaceHub;
using StreetCorsa.Services.Career;
using StreetCorsa.Services.Text;
using StreetCorsa.ViewModels;

namespace StreetCorsa.Dialogs.EventEntry
{
    /// <summary>
    /// View model for the event entry dialog where players select a car and confirm entry
    /// </summary>
    public class EventEntryDialogViewModel : BaseDialogViewModel
    {
        private readonly Action<EventEntryResult?> _callback;

        public RelayCommand ConfirmCommand { get; }
        public RelayCommand CancelCommand { get; }
        public RelayCommand<EligibleCarViewModel> SelectCarCommand { get; }

        // Event info
        public string EventName { get; }
        public string EventDescription { get; }
        public string Requirements { get; }
        public string Reward { get; }
        public bool IsPinkSlip { get; }
        public string RaceTypeDisplay { get; }

        // Opponent info
        public string OpponentName { get; }
        public string? OpponentNickname { get; }
        public string OpponentCarDisplay { get; }
        public bool IsPoolOpponent { get; }

        /// <summary>
        /// The cars that may enter: copies of the invitation's, so what is picked here is this dialog's alone
        /// and a cancelled pick does not show up highlighted the next time the invitation is opened
        /// </summary>
        public List<EligibleCarViewModel> EligibleCars { get; }

        private EligibleCarViewModel? _selectedCar;

        /// <summary>The car that enters. Setting it moves the highlight: exactly the selected car shows as picked</summary>
        public EligibleCarViewModel? SelectedCar
        {
            get => _selectedCar;
            set
            {
                if (ReferenceEquals(_selectedCar, value)) return;

                if (_selectedCar != null) _selectedCar.IsSelected = false;
                _selectedCar = value;
                if (_selectedCar != null) _selectedCar.IsSelected = true;

                OnPropertyChanged(nameof(SelectedCar));
                OnPropertyChanged(nameof(CanConfirm));
                OnPropertyChanged(nameof(SelectedCarDisplay));
            }
        }

        public string SelectedCarDisplay => SelectedCar?.DisplayName ?? GameText.Say("event.select_a_car");
        public bool CanConfirm => SelectedCar != null && CanAfford;

        /// <summary>A field: the rest of who lines up after the opponent, a line each; empty for a race of two</summary>
        public IReadOnlyList<string> FieldLines { get; }

        public bool HasField => FieldLines.Count > 0;

        /// <summary>What each car pays to start, said; empty when it is free</summary>
        public string EntryFee { get; }

        public bool HasEntryFee => EntryFee.Length > 0;

        /// <summary>The player has the entry fee</summary>
        public bool CanAfford { get; }

        public bool CannotAfford => !CanAfford;

        // For result
        private readonly EventInvitationViewModel _eventVm;
        private readonly EventOpponentResult _opponent;
        private readonly RaceEventDefinition _eventDef;

        public EventEntryDialogViewModel(
            DialogService dialogService,
            EventInvitationViewModel eventVm,
            RaceEventDefinition eventDef,
            EventOpponentResult opponent,
            string opponentCarDisplay,
            Action<EventEntryResult?> callback,
            IReadOnlyList<string>? fieldLines = null,
            decimal playerMoney = decimal.MaxValue)
            : base(dialogService)
        {
            FieldLines = fieldLines ?? [];
            EntryFee = eventDef.Entry > 0 ? GameText.Say("event.entry_fee", ("entry", eventDef.Entry)) : string.Empty;
            CanAfford = playerMoney >= eventDef.Entry;

            _eventVm = eventVm;
            _eventDef = eventDef;
            _opponent = opponent;
            _callback = callback;

            // Event info
            EventName = eventVm.Name;
            EventDescription = eventVm.Description;
            Requirements = eventVm.RequirementsDescription;
            Reward = eventVm.RewardDescription;
            IsPinkSlip = eventVm.IsPinkSlip;
            RaceTypeDisplay = eventVm.RaceTypeDisplay;

            // Opponent info
            OpponentName = opponent.OpponentName;
            OpponentNickname = opponent.OpponentNickname;
            OpponentCarDisplay = opponentCarDisplay;
            IsPoolOpponent = opponent.IsPoolOpponent;

            // Car selection
            EligibleCars = eventVm.EligibleCars.Select(c => c.Copy()).ToList();
            if (EligibleCars.Count > 0)
            {
                SelectedCar = EligibleCars[0];
            }

            ConfirmCommand = new RelayCommand(OnConfirm, () => CanConfirm);
            CancelCommand = new RelayCommand(OnCancel);
            SelectCarCommand = new RelayCommand<EligibleCarViewModel>(car =>
            {
                if (car != null) SelectedCar = car;
            });
        }

        private void OnConfirm()
        {
            if (SelectedCar == null)
                return;

            var result = new EventEntryResult
            {
                EventId = _eventVm.EventId,
                EventInstanceId = _eventVm.InstanceId,
                PlayerCarInstanceId = SelectedCar.InstanceId,
                Opponent = _opponent,
                EventDefinition = _eventDef,
                IsPinkSlip = IsPinkSlip
            };

            // Closed before the callback: it goes on to the race, and anything it shows must not close with this
            Close();
            _callback?.Invoke(result);
        }

        private void OnCancel()
        {
            Close();
            _callback?.Invoke(null);
        }
    }

    /// <summary>
    /// Result of event entry dialog
    /// </summary>
    public class EventEntryResult
    {
        public string EventId { get; set; } = string.Empty;
        public Guid EventInstanceId { get; set; }
        public Guid PlayerCarInstanceId { get; set; }
        public required EventOpponentResult Opponent { get; set; }
        public required RaceEventDefinition EventDefinition { get; set; }
        public bool IsPinkSlip { get; set; }
    }
}
