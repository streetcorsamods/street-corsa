using StreetCorsa.Models.Career.Events;
using StreetCorsa.Models.GameState;
using StreetCorsa.Models.Race;
using StreetCorsa.Services.Text;
using StreetCorsa.ViewModels;

namespace StreetCorsa.Screens.Newspaper
{
    /// <summary>
    /// View model for displaying a race event invitation in the newspaper
    /// </summary>
    public class EventInvitationViewModel : ObservableObject
    {
        // Event definition info
        public string EventId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string RequirementsDescription { get; set; } = string.Empty;
        public string RewardDescription { get; set; } = string.Empty;
        public RaceType RaceType { get; set; }
        public bool IsPinkSlip { get; set; }

        /// <summary>A bracket race: the player dials in before it</summary>
        public bool IsBracket { get; set; }

        // Instance state
        public Guid InstanceId { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public string ExpiresInText { get; set; } = string.Empty;
        public bool IsExpiringSoon { get; set; }

        // Eligibility
        public bool HasEligibleCars { get; set; }
        public List<EligibleCarViewModel> EligibleCars { get; set; } = [];
        public string EligibilityText { get; set; } = string.Empty;

        // Visual
        public double Opacity => HasEligibleCars ? 1.0 : 0.5;
        public bool CanEnter => HasEligibleCars;

        public string RaceTypeDisplay => RaceType switch
        {
            RaceType.DragRace when IsBracket => GameText.Say("paper.race_type.bracket"),
            RaceType.DragRace => GameText.Say("paper.race_type.drag"),
            RaceType.Circuit => GameText.Say("paper.race_type.circuit"),
            RaceType.Sprint => GameText.Say("paper.race_type.sprint"),
            _ => GameText.Say("paper.race_type.other")
        };

        // The expiry and eligibility texts are coloured in the view from IsExpiringSoon and HasEligibleCars
        // (DataTriggers in NewspaperScreenView.xaml), so the colours stay in Styles/.

        public string PinkSlipWarning => IsPinkSlip ? GameText.Say("paper.pink_slip_warning") : string.Empty;
        public bool ShowPinkSlipWarning => IsPinkSlip;

        /// <summary>
        /// Create a view model from an event instance and definition
        /// </summary>
        public static EventInvitationViewModel FromEvent(
            RaceEventInstance instance,
            RaceEventDefinition definition,
            List<EligibleCarViewModel> eligibleCars,
            DateTime currentTime,
            double prizeMultiplier)
        {
            var expiresIn = CalculateExpiresIn(instance.ExpiresAt, currentTime);
            var isExpiringSoon = instance.ExpiresAt.HasValue &&
                (instance.ExpiresAt.Value - currentTime).TotalHours < 24;

            return new EventInvitationViewModel
            {
                EventId = definition.Id,
                InstanceId = instance.InstanceId,
                Name = definition.Name,
                Description = definition.Description,
                RequirementsDescription = definition.GetEntryRequirementsDescription(),
                RewardDescription = definition.Reward.ScaledBy(prizeMultiplier).GetDescription(),
                RaceType = definition.RaceType,
                IsPinkSlip = definition.IsPinkSlip,
                IsBracket = definition.IsBracketRace,
                ExpiresAt = instance.ExpiresAt,
                ExpiresInText = expiresIn,
                IsExpiringSoon = isExpiringSoon,
                HasEligibleCars = eligibleCars.Count > 0,
                EligibleCars = eligibleCars,
                EligibilityText = eligibleCars.Count > 0
                    ? GameText.Say(eligibleCars.Count > 1 ? "paper.eligible.other" : "paper.eligible.one", ("count", eligibleCars.Count))
                    : GameText.Say("paper.eligible.none")
            };
        }

        private static string CalculateExpiresIn(DateTime? expiresAt, DateTime currentTime)
        {
            if (!expiresAt.HasValue)
                return GameText.Say("paper.expires.never");

            var timeLeft = expiresAt.Value - currentTime;

            if (timeLeft.TotalDays >= 1)
                return GameText.Say((int)timeLeft.TotalDays > 1 ? "paper.expires.days.other" : "paper.expires.days.one", ("count", (int)timeLeft.TotalDays));

            if (timeLeft.TotalHours >= 1)
                return GameText.Say((int)timeLeft.TotalHours > 1 ? "paper.expires.hours.other" : "paper.expires.hours.one", ("count", (int)timeLeft.TotalHours));

            if (timeLeft.TotalMinutes > 0)
                return GameText.Say("paper.expires.soon");

            return GameText.Say("paper.expires.gone");
        }
    }

    /// <summary>
    /// Represents a car that is eligible for an event
    /// </summary>
    public class EligibleCarViewModel : ObservableObject
    {
        public Guid InstanceId { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string ConditionText { get; set; } = string.Empty;

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        /// <summary>A copy for a dialog of its own: what one dialog selects must not show up in the next</summary>
        public EligibleCarViewModel Copy() => new()
        {
            InstanceId = InstanceId,
            DisplayName = DisplayName,
            ConditionText = ConditionText
        };

        public static EligibleCarViewModel FromCar(Car car, string displayName)
        {
            return new EligibleCarViewModel
            {
                InstanceId = car.InstanceId,
                DisplayName = displayName,
                ConditionText = Shared.ConditionDisplay.Of(car).Sentence
            };
        }
    }
}
