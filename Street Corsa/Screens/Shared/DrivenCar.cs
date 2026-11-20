using System.IO;
using System.Windows.Media;
using StreetCorsa.Configuration;
using StreetCorsa.Helpers;
using StreetCorsa.Models.GameState;
using StreetCorsa.Screens.Garage;
using StreetCorsa.Services.Career;
using StreetCorsa.Services.Catalog;
using StreetCorsa.Services.Showcase;
using StreetCorsa.Services.Text;
using StreetCorsa.ViewModels;

namespace StreetCorsa.Screens.Shared
{
    /// <summary>
    /// The car driven as the screens of a career of runs for the score tell of it (<see cref="DrivenCarCard"/>: Home, the
    /// roads, the Challenges): its picture, make, name, year and tier, its best run, its folder and skin
    /// </summary>
    public sealed class DrivenCar : ObservableObject
    {
        private const int PreviewDecodeWidth = 400;

        private readonly string? _firstSkin;
        private ImageSource? _preview;
        private bool _previewRead;

        private DrivenCar(string definitionId, string title, string brand, string name, string year, string tierText, string best,
            string directory, string? skinId, string? firstSkin)
        {
            DefinitionId = definitionId;
            Title = title;
            Brand = brand;
            Name = name;
            Year = year;
            TierText = tierText;
            Best = best;
            Directory = directory;
            SkinId = skinId;
            _firstSkin = firstSkin;
        }

        public string DefinitionId { get; }
        public string Title { get; }
        public string Brand { get; }
        public bool HasBrand => Brand.Length > 0;
        public string Name { get; }
        public string Year { get; }
        public bool HasYear => Year.Length > 0;
        public string TierText { get; }
        public bool HasTier => TierText.Length > 0;
        public string Best { get; }
        public string Directory { get; }
        public string? SkinId { get; }

        /// <summary>The car's picture (its skin's, else its first skin's), read off the UI thread the first time it is shown</summary>
        public ImageSource? Preview
        {
            get
            {
                if (!_previewRead)
                {
                    _previewRead = true;
                    _ = ReadPreviewAsync();
                }

                return _preview;
            }
        }

        public bool HasNoPreview => _previewRead && _preview == null;

        private async Task ReadPreviewAsync()
        {
            var carsPath = AppSettings.Instance.CarsPath;
            var (id, skin, first) = (DefinitionId, SkinId, _firstSkin);
            // A skin may come with no picture: the car's first skin's stands in
            _preview = await Thumbnails.LoadAsync(() =>
                    Parts.Export.AcCarFolder.PreviewImage(carsPath, id, skin)
                    ?? (first != null ? Parts.Export.AcCarFolder.PreviewImage(carsPath, id, first) : null),
                PreviewDecodeWidth, id);
            OnPropertyChanged(nameof(Preview));
            OnPropertyChanged(nameof(HasNoPreview));
        }

        /// <summary>
        /// The car the player has selected; null with none. One the catalog no longer has is told as <see cref="Bare"/>
        /// does: it is selected, and driven, all the same
        /// </summary>
        public static DrivenCar? Of(GameState gameState, IContentCatalogRepository catalog)
        {
            if (PracticeRuns.CarOf(gameState) is not { } car) return null;
            if (catalog.GetCar(car.DefinitionId) is not { } definition) return Bare(gameState, car);

            var rules = Rules.RuleBook.Current.Unlocks;
            var tier = ChallengeCareer.TierTold(Showroom.TierOf(definition, rules), rules);
            return new DrivenCar(
                car.DefinitionId,
                ShowcaseContent.Title(definition),
                definition.Brand ?? string.Empty,
                CarSelectCard.NameOf(definition),
                definition.Year?.ToString(Locale.Culture) ?? string.Empty,
                GameText.Say("showroom.tier", ("tier", tier)),
                BestOf(gameState, car),
                Path.Combine(AppSettings.Instance.CarsPath, car.DefinitionId),
                car.SkinId,
                definition.AvailableSkins.FirstOrDefault());
        }

        /// <summary>The car selected with no model in the catalog to tell of it: named by its folder, its best run, its picture if any</summary>
        public static DrivenCar Bare(GameState gameState, Car car) => new(
            car.DefinitionId,
            car.DefinitionId,
            string.Empty,
            car.DefinitionId,
            string.Empty,
            string.Empty,
            BestOf(gameState, car),
            Path.Combine(AppSettings.Instance.CarsPath, car.DefinitionId),
            car.SkinId,
            null);

        private static string BestOf(GameState gameState, Car car)
        {
            var best = (gameState.Career.ScoredRuns ?? [])
                .Where(r => string.Equals(r.CarDefinitionId, car.DefinitionId, StringComparison.OrdinalIgnoreCase))
                .Select(r => (double?)r.Score)
                .Max();
            return best is { } b
                ? GameText.Say("garage.select.best", ("score", b.ToString("N0", Locale.Culture)))
                : GameText.Say("garage.select.no_runs");
        }
    }
}
