using StreetCorsa.Audio;
using StreetCorsa.Looks;
using StreetCorsa.Parts;
using StreetCorsa.Screens.Shared;
using StreetCorsa.Services.Text;
using StreetCorsa.ViewModels;

namespace StreetCorsa.Screens.Garage
{
    /// <summary>A job shown done: the part on the bench or where it sits, each step's caption and sound, the condition and the clock going up</summary>
    public partial class PartsWorkbenchViewModel
    {

        // The show of the job on: told to stop (skipped, or the view closed); its run and its sound are the player's
        // (<see cref="JobShowPlayer"/>), so one stopped and another started at once is told apart
        private bool _workSkipped;
        private readonly JobShowPlayer _show = new();

        private bool _isWorking;

        /// <summary>A job is being shown done: the rest of the workbench waits</summary>
        public bool IsWorking
        {
            get => _isWorking;
            private set
            {
                if (!SetProperty(ref _isWorking, value)) return;
                OnPropertyChanged(nameof(IsIdle));
                RelayCommand.RaiseCanExecuteChanged();
            }
        }

        public bool IsIdle => !_isWorking;

        private Controls.WorkPiece? _workPiece;

        /// <summary>The part the 3D view shows worked on: on the workbench, or where it is mounted</summary>
        public Controls.WorkPiece? WorkPiece
        {
            get => _workPiece;
            private set => SetProperty(ref _workPiece, value);
        }

        private double _workGrime;

        /// <summary>How much grime the part being worked on still carries, for the 3D view</summary>
        public double WorkGrime
        {
            get => _workGrime;
            private set => SetProperty(ref _workGrime, value);
        }

        private string _workTitle = string.Empty;
        public string WorkTitle
        {
            get => _workTitle;
            private set => SetProperty(ref _workTitle, value);
        }

        private string _workCaption = string.Empty;

        /// <summary>The step of the job on now</summary>
        public string WorkCaption
        {
            get => _workCaption;
            private set => SetProperty(ref _workCaption, value);
        }

        private double _workProgress;
        public double WorkProgress
        {
            get => _workProgress;
            private set => SetProperty(ref _workProgress, value);
        }

        private string _workClock = string.Empty;

        /// <summary>The game's clock as the job goes, from when it started to when it is done</summary>
        public string WorkClock
        {
            get => _workClock;
            private set => SetProperty(ref _workClock, value);
        }

        private string _workCondition = string.Empty;

        /// <summary>The part's condition as the job goes</summary>
        public string WorkCondition
        {
            get => _workCondition;
            private set => SetProperty(ref _workCondition, value);
        }

        public RelayCommand SkipWorkCommand => _skipWorkCommand ??= new RelayCommand(SkipWork, () => _isWorking);
        private RelayCommand? _skipWorkCommand;

        /// <summary>The show of the job jumps to its end (the job is done already)</summary>
        public void SkipWork()
        {
            if (_isWorking) _workSkipped = true;
        }

        /// <summary>
        /// Plays the job done: each step's caption over the sound of the work, the part's grime fading, its condition and
        /// the game's clock going up, over <see cref="RebuildShow.Seconds"/>. Only the showing: the job is done already.
        /// </summary>
        private async void ShowWork(RebuildShow show, Controls.WorkPiece? piece)
        {
            CheckRebuildSounds();
            var run = _show.Begin();
            _workSkipped = false;
            IsWorking = true;
            WorkTitle = show.Name;
            WorkPiece = piece;
            var step = -1;
            // The condition and the clock are written again only as they read differently: the show ticks thirty times
            // a second, the text changes a few times a second at most
            var percent = -1;
            var minute = -1L;
            try
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                while (_show.IsCurrent(run))
                {
                    var progress = _workSkipped ? 1 : Math.Min(1, clock.Elapsed.TotalSeconds / show.Seconds);
                    var now = show.StepAt(progress);
                    if (now != step)
                    {
                        step = now;
                        var (caption, sound, start) = show.Steps[now];
                        WorkCaption = caption;
                        if (progress < 1) _show.Step(sound, start ?? (now > 0 ? Sfx.Clank : null));
                        else _show.Stop();
                    }

                    var wear = show.WearAt(progress);
                    WorkProgress = progress;
                    WorkGrime = PartGrime.Of(wear);
                    var wearPercent = (int)Math.Round(wear * 100, MidpointRounding.AwayFromZero);
                    if (wearPercent != percent)
                    {
                        percent = wearPercent;
                        WorkCondition = $"{percent}%";
                    }

                    var time = show.ClockAt(progress, _gameState.Date);
                    if (time.Ticks / TimeSpan.TicksPerMinute is var timeMinute && timeMinute != minute)
                    {
                        minute = timeMinute;
                        WorkClock = GameText.Date(time, "format.time");
                    }

                    if (progress >= 1) break;

                    await Task.Delay(30);
                }

                if (!_show.IsCurrent(run)) return;

                // Done: the last of the work's sound goes, a part set down, a moment to see it clean and hear how it came out
                _show.Stop(0.4f);
                Sfx.Play(Sfx.Clank);
                if (show.Verdict is { } verdict) WorkCaption = verdict;

                // A moment to read it, shorter for a show skipped, and a skip now ends it there
                var hold = show.Verdict != null ? _workSkipped ? 900 : 1600 : _workSkipped ? 250 : 700;
                _workSkipped = false;
                var held = System.Diagnostics.Stopwatch.StartNew();
                while (_show.IsCurrent(run) && !_workSkipped && held.ElapsedMilliseconds < hold) await Task.Delay(30);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "The job's show stopped");
            }
            finally
            {
                if (_show.IsCurrent(run)) EndShow();
            }
        }

        // The jobs whose sounds were checked against the sound cues: once, as the first job is shown (the cues are read
        // at start-up, on the sound's own thread)
        private static PartRebuilds? _soundsChecked;

        private void CheckRebuildSounds()
        {
            var cues = AudioHub.Shared.Cues;
            if (Catalog?.Rebuilds is not { } rebuilds || ReferenceEquals(rebuilds, _soundsChecked) || cues.All.Count == 0) return;

            _soundsChecked = rebuilds;
            var problems = rebuilds.UnknownSounds(name => cues.Get(name) != null);
            foreach (var problem in problems) _logger.Warning("Parts: {Problem}", problem);
            Mods.ModProblems.Add("Parts", problems);
        }

        /// <summary>Stops the show of the job on at once, its sound and all (the parts view closed, another car, leaving)</summary>
        public void EndWork()
        {
            if (!_isWorking) return;

            _show.Cancel();
            EndShow();
        }

        private void EndShow()
        {
            _show.Stop();
            WorkPiece = null;
            WorkGrime = 0;
            IsWorking = false;
        }
    }
}
