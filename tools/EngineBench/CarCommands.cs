using System.Diagnostics;
using StreetCorsa.Models.GameState;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Parts.Export;
using StreetCorsa.Parts.Logic;

namespace StreetCorsa;

/// <summary>
/// The cars and what the parts make for them: the factory engine (cars), used engines (tune), the workbench (bench), an
/// older save's engines (renew), the running gear (gear, axles), where the engine sits (layout), and prices
/// </summary>
public static partial class Program
{
    /// <summary>The engine each Assetto Corsa car would get, with the runners-up</summary>
    private static int Cars(PartsCatalog catalog, string carsFolder)
    {
        var index = EngineBuildIndex.Create(catalog);
        Console.WriteLine($"{index.Runnable.Count} builds run\n");

        foreach (var folder in AcCarFolder.InstalledCars(carsFolder))
        {
            if (!File.Exists(AcCarUi.PathIn(folder))) continue;

            // Mod cars often ship a ui_car.json that is not quite JSON: the car is reported and the run goes on
            try
            {
                var ui = AcCarUi.TryRead(folder, out var error);
                if (ui == null)
                {
                    Console.WriteLine($"{Path.GetFileName(folder)}: {error}");
                    continue;
                }

                var brand = AcCarUi.GetString(ui, "brand") ?? "";
                var name = AcCarUi.GetString(ui, "name") ?? "";
                var bhp = AcSpecs.ParsePower(AcCarUi.GetString(AcCarUi.GetObject(ui, "specs"), "bhp"));

                Console.WriteLine($"{Path.GetFileName(folder)}: {brand} | {name} | {bhp:0} bhp  (family '{CarBook.Current.FamilyOfCar(Path.GetFileName(folder), brand, name)}')");
                foreach (var match in StockEngineMatcher.Rank(index, Path.GetFileName(folder), brand, name, bhp).Take(4))
                    Console.WriteLine($"    {match.Score,6:0.00}  {match.Build.PowerHp,4:0} hp {match.Build.Litres,5:0.00} l  [{match.Build.Family,-6}] {match.Build.Build.Name}  ({match.Build.Build.Id})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{Path.GetFileName(folder)}: {ex.Message}");
            }
        }

        return 0;
    }

    /// <summary>
    /// What the parts cost new, kind by kind (the dyno's price book), and what every installed car costs new: its body by
    /// its weight, its factory engine and running gear
    /// </summary>
    private static int Prices(PartsCatalog catalog, string? carsFolder)
    {
        var watch = Stopwatch.StartNew();
        var index = EngineBuildIndex.Create(catalog);
        var book = PartPriceBook.Create(catalog, index);
        PartPricing.Book = () => book;
        Console.WriteLine($"{index.Runnable.Count} builds run, {book.Measured} parts measured on the dyno in {watch.ElapsedMilliseconds} ms\n");

        Console.WriteLine($"  {"kind",-18} {"parts",5} {"least",8} {"middle",8} {"most",8}   dearest");
        foreach (var kind in catalog.Parts.Values.GroupBy(PartKinds.GroupOf).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var priced = kind.Select(p => (Part: p, Price: PartPricing.NewPrice(p))).OrderBy(p => p.Price).ToList();
            var dearest = priced[^1];
            Console.WriteLine($"  {kind.Key,-18} {priced.Count,5} {priced[0].Price,8:0} {priced[priced.Count / 2].Price,8:0} {dearest.Price,8:0}   " +
                              $"{dearest.Part.Id} ({book.PowerOf(dearest.Part.Id) ?? 0:0} hp)");
        }

        if (carsFolder == null) return 0;

        // Not a folder: the parts whose id holds it, each with its power and price
        if (!Directory.Exists(carsFolder))
        {
            Console.WriteLine();
            foreach (var part in catalog.Parts.Values.Where(p => p.Id.Contains(carsFolder, StringComparison.OrdinalIgnoreCase)).OrderBy(p => p.Id))
                Console.WriteLine($"  {part.Id,-60} {PartKinds.GroupOf(part),-18} {book.PowerOf(part.Id) ?? 0,6:0.0} hp  ${PartPricing.NewPrice(part):0}");
            return 0;
        }

        Console.WriteLine($"\n  {"car",-36} {"kg",5} {"hp",4} {"hp/t",5} {"body",7} {"engine",7} {"gear",6} {"new",7}   factory engine");
        var rows = new List<(string Line, double Total)>();
        foreach (var (folder, specs) in InstalledCarSpecs(carsFolder))
        {
            var carId = Path.GetFileName(folder);
            var build = index.Get(CarBook.Current.Car(carId)?.Engine) ?? FirstMatch(index, folder, carId);
            var (facts, picks) = (FactsOf(folder), CarBook.Current.Car(carId)?.RunningGear);
            var axle = RunningGearGuess.ChooseAxle(catalog, specs, facts, picks);
            var gear = RunningGearFactory.Create(catalog, specs, 1, axle, facts, picks).SelectMany(p => p.SelfAndDescendants())
                .Select(p => catalog.Get(p.DefinitionId)).OfType<PartDefinition>();
            var car = CarPricing.New(catalog, specs.TotalMass, build, gear, build == null ? UiPower(folder) : null);
            var power = build?.PowerHp ?? 0;
            rows.Add(($"  {carId,-36} {specs.TotalMass,5:0} {power,4:0} {power * 1000 / specs.TotalMass,5:0} {car.Body,7:0} {car.Engine,7:0} {car.RunningGear,6:0} {car.Total,7:0}   {build?.Build.Name ?? "none"}", car.Total));
        }

        foreach (var (line, _) in rows.OrderBy(r => r.Total)) Console.WriteLine(line);
        return 0;
    }

    /// <summary>The build the engine matcher ranks first for a car without one in cars.json</summary>
    private static RatedBuild? FirstMatch(EngineBuildIndex index, string folder, string carId)
    {
        var ui = AcCarUi.TryRead(folder, out _);
        if (ui == null) return null;

        var brand = AcCarUi.GetString(ui, "brand") ?? "";
        var name = AcCarUi.GetString(ui, "name") ?? "";
        var bhp = AcSpecs.ParsePower(AcCarUi.GetString(AcCarUi.GetObject(ui, "specs"), "bhp"));
        return StockEngineMatcher.Rank(index, carId, brand, name, bhp).FirstOrDefault()?.Build;
    }

    private static int Tune(PartsCatalog catalog, string id, int count)
    {
        var index = EngineBuildIndex.Create(catalog);
        var stock = FindBuild(index.Runnable, b => b.Build.Id, id, " that runs");
        if (stock == null) return 1;

        Console.WriteLine($"{stock.Build.Name}: {stock.PowerHp:0} hp, family '{stock.Family}'\n");
        var random = new Random(1);
        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; i < count; i++)
        {
            var level = random.NextDouble();
            var engine = EngineFactory.CreateTuned(catalog, index, stock, 0.8, level, random);
            if (engine == null) continue;

            Console.WriteLine($"  level {level:0.00}: {engine.Report.Dyno?.MaxPowerHp,4:0} hp  ${PartPricing.WorthOfAssembly(catalog, engine.Root),6:0}  " +
                              $"{(engine.IsModified ? string.Join(", ", engine.Changes) : "stock")}{(engine.Report.Runs ? "" : "  DOES NOT RUN: " + engine.Report.Problem)}");
        }

        Console.WriteLine($"\n{stopwatch.Elapsed.TotalMilliseconds / count:0} ms per engine");
        return 0;
    }

    /// <summary>Every part of a build comes off and has to find its way back to where it was</summary>
    private static int Bench(PartsCatalog catalog, string id)
    {
        var index = EngineBuildIndex.Create(catalog);
        var stock = FindBuild(index.Runnable, b => b.Build.Id, id, " that runs");
        if (stock == null) return 1;

        var engine = EngineFactory.CreateStock(catalog, stock, 1.0, new Random(1));
        if (engine == null)
        {
            Console.WriteLine($"No build '{id}' that runs");
            return 1;
        }

        var car = new List<PartInstance> { engine.Root };
        var failures = 0;
        foreach (var part in engine.Root.SelfAndDescendants().Skip(1).ToList())
        {
            var parent = PartTrees.FindParent(engine.Root, part)!;
            var (slot, own) = (part.ParentSlot, part.OwnSlot);

            Workbench.Remove(car, part);
            var without = EngineFactory.Evaluate(catalog, engine.Root);
            var places = Workbench.FindPlaces(catalog, car, part);
            var back = places.FirstOrDefault(p => ReferenceEquals(p.Parent, parent) && p.ParentSlot == slot);
            var definition = catalog.Get(part.DefinitionId)!;
            var parentDefinition = catalog.Get(parent.DefinitionId)!;
            var alternatives = catalog.FindMountable(parentDefinition, parentDefinition.Slots.First(s => s.Id == slot));

            Console.WriteLine($"  {(back == null ? "LOST" : "ok  ")} {definition.DisplayName ?? definition.Id,-46} {places.Count} place(s), " +
                              $"{alternatives.Count} part(s) fit its slot{(alternatives.Any(a => a.Part == definition) ? "" : "  (not itself!)")}");
            Console.WriteLine($"         without it: {(without is { Runs: true } ? $"{without.Dyno!.MaxPowerHp:0} hp" : without?.Problem)}  (CR {without?.Dyno?.Compression:0.0})");
            if (back == null) failures++;

            Workbench.Mount(car, back ?? new MountPlace(parent, slot, own), part);
        }

        var after = EngineFactory.Evaluate(catalog, engine.Root);
        Console.WriteLine($"\n{failures} part(s) could not go back; engine makes {after?.Dyno?.MaxPowerHp:0} hp (was {engine.Report.Dyno?.MaxPowerHp:0})");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>
    /// What a save made with older parts goes through: every engine of the old catalog that holds parts
    /// the current one knows by another id is brought up to date and has to run again with all its parts on.
    /// </summary>
    private static int Renew(PartsCatalog catalog, string oldPartsFolder)
    {
        // The old catalog is worked by the new one's makes and groups: it is the new game that loads the old save
        var old = LoadCatalog(oldPartsFolder, useContent: false);
        var failures = 0;
        var renewed = 0;

        // A parts folder of before the parts had classes and specs of their own reads, but none of its engines runs on
        // this model: there is nothing to check, which is no pass
        var runnable = EngineBuildIndex.Create(old).Runnable;
        if (runnable.Count == 0)
        {
            Console.WriteLine($"No engine of {oldPartsFolder} runs ({old.Problems.Count} problem(s) reading it): nothing was checked. " +
                              "A parts folder of the format before classes and specs cannot be read by this model.");
            foreach (var problem in old.Problems.Take(10)) Console.WriteLine($"  {problem}");
            return 1;
        }

        foreach (var stock in runnable)
        {
            var engine = EngineFactory.CreateStock(old, stock, 1.0, new Random(1));
            if (engine == null) continue;

            var parts = engine.Root.SelfAndDescendants().Count();
            var loose = new List<PartInstance>();
            if (!SavedParts.BringUpToDate(catalog, engine.Root, loose)) continue;

            renewed++;
            var gone = engine.Root.SelfAndDescendants().Concat(loose.SelectMany(l => l.SelfAndDescendants())).Count(p => catalog.Get(p.DefinitionId) == null);
            var after = EngineFactory.Evaluate(catalog, engine.Root);
            var runs = after is { Runs: true };
            if (!runs || gone > 0) failures++;

            Console.WriteLine($"  {(runs && gone == 0 ? "ok  " : "FAIL")} {Short(stock.Build.Id),-48} {stock.PowerHp,4:0} hp -> " +
                              $"{(runs ? $"{after!.Dyno!.MaxPowerHp,4:0} hp" : after?.Problem ?? "no engine")}  {parts} parts, {loose.Count} came off, {gone} unknown");
            foreach (var part in loose) Console.WriteLine($"         off: {catalog.Get(part.DefinitionId)?.DisplayName ?? part.DefinitionId}");
        }

        Console.WriteLine($"\n{renewed} engine(s) brought up to date, {failures} with problems");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>What the game knows of a car for the parts' came_in: its make, family, country (the make's, else its ui's) and year</summary>
    private static CarFacts FactsOf(string folder)
    {
        var id = Path.GetFileName(Path.TrimEndingDirectorySeparator(folder));
        var ui = AcCarUi.TryRead(folder, out _);
        var (brand, name) = (AcCarUi.GetString(ui, "brand"), AcCarUi.GetString(ui, "name"));
        var book = CarBook.Current;
        var make = book.MakeOf(id, brand, name);
        var country = book.CountryOfMake(make) ?? (AcCarUi.GetString(ui, "country") is { Length: > 0 } named ? book.Country(named) : null);
        return new CarFacts(make, book.FamilyOfCar(id, brand, name), AcCarUi.GetInt(ui, "year"), country);
    }

    /// <summary>The power a car's ui_car.json gives, bhp; null when it gives none</summary>
    private static double? UiPower(string folder) =>
        AcSpecs.ParsePower(AcCarUi.GetString(AcCarUi.GetObject(AcCarUi.TryRead(folder, out _), "specs"), "bhp"));

    /// <summary>Every installed car with a ui_car.json and the data it reads; one line for each car whose data does not read</summary>
    private static IEnumerable<(string Folder, AcCarSpecs Specs)> InstalledCarSpecs(string carsFolder)
    {
        foreach (var folder in AcCarFolder.InstalledCars(carsFolder))
        {
            if (!File.Exists(Path.Combine(folder, "ui", "ui_car.json"))) continue;

            AcCarSpecs specs;
            try { specs = AcCarSpecs.Read(AcCarDataReader.ForCar(folder)); }
            catch (Exception ex) { Console.WriteLine($"{Path.GetFileName(folder)}: {ex.Message}"); continue; }

            yield return (folder, specs);
        }
    }

    /// <summary>Where every car carries its engine, as guessed from its weight and the wheels it drives</summary>
    private static int Layout(string carsFolder)
    {
        foreach (var (folder, specs) in InstalledCarSpecs(carsFolder))
        {
            Console.WriteLine($"{EngineLayouts.Guess(specs),-5}  {specs.FrontWeightShare,5:0.000} front  {specs.Traction ?? "?",-4}  {Path.GetFileName(folder)}");
        }

        return 0;
    }

    /// <summary>The running gear every car leaves the factory with, matched to its own data</summary>
    private static int Gear(PartsCatalog catalog, string carsFolder)
    {
        foreach (var (folder, specs) in InstalledCarSpecs(carsFolder))
        {
            var facts = FactsOf(folder);
            Console.WriteLine($"{Path.GetFileName(folder)}: {facts.Make ?? "?"} {facts.Year} ({facts.Country ?? "?"}), {specs.TotalMass:0} kg, {specs.FrontWeightShare:0%} front");
            if (RunningGearFactory.Choose(catalog, specs, facts, CarBook.Current.Car(Path.GetFileName(folder))?.RunningGear) is not var (front, rear))
            {
                Console.WriteLine("    no running gear in the catalog");
                continue;
            }

            foreach (var (axle, parts, name) in new[] { (specs.Front, front, "front"), (specs.Rear, rear, "rear") })
            {
                Console.WriteLine($"  {name}: {axle.SuspensionType ?? "?"}, tyre {axle.TyreWidth * 1000:0}/{axle.TyreRadius:0.000}/{axle.RimRadius:0.000} m, brake {axle.BrakeTorque:0} Nm, spring {axle.SpringRate:0} N/m, damp {axle.DampBump:0}/{axle.DampRebound:0}, sway bar {axle.SwayBarRate:0} N/m, {axle.CornerLoad:0} kg/wheel");
                Console.WriteLine($"    tyre   {RunningGear.TyreWidth(parts.Tyre) * 1000:0}/{RunningGear.TyreRadius(parts.Tyre):0.000}/{RunningGear.TyreRimRadius(parts.Tyre):0.000} m  grip {RunningGear.TyreGrip(parts.Tyre):0.00}  {parts.Tyre.Id}");
                Console.WriteLine($"    rim    {RunningGear.RimWidth(parts.Rim):0.0}\" offset {RunningGear.RimOffset(parts.Rim) * 1000:0} mm, {parts.Rim.Mass:0.0} kg  {parts.Rim.Id}");
                Console.WriteLine($"    brake  {parts.BrakeTorque:0} Nm  {parts.Brake.DisplayName ?? parts.Brake.Id}" +
                                  (parts.Shoes == null ? "" : $", {parts.Shoes.DisplayName}, {parts.Cylinder?.DisplayName}") +
                                  (RunningGear.Heat(parts.Brake, parts.Shoes) is { } heat ? $"  fade {heat.TorqueK:0.00} C/kJ {heat.Curve}" : "  no fade of its own"));
                Console.WriteLine($"    spring {RunningGear.SpringRate(parts.Spring):0} N/m for {RunningGear.SpringDesignLoad(parts.Spring):0} kg  {parts.Spring.DisplayName ?? parts.Spring.Id}");
                Console.WriteLine($"    shock  {RunningGear.Damping(parts.Shock).Bump:0}/{RunningGear.Damping(parts.Shock).Rebound:0}  {parts.Shock.DisplayName ?? parts.Shock.Id}");
                Console.WriteLine(parts.SwayBar is { } bar ? $"    bar    {RunningGear.SwayBarRate(bar):0} N/m  {bar.DisplayName ?? bar.Id}" : "    bar    none");
            }
        }

        return 0;
    }

    /// <summary>
    /// The rear axle every car leaves the factory with (cars.json's picks, else the guess from the parts' "came_in"),
    /// beside what its own data says: the rear suspension, the final drive, the differential and the engine's peak torque
    /// </summary>
    private static int Axles(PartsCatalog catalog, string carsFolder)
    {
        var housings = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (folder, specs) in InstalledCarSpecs(carsFolder))
        {
            // The game's cars, when the mods name any
            var id = Path.GetFileName(folder);
            if (CarBook.Current.Cars.Count > 0 && CarBook.Current.Car(id) == null) continue;
            var ui = AcCarUi.TryRead(folder, out _);
            var (brand, name, year) = (AcCarUi.GetString(ui, "brand"), AcCarUi.GetString(ui, "name"), AcCarUi.GetInt(ui, "year"));
            var facts = FactsOf(folder);
            var (power, coast, preload) = specs.Differential;
            Console.WriteLine($"{id}: {brand} {name} {year}, make {facts.Make ?? "?"} ({facts.Family}), {specs.Traction ?? "?"}, rear {specs.Rear.SuspensionType ?? "?"}, " +
                              $"{specs.PeakTorque:0} Nm, final {specs.FinalRatio:0.00}, diff {power:0.00}/{coast:0.00}/{preload:0}, brake temps {(specs.Front.HasBrakeTemps ? "front" : "-")}/{(specs.Rear.HasBrakeTemps ? "rear" : "-")}");

            var picks = CarBook.Current.Car(id)?.RunningGear;
            if (RunningGearGuess.ChooseAxle(catalog, specs, facts, picks) is not { } axle)
            {
                Console.WriteLine(specs.DrivesRear ? "    NO AXLE in the catalog for it" : "    no rear axle: the rear wheels are not driven");
                continue;
            }

            var (lockPower, lockCoast, lockPreload) = RearAxle.Lock(axle.Carrier);
            Console.WriteLine($"    {axle.Housing.DisplayName}{(picks?.Axle != null ? " (picked)" : "")}, {RearAxle.Strength(axle.Housing):0} Nm");
            Console.WriteLine($"    {axle.Carrier.DisplayName}, {lockPower:0.00}/{lockCoast:0.00}/{lockPreload:0}; {axle.Gears.DisplayName}; {axle.Shaft.DisplayName}");
            housings[axle.Housing.DisplayName ?? axle.Housing.Id] = housings.GetValueOrDefault(axle.Housing.DisplayName ?? axle.Housing.Id) + 1;
        }

        Console.WriteLine();
        foreach (var (housing, count) in housings.OrderByDescending(h => h.Value)) Console.WriteLine($"{count,4}  {housing}");
        return 0;
    }
}
