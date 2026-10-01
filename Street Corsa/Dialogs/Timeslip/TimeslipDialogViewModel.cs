using System.Globalization;
using StreetCorsa.Models.Race;
using StreetCorsa.Services.Text;
using StreetCorsa.ViewModels;
using StreetCorsa.Helpers;

namespace StreetCorsa.Dialogs.Timeslip
{
    /// <summary>
    /// Timeslips, mark by mark, as the strip hands them out: a column for each lane of a drag race, or for each pass of
    /// a test-and-tune. A bracket race's slip starts with the dial-ins.
    /// </summary>
    public class TimeslipDialogViewModel : BaseDialogViewModel
    {
        private readonly DialogService _dialogService;

        public TimeslipDialogViewModel(DialogService dialogService, TimeslipCard card)
        {
            _dialogService = dialogService;
            Title = card.Title ?? GameText.Say("timeslip.title");
            Names = card.Lanes.Select(l => l.Name).ToList();
            Rows = BuildRows(card.Lanes);
            Note = card.Note ?? string.Empty;
            OkCommand = new RelayCommand(() => _dialogService.CloseDialog());
        }

        public string Title { get; }

        /// <summary>Whose each column is</summary>
        public IReadOnlyList<string> Names { get; }

        public int Columns => Math.Max(1, Names.Count);

        public IReadOnlyList<TimeslipRow> Rows { get; }

        public string Note { get; }

        public bool HasNote => Note.Length > 0;

        public RelayCommand OkCommand { get; }

        public static IReadOnlyList<TimeslipRow> BuildRows(IReadOnlyList<TimeslipLane> lanes)
        {
            TimeslipRow Row(string label, Func<Models.Race.Timeslip, double?> value, string format) =>
                new(label, lanes.Select(l => Show(l.Slip, value, format)).ToList());

            var rows = new List<TimeslipRow>();
            if (lanes.Any(l => l.DialIn != null))
                rows.Add(new TimeslipRow(GameText.Say("timeslip.dial"), lanes.Select(l => l.DialIn is { } d ? d.ToString("0.00", Locale.Culture) : "-").ToList()));

            rows.Add(new TimeslipRow(GameText.Say("timeslip.reaction"), lanes.Select(l => Reaction(l.Slip)).ToList()));
            // The strip's marks are the quarter mile's wherever it is; its trap speeds are in the player's units
            var unit = Measure.NameOf(Quantity.Speed).ToUpper(Locale.Culture);
            rows.Add(Row(GameText.Say("timeslip.sixty"), s => s.SixtyFeetSeconds, "0.000"));
            rows.Add(Row(GameText.Say("timeslip.three_thirty"), s => s.ThreeThirtyFeetSeconds, "0.000"));
            rows.Add(Row(GameText.Say("timeslip.eighth"), s => s.EighthMileSeconds, "0.000"));
            rows.Add(Row(GameText.Say("timeslip.eighth.speed", ("unit", unit)), s => Speed(s.EighthMileMph), "0.00"));
            rows.Add(Row(GameText.Say("timeslip.thousand"), s => s.ThousandFeetSeconds, "0.000"));
            rows.Add(Row(GameText.Say("timeslip.quarter"), s => s.QuarterMileSeconds, "0.000"));
            rows.Add(Row(GameText.Say("timeslip.quarter.speed", ("unit", unit)), s => Speed(s.QuarterMileMph), "0.00"));
            return rows;
        }

        /// <summary>A trap speed the slip keeps in mph, in the player's units</summary>
        private static double? Speed(double? mph) => mph is { } value ? Measure.SpeedFromMph(value).Value : null;

        /// <summary>The reaction time, a red light marked as the strips mark it: "-0.052 RL"</summary>
        private static string Reaction(Models.Race.Timeslip? slip)
        {
            var shown = Show(slip, s => s.ReactionSeconds, "0.000");
            return slip?.RedLight == true ? GameText.Say("timeslip.red_light", ("reaction", shown)) : shown;
        }

        /// <summary>A mark the car never reached is a dash, as on a real slip</summary>
        private static string Show(Models.Race.Timeslip? slip, Func<Models.Race.Timeslip, double?> value, string format) =>
            slip != null && value(slip) is { } number && double.IsFinite(number) ? number.ToString(format, Locale.Culture) : "-";
    }

    /// <summary>One mark of the slip: its label and each column's figure</summary>
    public sealed record TimeslipRow(string Label, IReadOnlyList<string> Values);
}
