using StreetCorsa.Helpers;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Services.Time;
using StreetCorsa.ViewModels;

namespace StreetCorsa.Screens.Shared
{
    /// <summary>A job on the player's car as a row reads it, in the garage's repair bay and at the gas station's pump: what, why, the price, the time, and the button that does it</summary>
    public sealed class RepairJobRow
    {
        public RepairJobRow(RepairJob job, AsyncRelayCommand command)
        {
            Name = job.Name;
            Detail = job.Detail;
            CostDisplay = Money.Text(job.Cost);
            TimeDisplay = job.Time.GetTimeDescription();
            Command = command;
        }

        public string Name { get; }
        public string Detail { get; }

        public string CostDisplay { get; }
        public string TimeDisplay { get; }
        public AsyncRelayCommand Command { get; }
    }
}
