using StreetCorsa.Models.Career.Events;
using StreetCorsa.Models.GameState;
using StreetCorsa.Services.Career;
using StreetCorsa.Services.Catalog;

namespace StreetCorsa.Dialogs.RaceHub
{
    /// <summary>
    /// The race invitations the racing hub lists: the events open now, each with the player's cars that may enter it,
    /// the ones the player can enter first, then the soonest to run out
    /// </summary>
    public static class RaceInvitations
    {
        public static List<EventInvitationViewModel> Load(GameState gameState, IRaceEventService events, ICarFilterService filter,
            IContentCatalogRepository catalog)
        {
            var now = gameState.Date;
            var invitations = new List<EventInvitationViewModel>();
            foreach (var instance in events.GetActiveEvents(gameState.Career, now))
            {
                if (events.GetEventDefinition(instance.EventDefinitionId) is not { } definition) continue;

                invitations.Add(EventInvitationViewModel.FromEvent(instance, definition, EligibleCars(gameState, definition, filter, catalog),
                    now, gameState.Rules.RacePrizeMultiplier));
            }

            return invitations
                .OrderByDescending(e => e.HasEligibleCars)
                .ThenBy(e => e.ExpiresAt ?? DateTime.MaxValue)
                .ToList();
        }

        /// <summary>The player's cars the event lets in</summary>
        private static List<EligibleCarViewModel> EligibleCars(GameState gameState, RaceEventDefinition definition, ICarFilterService filter,
            IContentCatalogRepository catalog)
        {
            var eligible = new List<EligibleCarViewModel>();
            foreach (var car in gameState.Player.Cars)
            {
                if (catalog.GetCar(car.DefinitionId) is not { } carDef) continue;
                if (definition.EntryRequirements != null && !filter.Matches(definition.EntryRequirements, carDef, car)) continue;

                eligible.Add(EligibleCarViewModel.FromCar(car, $"{carDef.Year} {carDef.Brand} {carDef.Name}"));
            }

            return eligible;
        }
    }
}
