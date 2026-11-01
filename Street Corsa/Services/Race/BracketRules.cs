using StreetCorsa.Models.GameState;
using StreetCorsa.Models.Race;

namespace StreetCorsa.Services.Race
{
    /// <summary>What came of a bracket race, by its own rules</summary>
    public readonly record struct BracketOutcome(bool PlayerWon, bool PlayerBrokeOut, bool OpponentBrokeOut);

    /// <summary>
    /// Bracket racing: each racer dials in the time they expect to run to the strip's finish (the quarter mile
    /// unless the format's marks say otherwise). The
    /// slower dial-in gets the green first, by the difference, so two cars on their dial-ins get to the quarter
    /// together, and the sharper leaver wins. Running quicker than your dial-in is a breakout, which loses, unless the
    /// other car broke out by more. The race mode runs the trees and judges the race the same way
    /// (mode.lua, bracketPlayerWins); the career decides from the slips.
    ///
    /// The player picks a dial-in, suggested from the car's best quarter. A rival dials in from its car: its best
    /// quarter when it has run one, else an estimate from its power and weight; the better the driver, the sharper
    /// it leaves and the closer to its dial-in it runs (it takes the stripe, as racers do).
    /// </summary>
    public static class BracketRules
    {
        /// <summary>The quarter mile, in metres: a strip's finish when its format gives no marks of its own</summary>
        public const double QuarterMetres = 402.336;

        /// <summary>
        /// A layout runs a finish when it is at least this share of it long: a quarter-mile strip may say "400 m" in its
        /// ui_track.json (400 is this share of the quarter)
        /// </summary>
        public const double StripShare = 400 / QuarterMetres;

        /// <summary>
        /// How an elapsed time grows with the strip's length: ET ∝ metres^this. The rule of thumb that an eighth-mile time
        /// is the quarter's over 1.55 (ln 1.55 / ln 2)
        /// </summary>
        public const double StripExponent = 0.632;

        /// <summary>Dial-ins go in steps of this, in seconds, and between these</summary>
        public const double DialInStep = 0.01, MinDialIn = 6, MaxDialIn = 30;

        /// <summary>What a car with no time and no figures dials in</summary>
        public const double UnknownCarDialIn = 15;

        /// <summary>A driver of the weight of a person in the car, in kg, for the estimate</summary>
        public const double DriverKg = 80;

        /// <summary>
        /// Hale's quarter-mile formula (ET = 5.825 × (lb/hp)^⅓) is for a car on slicks; a street car on street
        /// tyres, as AC's AI launches it, runs about 8% slower. Not yet checked against AC's AI (the user's to play).
        /// </summary>
        public const double StreetEtFactor = 6.29;

        /// <summary>
        /// The bracket race decided from the two slips; null when the player never got to the quarter (then the race
        /// is decided like any other, by what stopped the car). The mode decides on the same numbers, the slips'
        /// (rounded to the millisecond), added up in the same order, so both come to the same verdict.
        /// </summary>
        public static BracketOutcome? Decide(Timeslip? player, double playerDialIn, Timeslip? opponent, double opponentDialIn)
        {
            if (FinishOf(player) is not { } mine) return null;
            var playerOut = mine.Et < playerDialIn;

            if (FinishOf(opponent) is not { } theirs)
                return new BracketOutcome(!playerOut, playerOut, false);

            var opponentOut = theirs.Et < opponentDialIn;
            bool won;
            if (playerOut && opponentOut) won = playerDialIn - mine.Et < opponentDialIn - theirs.Et;
            else if (playerOut != opponentOut) won = opponentOut;
            else won = mine.At < theirs.At;
            return new BracketOutcome(won, playerOut, opponentOut);
        }

        /// <summary>
        /// When the car crossed the quarter, in seconds from AC's start, and its elapsed time; null when it didn't. The
        /// slip comes from the result file, which is not trusted: an elapsed time no car runs (0, negative, minutes) is
        /// read as no quarter at all, with the same bounds the car's history keeps its quarters in.
        /// </summary>
        public static (double At, double Et)? FinishOf(Timeslip? slip)
        {
            if (slip?.QuarterMileSeconds is not { } et || !double.IsFinite(et)
                || et < CarHistory.MinQuarterSeconds || et > CarHistory.MaxQuarterSeconds) return null;
            var green = slip.GreenSeconds is { } g && double.IsFinite(g) ? g : 0;
            var reaction = slip.ReactionSeconds is { } r && double.IsFinite(r) ? r : 0;
            return (green + reaction + et, et);
        }

        /// <summary>
        /// The dial-in the player is offered for a car on a strip that finishes at <paramref name="stripMetres"/>: its best
        /// quarter (made over for another strip's length, <see cref="ForStrip"/>), rounded up to the step; null when it has none
        /// </summary>
        public static double? SuggestDialIn(Car car, double stripMetres = QuarterMetres) =>
            car.History.BestQuarterSeconds is { } best ? Clamp(RoundUp(ForStrip(best, stripMetres))) : null;

        /// <summary>
        /// The time the car should run to a strip's finish (the quarter mile unless told), from its power and weight (kg,
        /// without a driver): the dial-in for a car that has never run one. Null without both figures.
        /// </summary>
        public static double? EstimateEt(double? powerHp, double? weightKg, double stripMetres = QuarterMetres)
        {
            if (powerHp is not { } hp || weightKg is not { } kg || !double.IsFinite(hp) || !double.IsFinite(kg) || hp <= 0 || kg <= 0)
                return null;
            var pounds = (kg + DriverKg) / Helpers.Conversion.KgPerLb;
            return Clamp(RoundUp(ForStrip(StreetEtFactor * Math.Cbrt(pounds / hp), stripMetres)));
        }

        /// <summary>
        /// The estimate for a car from what is known of it: the dyno figure of its parts (<see cref="Car.PowerHp"/>) or
        /// its spec sheet's power, and the spec sheet's weight; <see cref="UnknownCarDialIn"/> without them
        /// </summary>
        public static double EstimateFor(Car car, Models.Catalog.CarDefinition? definition, double stripMetres = QuarterMetres)
        {
            return EstimateEt(CarPower.Of(car, definition), StreetCorsa.Parts.Cars.AcSpecs.ParseWeight(definition?.Specs?.Weight), stripMetres)
                   ?? Clamp(ForStrip(UnknownCarDialIn, stripMetres));
        }

        /// <summary>A rival's dial-in: its car's best quarter when it has run one, else the estimate (<see cref="EstimateFor"/>)</summary>
        public static double RivalDialInFor(Car car, Models.Catalog.CarDefinition? definition, double stripMetres = QuarterMetres) =>
            SuggestDialIn(car, stripMetres) ?? EstimateFor(car, definition, stripMetres);

        /// <summary>A quarter-mile time made over for a strip that finishes at <paramref name="stripMetres"/> (<see cref="StripExponent"/>)</summary>
        public static double ForStrip(double quarterSeconds, double stripMetres) =>
            IsQuarter(stripMetres) ? quarterSeconds : quarterSeconds * Math.Pow(stripMetres / QuarterMetres, StripExponent);

        /// <summary>
        /// Where a format's strip finishes, in metres: its last mark (<see cref="RaceFormat.Marks"/>), else the quarter mile
        /// </summary>
        public static double StripMetres(RaceFormat? format) =>
            format?.Marks is { Count: RaceFormats.DragMarks } marks ? marks[^1] : QuarterMetres;

        /// <summary>Where the strip of the format of this id finishes (<see cref="StripMetres(RaceFormat?)"/>)</summary>
        public static double StripMetres(string? formatId) => StripMetres(RaceFormats.Current.Get(formatId));

        /// <summary>
        /// A finish that is the quarter mile, near enough (a strip's "400 m" is): its times are the car's quarters, the
        /// ones its history keeps
        /// </summary>
        public static bool IsQuarter(double stripMetres) => stripMetres >= QuarterMetres * StripShare && stripMetres <= QuarterMetres / StripShare;

        /// <summary>The bracket race as the race mode runs it: both dial-ins, and how the rival drives by its AI level</summary>
        public static BracketSetup Setup(double playerDialIn, double opponentDialIn, int opponentAiLevel)
        {
            var (reaction, margin) = RivalDriving(opponentAiLevel);
            return new BracketSetup(Clamp(playerDialIn), Clamp(opponentDialIn), reaction, margin);
        }

        /// <summary>
        /// How a rival of this AI level (<see cref="Opponents.OpponentAIAdapter"/>, 85 to 100) leaves and takes the
        /// stripe: seconds from its green to leaving, 0.10 for the best to 0.40; and how far over its dial-in it aims,
        /// 0.02 s to 0.14.
        /// </summary>
        public static (double Reaction, double Margin) RivalDriving(int aiLevel)
        {
            var below = Math.Clamp(100 - aiLevel, 0, 15);
            return (0.10 + below * 0.02, 0.02 + below * 0.008);
        }

        /// <summary>A dial-in as the game keeps it: in steps of <see cref="DialInStep"/>, within the bounds</summary>
        public static double Clamp(double dialIn) =>
            double.IsFinite(dialIn) ? Math.Round(Math.Clamp(dialIn, MinDialIn, MaxDialIn), 2) : UnknownCarDialIn;

        /// <summary>A layout's length from its ui_track.json, in metres: the layout's own, else the track's; null when neither is one</summary>
        public static double? LengthMetres(Models.AC.TrackInfo track, Models.AC.TrackConfiguration? layout) =>
            StreetCorsa.Parts.Cars.AcSpecs.ParseLength(string.IsNullOrWhiteSpace(layout?.Length) ? track.Length : layout.Length);

        /// <summary>
        /// A layout that runs to a strip's finish (the quarter mile unless told), for a bracket race, which the mode ends
        /// there: at least <see cref="StripShare"/> of it long
        /// </summary>
        public static bool RunsTheStrip(Models.AC.TrackInfo track, Models.AC.TrackConfiguration? layout, double stripMetres = QuarterMetres) =>
            LengthMetres(track, layout) >= stripMetres * StripShare - 1e-6;

        /// <summary>A time as the strip shows it: "12.45", in the game's culture like the timeslip ("12,45" in a German game)</summary>
        public static string Show(double? seconds) =>
            seconds is { } s && double.IsFinite(s) ? s.ToString("0.00", Helpers.Locale.Culture) : "-";

        private static double RoundUp(double seconds) => Math.Ceiling(seconds * 20 - 1e-9) / 20;
    }
}
