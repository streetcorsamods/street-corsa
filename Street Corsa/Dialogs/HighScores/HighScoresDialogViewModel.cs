using System.Collections.ObjectModel;
using StreetCorsa.Helpers;
using StreetCorsa.Models.GameState;
using StreetCorsa.Screens.Shared;
using StreetCorsa.Services.Career;
using StreetCorsa.Services.Race;
using StreetCorsa.Services.Text;
using StreetCorsa.ViewModels;

namespace StreetCorsa.Dialogs.HighScores
{
    /// <summary>
    /// The high scores of the runs for the score (<see cref="CareerState.ScoredRuns"/>): one table per road and layout,
    /// best first (<see cref="ScoredRun.Table"/>), each run marked with where it was driven from, and a table of the
    /// Challenges' best totals
    /// </summary>
    public sealed class HighScoresDialogViewModel : BaseDialogViewModel
    {
        private readonly GameState _gameState;
        private readonly Func<string, string?> _carName;
        private HighScoreTableViewModel? _selected;

        /// <param name="roads">The installed roads, for their names</param>
        /// <param name="carName">A car's name by its model; null when the catalog has none</param>
        public HighScoresDialogViewModel(DialogService dialogService, GameState gameState, IReadOnlyList<PracticeTrack> roads, Func<string, string?> carName)
            : base(dialogService)
        {
            _gameState = gameState;
            _carName = carName;
            CloseCommand = new RelayCommand(() => Close());
            PickCommand = new RelayCommand<HighScoreTableViewModel>(table => Selected = table, table => table != null);

            var runs = gameState.Career.ScoredRuns ?? [];
            if (ChallengeBook.Installed.All.Count > 0) Tables.Add(HighScoreTableViewModel.Totals());
            foreach (var road in runs.GroupBy(r => (Format: r.Format.ToLowerInvariant(), Track: r.TrackId.ToLowerInvariant(), Layout: (r.TrackConfig ?? string.Empty).ToLowerInvariant()))
                         .Select(g => g.First())
                         .OrderBy(r => NameOf(roads, r), StringComparer.CurrentCultureIgnoreCase))
                Tables.Add(HighScoreTableViewModel.Road(road.Format, road.TrackId, road.TrackConfig, NameOf(roads, road)));
            Selected = Tables.FirstOrDefault(t => !t.IsTotals) ?? Tables.FirstOrDefault();
        }

        private static string NameOf(IReadOnlyList<PracticeTrack> roads, ScoredRun run) =>
            roads.FirstOrDefault(t => string.Equals(t.TrackId, run.TrackId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(t.Configuration ?? string.Empty, run.TrackConfig ?? string.Empty, StringComparison.OrdinalIgnoreCase))?.DisplayName
            ?? (run.TrackConfig == null ? run.TrackId : $"{run.TrackId} - {run.TrackConfig}");

        public RelayCommand CloseCommand { get; }
        public RelayCommand<HighScoreTableViewModel> PickCommand { get; }

        /// <summary>The tables: the Challenges' totals first, then the roads by name</summary>
        public ObservableCollection<HighScoreTableViewModel> Tables { get; } = [];

        public bool HasTables => Tables.Count > 0;

        public ObservableCollection<HighScoreRowViewModel> Rows { get; } = [];

        public bool HasRows => Rows.Count > 0;

        public HighScoreTableViewModel? Selected
        {
            get => _selected;
            set
            {
                if (ReferenceEquals(_selected, value)) return;
                if (_selected != null) _selected.IsSelected = false;
                _selected = value;
                if (_selected != null) _selected.IsSelected = true;
                Fill();
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsRoad));
                OnPropertyChanged(nameof(IsTotals));
            }
        }

        public bool IsRoad => Selected is { IsTotals: false };
        public bool IsTotals => Selected is { IsTotals: true };

        private void Fill()
        {
            Rows.Clear();
            if (Selected is not { } table) return;

            if (table.IsTotals)
            {
                var place = 0;
                foreach (var challenge in ChallengeBook.Installed.All
                             .Select(c => (Challenge: c, Progress: _gameState.Career.Challenges?.GetValueOrDefault(c.Id)))
                             .Where(c => c.Progress?.BestTotal != null)
                             .OrderByDescending(c => c.Progress!.BestTotal))
                    Rows.Add(HighScoreRowViewModel.Total(++place, challenge.Challenge, challenge.Progress!));
            }
            else
            {
                var format = RaceFormats.Current.Get(table.Format);
                var higher = format?.HigherWins ?? true;
                var place = 0;
                foreach (var run in ScoredRun.Table(_gameState.Career.ScoredRuns ?? [], table.Format, table.TrackId, table.Layout, higher))
                    Rows.Add(HighScoreRowViewModel.Run(++place, run, format?.ScoreIsTime ?? false, _carName(run.CarDefinitionId) ?? run.CarDefinitionId));
            }

            OnPropertyChanged(nameof(HasRows));
        }
    }

    /// <summary>A table in the list: a road's (by format, track and layout), or the Challenges' totals</summary>
    public sealed class HighScoreTableViewModel : ObservableObject
    {
        private bool _isSelected;

        private HighScoreTableViewModel(bool totals, string format, string trackId, string? layout, string name)
        {
            IsTotals = totals;
            Format = format;
            TrackId = trackId;
            Layout = layout;
            Name = name;
        }

        public static HighScoreTableViewModel Totals() => new(true, string.Empty, string.Empty, null, GameText.Say("highscores.totals"));

        public static HighScoreTableViewModel Road(string format, string trackId, string? layout, string name) => new(false, format, trackId, layout, name);

        public bool IsTotals { get; }
        public string Format { get; }
        public string TrackId { get; }
        public string? Layout { get; }
        public string Name { get; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>A line of a table: a run on a road, or a Challenge's best total</summary>
    public sealed class HighScoreRowViewModel
    {
        public string Place { get; private init; } = string.Empty;
        public string Score { get; private init; } = string.Empty;
        public string Time { get; private init; } = string.Empty;
        public string Distance { get; private init; } = string.Empty;
        public string Speed { get; private init; } = string.Empty;
        public string Crashes { get; private init; } = string.Empty;
        public string Car { get; private init; } = string.Empty;
        public string Date { get; private init; } = string.Empty;

        /// <summary>Where it was driven from: Free Drive, or the Challenge's name; the density when it was not the road's own</summary>
        public string Source { get; private init; } = string.Empty;

        public static HighScoreRowViewModel Run(int place, ScoredRun run, bool scoreIsTime, string car)
        {
            var source = run.Source == ScoredRun.Sources.Challenge
                ? ChallengeBook.Installed.Get(run.Challenge)?.Name ?? GameText.Say("highscores.source.challenge")
                : GameText.Say("highscores.source.free");
            if (Math.Abs(run.TrafficDensity - 1) > 0.01)
                source += GameText.Say("highscores.density", ("density", run.TrafficDensity.ToString("0.0#", Locale.Culture)));
            return new HighScoreRowViewModel
            {
                Place = place.ToString(Locale.Culture),
                Score = scoreIsTime ? RaceResultProcessor.LapTime(run.Score) : run.Score.ToString("N0", Locale.Culture),
                Time = RaceResultProcessor.LapTime(run.TimeMs),
                Distance = Measure.Distance(run.DistanceKm).ToString("0.0#", Locale.Culture),
                Speed = Measure.Speed(run.AverageKmh).ToString("0", Locale.Culture),
                Crashes = run.Clean ? GameText.Say("highscores.clean") : run.Crashes.ToString(Locale.Culture),
                Car = car,
                Date = GameText.Date(run.Date, "format.date.short"),
                Source = source
            };
        }

        public static HighScoreRowViewModel Total(int place, ChallengeDefinition challenge, ChallengeProgress progress) => new()
        {
            Place = place.ToString(Locale.Culture),
            Score = (progress.BestTotal ?? 0).ToString("N0", Locale.Culture),
            Car = challenge.Name,
            Source = GameText.Say("highscores.finished", ("count", progress.Finished)),
            Crashes = GameText.Say("highscores.golds", ("golds", progress.Medals?.Count(m => m == Medal.Gold) ?? 0), ("roads", challenge.Roads.Count))
        };
    }
}
