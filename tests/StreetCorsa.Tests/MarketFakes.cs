using StreetCorsa.Models.Catalog;
using StreetCorsa.Models.GameState;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Parts.Export;
using StreetCorsa.Parts.Logic;
using StreetCorsa.Parts.Sounds;
using StreetCorsa.Services.Catalog;
using StreetCorsa.Services.Market;
using StreetCorsa.Services.Parts;
using StreetCorsa.Services.Time;

namespace StreetCorsa.Tests;

/// <summary>
/// A market that values every car at <see cref="Value"/> and writes down what is put on a lot, for the services
/// that sell cars (the player's sales, the rivals' days)
/// </summary>
internal sealed class FakeMarket(decimal value, List<DealerLocation>? dealers = null) : IUsedCarMarketService
{
    public decimal Value { get; set; } = value;
    public List<(Car Car, decimal Price, string Location)> Listed { get; } = [];

    public decimal ValueOf(Car car) => Value;
    public string TradeInLocation(IReadOnlyList<DealerLocation>? known, Car? car = null) => "industrial_motors";
    public List<DealerLocation> GetDefaultDealers() => dealers ?? [];

    public UsedCarListing ListCar(Car car, decimal price, string location, DateTime listedDate, bool describeEngine = true)
    {
        Listed.Add((car, price, location));
        return new UsedCarListing { CarDefinitionId = car.DefinitionId, Price = price, DealerLocation = location, ListedDate = listedDate };
    }

    public EngineDescription? DescribeEngine(Car car) => null;
    public IReadOnlySet<string>? FactoryParts(string carDefinitionId) => null;

    public Task<List<UsedCarListing>> SpawnListingsAsync(List<DealerLocation> dealers, DateTime currentDate, double priceMultiplier) => throw new NotSupportedException();
    /// <summary>What a refresh does with the listings there are; unset, a refresh is not expected</summary>
    public Func<List<UsedCarListing>, Task<List<UsedCarListing>>>? Refresh { get; set; }

    public Task<List<UsedCarListing>> RefreshMarketAsync(List<UsedCarListing> currentListings, List<DealerLocation> dealers, DateTime currentDate, double priceMultiplier) =>
        Refresh?.Invoke(currentListings) ?? throw new NotSupportedException();
    public List<UsedCarListing> GetAvailableListings(List<UsedCarListing> allListings) => allListings;
    public List<UsedCarListing> GetListingsByDealer(List<UsedCarListing> allListings, string dealerLocationId) => allListings;
}

/// <summary>
/// No parts catalog: the cars are judged on their own figures, the body shop still works. Every member is virtual, so
/// a fake that differs in one thing (<see cref="GatedParts"/>, <see cref="CatalogParts"/>) says only that
/// </summary>
internal class NullCarPartsService : ICarPartsService
{
    public virtual PartsCatalog Catalog => throw new NotSupportedException();
    public virtual EngineBuildIndex Builds => throw new NotSupportedException();
    public virtual bool IsAvailable => false;
    public virtual Task WarmUpAsync() => Task.CompletedTask;
    public virtual RatedBuild? GetStockBuild(CarDefinition car) => null;
    public virtual bool EnsureParts(Car car) => false;
    public virtual Task<bool> EnsurePartsAsync(Car car) => Task.FromResult(false);
    public virtual bool BringUpToDate(GameState game) => false;
    public virtual BuiltEngine? CreateUsedEngine(CarDefinition car, double condition) => null;
    public virtual string? Describe(PartInstance engine, EngineReport? report) => null;
    public virtual EngineReport? Evaluate(Car car) => null;
    public virtual StreetCorsa.Models.Race.EngineCoolingRating? RateCooling(Car car, EngineReport? engine) => null;
    public virtual Func<PartInstance, double, double>? EngineCoolingOf(Car car) => null;
    public virtual SoundLibrary Sounds => throw new NotSupportedException();
    public virtual CarSound? ChooseSound(Car car, EngineReport? report) => null;
    public virtual double? FactoryEngineMass(Car car) => null;
    public virtual AcCarSpecs? Specs(string carDefinitionId) => null;
    public virtual EngineLayout EngineLayoutOf(string carDefinitionId) => EngineLayout.Front;
    public virtual (RunningGearFactory.AxleParts Front, RunningGearFactory.AxleParts Rear)? FactoryRunningGear(Car car) => null;
    public virtual System.Numerics.Vector3 EngineOffsetOf(string carDefinitionId) => System.Numerics.Vector3.Zero;
    public virtual void SetEngineOffset(string carDefinitionId, System.Numerics.Vector3 offset) { }
    public virtual ShownSet? Shown(string carDefinitionId, List<PartInstance> parts, CarLook? look = null, CornerHeights? heights = null) => null;
}

/// <summary>No parts catalog</summary>
internal sealed class NoParts : NullCarPartsService;

/// <summary>No parts catalog, and putting parts on a car takes until the test says so</summary>
internal sealed class GatedParts : NullCarPartsService
{
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override async Task<bool> EnsurePartsAsync(Car car)
    {
        await Release.Task;
        return false;
    }
}

/// <summary>The real parts catalog and nothing else</summary>
internal sealed class CatalogParts : NullCarPartsService
{
    private static readonly Lazy<PartsCatalog> Loaded = new(() => PartsCatalog.Load(RepoPaths.PartsFolder));
    private static readonly Lazy<EngineBuildIndex> Index = new(() => EngineBuildIndex.Create(Loaded.Value));

    public override PartsCatalog Catalog => Loaded.Value;
    public override EngineBuildIndex Builds => Index.Value;
    public override bool IsAvailable => true;
}

/// <summary>A catalog without a car in it</summary>
internal sealed class NoCars : IContentCatalogRepository
{
    public void UpsertCar(CarDefinition car) { }
    public void UpsertCars(IEnumerable<CarDefinition> cars) { }
    public CarDefinition? GetCar(string id) => null;
    public List<CarDefinition> GetAllCars() => [];
    public List<CarDefinition> GetCarsByStatus(ContentStatus status) => [];
    public bool CarExists(string id) => false;
    public void UpdateCarStatus(string id, ContentStatus status) { }
    public void MarkAllCarsAsLegacy() { }
    public void DeleteBrokenCars() { }
    public int GetCarCount() => 0;
}

/// <summary>Time that passes without a scheduler behind it; a fake that keeps count overrides the spending</summary>
internal class QuietTime : IGameTimeService
{
    public int DayStartHour => GameState.DayStartHour;
    public int DayEndHour => GameState.DayEndHour;
    public virtual Task<TimeSpendResult> SpendTimeAsync(GameState gameState, GameAction action) => Task.FromResult(new TimeSpendResult());
    public Task<TimeSpendResult> SpendTimeAsync(GameState gameState, int minutes) => Task.FromResult(new TimeSpendResult());
    public int GetRemainingMinutesToday(GameState gameState) => 600;
    public double GetRemainingHoursToday(GameState gameState) => 10;
    public string GetFormattedTime(GameState gameState) => string.Empty;
    public string GetFormattedDate(GameState gameState) => string.Empty;
    public bool HasTimeFor(GameState gameState, GameAction action) => true;
    public Task<TimeSpendResult> EndDayAsync(GameState gameState) => Task.FromResult(new TimeSpendResult());
}

/// <summary>A car catalog held in memory: whatever the test puts in <see cref="Cars"/>; counts its reads</summary>
internal sealed class MemoryCatalog : IContentCatalogRepository
{
    public Dictionary<string, CarDefinition> Cars { get; } = new(StringComparer.OrdinalIgnoreCase);
    public int Reads { get; private set; }

    public void UpsertCar(CarDefinition car) => Cars[car.Id] = car;
    public void UpsertCars(IEnumerable<CarDefinition> cars) { foreach (var car in cars) UpsertCar(car); }
    public CarDefinition? GetCar(string id) { Reads++; return Cars.GetValueOrDefault(id); }
    public List<CarDefinition> GetAllCars() { Reads++; return [.. Cars.Values]; }
    public List<CarDefinition> GetCarsByStatus(ContentStatus status) { Reads++; return [.. Cars.Values.Where(c => c.Status == status)]; }
}

/// <summary>Car profiles held in memory; counts its reads</summary>
internal sealed class MemoryProfiles : ICarProfileRepository
{
    public Dictionary<string, CarProfile> Profiles { get; } = new(StringComparer.OrdinalIgnoreCase);
    public int Reads { get; private set; }

    public CarProfile? GetProfile(string carDefinitionId) { Reads++; return Profiles.GetValueOrDefault(carDefinitionId); }
    public CarProfile GetOrCreateProfile(string carDefinitionId, Func<CarProfile> createDefault) =>
        Profiles.TryGetValue(carDefinitionId, out var profile) ? profile : Profiles[carDefinitionId] = createDefault();
    public void UpsertProfile(CarProfile profile) => Profiles[profile.CarDefinitionId] = profile;
    public void UpsertProfiles(IEnumerable<CarProfile> profiles) { foreach (var profile in profiles) UpsertProfile(profile); }

    public bool UpdateProfile(string carDefinitionId, Func<CarProfile, CarProfile?> change)
    {
        if (!Profiles.TryGetValue(carDefinitionId, out var stored) || change(stored) is not { } changed) return false;
        Profiles[carDefinitionId] = changed;
        return true;
    }

    public int MergeProfiles(IEnumerable<CarProfile> created, IEnumerable<KeyValuePair<string, Func<CarProfile, CarProfile?>>> changes)
    {
        var stored = 0;
        foreach (var profile in created)
        {
            if (Profiles.TryAdd(profile.CarDefinitionId, profile)) stored++;
        }

        foreach (var (id, change) in changes)
        {
            if (UpdateProfile(id, change)) stored++;
        }

        return stored;
    }

    public List<CarProfile> GetAllProfiles() { Reads++; return [.. Profiles.Values]; }
    public bool ProfileExists(string carDefinitionId) => Profiles.ContainsKey(carDefinitionId);
    public int GetProfileCount() => Profiles.Count;
}
