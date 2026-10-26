using LiteDB;
using StreetCorsa.Models.Catalog;

namespace StreetCorsa.Services.Catalog
{
    /// <summary>
    /// LiteDB implementation of content catalog repository.
    /// catalog.db is the catalog's own database (%AppData%\StreetCorsa\Catalog), apart from the game saves, kept open
    /// for the life of the game (<see cref="CatalogDatabase"/>)
    /// </summary>
    public class ContentCatalogRepository : IContentCatalogRepository
    {
        private readonly string _databasePath;
        private const string CarsCollection = "catalog_cars";

        /// <summary>The indexes are made by the first write of the session; after that they are there</summary>
        private bool _indexesEnsured;

        public ContentCatalogRepository() : this(CatalogDatabase.DefaultPath)
        {
        }

        /// <param name="databasePath">The catalog's file: the game's own, or a test's</param>
        internal ContentCatalogRepository(string databasePath)
        {
            _databasePath = databasePath;
        }

        private void EnsureIndexes(ILiteCollection<CarDefinition> collection)
        {
            if (_indexesEnsured) return;

            collection.EnsureIndex(x => x.Brand);
            collection.EnsureIndex(x => x.Source);
            collection.EnsureIndex(x => x.Status);
            _indexesEnsured = true;
        }

        private T Cars<T>(Func<ILiteCollection<CarDefinition>, T> work) =>
            CatalogDatabase.Use(_databasePath, db => work(db.GetCollection<CarDefinition>(CarsCollection)));

        public void UpsertCar(CarDefinition car) => Cars(collection =>
        {
            EnsureIndexes(collection);
            return collection.Upsert(car);
        });

        public void UpsertCars(IEnumerable<CarDefinition> cars) => Cars(collection =>
        {
            EnsureIndexes(collection);
            return collection.Upsert(cars);
        });

        public CarDefinition? GetCar(string id) => Cars(collection => collection.FindById(id));

        public List<CarDefinition> GetAllCars() => Cars(collection => collection.FindAll().ToList());

        public List<CarDefinition> GetCarsByStatus(ContentStatus status) => Cars(collection => collection.Find(x => x.Status == status).ToList());
    }
}
