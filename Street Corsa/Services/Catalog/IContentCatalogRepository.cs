using StreetCorsa.Models.Catalog;

namespace StreetCorsa.Services.Catalog
{
    /// <summary>
    /// Repository for managing the content catalog (cars and tracks)
    /// </summary>
    public interface IContentCatalogRepository
    {
        // Car operations
        void UpsertCar(CarDefinition car);
        void UpsertCars(IEnumerable<CarDefinition> cars);
        CarDefinition? GetCar(string id);
        List<CarDefinition> GetAllCars();
        List<CarDefinition> GetCarsByStatus(ContentStatus status);
    }
}
