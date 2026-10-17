using StreetCorsa.Models.Catalog;
using StreetCorsa.Parts.Sounds;
using StreetCorsa.Models.GameState;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Parts.Export;
using StreetCorsa.Parts.Logic;

namespace StreetCorsa.Services.Parts
{
    /// <summary>
    /// Ties the cars of the game to the parts catalog. The catalog, the dyno figures of its engine builds and the
    /// sound library are loaded when first asked for; <see cref="WarmUpAsync"/> does that ahead of time, off the UI thread.
    /// </summary>
    public interface ICarPartsService
    {
        PartsCatalog Catalog { get; }

        EngineBuildIndex Builds { get; }

        /// <summary>
        /// False when there are no converted parts to work with, or they could not be read; cars then simply
        /// have no part trees. Never throws.
        /// </summary>
        bool IsAvailable { get; }

        /// <summary>Loads the catalog and gives every car profile without a factory engine its suggestion</summary>
        Task WarmUpAsync();

        /// <summary>The engine build a car leaves the factory with: the profile's, else the best match</summary>
        RatedBuild? GetStockBuild(CarDefinition car);

        /// <summary>
        /// Gives a car that never had parts (an older save, a car made without them) its factory engine, worn
        /// like the car. True when the car changed.
        /// </summary>
        bool EnsureParts(Car car);

        /// <summary>
        /// <see cref="EnsureParts"/> for a screen: the engine is put together on a worker thread, the car is
        /// changed on the thread that called. Saving the car is the caller's, on that thread as well.
        /// </summary>
        Task<bool> EnsurePartsAsync(Car car);

        /// <summary>
        /// Brings the parts of a loaded save in step with the catalog: a pack replaced by a later release gave its
        /// parts new ids and moved some slots. Parts of the player that no longer fit where they were go on the
        /// shelf; a car whose engine the catalog no longer has gets its factory engine again; parts and cars for
        /// sale that are gone leave the offer. True when the save changed.
        /// </summary>
        bool BringUpToDate(GameState game);

        /// <summary>The engine of a car that has been around: the factory build, now and then worked on</summary>
        BuiltEngine? CreateUsedEngine(CarDefinition car, double condition);

        /// <summary>An engine in a few words, e.g. "Chevy 327-396 V8, 258 hp"</summary>
        string? Describe(PartInstance engine, EngineReport? report);

        /// <summary>The car's engine on the dyno; null for a car without one or without parts</summary>
        EngineReport? Evaluate(Car car);

        /// <summary>
        /// How the car's engine is cooled and oiled, for the race mode's heat and oil (<see cref="EngineCooling"/>),
        /// with <paramref name="engine"/> the car's engine on the dyno; null for a car without parts
        /// </summary>
        Models.Race.EngineCoolingRating? RateCooling(Car car, EngineReport? engine);

        /// <summary>
        /// How well an engine would be cooled in <paramref name="car"/> at a power, for a tuner weighing an upgrade
        /// (<see cref="Models.Race.EngineCoolingRating.Cooling"/>); null without parts
        /// </summary>
        Func<PartInstance, double, double>? EngineCoolingOf(Car car);

        /// <summary>Every engine sound there is: the library's and the installed cars' own, read once</summary>
        SoundLibrary Sounds { get; }

        /// <summary>
        /// The sound the car's engine races with; null when it keeps its own (no engine, no sounds, or the
        /// choice is the bank the car already has)
        /// </summary>
        CarSound? ChooseSound(Car car, EngineReport? report);

        /// <summary>Kilograms of the engine the car's Assetto Corsa data was made with: its factory build</summary>
        double? FactoryEngineMass(Car car);

        /// <summary>What the car's own Assetto Corsa data says about its running gear; null when it cannot be read</summary>
        AcCarSpecs? Specs(string carDefinitionId);

        /// <summary>Where the car carries its engine: as set in the catalog editor, or else guessed from its data</summary>
        EngineLayout EngineLayoutOf(string carDefinitionId);

        /// <summary>The running gear the car left the factory with, matched to its data; null without a catalog or data</summary>
        (RunningGearFactory.AxleParts Front, RunningGearFactory.AxleParts Rear)? FactoryRunningGear(Car car);

        /// <summary>The rear axle the car leaves the factory with (cars.json's picks, else the guess); null for a car whose rear wheels are not driven</summary>
        RearAxleChoice? FactoryAxle(string carDefinitionId);

        /// <summary>Where the car's engine is moved from its place by hand (cars.json); zero when it is not</summary>
        System.Numerics.Vector3 EngineOffsetOf(string carDefinitionId);

        /// <summary>
        /// Moves the car's engine from its place (the garage's placement mode): into the car book now, and into the base
        /// game's cars.json beside, off the caller's thread (a file that cannot be written is told in the log)
        /// </summary>
        void SetEngineOffset(string carDefinitionId, System.Numerics.Vector3 offset);

        /// <summary>
        /// Everything the car shows, in a race and in the game's scenes, that its own model does not have: its wheels, the
        /// engine parts that stand out (<see cref="ShownEngineParts"/>), how used it looks (<see cref="CarLooks"/>) and the
        /// colour it was painted; null when it shows nothing but its model
        /// </summary>
        ShownSet? Shown(Car car) => Shown(car.DefinitionId, car.Parts, CarLooks.Of(car), CornerHeights.Of(CarLooks.RideHeight(car)),
            PaintColour.Parse(car.PaintColour));

        /// <summary>A car for sale: as <see cref="Shown(Car)"/>, for the listing's car</summary>
        ShownSet? Shown(UsedCarListing listing) => Shown(listing.CarDefinitionId, listing.Parts, CarLooks.Of(listing),
            CornerHeights.Of(CarLooks.RideHeight(listing)), PaintColour.Parse(listing.PaintColour));

        /// <summary>
        /// What a car of <paramref name="carDefinitionId"/> with <paramref name="parts"/> shows, looking as
        /// <paramref name="look"/> says (a car for sale: <see cref="CarLooks.Of(UsedCarListing)"/>), painted
        /// <paramref name="paint"/> over its skin's colours
        /// </summary>
        ShownSet? Shown(string carDefinitionId, List<PartInstance> parts, CarLook? look = null, CornerHeights? heights = null, PaintColour? paint = null);

        /// <summary>
        /// Forgets what <see cref="Shown(string, List{PartInstance}, CarLook?, CornerHeights?, PaintColour?)"/> keeps of
        /// the cars' factory builds: the catalog editor has changed a car's factory engine
        /// </summary>
        void ForgetFactoryBuilds() { }
    }
}
