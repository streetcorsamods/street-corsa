using StreetCorsa.Models.Catalog;

namespace StreetCorsa.Services.Catalog
{
    /// <summary>
    /// Service for importing Assetto Corsa cars into the content catalog
    /// </summary>
    public interface ICarImportService
    {
        /// <summary>
        /// Performs a full import of all cars from AC installation, on a worker thread. Cars no longer installed
        /// are marked Legacy, but only after a scan of an existing cars folder that worked.
        /// </summary>
        /// <param name="progress">Optional progress callback (current, total, carName)</param>
        Task<ImportResult> ImportCarsAsync(IProgress<ImportProgress>? progress = null);
    }

    /// <summary>
    /// Result of an import operation
    /// </summary>
    public class ImportResult
    {
        public int TotalFound { get; set; }
        public int Imported { get; set; }
        public int Updated { get; set; }
        public int Skipped { get; set; }
        public int Failed { get; set; }

        /// <summary>Catalog cars the scan no longer found, marked Legacy</summary>
        public int Retired { get; set; }

        /// <summary>Legacy cars found installed again, unchanged, and made Active (counted among the skipped too)</summary>
        public int Returned { get; set; }

        public List<string> Errors { get; set; } = [];
        public TimeSpan Duration { get; set; }

        /// <summary>The catalog's cars are not what they were: one came, changed, went or came back</summary>
        public bool Changed => Imported + Updated + Retired + Returned > 0;
    }

    /// <summary>
    /// Progress information for import operations
    /// </summary>
    public class ImportProgress
    {
        public int Current { get; set; }
        public int Total { get; set; }
        public string CurrentCarName { get; set; } = string.Empty;
        public string Operation { get; set; } = string.Empty;
    }
}
