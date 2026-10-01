using LiteDB;
using StreetCorsa.Models.Catalog;

namespace StreetCorsa.Services.Catalog
{
    /// <summary>
    /// LiteDB implementation of car profile repository
    /// Shares catalog.db with CarDefinitions but uses separate collection.
    /// A profile read through <see cref="GetProfile"/> or <see cref="GetAllProfiles"/> carries what the mods give the car
    /// (<see cref="StreetCorsa.Parts.Cars.CarBook"/>, cars.json) over what was worked out: the stored profile is never changed by
    /// it, so a mod taken out leaves the car as the game works it out. The writes take the profile as stored.
    /// </summary>
    public class CarProfileRepository : ICarProfileRepository
    {
        private readonly string _databasePath;
        private const string ProfilesCollection = "catalog_carprofiles";

        /// <summary>The indexes are made by the first write of the session; after that they are there</summary>
        private bool _indexesEnsured;

        public CarProfileRepository()
        {
            _databasePath = CatalogDatabase.DefaultPath;
        }

        private void EnsureIndexes(ILiteCollection<CarProfile> collection)
        {
            if (_indexesEnsured) return;

            collection.EnsureIndex(x => x.DealerPrecedence);
            _indexesEnsured = true;
        }

        public CarProfile? GetProfile(string carDefinitionId)
        {
            using var db = CatalogDatabase.Open(_databasePath);
            var collection = db.GetCollection<CarProfile>(ProfilesCollection);
            return Given(collection.FindById(carDefinitionId));
        }

        public CarProfile GetOrCreateProfile(string carDefinitionId, Func<CarProfile> createDefault)
        {
            var existing = GetProfile(carDefinitionId);
            if (existing != null)
                return existing;

            var newProfile = createDefault();
            UpsertProfile(newProfile);
            return Given(newProfile)!;
        }

        public void UpsertProfile(CarProfile profile)
        {
            using var db = CatalogDatabase.Open(_databasePath);
            var collection = db.GetCollection<CarProfile>(ProfilesCollection);

            EnsureIndexes(collection);
            collection.Upsert(profile);
        }

        public void UpsertProfiles(IEnumerable<CarProfile> profiles)
        {
            using var db = CatalogDatabase.Open(_databasePath);
            var collection = db.GetCollection<CarProfile>(ProfilesCollection);

            EnsureIndexes(collection);
            collection.Upsert(profiles);
        }

        public bool UpdateProfile(string carDefinitionId, Func<CarProfile, CarProfile?> change)
        {
            using var db = CatalogDatabase.Open(_databasePath);
            var collection = db.GetCollection<CarProfile>(ProfilesCollection);

            var stored = collection.FindById(carDefinitionId);
            return stored != null && change(stored) is { } changed && collection.Update(changed);
        }

        public int MergeProfiles(IEnumerable<CarProfile> created, IEnumerable<KeyValuePair<string, Func<CarProfile, CarProfile?>>> changes)
        {
            using var db = CatalogDatabase.Open(_databasePath);
            var collection = db.GetCollection<CarProfile>(ProfilesCollection);
            EnsureIndexes(collection);

            var stored = 0;

            // One transaction for the lot, as one Upsert was: a batch of hundreds is one write, not hundreds
            db.BeginTrans();
            try
            {
                foreach (var profile in created)
                {
                    if (collection.FindById(profile.CarDefinitionId) != null) continue;
                    collection.Insert(profile);
                    stored++;
                }

                foreach (var (id, change) in changes)
                {
                    if (collection.FindById(id) is { } current && change(current) is { } changed && collection.Update(changed))
                        stored++;
                }

                db.Commit();
            }
            catch
            {
                db.Rollback();
                throw;
            }

            return stored;
        }

        public List<CarProfile> GetAllProfiles()
        {
            using var db = CatalogDatabase.Open(_databasePath);
            var collection = db.GetCollection<CarProfile>(ProfilesCollection);
            return [.. collection.FindAll().Select(Given).OfType<CarProfile>()];
        }

        /// <summary>
        /// The profile with what the mods give its car laid over it: the price, how common it is, the factory engine
        /// (picked, so no suggestion takes its place) and the engine layout
        /// </summary>
        public static CarProfile? Given(CarProfile? profile)
        {
            if (profile == null || StreetCorsa.Parts.Cars.CarBook.Current.Car(profile.CarDefinitionId) is not { } given) return profile;

            if (given.Price is { } price) profile.BasePrice = price;
            if (given.Common is { } common) profile.DealerPrecedence = common;
            if (given.Engine is { Length: > 0 } engine)
            {
                profile.StockEngineBuildId = engine;
                profile.StockEngineIsManual = true;
            }

            if (given.EngineLayout is { } layout) profile.EngineLayout = layout;
            return profile;
        }

        public bool ProfileExists(string carDefinitionId)
        {
            using var db = CatalogDatabase.Open(_databasePath);
            var collection = db.GetCollection<CarProfile>(ProfilesCollection);
            return collection.Exists(x => x.CarDefinitionId == carDefinitionId);
        }

        public int GetProfileCount()
        {
            using var db = CatalogDatabase.Open(_databasePath);
            var collection = db.GetCollection<CarProfile>(ProfilesCollection);
            return collection.Count();
        }
    }
}
