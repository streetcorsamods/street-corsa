using StreetCorsa.Models.GameState;

namespace StreetCorsa.Services.CarLot
{
    /// <summary>
    /// Where the player's cars are kept: warehouses numbered from 1, each with as many bays as its scene has
    /// (<see cref="WarehouseScene.Capacity"/>). There is no limit: a car that does not fit in the first warehouse goes in
    /// the second, and so on.
    ///
    /// A car keeps its place (<see cref="Car.Warehouse"/>, <see cref="Car.Bay"/>) from one visit to the next, so the
    /// player finds it where they left it. A car without one (just bought, won, an older save) takes the first free bay,
    /// counting from warehouse 1. A car that changes hands gives its place up (<see cref="Car.ClearPlace"/>), so one
    /// that comes back to the player is a car without one. A place is kept only while it is real and nobody else's: of
    /// two cars in one bay, the one the player has had longer (earlier in <see cref="Racer.Cars"/>) keeps it.
    ///
    /// The car the player drives (<see cref="Player.SelectedCarInstanceId"/>) is not in a warehouse: it is in the garage,
    /// and has no place. Taking another car out swaps them (<see cref="TakeOut"/>): the one from the garage is parked in
    /// the bay the other leaves, so no bay stands empty for a car that is out. A car in the police impound
    /// (<see cref="Car.IsImpounded"/>) keeps its bay, or is given one like any other, for when it is collected; until
    /// then it is not standing in it (<see cref="In"/>).
    /// </summary>
    public static class CarStorage
    {
        /// <summary>
        /// Gives every car but the one in the garage (<paramref name="inGarage"/>) a place, keeping those it has; the one
        /// in the garage gives its place up. <paramref name="capacityOf"/> is how many bays warehouse n (from 1) has;
        /// always at least one. True when a car was given a new place or gave one up.
        /// </summary>
        public static bool Arrange(IReadOnlyList<Car> cars, Func<int, int> capacityOf, Guid? inGarage)
        {
            var taken = new HashSet<(int Warehouse, int Bay)>();
            var homeless = new List<Car>();
            var changed = false;

            foreach (var car in cars)
            {
                if (car.InstanceId == inGarage)
                {
                    changed |= car.Warehouse != null || car.Bay != null;
                    car.Warehouse = null;
                    car.Bay = null;
                    continue;
                }

                if (car.Warehouse is { } w && car.Bay is { } b && w >= 1 && b >= 0 && b < Capacity(capacityOf, w) && taken.Add((w, b)))
                    continue;

                homeless.Add(car);
            }

            var warehouse = 1;
            var bay = 0;
            foreach (var car in homeless)
            {
                while (taken.Contains((warehouse, bay)))
                {
                    bay++;
                    if (bay < Capacity(capacityOf, warehouse)) continue;
                    warehouse++;
                    bay = 0;
                }

                car.Warehouse = warehouse;
                car.Bay = bay;
                taken.Add((warehouse, bay));
            }

            return changed || homeless.Count > 0;
        }

        /// <summary>
        /// The player takes <paramref name="car"/> out to the garage, and the car that was there
        /// (<paramref name="fromGarage"/>, null when there was none) is parked in the bay it leaves
        /// </summary>
        public static void TakeOut(Car car, Car? fromGarage)
        {
            if (fromGarage != null && fromGarage != car)
            {
                fromGarage.Warehouse = car.Warehouse;
                fromGarage.Bay = car.Bay;
            }

            car.Warehouse = null;
            car.Bay = null;
        }

        /// <summary>
        /// <paramref name="car"/> goes into the garage and the car that stood there to the car lot: into the bay the new one
        /// leaves, or, when it had none (just bought), the first free one. Wherever a car is taken in, the same way.
        /// </summary>
        public static void ToGarage(Player player, Car car, Func<int, int> capacityOf)
        {
            var fromGarage = player.Cars.FirstOrDefault(c => c.InstanceId == player.SelectedCarInstanceId && c != car);
            TakeOut(car, fromGarage);
            player.SelectedCarInstanceId = car.InstanceId;
            if (fromGarage is { Warehouse: null } or { Bay: null }) Arrange(player.Cars, capacityOf, car.InstanceId);
        }

        /// <summary>How many bays warehouse <paramref name="number"/> (from 1) has: its scene's, the installed ones in turn</summary>
        public static int CapacityOf(IReadOnlyList<WarehouseScene> scenes, int number) =>
            scenes.Count == 0 ? FallbackCapacity : scenes[(number - 1) % scenes.Count].Capacity;

        /// <summary>The bays of a warehouse when no scene is installed: the car lot is a list then</summary>
        public const int FallbackCapacity = 10;

        /// <summary>
        /// How many warehouses the player has: up to the last one with a car in it, and always the first (an empty one when
        /// they have no car). A warehouse emptied between two full ones stays, and is filled first.
        /// </summary>
        public static int WarehouseCount(IEnumerable<Car> cars) => Math.Max(1, cars.Select(c => c.Warehouse ?? 0).DefaultIfEmpty(0).Max());

        /// <summary>The cars standing in warehouse <paramref name="number"/>, by bay: not those at the impound</summary>
        public static IReadOnlyList<Car> In(IEnumerable<Car> cars, int number) =>
            cars.Where(c => c.Warehouse == number && c.Bay != null && !c.IsImpounded).OrderBy(c => c.Bay).ToList();

        private static int Capacity(Func<int, int> capacityOf, int warehouse) => Math.Max(1, capacityOf(warehouse));
    }
}
