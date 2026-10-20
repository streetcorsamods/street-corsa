using StreetCorsa.Models.Race;
using StreetCorsa.Services.Configuration.Models;

namespace StreetCorsa.Services.Configuration.Models
{
    /// <summary>
    /// Base class for all Assetto Corsa launch intents.
    /// Launch intents represent high-level user actions that generate one or more configuration intents.
    /// </summary>
    public abstract class LaunchIntent
    {
        /// <summary>
        /// The executable to launch (acs.exe or acShowroom.exe)
        /// </summary>
        public abstract string Executable { get; }

        /// <summary>
        /// Description of what this launch intent will do
        /// </summary>
        public abstract string Description { get; }

        /// <summary>
        /// Generate all required modification intents for this launch
        /// </summary>
        public abstract IEnumerable<ModificationIntent> GetConfigurationIntents();

        /// <summary>
        /// Optional metadata for tracking execution context
        /// </summary>
        public Dictionary<string, object> Metadata { get; set; } = new();

        /// <summary>
        /// Data files the cars of this launch race with, as their parts make them, by car folder. Put into
        /// the install for the launch and taken out after it.
        /// </summary>
        public List<Race.RaceCarData> CarData { get; set; } = new();
    }

    /// <summary>
    /// Intent to launch a full race session with specified configuration
    /// </summary>
    public class RaceLaunchIntent : LaunchIntent
    {
        public string CarId { get; set; } = string.Empty;
        public string SkinId { get; set; } = string.Empty;
        public string TrackId { get; set; } = string.Empty;
        public string? TrackConfig { get; set; }

        public override string Executable => "acs.exe";

        public override string Description =>
            $"Launch race with car '{CarId}' on track '{TrackId}'";

        public override IEnumerable<ModificationIntent> GetConfigurationIntents()
        {
            yield return new RaceConfigIntent
            {
                CarId = this.CarId,
                SkinId = this.SkinId,
                TrackId = this.TrackId,
                TrackConfig = this.TrackConfig
            };

            // Future: Add DisableAssistsIntent, OpponentIntent, etc.
        }
    }

    /// <summary>
    /// Intent to take a car out alone: a practice session on a track, ended by the player
    /// </summary>
    public class FreeRunLaunchIntent : LaunchIntent
    {
        public string CarId { get; set; } = string.Empty;
        public string SkinId { get; set; } = string.Empty;
        public string PlayerName { get; set; } = string.Empty;
        public string TrackId { get; set; } = string.Empty;
        public string? TrackConfig { get; set; }

        /// <summary>
        /// What the car shows that its model does not have (never the factory's: a stock car has none); null runs without a
        /// mode, as does a run whose mode cannot go in (no CSP)
        /// </summary>
        public StreetCorsa.Parts.Cars.ShownSet? Shown { get; set; }

        /// <summary>Whether the free run needs its mode, sc_free: the car shows parts its model does not have</summary>
        public bool HasShownParts => Shown != null;

        public override string Executable => "acs.exe";

        public override string Description => $"Free run: {CarId} on {TrackId}";

        public override IEnumerable<ModificationIntent> GetConfigurationIntents()
        {
            yield return new FreeRunIntent
            {
                CarId = CarId,
                Skin = SkinId,
                PlayerName = PlayerName,
                TrackId = TrackId,
                TrackConfig = TrackConfig,
                Shown = Shown
            };
        }
    }

    /// <summary>
    /// Intent to launch a race: two cars (or the player alone, a test-and-tune) in the game's race mode. The race as
    /// race.ini gets it is one record, <see cref="Race"/>, which is the configuration intent itself (no copy between
    /// the two, so a field added to the race is added once); what is here besides is what the launch means to the
    /// game and race.ini never sees: the stakes, whose cars they are, where the player comes back to.
    /// </summary>
    public class DragRaceLaunchIntent : LaunchIntent
    {
        /// <summary>The race as race.ini gets it: the cars, the drivers, the track, the format, the AI, the damage, the police</summary>
        public DragRaceIntent Race { get; init; } = new();

        /// <summary>
        /// Player's car instance ID (for result correlation)
        /// </summary>
        public Guid PlayerCarInstanceId { get; set; }

        /// <summary>
        /// Opponent's car instance ID (for result correlation)
        /// </summary>
        public Guid OpponentCarInstanceId { get; set; }

        /// <summary>
        /// Cash wager amount (0 if no wager)
        /// </summary>
        public decimal CashWager { get; set; }

        /// <summary>
        /// Whether this is a pink slip race (loser gives up their car)
        /// </summary>
        public bool IsPinkSlip { get; set; }

        /// <summary>
        /// The flow id of the screen the race was set up from, where the player comes back to after it (or where its
        /// <c>after_race</c> way out leads); none: home
        /// </summary>
        public string? ReturnTo { get; set; }

        /// <summary>The diner the race was set up at (<see cref="Models.Diners.Diner.Id"/>), to come back to; null elsewhere</summary>
        public string? ReturnDiner { get; set; }

        // What the launcher and the screens read of the race, off the one record
        public string PlayerCarId => Race.PlayerCarId;
        public string PlayerName => Race.PlayerName;
        public string OpponentCarId => Race.OpponentCarId;
        public string OpponentName => Race.OpponentName;
        public string TrackId => Race.TrackId;
        public string? TrackConfig => Race.TrackConfig;

        /// <summary>The race's format id (<see cref="Services.Race.RaceFormats"/>)</summary>
        public string Format => Race.Format;

        /// <summary>The shape the player's car starts in (damage from earlier races); null starts it as AC would</summary>
        public RaceStartState? PlayerStart => Race.PlayerStart;

        /// <summary>The shape the opponent's car starts in; null starts it as AC would</summary>
        public RaceStartState? OpponentStart => Race.OpponentStart;

        /// <summary>Test-and-tune: the player alone on the strip for this many passes; null for a race</summary>
        public int? TunePasses => Race.TunePasses;

        /// <summary>The time the session takes in the game</summary>
        public Time.GameAction SessionAction => TunePasses != null ? Time.GameAction.TestAndTune
            : Services.Race.RaceFormats.Current.IsDrag(Format) ? Time.GameAction.DragRace
            : Time.GameAction.RoadRace;

        public override string Executable => "acs.exe";

        public override string Description => TunePasses != null
            ? $"Launch test-and-tune: {PlayerName} on {TrackId}"
            : $"Launch race: {PlayerName} vs {OpponentName}";

        /// <summary>The race itself, with the context id the launch carries written into it</summary>
        public override IEnumerable<ModificationIntent> GetConfigurationIntents()
        {
            Race.ContextId = Metadata.TryGetValue("RaceContext", out var context) && context is RaceContext raceContext
                ? raceContext.ContextId
                : null;
            yield return Race;
        }
    }
}
