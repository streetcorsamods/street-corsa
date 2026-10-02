using StreetCorsa.Logging;
using StreetCorsa.Models.GameState;
using StreetCorsa.Services.Catalog;
using StreetCorsa.Services.News;
using StreetCorsa.Services.Opponents;
using StreetCorsa.Services.Simulation;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Services.Scheduler.Tasks
{
    /// <summary>
    /// Simulates AI races daily when time advances
    /// </summary>
    public class RaceSimulatorTask : IScheduledTask
    {
        private readonly RaceSimulatorService _simulatorService;
        private readonly IContentCatalogRepository? _catalogRepo;
        private readonly IAppLogger _logger;
        private readonly Random _random = new();

        public string TaskId => "race_simulator";
        public int IntervalDays => 1;

        /// <param name="catalogRepo">Names the cars in the street talk; null and they go by their folder names</param>
        public RaceSimulatorTask(RaceSimulatorService simulatorService, IContentCatalogRepository? catalogRepo = null)
        {
            _simulatorService = simulatorService;
            _catalogRepo = catalogRepo;
            _logger = AppLoggerFactory.CreateLogger("RaceSimulatorTask");
        }

        public Task ExecuteAsync(GameState gameState, DateTime currentDate)
        {
            _logger.Information("Running daily race simulation for date {Date}", currentDate);

            var result = _simulatorService.SimulateDay(gameState, currentDate);

            _logger.Information(
                "Race simulation complete. Races simulated: {TotalRaces}",
                result.TotalRaces);

            // What the street talks about, and the paper writes up: cars changing hands and cars wrecked
            var talk = new List<string>();
            var news = new List<NewsArticle>();
            var carName = _catalogRepo == null ? (Func<string, string>)(id => id) : CarNames.Book(_catalogRepo);
            foreach (var race in result.Races)
            {
                var car = race.LoserCar == null ? "car" : carName(race.LoserCar.DefinitionId);
                if (race.IsPinkSlip)
                {
                    _logger.Information("Pink slip race! {Winner} won {Loser}'s car", race.WinnerName, race.LoserName);
                    talk.Add(GameText.Installed.Pick(race.LoserCrashed ? "street.rivals.pink_wreck" : "street.rivals.pink", Random.Shared, Values(race, car)));
                }
                else if (race.LoserCrashed)
                {
                    talk.Add(GameText.Installed.Pick("street.rivals.wreck", Random.Shared, Values(race, car)));
                }

                var loserPossessive = gameState.Racers.Find(race.LoserName) is { } loser ? OpponentRules.Possessive(loser) : "their";
                if (NewsWriter.RivalRace(currentDate, race.WinnerName, race.LoserName, loserPossessive, car, race.RaceType == "Drag",
                        race.IsPinkSlip, race.LoserCrashed, race.LoserCar?.History.Wins ?? 0, _random) is { } article)
                    news.Add(article);
            }

            // Kept as they were, so a rival talked to can tell them true
            Talk.TalkPeople.RecordRivalRaces(gameState, result.Races.Select(race => new RivalRaceRecord
            {
                Date = currentDate,
                Winner = race.WinnerName,
                Loser = race.LoserName,
                LoserCar = race.LoserCar?.DefinitionId,
                RaceType = race.RaceType,
                PinkSlip = race.IsPinkSlip,
                Crashed = race.LoserCrashed
            }));

            OpponentLifeService.AddTalk(gameState, currentDate, talk);
            NewsWriter.Add(gameState, currentDate, news);
            return Task.CompletedTask;
        }

        private static TextValues Values(Simulation.SimulatedRaceResult race, string car) => new()
        {
            ["winner"] = race.WinnerName,
            ["loser"] = race.LoserName,
            ["car"] = car,
            ["raceType"] = race.RaceType.ToLowerInvariant()
        };
    }
}
