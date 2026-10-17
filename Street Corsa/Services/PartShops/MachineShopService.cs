using StreetCorsa.Logging;
using StreetCorsa.Models.GameState;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Services.Dealers;
using StreetCorsa.Services.Parts;

namespace StreetCorsa.Services.PartShops
{
    /// <summary>A part the player can leave at a machine shop, or has there: where it is, and the parts of it the machinist can work on</summary>
    /// <param name="Order">The shop has it already; null for a part in the garage</param>
    /// <param name="FromStand">The engine on the garage's stand</param>
    public sealed record MachinePiece(PartInstance Part, MachineOrder? Order, bool FromStand)
    {
        public bool AtShop => Order != null;
    }

    /// <summary>A machine job on one part of a piece the player brought: the part (the piece's root, or a part on it) and the offer</summary>
    public sealed record MachineWork(MachinePiece Piece, PartInstance Part, MachineOffer Offer);

    /// <summary>The machine shops' work: see <see cref="MachineShopService"/></summary>
    public interface IMachineShopService
    {
        /// <summary>What the player can bring to the shop (the shelf, the engine on the stand) and has there already</summary>
        IReadOnlyList<MachinePiece> Pieces(GameState gameState, PartShopLocation shop);

        /// <summary>Every machine job on a piece and the parts on it, at the shop's prices today, the root's first</summary>
        IReadOnlyList<MachineWork> Offers(GameState gameState, PartShopLocation shop, MachinePiece piece);

        /// <summary>
        /// The player has the job done: paid now, the piece left at the shop (or kept there longer, for work on a part it
        /// has) until the job's days are up; null when it cannot be done (shut, no money, the job's price or the part
        /// changed)
        /// </summary>
        MachineOrder? Order(GameState gameState, PartShopLocation shop, MachineWork work, Random random);

        /// <summary>
        /// What is ready by <paramref name="date"/> back in the garage: an engine on the stand if it came from there and the
        /// stand is free, else on the shelf, the player told of it in the garage
        /// </summary>
        List<MachineOrder> Deliver(GameState gameState, DateTime date);
    }

    /// <summary>
    /// A machine shop (<see cref="PartShopKind.Machine"/>) on the part shops' map: the player brings a part off the
    /// garage's shelf or the engine off its stand, picks the work (<see cref="Machining"/>: a bore, a crank grind, a head
    /// milled, speed work), pays as they leave it, and the shop brings it back on the morning it is ready. The prices are
    /// the shop's (<see cref="PartShopRules.PriceFactor"/>), less a closing-down sale and a regular's discount. A part at the
    /// shop is the player's all along (<see cref="Player.MachineWork"/>):
    /// a shop going under still brings it back.
    /// </summary>
    public sealed class MachineShopService : IMachineShopService
    {
        private readonly IPartShopCatalog _catalog;
        private readonly ICarPartsService _parts;
        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger("MachineShop");

        public MachineShopService(IPartShopCatalog catalog, ICarPartsService parts)
        {
            _catalog = catalog;
            _parts = parts;
        }

        public IReadOnlyList<MachinePiece> Pieces(GameState gameState, PartShopLocation shop)
        {
            var player = gameState.Player;
            var pieces = new List<MachinePiece>();
            if (!_parts.IsAvailable) return pieces;

            var catalog = _parts.Catalog;
            pieces.AddRange((player.MachineWork ?? []).Where(o => string.Equals(o.ShopId, shop.Id, StringComparison.OrdinalIgnoreCase) && catalog.Get(o.Part.DefinitionId) != null)
                .Select(o => new MachinePiece(o.Part, o, o.FromStand)));
            if (player.EngineStand is { } engine && catalog.Get(engine.DefinitionId) != null) pieces.Add(new MachinePiece(engine, null, FromStand: true));
            pieces.AddRange(player.Parts.Where(p => catalog.Get(p.DefinitionId) != null).Select(p => new MachinePiece(p, null, FromStand: false)));
            return pieces;
        }

        public IReadOnlyList<MachineWork> Offers(GameState gameState, PartShopLocation shop, MachinePiece piece) =>
            piece.Part.SelfAndDescendants().SelectMany(part => OffersOn(gameState, shop, piece, part)).Where(w => Suits(piece, w)).ToList();

        /// <summary>
        /// A job made to any size on a part inside a block (new pistons) is offered at the block's size only: any other
        /// would not go back in it
        /// </summary>
        private bool Suits(MachinePiece piece, MachineWork work)
        {
            if (!work.Offer.Job.AnySize || work.Offer.Job.Cut is not { } cut || ReferenceEquals(work.Part, piece.Part)) return true;

            var catalog = _parts.Catalog;
            foreach (var (matched, kinds, _) in catalog.Rebuilds.MatchedCuts().Where(m => m.Cut == cut))
            {
                var holder = piece.Part.SelfAndDescendants().FirstOrDefault(p => catalog.Get(p.DefinitionId) is { } d && catalog.WorkOrder.KindOf(d) is { } k
                                                                                  && kinds.Contains(k, StringComparer.OrdinalIgnoreCase));
                if (holder != null) return work.Offer.Step == holder.CutOf(matched);
            }

            return true;
        }

        /// <summary>The jobs on one part of a piece, at the shop's prices today</summary>
        private IEnumerable<MachineWork> OffersOn(GameState gameState, PartShopLocation shop, MachinePiece piece, PartInstance part)
        {
            if (!_parts.IsAvailable || _catalog.Get(shop.Id) is not { } definition) return [];

            var factor = FactorAt(gameState, definition, shop);
            return Machining.Offers(_parts.Catalog, part, factor, gameState.Rules.PartPriceMultiplier).Select(o => new MachineWork(piece, part, o));
        }

        /// <summary>What the shop asks against the work's list price today: its own prices, a closing-down sale, a regular's discount</summary>
        private static double FactorAt(GameState gameState, PartShopDefinition definition, PartShopLocation shop) =>
            PartShopRules.PriceFactor(definition) * (double)(1 - DealerRules.DiscountOn(shop, gameState.Date)) * (double)(1 - PartShopRules.RegularDiscountFor(shop.PlayerSpent));

        public MachineOrder? Order(GameState gameState, PartShopLocation shop, MachineWork work, Random random)
        {
            if (!shop.IsTrading || !_parts.IsAvailable || _catalog.Get(shop.Id) is not { } definition) return null;

            var player = gameState.Player;
            var piece = work.Piece;

            // The piece is still where the counter saw it: at the shop, on the stand, on the shelf
            var there = piece.Order != null ? (player.MachineWork ?? []).Contains(piece.Order)
                : piece.FromStand ? ReferenceEquals(player.EngineStand, piece.Part)
                : player.Parts.Contains(piece.Part);
            if (!there || !piece.Part.SelfAndDescendants().Contains(work.Part)) return null;

            // The job as it stands now, at its price now: the part or the money may have changed since it was listed
            var now = OffersOn(gameState, shop, piece, work.Part).FirstOrDefault(w => w.Offer.Job == work.Offer.Job && w.Offer.Step == work.Offer.Step);
            if (now is not { Offer.CanDo: true } || now.Offer.Cost != work.Offer.Cost || player.Money < now.Offer.Cost) return null;

            var catalog = _parts.Catalog;
            var step = now.Offer.Job.AnySize ? now.Offer.Step : (int?)null;
            var (_, extras) = Machining.Do(catalog, now.Offer.Job, work.Part, random, step);
            player.Money -= now.Offer.Cost;
            shop.PlayerSpent += now.Offer.Cost;
            shop.Trade += PartShopRules.TradePerSale;

            var ready = gameState.Date.Date.AddDays(now.Offer.Days);
            var order = piece.Order;
            if (order == null)
            {
                if (piece.FromStand) player.EngineStand = null;
                else player.Parts.Remove(piece.Part);

                order = new MachineOrder
                {
                    Part = piece.Part, ShopId = shop.Id, ShopName = definition.Name, Work = now.Offer.Name, Paid = now.Offer.Cost,
                    LeftOn = gameState.Date, ReadyOn = ready, FromStand = piece.FromStand
                };
                (player.MachineWork ??= []).Add(order);
            }
            else
            {
                // More work on a part it has: it stays till the longest job is done
                order.Work = string.IsNullOrEmpty(order.Work) ? now.Offer.Name : $"{order.Work}; {now.Offer.Name}";
                order.Paid += now.Offer.Cost;
                if (ready > order.ReadyOn) order.ReadyOn = ready;
            }

            // A set of pistons made for a bare block comes back with it
            foreach (var extra in extras)
            {
                player.MachineWork.Add(new MachineOrder
                {
                    Part = extra, ShopId = shop.Id, ShopName = definition.Name, Work = now.Offer.Name, LeftOn = gameState.Date, ReadyOn = order.ReadyOn
                });
            }

            _logger.Information("{Job} on {Part} at {Shop} for ${Cost}, ready {Ready:d}", now.Offer.Job.Id, work.Part.DefinitionId, definition.Name, now.Offer.Cost, order.ReadyOn);
            return order;
        }

        public List<MachineOrder> Deliver(GameState gameState, DateTime date)
        {
            var player = gameState.Player;
            var ready = (player.MachineWork ?? []).Where(o => o.ReadyOn.Date <= date.Date).ToList();
            foreach (var order in ready)
            {
                player.MachineWork.Remove(order);
                if (order.FromStand && player.EngineStand == null)
                {
                    order.Part.ParentSlot = 0;
                    player.EngineStand = order.Part;
                }
                else
                {
                    player.Parts.Add(order.Part);
                }

                var name = _parts.IsAvailable && _parts.Catalog.Get(order.Part.DefinitionId) is { } definition ? definition.DisplayName ?? definition.Name : order.Part.DefinitionId;
                player.Delivered.Add(Text.GameText.Say("machine.delivered", ("part", name), ("shop", order.ShopName)));
            }

            if (ready.Count > 0) _logger.Information("The machine shops brought back {Count} part(s)", ready.Count);
            return ready;
        }
    }
}
