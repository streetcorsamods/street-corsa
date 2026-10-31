using StreetCorsa.Controls;
using StreetCorsa.Models.GameState;
using StreetCorsa.Parts;
using StreetCorsa.Services.Parts;
using StreetCorsa.Services.PartShops;
using StreetCorsa.Services.Street;
using StreetCorsa.ViewModels;

namespace StreetCorsa.Screens.PartShop
{
    /// <summary>
    /// The inside of a shop as the screens show it (<see cref="ShopViewport3D"/>), the same for a part shop and a machine
    /// shop: the mods' scene for the shop's kind, dressed for the hour, what stands in it, and the counter up over it
    /// (<see cref="IsCounterOpen"/>: the list and the clerk; without a scene installed the counter is the screen). The
    /// parts catalog is loaded here too (<see cref="OpenAsync"/>), on a worker, never by the view's binding.
    /// </summary>
    public sealed class ShopRoomViewModel : ObservableObject
    {
        private readonly ShopScene? _scene;
        private readonly ClerkTalk _clerk;

        /// <param name="scene">The room's scene; the installed one for the shop's kind when null</param>
        public ShopRoomViewModel(PartShopDefinition definition, DateTime date, ClerkTalk clerk, ShopScene? scene = null)
        {
            _clerk = clerk;
            // A mod's scene for this kind of shop (wheels, speed shops, machine shops) before the one for any
            var kind = definition.KindId;
            _scene = scene ?? ShopScenes.For(kind);

            if (_scene != null)
            {
                var look = _scene.LookFor(definition.Look, kind);
                var dressing = _scene.Dress(look, StreetLights.At(date));
                Dressing = new LotDressing(dressing.Lighting, dressing.Sun, dressing.Pictures, dressing.Glow);
            }
        }

        /// <summary>The shop's room, when a scene is installed; none, and the counter is the screen</summary>
        public string? SceneKn5 => _scene?.Kn5;

        public bool HasScene => _scene != null;

        public IReadOnlyList<ShopPlace>? Places => _scene?.Places;

        public ShopCamera? View => _scene?.Camera;

        /// <summary>How far the camera may go: the room's walls</summary>
        public ShopRoom? Bounds => _scene?.Room;

        /// <summary>The room dressed for this kind of shop at this hour</summary>
        public LotDressing? Dressing { get; }

        private PartsCatalog? _catalog;

        /// <summary>The parts catalog, once <see cref="OpenAsync"/> has loaded it on a worker; null until then, and for good when there are no parts</summary>
        public PartsCatalog? Catalog
        {
            get => _catalog;
            private set => SetProperty(ref _catalog, value);
        }

        private IReadOnlyList<ShopStockPart>? _stockShown;

        /// <summary>What stands in the room: a part of each line on the shelves, every part in the yard, the work on the machines</summary>
        public IReadOnlyList<ShopStockPart>? StockShown
        {
            get => _stockShown;
            set => SetProperty(ref _stockShown, value);
        }

        private bool _isCounterOpen;

        /// <summary>The counter is up over the room: the list and the clerk; without a scene it always is</summary>
        public bool IsCounterOpen
        {
            get => _isCounterOpen || !HasScene;
            set
            {
                if (!SetProperty(ref _isCounterOpen, value)) return;
                OnPropertyChanged(nameof(ShowsCounterButton));
                _clerk.IsOpen = value;
            }
        }

        /// <summary>The counter was opened by the player, over a room (the back button closes it first)</summary>
        public bool IsCounterOverRoom => _isCounterOpen && HasScene;

        /// <summary>The way up to the counter, while it is not open</summary>
        public bool ShowsCounterButton => HasScene && !IsCounterOpen;

        /// <summary>
        /// The shop's files and the parts, loaded on a worker: the roster brought up to date with the shops' files and the
        /// parts catalog set. False when the shops are not available (nothing is loaded).
        /// </summary>
        public async Task<bool> OpenAsync(IPartShopService shops, ICarPartsService parts, GameState gameState)
        {
            if (!await Task.Run(() => shops.IsAvailable)) return false;

            PartShopRoster.Sync(gameState, shops.Catalog);
            Catalog = await Task.Run(() => parts.IsAvailable ? parts.Catalog : null);
            return true;
        }
    }
}
