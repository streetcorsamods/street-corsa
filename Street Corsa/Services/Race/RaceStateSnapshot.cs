using LiteDB;
using StreetCorsa.Models.GameState;
using StreetCorsa.Models.Race;
using StreetCorsa.Services.Storage;

namespace StreetCorsa.Services.Race
{
    /// <summary>
    /// The parts of the game state a race changes, as the save would store them. Restoring replaces them with
    /// fresh objects read back from that copy: the same round trip a save and a load make.
    /// </summary>
    internal sealed class RaceStateSnapshot
    {
        // Its own mapper (made like the one the saves use), used under a lock: LiteDB builds a type's
        // mapping on first use and a second thread can see it half-built, which would make a snapshot that
        // silently drops members. The shared global mapper is also used by the catalog and the saves on
        // other threads.
        private static readonly BsonMapper Mapper = SaveMapper.Create();

        private BsonDocument _player = null!;
        private BsonDocument _racers = null!;
        private BsonDocument _career = null!;
        private RaceContext? _pendingRace;
        private DateTime _lastPlayed;

        // Never changed once written: the lists are copied, the items shared
        private List<StreetTalkItem> _streetTalk = null!;
        private List<NewsArticle> _news = null!;

        public static RaceStateSnapshot Take(GameState gameState)
        {
            lock (Mapper)
            {
                return new RaceStateSnapshot
                {
                    _player = Mapper.ToDocument(gameState.Player),
                    _racers = Mapper.ToDocument(gameState.Racers),
                    _career = Mapper.ToDocument(gameState.Career),
                    _pendingRace = gameState.PendingRace,
                    _lastPlayed = gameState.LastPlayedDate,
                    _streetTalk = [.. gameState.StreetTalk ?? []],
                    _news = [.. gameState.News ?? []]
                };
            }
        }

        public void Restore(GameState gameState)
        {
            // All three are read back before any is replaced: a restore that fails leaves the state as it is
            Player player;
            RacerCollection racers;
            Models.GameState.CareerState career;
            lock (Mapper)
            {
                player = Mapper.ToObject<Player>(_player);
                racers = Mapper.ToObject<RacerCollection>(_racers);
                career = Mapper.ToObject<Models.GameState.CareerState>(_career);
            }

            gameState.Player = player;
            gameState.Racers = racers;
            gameState.Career = career;
            gameState.PendingRace = _pendingRace;
            gameState.LastPlayedDate = _lastPlayed;
            gameState.StreetTalk = [.. _streetTalk];
            gameState.News = [.. _news];
        }
    }
}
