using StreetCorsa.Models.GameState;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Screens.Garage
{
    /// <summary>Where the picked shelf part can go on the car or the engine on the stand (<see cref="Workbench.FindPlaces"/>), the work order's say on each place, and the hint under the shelf</summary>
    public partial class PartsWorkbenchViewModel
    {
        private void UpdateCandidates()
        {
            var catalog = Catalog;
            var item = SelectedShelfItem;
            if (catalog == null || item == null)
            {
                Candidates = null;
                ShelfHint = string.Empty;
                return;
            }

            // An engine on the stand's view goes on the stand, not on the engine there
            var stand = _gameState.Player.EngineStand;
            if (_isStand && (item.IsEngine || stand == null))
            {
                Candidates = null;
                ShelfHint = !item.IsEngine ? GameText.Say("workbench.hint.stand_empty")
                    : stand == null ? GameText.Say("workbench.hint.to_stand")
                    : GameText.Say("workbench.hint.stand_taken");
                return;
            }

            if (!_isStand && _car == null)
            {
                Candidates = null;
                ShelfHint = GameText.Say("workbench.hint.no_car");
                return;
            }

            var specs = _car == null ? null : _parts.Specs(_car.DefinitionId);
            var liveAxle = _car != null && (specs?.HasLiveAxle ?? true);
            var places = _isStand ? Workbench.FindPlacesOnStand(catalog, stand!, item.Part)
                : Workbench.FindPlaces(catalog, _car!.Parts, item.Part, liveAxle, specs?.DrivesRear ?? true);

            // Where it fits, but the work order says not yet: the reason is the hint, and the place is no place
            var engine = CurrentEngine;
            var fitting = places.Count;
            var onEngine = engine == null ? null : new HashSet<PartInstance>(engine.SelfAndDescendants(), ReferenceEqualityComparer.Instance);
            var blocked = onEngine != null && places.Any(p => Workbench.IsOnEngine(onEngine, p))
                ? EngineWork.WhyNotOn(catalog, engine!, item.Part, _isStand)
                : null;
            if (blocked != null) places = places.Where(p => !Workbench.IsOnEngine(onEngine!, p)).ToList();

            // The running gear's work order the same way, place by place: the left drum waits for the left wheel only
            if (!_isStand)
            {
                var factory = _parts.FactoryRunningGear(_car!) is var (front, rear) ? front.All.Concat(rear.All).ToList() : null;
                var free = new List<MountPlace>();
                foreach (var place in places)
                {
                    if (onEngine != null && Workbench.IsOnEngine(onEngine, place)) free.Add(place);
                    else if (place.Parent == null && place.ParentSlot == PartInstance.CarEngineSlot && _car!.IsLifted) blocked ??= PartWords.Say("work.lift_hoist");
                    else if (CarWork.WhyNotOn(catalog, _car!.Parts, item.Part, place, liveAxle, factory, _car.IsLifted) is { } why) blocked ??= why;
                    else free.Add(place);
                }

                places = free;
            }

            var installedOf = Trees.SelectMany(t => t.Saved).ToDictionary(p => p.Value, p => p.Key);

            var candidates = new List<MountCandidate>();
            foreach (var place in places)
            {
                // A tree of its own for every place: the view tells them apart by their parts
                var loose = PartTrees.ToInstalled(catalog, item.Part);
                var parent = place.Parent == null ? null : installedOf.GetValueOrDefault(place.Parent);
                if (loose == null || (place.Parent != null && parent == null)) continue;

                candidates.Add(new MountCandidate(loose.Root, parent, place.ParentSlot, place.OwnSlot, place));
            }

            Candidates = candidates;
            ShelfHint = candidates.Count switch
            {
                0 when fitting > 0 && blocked != null => blocked,
                0 when item.IsEngine => GameText.Say("workbench.hint.bay_taken"),
                0 when _isStand => GameText.Say("workbench.hint.no_fit_engine"),
                0 => GameText.Say("workbench.hint.no_fit_car"),
                1 => GameText.Say("workbench.hint.one_place"),
                _ => GameText.Say("workbench.hint.places", ("count", candidates.Count))
            };
        }
    }
}
