using StreetCorsa.Models.GameState;
using StreetCorsa.Parts.Cars;

namespace StreetCorsa.Screens.Shared
{
    /// <summary>
    /// A car copied for a worker thread: its figures and a copy of its part tree (the parts' ids kept). The screens make
    /// one on the UI thread before handing a car to <c>Task.Run</c>, because the workbench may change the car's own parts
    /// while the worker reads them. Only the history is shared, for reading: nothing else written to the copy reaches the car.
    /// </summary>
    public static class CarSnapshot
    {
        public static Car Of(Car car) => new(car.DefinitionId)
        {
            InstanceId = car.InstanceId,
            SkinId = car.SkinId,
            OdometerKM = car.OdometerKM,
            EngineHealth = car.EngineHealth,
            TransmissionHealth = car.TransmissionHealth,
            BodyCondition = car.BodyCondition,
            TireCondition = car.TireCondition,
            BodyDamageKmh = car.BodyDamageKmh?.ToArray() ?? new double[4],
            FuelLitres = car.FuelLitres,
            FuelTankLitres = car.FuelTankLitres,
            BodyDirt = car.BodyDirt,
            OilLevel = car.OilLevel,
            Parts = car.Parts.Select(p => PartTrees.Clone(p, keepIds: true)).ToList(),
            HasPartsAssigned = car.HasPartsAssigned,
            HasRunningGearAssigned = car.HasRunningGearAssigned,
            PowerHp = car.PowerHp,
            PurchasePrice = car.PurchasePrice,
            ImpoundedUntil = car.ImpoundedUntil,
            ImpoundFee = car.ImpoundFee,
            PurchaseDate = car.PurchaseDate,
            Warehouse = car.Warehouse,
            Bay = car.Bay,
            History = car.History
        };
    }
}
