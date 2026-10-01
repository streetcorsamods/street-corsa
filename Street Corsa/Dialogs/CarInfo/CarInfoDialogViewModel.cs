using StreetCorsa.Models.GameState;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Screens.Shared;
using StreetCorsa.Services.Text;
using StreetCorsa.ViewModels;
using StreetCorsa.Helpers;

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
        public string ConditionDisplay => Tear < 0.995 ? $"{Wear * 100:0}%" + GameText.Say("garage.part.damaged", ("left", $"{Tear * 100:0}")) : $"{Wear * 100:0}%";

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
                new(GameText.Say("carinfo.condition"), ConditionDisplay.Of(car).Sentence),
                new(GameText.Say("carinfo.mileage"), Measure.Distance(car.OdometerKM).ToString())
            };

            if (EngineName(car, catalog) is { } name)
                lines.Add(new(GameText.Say("carinfo.engine"), engine is { Runs: false } ? GameText.Say("carinfo.engine.wont_run", ("engine", name), ("problem", engine.Problem)) : name));
            else if (car.HasPartsAssigned && catalog != null)
                lines.Add(new(GameText.Say("carinfo.engine"), GameText.Say("carinfo.engine.none")));
            if (car.PowerHp is { } hp) lines.Add(new(GameText.Say("carinfo.power"), hp > 0 ? GameText.Say("carinfo.power.dyno", ("hp", $"{hp:0}")) : GameText.Say("carinfo.power.no_run")));

            lines.Add(new(GameText.Say("carinfo.fuel"), ConditionDisplay.Fuel(car)));
            lines.Add(new(GameText.Say("carinfo.oil"), $"{CarCondition.OilLeft(car) * 100:0}%"));
            lines.Add(new(GameText.Say("carinfo.body"), CarCondition.IsTotaled(car) ? GameText.Say("carinfo.body.totaled") : $"{CarCondition.BodyCondition(car) * 100:0}%"));
            lines.Add(new(GameText.Say("carinfo.dirt"), DirtOf(car.BodyDirt)));

            if (car.PurchaseDate != default)
                lines.Add(new(GameText.Say("carinfo.bought"), car.PurchasePrice > 0 ? GameText.Say("carinfo.bought.price", ("date", car.PurchaseDate), ("price", new Money(car.PurchasePrice))) : GameText.Date(car.PurchaseDate, "format.date.day_month_year")));
            if (car.ImpoundedUntil is { } until) lines.Add(new(GameText.Say("carinfo.impound"), GameText.Say("carinfo.impound.held", ("until", until), ("fee", new Money(car.ImpoundFee)))));

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

        private static string DirtOf(double dirt) => dirt switch
        {
            < 0.1 => GameText.Say("carinfo.dirt.clean"),
            < 0.35 => GameText.Say("carinfo.dirt.dusty"),
            < 0.7 => GameText.Say("carinfo.dirt.dirty"),
            _ => GameText.Say("carinfo.dirt.filthy")
        };

        /// <summary>Every part on the car, each under what it is mounted on, in the order they are mounted</summary>
        private static List<CarInfoPart> PartsOf(Car car, PartsCatalog catalog) =>
            (car.Parts ?? [])
                .Where(root => root != null)
                .SelectMany(root => root.SelfAndDescendantsWithDepth())
                .Select(p =>
                {
                    var definition = catalog.Get(p.Part.DefinitionId);
                    return new CarInfoPart(
                        definition?.DisplayName ?? definition?.Name ?? p.Part.DefinitionId,
                        definition == null ? string.Empty : PartKinds.GroupOf(definition),
                        p.Depth,
                        p.Part.Wear,
                        p.Part.Tear);
                })
                .ToList();
    }
}
