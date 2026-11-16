using StreetCorsa.Logging;
using StreetCorsa.Models.GameState;

namespace StreetCorsa.Services.Career
{
    /// <summary>
    /// The AI rivals a Challenge road names (<see cref="ChallengeRoad.Rivals"/>), experimental: one-off entrants as an
    /// event's are, named from the names' pool, in the cars the road names or else in cars of the player's car's tier
    /// (open or not: a rival may well drive what the player has yet to open), each car once while there are enough
    /// </summary>
    public static class ChallengeRivals
    {
        private static readonly IAppLogger Logger = AppLoggerFactory.CreateLogger("Challenges");

        /// <param name="cars">The game's cars by tier (<see cref="Showroom.Cars"/>)</param>
        /// <param name="playerTier">The tier of the player's car</param>
        /// <param name="name">A rival's name, rolled</param>
        public static List<ChallengeRival> Pick(ChallengeRoad road, IReadOnlyList<ShowroomCar> cars, int playerTier, Func<Random, string> name, Random random)
        {
            var count = road.Rivals ?? 0;
            if (count <= 0 || cars.Count == 0) return [];

            List<ShowroomCar> pool;
            if (road.RivalCars is { Count: > 0 } named)
            {
                pool = named.Select(id => cars.FirstOrDefault(c => string.Equals(c.Definition.Id, id, StringComparison.OrdinalIgnoreCase)))
                    .OfType<ShowroomCar>().ToList();
                foreach (var missing in named.Where(id => pool.All(c => !string.Equals(c.Definition.Id, id, StringComparison.OrdinalIgnoreCase))))
                    Logger.Warning("A Challenge road's rival car {Car} is not installed or not in the game: left out", missing);
            }
            else
            {
                pool = cars.Where(c => c.Tier == playerTier).ToList();
            }

            if (pool.Count == 0) pool = [.. cars];

            var rivals = new List<ChallengeRival>();
            var left = new List<ShowroomCar>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < count; i++)
            {
                if (left.Count == 0) left = [.. pool];
                var car = left[random.Next(left.Count)];
                left.Remove(car);

                var rolled = name(random);
                for (var tries = 0; tries < 10 && !names.Add(rolled); tries++) rolled = name(random);
                var skins = car.Definition.AvailableSkins;
                rivals.Add(new ChallengeRival(
                    rolled,
                    car.Definition.Id,
                    skins is { Count: > 0 } ? skins[random.Next(skins.Count)] : "default",
                    road.RivalSkill ?? random.Next(Opponent.MinSkill, Opponent.MaxSkill + 1),
                    road.RivalAggression ?? 40 + random.Next(30)));
            }

            return rivals;
        }
    }

    /// <summary>A Challenge road's rival: a one-off entrant, their car and skin, how they drive</summary>
    public sealed record ChallengeRival(string Name, string CarId, string Skin, int Skill, int Aggression);
}
