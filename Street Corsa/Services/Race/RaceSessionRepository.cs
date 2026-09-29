using LiteDB;
using StreetCorsa.Logging;
using StreetCorsa.Models.Race;
using StreetCorsa.Services.Storage;
using System.IO;
using System.Runtime.CompilerServices;

namespace StreetCorsa.Services.Race
{
    /// <summary>
    /// Repository for managing processed race sessions in LiteDB
    /// Stores sessions in the save's database file, through the same open instance as the game state
    /// (<see cref="SaveDatabase"/>): the two never open the file at once, and a race's record can be written
    /// in the same transaction as the state it changed.
    /// Collection: "ProcessedRaceSessions"
    /// </summary>
    public class RaceSessionRepository : IRaceSessionRepository
    {
        private const string Collection = "ProcessedRaceSessions";

        private readonly SaveDatabase _database;
        private readonly IAppLogger _logger;

        public RaceSessionRepository(SaveDatabase database)
        {
            _database = database;
            _logger = AppLoggerFactory.CreateLogger(LogCategory.RaceIngestion);
        }

        public async Task SaveAsync(string saveName, ProcessedRaceSession session)
        {
            await Task.Run(() =>
            {
                try
                {
                    _database.Use(saveName, db => Save(db, session));
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to save session {SessionId}", session.SessionId);
                    throw new InvalidOperationException($"Failed to save session: {ex.Message}", ex);
                }
            });
        }

        public void Save(LiteDatabase database, ProcessedRaceSession session)
        {
            // SessionId is the document's _id: the upsert is keyed on it
            Sessions(database).Upsert(session);
            _logger.Debug("Saved processed session {SessionId} to database", session.SessionId);
        }

        public Task<ProcessedRaceSession?> GetBySessionIdAsync(string saveName, string sessionId) =>
            Task.Run(() => Read(saveName, db => db.GetCollection<ProcessedRaceSession>(Collection).FindById(sessionId)));

        public async Task<bool> IsProcessedAsync(string saveName, string sessionId) =>
            await GetBySessionIdAsync(saveName, sessionId) != null;

        public Task<bool> IsContextSettledAsync(string saveName, Guid contextId) =>
            Task.Run(() => Read(saveName, db =>
                // A save with no sessions yet has none settled: the read does not create the collection for that
                db.CollectionExists(Collection)
                && Sessions(db).Exists(Query.EQ(nameof(ProcessedRaceSession.RaceContextId), contextId))));

        // The open databases whose index has been made sure of: EnsureIndex takes the collection's write lock every
        // time it is called, so it is called once per database opened, not on every ask
        private static readonly ConditionalWeakTable<LiteDatabase, object> Indexed = new();

        /// <summary>
        /// The sessions, with the race context indexed: "is this race settled" is asked for every result file on
        /// every pass, and without the index each ask reads every session the save has
        /// </summary>
        private static ILiteCollection<ProcessedRaceSession> Sessions(LiteDatabase database)
        {
            var sessions = database.GetCollection<ProcessedRaceSession>(Collection);
            if (!Indexed.TryGetValue(database, out _))
            {
                sessions.EnsureIndex(x => x.RaceContextId);
                Indexed.AddOrUpdate(database, Indexed);
            }

            return sessions;
        }

        public async Task<List<ProcessedRaceSession>> GetAllAsync(string saveName)
        {
            try
            {
                return await Task.Run(() =>
                    Read(saveName, db => db.GetCollection<ProcessedRaceSession>(Collection).FindAll().ToList()) ?? new List<ProcessedRaceSession>());
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error retrieving all sessions");
                return new List<ProcessedRaceSession>();
            }
        }

        public async Task<int> GetCountAsync(string saveName)
        {
            try
            {
                return await Task.Run(() => Read(saveName, db => db.GetCollection<ProcessedRaceSession>(Collection).Count()));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error getting session count");
                return 0;
            }
        }

        /// <summary>A read of a save that has no file yet finds nothing, and does not create it</summary>
        private T? Read<T>(string saveName, Func<LiteDatabase, T> read)
        {
            if (!File.Exists(_database.PathOf(saveName)))
                return default;
            return _database.Use(saveName, read);
        }
    }
}
