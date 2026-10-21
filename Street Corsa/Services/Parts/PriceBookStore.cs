using System.IO;
using StreetCorsa.Logging;
using StreetCorsa.Parts.Cars;

namespace StreetCorsa.Services.Parts
{
    /// <summary>
    /// The parts' price book for the session (<see cref="PartPriceBook"/>): every part measured on the dyno, once,
    /// several seconds, so the measuring is kept on disk with a fingerprint of the parts in the mods and taken back
    /// while they are the same ones. Made when first asked for (<see cref="Book"/>), by the warm-up as a rule; a caller
    /// that must not wait for it (the UI thread pricing a shelf before the warm-up is through) asks for
    /// <see cref="IfMade"/> and prices every part at its kind's base meanwhile.
    /// </summary>
    internal sealed class PriceBookStore
    {
        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger(LogCategory.Parts);
        private readonly Func<IEnumerable<string>> _partsFolders;
        private readonly Func<IReadOnlyDictionary<string, double>?, PartPriceBook?> _measure;
        private readonly string _path;
        private readonly Lazy<PartPriceBook> _book;

        /// <param name="partsFolders">The parts folders of the mods and their mod.json files, the fingerprint's</param>
        /// <param name="measure">
        /// The book from the measuring kept (null: none kept for these parts); null when there are no parts to measure
        /// </param>
        /// <param name="path">Where the measuring is kept</param>
        public PriceBookStore(Func<IEnumerable<string>> partsFolders, Func<IReadOnlyDictionary<string, double>?, PartPriceBook?> measure, string path)
        {
            _partsFolders = partsFolders;
            _measure = measure;
            _path = path;
            _book = new Lazy<PartPriceBook>(Create);
        }

        /// <summary>The book, made now when it is not yet: several seconds the first time</summary>
        public PartPriceBook Book => _book.Value;

        /// <summary>The book once it is made; null before that, for a caller that must not wait for it</summary>
        public PartPriceBook? IfMade => _book.IsValueCreated ? _book.Value : null;

        private PartPriceBook Create()
        {
            try
            {
                var started = DateTime.Now;
                var fingerprint = PartPriceBook.Fingerprint(_partsFolders());
                var kept = ReadMeasuring(fingerprint);
                if (_measure(kept) is not { } book) return PartPriceBook.Empty;

                // A build the dyno could not run prices nothing: each is told once, so a broken part is found
                foreach (var fault in book.Faults) _logger.Warning("Part prices: {Fault}", fault);

                if (kept != null)
                {
                    _logger.Information("Part prices: {Measured} parts as measured before, in {Ms} ms", book.Measured,
                        (int)(DateTime.Now - started).TotalMilliseconds);
                    return book;
                }

                _logger.Information("Part prices: {Measured} parts measured on the dyno in {Ms} ms", book.Measured,
                    (int)(DateTime.Now - started).TotalMilliseconds);
                KeepMeasuring(fingerprint, book);
                return book;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not measure the parts on the dyno: every part at its kind's base price");
                return PartPriceBook.Empty;
            }
        }

        /// <summary>The power kept for these parts; null when none is, or it is of other parts, or it does not read</summary>
        private Dictionary<string, double>? ReadMeasuring(string fingerprint)
        {
            try
            {
                if (!File.Exists(_path)) return null;
                var kept = System.Text.Json.JsonSerializer.Deserialize<KeptMeasuring>(File.ReadAllText(_path));
                return kept?.Fingerprint == fingerprint ? kept.Power : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                _logger.Warning("The parts' kept measuring could not be read; they are measured again: {Error}", ex.Message);
                return null;
            }
        }

        private void KeepMeasuring(string fingerprint, PartPriceBook book)
        {
            try
            {
                Helpers.SafeFile.WriteAllText(_path,
                    System.Text.Json.JsonSerializer.Serialize(new KeptMeasuring(fingerprint, new Dictionary<string, double>(book.Power))));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.Warning("The parts' measuring could not be kept; the next start measures them again: {Error}", ex.Message);
            }
        }

        private sealed record KeptMeasuring(string Fingerprint, Dictionary<string, double> Power);
    }
}
