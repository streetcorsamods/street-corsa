using System.IO;
using System.Windows.Threading;
using StreetCorsa.Configuration;
using StreetCorsa.Controls.Street;
using StreetCorsa.Models.GameState;
using StreetCorsa.Navigation;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Screens.Shared;
using StreetCorsa.Services.Diner;
using StreetCorsa.Services.Street;
using StreetCorsa.Services.Text;
using StreetCorsa.ViewModels;

namespace StreetCorsa.Screens.Diner
{
    /// <summary>
    /// The diner's street: the player drives there, seen from the driver's seat, comes down Main Street and parks in a bay
    /// in front of the diner. The street is the screen; the diner (who is in, the word on the street, a racer's options
    /// and talk) opens in one window over it. Leaving, the car backs out and drives off, to the garage or the street.
    /// Back from a race that started here, the car stands in its bay already.
    /// </summary>
    public partial class DinerScreenViewModel
    {
        private bool _leaving;
        private Car? _car;

        // Where the player goes once the car has driven off
        private string _exit = "back";

        /// <summary>At the wheel: the radio is the car's</summary>
        public override bool InCar => true;

        public DinerScene? Scene { get; private set; }

        public StreetLight Light { get; private set; }

        public StreetCar? PlayerCar { get; private set; }

        /// <summary>The cars of the racers who are in, parked in the bays along the diner's front</summary>
        public IReadOnlyList<StreetCar> ParkedCars { get; private set; } = [];

        public string Title => Scene?.Title ?? GameText.Say("diner.title");

        /// <summary>The street has its model to show; without it the diner is a picture</summary>
        public bool HasStreetModel => Scene?.HasModel == true;

        /// <summary>The car stands in its bay and nobody is leaving: the diner's door is open</summary>
        public bool IsParked => Stage.Phase == DriveInPhase.Standing && !_leaving;

        private bool _isDinerOpen;

        /// <summary>The diner's window is open over the street</summary>
        public bool IsDinerOpen
        {
            get => _isDinerOpen;
            set
            {
                if (_isDinerOpen == value) return;
                _isDinerOpen = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ShowsDinerButton));
            }
        }

        /// <summary>The button into the diner, over the street while its window is shut</summary>
        public bool ShowsDinerButton => IsParked && !IsDinerOpen;

        public RelayCommand ToggleDinerCommand { get; private set; } = null!;

        /// <summary>From a racer back to everyone who is in</summary>
        public RelayCommand BackToListCommand { get; private set; } = null!;

        /// <summary>How many racers are in tonight, under the diner's name</summary>
        public string RacersIn => Opponents.Count == 0 ? GameText.Say("diner.ui.racers_in.none")
            : GameText.Say(Opponents.Count == 1 ? "diner.ui.racers_in.one" : "diner.ui.racers_in.other", ("count", Opponents.Count));

        private void MakeStreetCommands()
        {
            ToggleDinerCommand = new RelayCommand(() => IsDinerOpen = !IsDinerOpen, () => IsParked);
            BackToListCommand = new RelayCommand(() => SelectedOpponent = null);
            Stage.Arrived += OnParked;
            Stage.Gone += OnGone;
        }

        /// <summary>
        /// The street up with the player's car on it: it drives in once the street is on screen (or at once when it cannot
        /// be shown), or stands in its bay already when the player is back from a race
        /// </summary>
        private void SetUpStreet()
        {
            _car = _gameState.Player.Cars.FirstOrDefault(c => c.InstanceId == _gameState.Player.SelectedCarInstanceId);
            Scene = DinerScenes.Current;
            Light = StreetLights.At(_gameState.Date);
            Stage.Time = _gameState.Date;
            PlayerCar = _car == null ? null : new StreetCar(Path.Combine(AppSettings.Instance.CarsPath, _car.DefinitionId),
                string.IsNullOrEmpty(_car.SkinId) ? null : _car.SkinId, _partsService.Shown(_car));
            OnPropertyChanged(nameof(Scene));
            OnPropertyChanged(nameof(HasStreetModel));
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(Light));
            OnPropertyChanged(nameof(PlayerCar));
            PlayerEngine.Place(null, StreetStage.OwnEngineVolume);
            if (_car != null) _ = StartOwnEngineAsync(_partsService, _catalogRepository, _car, running: !_returning);

            ComeInOnceShown(() => Scene?.HasModel == true && PlayerCar != null, () =>
            {
                if (_returning) Stage.StandThere(PlayerEngine);
                else Stage.Arrive(PlayerEngine, towed: false);
            });
        }

        /// <summary>
        /// The street in the light of the hour now: the visit's half hour, spent once the car is on its way, can turn dusk
        /// to night or a late night into the next morning
        /// </summary>
        private void FollowTheClock()
        {
            Stage.Time = _gameState.Date;
            var light = StreetLights.At(_gameState.Date);
            if (light == Light) return;
            Light = light;
            OnPropertyChanged(nameof(Light));
        }

        /// <summary>In the bay: the engine off, the diner's door open; the flow's enter script, when a mod has one, plays now</summary>
        private void OnParked()
        {
            if (!IsActive) return;
            if (PlayerEngine.IsRunning) PlayerEngine.Toggle();
            RaiseStreet();
            _ = Dispatcher.CurrentDispatcher.BeginInvoke(() => _navigationService.ScreenReady(this));
        }

        /// <summary>
        /// The car starts up, backs out of its bay and drives off; once it has gone, the player is at <paramref name="exit"/>
        /// (the garage, the street)
        /// </summary>
        private async void DriveOff(string exit)
        {
            if (!IsParked) return;
            _leaving = true;
            _exit = exit;
            IsDinerOpen = false;
            RaiseStreet();

            // Nothing may escape an async void; a throw leaves the player in the bay to try again
            try
            {
                await DrivingHome.StartUpAsync(PlayerEngine, () => IsActive);
                if (!IsActive) return;
                Stage.Time = _gameState.Date;
                Stage.Leave();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not drive off from the diner");
                if (!IsActive) return;
                StayParked();
            }
        }

        private async void OnGone()
        {
            if (!IsActive) return;
            try
            {
                // Home, the car drives into the garage (its engine worked out here), as from the street
                var catalog = _partsService.IsAvailable ? _partsService.Catalog : null;
                var args = _exit == "back" ? new FlowArgs { Arrival = DrivingHome.Arrival(_gameState, OwnEngine, catalog) } : null;
                if (await _navigationService.LeaveAsync(this, _exit, _gameState, args, first: CloseCurtain)) return;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not leave the diner");
            }

            // The way out went nowhere: back in the bay, engine off, to try again
            Stage.BackToStand();
            OpenCurtain?.Invoke();
            StayParked();
        }

        private void StayParked()
        {
            if (PlayerEngine.IsRunning) PlayerEngine.Toggle();
            _leaving = false;
            RaiseStreet();
        }

        /// <summary>Whoever is in has parked their car out front</summary>
        private void ParkRacersCars()
        {
            ParkedCars = Opponents.Select(o => o.Opponent.Cars.FirstOrDefault()).OfType<Car>()
                .Select(car => new StreetCar(Path.Combine(AppSettings.Instance.CarsPath, car.DefinitionId),
                    string.IsNullOrEmpty(car.SkinId) ? null : car.SkinId, _partsService.Shown(car)))
                .ToList();
            OnPropertyChanged(nameof(ParkedCars));
        }

        private void RaiseStreet()
        {
            OnPropertyChanged(nameof(IsParked));
            OnPropertyChanged(nameof(ShowsDinerButton));
            OnPropertyChanged(nameof(CanCruise));
            RelayCommand.RaiseCanExecuteChanged();
        }
    }
}
