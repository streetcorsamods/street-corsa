using StreetCorsa.Models.GameState;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Services.Text;
using StreetCorsa.ViewModels;

namespace StreetCorsa.Screens.Garage
{
    /// <summary>The garage's lift: where it has the car, its button and the Lower button, what the work order allows at each stop</summary>
    public partial class PartsWorkbenchViewModel
    {

        /// <summary>Where the garage's lift has the car in the bay (<see cref="Car.LiftStop"/>); down with no car</summary>
        public LiftStop LiftStop => _car?.LiftStop ?? LiftStop.Down;

        /// <summary>The lift's button is shown: a car in the bay with parts to work on, the stand not on show</summary>
        public bool CanUseLift => IsAvailable && _car != null && !_isStand;

        /// <summary>The car is up on the lift: the lift's button is lit</summary>
        public bool IsLifted => _car?.IsLifted == true;

        /// <summary>The lift's button: the car to the next stop (<see cref="LiftLayout.Next"/>), not while a job is being shown</summary>
        public RelayCommand LiftCommand => _liftCommand ??= new RelayCommand(StepLift, () => CanUseLift && !_isWorking);
        private RelayCommand? _liftCommand;

        /// <summary>The lift's button says where the car is</summary>
        public string LiftText => GameText.Say(LiftStop switch
        {
            LiftStop.WheelsClear => "garage.view.lift.wheels_clear",
            LiftStop.Full => "garage.view.lift.full",
            _ => "garage.view.lift.down"
        });

        /// <summary>What the Lower button does, or why it does nothing now</summary>
        public string LowerTip => LiftLayout.Below(LiftStop) == LiftStop.Down && _car != null && WhyNotDown(_car) is { } why
            ? why
            : GameText.Say(LiftLayout.Below(LiftStop) == LiftStop.Down ? "garage.view.lift.tip.down" : "garage.view.lift.tip.lower");

        /// <summary>What a click on the lift's button does, or why it does nothing now</summary>
        public string LiftTip
        {
            get
            {
                var next = LiftLayout.Next(LiftStop);
                if (next == LiftStop.Down && _car != null && WhyNotDown(_car) is { } why) return why;
                return GameText.Say(next switch
                {
                    LiftStop.WheelsClear => "garage.view.lift.tip.wheels_clear",
                    LiftStop.Full => "garage.view.lift.tip.full",
                    _ => "garage.view.lift.tip.down"
                });
            }
        }

        /// <summary>
        /// The car to the lift's next stop: down, the wheels clear, full height, down again. It does not come down while a
        /// wheel is off (<see cref="CarWork.WhyNotDown"/>), nor move while a job is being shown.
        /// </summary>
        public void StepLift()
        {
            if (_car != null) MoveLift(LiftLayout.Next(_car.LiftStop));
            else NotifyLift();
        }

        /// <summary>The car to <paramref name="next"/> on the lift; it does not come down while a wheel is off</summary>
        private void MoveLift(LiftStop next)
        {
            if (_car == null || _isWorking || !CanUseLift || next == _car.LiftStop)
            {
                NotifyLift();
                return;
            }

            if (next == LiftStop.Down && WhyNotDown(_car) is { } why)
            {
                _logger.Information("{Car} stays up on the lift: {Why}", _car.DefinitionId, why);
                NotifyLift();
                return;
            }

            _car.LiftStop = next;
            _logger.Information("{Car} on the lift: {Stop}", _car.DefinitionId, next);
            NotifyLift();
            Finish(1, LiftMinutes, carChanged: false, refresh: false);
        }

        /// <summary>
        /// The car changes for another in the bay: the one going comes down off the lift, unless a wheel is off it (it
        /// stays up, and comes back up with it)
        /// </summary>
        private void LowerLeaving(Car? leaving, Car? coming)
        {
            if (leaving == null || ReferenceEquals(leaving, coming) || !leaving.IsLifted || WhyNotDown(leaving) != null) return;

            leaving.LiftStop = LiftStop.Down;
            _logger.Information("{Car} came down off the lift as it left the bay", leaving.DefinitionId);
            Save();
        }

        private static string? WhyNotDown(Car car) => CarWork.WhyNotDown(car.Parts, car.HasRunningGearAssigned);

        /// <summary>The car went up or down, or another took its place: the button, and what the rules allow now</summary>
        private void NotifyLift()
        {
            OnPropertyChanged(nameof(LiftStop));
            OnPropertyChanged(nameof(IsLifted));
            OnPropertyChanged(nameof(LiftText));
            OnPropertyChanged(nameof(LiftTip));
            OnPropertyChanged(nameof(LowerTip));
            OnPropertyChanged(nameof(CanUseLift));
            OnPropertyChanged(nameof(SelectedBlockedDisplay));
            OnPropertyChanged(nameof(CanInstallFromStand));
            FillCrates(Catalog);
            UpdateCandidates();
            RelayCommand.RaiseCanExecuteChanged();
        }
    }
}
