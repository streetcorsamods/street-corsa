using StreetCorsa.Models.Catalog;
using StreetCorsa.Models.GameState;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Services.Text;
using StreetCorsa.Helpers;

namespace StreetCorsa.Screens.Diner
{
    /// <summary>
    /// View model for displaying an opponent in the list
    /// </summary>
    public class OpponentDisplayViewModel
    {
        public Opponent Opponent { get; set; } = new();
        public CarDefinition CarDefinition { get; set; } = new();
        public string? PortraitPath { get; set; }

        /// <summary>How tough the opponent is for the player; the view picks the colours for it</summary>
        public OpponentDifficulty Difficulty { get; set; }

        public string Name => Opponent.Name;

        /// <summary>Lost a pink slip to the player and wants a rematch: they take any pink-slip race with the player</summary>
        public bool WantsRematch => Opponent.Grudge != null;

        /// <summary>What the badge says under a rival who wants a rematch</summary>
        public string RematchDisplay => Opponent.Grudge is { } grudge ? GameText.Say("diner.rematch", ("until", grudge.Until)) : string.Empty;

        /// <summary>A regular of a diner above the player's car: they will not race them here (<see cref="Services.Diner.DinerBook.Snubs(Models.Diners.Diner, Models.GameState.Opponent, double?)"/>; never the King or a rival out for a rematch)</summary>
        public bool IsSnubbing { get; set; }

        /// <summary>What the badge says under a regular who will not race the player's car</summary>
        public string SnubDisplay => GameText.Say("diner.snub.badge");
        public string Nickname => Opponent.Nickname;
        public string CarDisplay => $"{CarDefinition.Brand} {CarDefinition.Name}";
        public string CarBrand => CarDefinition.Brand;
        public string CarName => CarDefinition.Name;
        public int Reputation => Opponent.Stats.Reputation;
        public string ReputationTier => Opponent.Stats.GetReputationTier();
        public string RecordDisplay => GameText.Say("diner.record.line", ("wins", Opponent.Stats.Wins), ("losses", Opponent.Stats.Losses));

        // Drag race record display
        public string DragRecordDisplay => Opponent.Stats.DragRaces > 0
            ? GameText.Say("diner.record.line", ("wins", Opponent.Stats.DragWins), ("losses", Opponent.Stats.DragLosses))
            : GameText.Say("diner.record.none");

        // Road race record display
        public string RoadRecordDisplay => Opponent.Stats.RoadRaces > 0
            ? GameText.Say("diner.record.line", ("wins", Opponent.Stats.RoadWins), ("losses", Opponent.Stats.RoadLosses))
            : GameText.Say("diner.record.none");

        public bool HasPortrait => !string.IsNullOrEmpty(PortraitPath);

        /// <summary>
        /// Width the portrait is decoded at: the largest place it is shown is 150 px, and twice that keeps it sharp on a
        /// high-DPI screen
        /// </summary>
        private const int PortraitDecodeWidth = 300;

        /// <summary>The portrait, decoded small and shared (<see cref="Shared.Thumbnails"/>); null without one</summary>
        public System.Windows.Media.ImageSource? PortraitImage => Shared.Thumbnails.Load(PortraitPath, PortraitDecodeWidth);

        // Additional record stats
        public string WinRateDisplay
        {
            get
            {
                var winRate = Opponent.Stats.Races > 0
                    ? (Opponent.Stats.Wins * 100.0 / Opponent.Stats.Races)
                    : 0.0;
                return $"{winRate:F0}%";
            }
        }

        public string PinkSlipsDisplay
        {
            get
            {
                var won = Opponent.Stats.PinkSlipsWon;
                var lost = Opponent.Stats.PinkSlipsLost;
                return GameText.Say("diner.record.line", ("wins", won), ("losses", lost));
            }
        }

        // Car stats
        public string CarYear => CarDefinition.Year.HasValue ? CarDefinition.Year.Value.ToString() : "";

        /// <summary>
        /// The car's power: its dyno figure when it has one, else its spec sheet's (<see cref="Models.GameState.CarPower"/>),
        /// the same figure the matchup beside it shows
        /// </summary>
        public string CarPower
        {
            get
            {
                var power = Models.GameState.CarPower.Of(Opponent.Cars.FirstOrDefault(), CarDefinition) ?? 0;
                return power > 0 ? Measure.Power(power).ToString("N0", null) : "";
            }
        }

        public string CarWeight
        {
            get
            {
                var weight = AcSpecs.ParseWeight(CarDefinition.Specs?.Weight) ?? 0;
                return weight > 0 ? Measure.Mass(weight).ToString("N0", null) : "";
            }
        }

        public string CarDrivetrain => CarDefinition.Specs?.Drivetrain ?? "";
    }

    public enum OpponentDifficulty
    {
        Easy,
        Matched,
        Hard
    }
}
