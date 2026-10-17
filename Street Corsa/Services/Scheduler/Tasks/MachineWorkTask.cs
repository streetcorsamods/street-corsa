using StreetCorsa.Models.GameState;
using StreetCorsa.Services.PartShops;

namespace StreetCorsa.Services.Scheduler.Tasks
{
    /// <summary>
    /// Brings back what the machine shops have ready, daily, whatever the game has switched off: a part left at one is
    /// the player's, and comes home with the mail order's deliveries (<see cref="MachineShopService.Deliver"/>)
    /// </summary>
    public class MachineWorkTask(IMachineShopService machineShops) : IScheduledTask
    {
        private readonly IMachineShopService _machineShops = machineShops;

        public string TaskId => "machine_work";
        public int IntervalDays => 1;

        public Task ExecuteAsync(GameState gameState, DateTime currentDate)
        {
            _machineShops.Deliver(gameState, currentDate);
            return Task.CompletedTask;
        }
    }
}
