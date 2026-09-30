using StreetCorsa.Models.GameState;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Screens.Shared;
using StreetCorsa.ViewModels;

namespace StreetCorsa.Dialogs.CarInfo
{
    /// <summary>One line of the car's sheet: "Mileage", "48,210 km"</summary>
    public sealed record CarInfoLine(string Label, string Value);

    /// <summary>
    /// One part in the car, as the sheet lists it: indented under what it is mounted on, with its wear (the bar) and
    /// its damage, if any
    /// </summary>
    public sealed record CarInfoPart(string Name, string Kind, int Depth, double Wear, double Tear)
    {
        /// <summary>The indent of the part's name, under what it is mounted on</summary>
        public System.Windows.Thickness Indent => new(Depth * 18, 0, 0, 0);

        /// <summary>"82%", or "82%, damaged (40% left)"</summary>
        public string ConditionDisplay => Tear < 0.995 ? $"{Wear * 100:0}%, damaged ({Tear * 100:0}% left)" : $"{Wear * 100:0}%";

        /// <summary>What the bar shows: the part's shape, wear and damage together</summary>
        public double Condition => Helpers.Unit.Clamp01(Wear, 0) * Helpers.Unit.Clamp01(Tear, 0);

        public bool IsWorn => Condition < 0.4;
    }

    /// <summary>
    /// Everything about one car, read-only: its overall shape, the mileage, what is in the tank and the sump, the body,
    /// the engine, where it came from and how it has raced, and every part in it with the part's condition
    /// </summary>
    public class CarInfoDialogViewModel : BaseDialogViewModel
    {
        private readonly DialogService _dialogService;

        public string Title { get; }
        public IReadOnlyList<CarInfoLine> Lines { get; }
        public IReadOnlyList<CarInfoPart> Parts { get; }
        public bool HasParts => Parts.Count > 0;
        public string History { get; }
        public bool HasHistory => History.Length > 0;
        public RelayCommand CloseCommand { get; }

        /// <param name="catalog">The parts catalog, for the parts' names; null when the parts are not available</param>
        /// <param name="engine">The car's engine as the garage worked it out, if it has</param>
        public CarInfoDialogViewModel(DialogService dialogService, Car car, string title, PartsCatalog? catalog, Audio.EngineSpec? engine)
        {
            _dialogService = dialogService;
            Title = title;
            Lines = LinesOf(car, catalog, engine);
            Parts = catalog == null ? Array.Empty<CarInfoPart>() : PartsOf(car, catalog);
            History = CarHistoryDisplay.Summary(car.History, forSale: false);
            CloseCommand = new RelayCommand(() => _dialogService.CloseDialog());
        }

        private static List<CarInfoLine> LinesOf(Car car, PartsCatalog? catalog, Audio.EngineSpec? engine)
        {
            var lines = new List<CarInfoLine>
            {
                new("Condition", ConditionDisplay.Of(car).Sentence),
                new("Mileage", $"{car.OdometerKM:N0} km")
            };

            if (EngineName(car, catalog) is { } name)
                lines.Add(new("Engine", engine is { Runs: false } ? $"{name} (won't run: {engine.Problem})" : name));
            else if (car.HasPartsAssigned && catalog != null)
                lines.Add(new("Engine", "None"));
            if (car.PowerHp is { } hp) lines.Add(new("Power", hp > 0 ? $"{hp:0} hp on the dyno" : "Doesn't run on the dyno"));

            lines.Add(new("Fuel", FuelOf(car)));
            lines.Add(new("Oil", $"{CarCondition.OilLeft(car) * 100:0}%"));
            lines.Add(new("Body", CarCondition.IsTotaled(car) ? "Totaled" : $"{CarCondition.BodyCondition(car) * 100:0}%"));
            lines.Add(new("Dirt", DirtOf(car.BodyDirt)));

            if (car.PurchaseDate != default)
                lines.Add(new("Bought", car.PurchasePrice > 0 ? $"{car.PurchaseDate:d MMMM yyyy} for ${car.PurchasePrice:N0}" : $"{car.PurchaseDate:d MMMM yyyy}"));
            if (car.ImpoundedUntil is { } until) lines.Add(new("Impound", $"Held until {until:dddd d MMMM}, ${car.ImpoundFee:N0} to collect"));

            return lines;
        }

        /// <summary>The engine by its block: "Ford 302 V8" for the "Ford 302 V8 block"; null without the parts to tell</summary>
        private static string? EngineName(Car car, PartsCatalog? catalog)
        {
            if (catalog == null || car.Engine is not { } block || catalog.Get(block.DefinitionId) is not { } definition) return null;

            var name = definition.DisplayName ?? definition.Name;
            const string suffix = " block";
            return name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && name.Length > suffix.Length ? name[..^suffix.Length] : name;
        }

        /// <summary>"Full" until a race says otherwise, then "12 of 60 l"</summary>
        private static string FuelOf(Car car) =>
            CarCondition.FuelLeft(car) is { } left && CarCondition.TankLitres(car) is { } tank
                ? left < 10 ? $"{left:0.0} of {tank:0} l" : $"{left:0} of {tank:0} l"
                : "Full";

        private static string DirtOf(double dirt) => dirt switch
        {
            < 0.1 => "Clean",
            < 0.35 => "Dusty",
            < 0.7 => "Dirty",
            _ => "Filthy"
        };

        /// <summary>Every part on the car, each under what it is mounted on, in the order they are mounted</summary>
        private static List<CarInfoPart> PartsOf(Car car, PartsCatalog catalog)
        {
            var parts = new List<CarInfoPart>();
            var stack = new Stack<(PartInstance Part, int Depth)>();
            for (var i = (car.Parts?.Count ?? 0) - 1; i >= 0; i--)
            {
                if (car.Parts![i] is { } root) stack.Push((root, 0));
            }

            while (stack.Count > 0)
            {
                var (part, depth) = stack.Pop();
                var definition = catalog.Get(part.DefinitionId);
                parts.Add(new CarInfoPart(
                    definition?.DisplayName ?? definition?.Name ?? part.DefinitionId,
                    definition == null ? string.Empty : PartKinds.GroupOf(definition),
                    depth,
                    part.Wear,
                    part.Tear));

                // A save is anyone's file: no deeper than the parts themselves go
                if (depth >= PartInstance.MaxDepth || part.Children is not { } children) continue;
                for (var i = children.Count - 1; i >= 0; i--)
                {
                    if (children[i] != null) stack.Push((children[i], depth + 1));
                }
            }

            return parts;
        }
    }
}
