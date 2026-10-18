using StreetCorsa.Models.GameState;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Services.PartShops;
using StreetCorsa.Services.Talk;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Screens.PartShop
{
    /// <summary>
    /// The machine shop's counter as the machinist's talk sees it (<see cref="IShopCounter"/>): the kinds of part the
    /// shop works on among what the player has, what the machinist knows of the shop and of the player's parts here, and
    /// the search in the work the shop can do, with whole engines put on the dyno to say what a job does for them
    /// </summary>
    public sealed partial class MachineShopScreenViewModel
    {
        // What a job does for a whole engine on the dyno, worked out once a visit per job, part and size
        private readonly Dictionary<(Guid Part, string Job, int Step), (double Before, double After)?> _power = new();

        // The jobs the machinist last picked out, by key: the list marks them
        private readonly HashSet<string> _picks = new(StringComparer.Ordinal);

        private static string KeyOf(MachineWork work) => $"{work.Part.InstanceId:N}:{work.Offer.Job.Id}:{work.Offer.Step}";

        /// <summary>Every job the shop could do on what the player has, at its prices as last worked out</summary>
        private List<MachineWork> AllWork() => _allWork.Where(w => w.Offer.CanDo).ToList();

        private string KindOf(MachineWork work) =>
            Catalog?.Get(work.Part.DefinitionId) is { } definition ? PartKinds.GroupName(PartKinds.GroupOf(definition)) : string.Empty;

        /// <summary>
        /// The kinds of part the shop works on among what the player has: read off the parts themselves, not the priced
        /// work, so an ask made while the work is still being priced knows them
        /// </summary>
        IReadOnlyList<string> IShopCounter.Kinds
        {
            get
            {
                if (Catalog is not { } catalog) return [];

                return Pieces.SelectMany(p => p.Piece.Part.SelfAndDescendants())
                    .Select(p => catalog.Get(p.DefinitionId)).OfType<PartDefinition>()
                    .Where(d => catalog.Rebuilds.For(d, catalog.WorkOrder.KindOf(d)).Any(j => j.IsMachine))
                    .Select(d => PartKinds.GroupName(PartKinds.GroupOf(d)))
                    .Where(k => k.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
            }
        }

        IEnumerable<string> IShopCounter.Facts()
        {
            yield return GameText.Say("chat.fact.machine_shop", ("shop", Definition.Name), ("region", Region), ("blurb", Blurb));
            if (Shop is not { } shop) yield break;

            foreach (var fact in PartShopNotice.Facts(shop, _gameState.Date)) yield return fact;

            var here = (_gameState.Player.MachineWork ?? []).Where(o => string.Equals(o.ShopId, _shopId, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var order in here)
                yield return GameText.Say("chat.fact.machine_here", ("part", NameOf(order.Part)), ("work", order.Work), ("day", order.ReadyOn));

            var brought = Machine.Pieces(_gameState, shop).Where(p => !p.AtShop).ToList();
            yield return brought.Count == 0
                ? GameText.Say("chat.fact.machine_nothing_brought")
                : GameText.Say("chat.fact.machine_brought", ("parts", string.Join(", ", brought.Take(12).Select(p => NameOf(p.Part)))));
        }

        private string NameOf(PartInstance part) =>
            Catalog?.Get(part.DefinitionId) is { } definition ? definition.DisplayName ?? definition.Name : part.DefinitionId;

        /// <summary>
        /// The machinist's search: of every job on what the player has, those of the kind of part asked about, under the
        /// price, by a name; the best few picked out (for power, what gains a whole engine most on the dyno, then the speed
        /// work; cheap, the cheapest; else repairs first, the cheapest) and marked in the list
        /// </summary>
        async Task<ClerkFindings> IShopCounter.FindAsync(ClerkSearch search, CancellationToken cancellationToken)
        {
            Room.IsCounterOpen = true;
            await _pricing;
            var all = AllWork();
            var named = search.Words is { } words && all.Any(w => Matches(w, words)) ? words : null;
            var matching = all
                .Where(w => search.Kind == null || string.Equals(KindOf(w), search.Kind, StringComparison.OrdinalIgnoreCase))
                .Where(w => search.MaxPrice is not { } most || w.Offer.Cost <= most)
                .Where(w => named == null || Matches(w, named))
                .ToList();

            // A whole engine's work goes on the dyno, the cheapest first, so the machinist can say what it does
            if (Catalog is { } catalog && search.Want != ClerkSearch.Cheap)
            {
                // The engines are copied here, on the UI thread they belong to, and go on the dyno as copies
                var trying = matching.Where(w => IsEngine(catalog, w.Piece.Part) && !_power.ContainsKey(PowerKey(w)))
                    .OrderBy(w => w.Offer.Cost).Take(MostDynoRuns)
                    .Select(w => (Key: PowerKey(w), Work: w, Engine: PartTrees.Clone(w.Piece.Part, keepIds: true))).ToList();
                if (trying.Count > 0)
                {
                    var pricedAt = _pricingRun;
                    var found = await Task.Run(() => trying.Select(t => (t.Key, Gain: PowerWith(catalog, t.Work, t.Engine))).ToList(), cancellationToken);
                    // Not when the parts changed meanwhile: the gains are of the engine as it was
                    if (pricedAt == _pricingRun)
                    {
                        foreach (var (key, gain) in found) _power[key] = gain;
                    }
                }
            }

            double Gain(MachineWork w) => _power.GetValueOrDefault(PowerKey(w)) is { } p ? p.After - p.Before : 0;
            IEnumerable<MachineWork> ranked = search.Want switch
            {
                ClerkSearch.Power => matching.OrderByDescending(Gain).ThenByDescending(w => w.Offer.Job.Speed).ThenBy(w => w.Offer.Cost),
                ClerkSearch.Cheap => matching.OrderBy(w => w.Offer.Cost),
                _ => matching.OrderBy(w => w.Offer.Job.Speed).ThenBy(w => w.Offer.Cost)
            };

            var picks = ranked.Take(MostPicks).ToList();
            _picks.Clear();
            foreach (var pick in picks) _picks.Add(KeyOf(pick));

            // The first pick's piece shows, its jobs marked
            if (picks.FirstOrDefault() is { } first)
                SelectedPiece = Pieces.FirstOrDefault(p => ReferenceEquals(p.Piece.Part, first.Piece.Part)) ?? SelectedPiece;
            FillJobs();

            return new ClerkFindings(search with { Words = named, Fits = false }, matching.Count, picks.Select(PickOf).ToList(),
                search.Words != null && named == null ? search.Words : null);
        }

        ClerkFindings IShopCounter.Recheck(ClerkFindings found)
        {
            var now = AllWork().ToDictionary(KeyOf, StringComparer.Ordinal);
            var picks = found.Picks.Where(p => now.ContainsKey(p.Id)).Select(p => PickOf(now[p.Id])).ToList();
            return found with { Matching = Math.Max(picks.Count, found.Matching - (found.Picks.Count - picks.Count)), Picks = picks };
        }

        private bool Matches(MachineWork work, string words) =>
            NameOf(work.Part).Contains(words, StringComparison.OrdinalIgnoreCase) || work.Offer.Name.Contains(words, StringComparison.OrdinalIgnoreCase);

        private static (Guid, string, int) PowerKey(MachineWork work) => (work.Part.InstanceId, work.Offer.Job.Id, work.Offer.Step);

        private static bool IsEngine(PartsCatalog catalog, PartInstance part) =>
            catalog.Get(part.DefinitionId) is { } definition && PartKinds.GroupOf(definition) == PartGroups.Blocks;

        /// <summary>A pick as the machinist is told it: the job, the part it is on, the price, the days, what a whole engine gains</summary>
        private ClerkPick PickOf(MachineWork work)
        {
            var power = _power.GetValueOrDefault(PowerKey(work));
            return new ClerkPick(KeyOf(work), work.Offer.Name, NameOf(work.Part), work.Offer.Cost, work.Offer.Job.Speed, work.Offer.Days, power?.Before, power?.After);
        }

        /// <summary>What the job does for the whole engine on the dyno, measured on <paramref name="engine"/>, a copy of the piece nobody else has</summary>
        private (double Before, double After)? PowerWith(PartsCatalog catalog, MachineWork work, PartInstance engine)
        {
            try
            {
                if (EngineTuner.PowerOf(catalog, engine) is not { } before) return null;

                var part = engine.SelfAndDescendants().First(p => p.InstanceId == work.Part.InstanceId);
                Machining.Apply(catalog, work.Offer.Job, part, work.Offer.Job.AnySize ? work.Offer.Step : null);
                return EngineTuner.PowerOf(catalog, engine) is { } after ? (before, after) : null;
            }
            catch (Exception ex)
            {
                _logger.Warning("{Job} could not go on the dyno for the machinist: {Error}", work.Offer.Job.Id, ex.Message);
                return null;
            }
        }
    }
}
