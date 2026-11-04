using System.IO;
using StreetCorsa.Audio;

namespace StreetCorsa.Screens.Settings
{
    /// <summary>
    /// The icons' credits, off Assets\Images\Icons\credits.csv (built into the game, and installed beside the icons with
    /// CREDITS.txt): the credits screen's lines, in code and not in a text pool, so no translation or mod can drop what
    /// the icons' licences ask for (as the FMOD credit beside it). A row with a <c>credit</c> is shown as its site asks,
    /// word for word; the rest are named by site and licence, authors together; the game's own ask nothing.
    /// </summary>
    internal static class IconCredits
    {
        public const string ResourceName = "StreetCorsa.IconCredits.csv";

        public sealed record Row(string Icon, string Author, string Source, string Licence, string Link, string Credit)
        {
            public bool IsOurs => Source.Equals("Street Corsa", StringComparison.OrdinalIgnoreCase);
        }

        public static IReadOnlyList<Row> Load()
        {
            using var stream = typeof(IconCredits).Assembly.GetManifestResourceStream(ResourceName)
                ?? throw new InvalidOperationException($"{ResourceName} is not built into the game");
            using var reader = new StreamReader(stream);
            return Parse(reader.ReadToEnd());
        }

        public static IReadOnlyList<Row> Parse(string csv)
        {
            var rows = SoundFiles.ParseCsv(csv);
            if (rows.Count == 0) return [];

            var header = rows[0].Select(h => h.Trim().ToLowerInvariant()).ToList();
            int Col(string name) => header.IndexOf(name);
            var (icon, author, source, licence, link, credit) =
                (Col("icon"), Col("author"), Col("source"), Col("licence"), Col("link"), Col("credit"));

            return rows.Skip(1)
                .Select(row =>
                {
                    string At(int i) => i >= 0 && i < row.Count ? row[i].Trim() : "";
                    return new Row(At(icon), At(author), At(source), At(licence), At(link), At(credit));
                })
                .Where(r => r.Icon.Length > 0)
                .ToList();
        }

        /// <summary>
        /// The credits screen's lines for the icons: "Icons by A, B from site (licence)" per site and licence, then each
        /// "Icon by C (link)" its site asks for word for word; none when no icon asks for a credit
        /// </summary>
        public static IReadOnlyList<string> Entries(IReadOnlyList<Row> rows)
        {
            var entries = new List<string>();
            foreach (var group in rows.Where(r => !r.IsOurs && r.Credit.Length == 0).GroupBy(r => (r.Source, r.Licence)))
            {
                var authors = group.Select(r => r.Author).Where(a => a.Length > 0 && a != group.Key.Source).Distinct().ToList();
                var by = authors.Count == 0 ? "" : $"by {Join(authors)} ";
                entries.Add($"Icons {by}from {group.Key.Source} ({group.Key.Licence})");
            }
            entries.AddRange(rows.Where(r => !r.IsOurs && r.Credit.Length > 0).Select(r => r.Credit).Distinct());
            return entries;
        }

        private static string Join(List<string> names) =>
            names.Count == 1 ? names[0] : string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1];
    }
}
