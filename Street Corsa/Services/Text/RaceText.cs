using System.Text;

namespace StreetCorsa.Services.Text
{
    /// <summary>
    /// What the race mode says in AC (its messages, the result card), from the text pools under <c>race.</c>
    /// (<c>text\race.json</c>), handed over in race.ini's <c>[STREET_CORSA_TEXT]</c>: <c>race.result.win</c> is the key
    /// <c>RESULT_WIN</c>. A value is percent-encoded UTF-8, so a comma, a semicolon, a line break or a letter outside
    /// ASCII reaches the mode as written whatever AC's INI reader makes of them; the mode decodes it and fills its
    /// {placeholders}. A key the mode does not find there says the mode's own English.
    /// </summary>
    public static class RaceText
    {
        public const string Section = "STREET_CORSA_TEXT";

        private const string Prefix = "race.";

        /// <summary>The section's lines: every <c>race.</c> pool's first line, in the player's language</summary>
        public static IEnumerable<string> IniLines(GameText text)
        {
            yield return $"[{Section}]";
            foreach (var key in text.Keys.Where(k => k.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)).Order(StringComparer.OrdinalIgnoreCase))
            {
                var lines = text.Lines(key);
                if (lines.Count > 0) yield return $"{IniKey(key)}={Encode(lines[0])}";
            }

            // The trap speeds, which the mode keeps in mph, in the player's units: the factor and the unit's words, both
            // from this text, the unit's id where a mod has no words for it
            var speed = Helpers.Measure.UnitOf(Helpers.Quantity.Speed, Helpers.Locale.Units);
            var perMph = Helpers.Measure.SpeedFromMph(1).In(Helpers.Locale.Units);
            yield return $"SPEED_PER_MPH={Encode(perMph.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture))}";
            yield return $"UNIT_SPEED={Encode(text.Lines("unit." + speed) is { Count: > 0 } words ? words[0] : "{n} " + speed)}";
            var name = text.Lines($"unit.{speed}.name") is { Count: > 0 } names ? names[0] : speed;
            yield return $"UNIT_SPEED_NAME={Encode(name.ToUpper(Helpers.Locale.Culture))}";
            // And the decimal mark the career writes its figures with, so a slip in AC reads like the one at home
            yield return $"DECIMAL={Encode(Helpers.Locale.Culture.NumberFormat.NumberDecimalSeparator)}";
        }

        /// <summary><c>race.result.win</c> as the mode asks for it: <c>RESULT_WIN</c></summary>
        public static string IniKey(string key) =>
            key[Prefix.Length..].Replace('.', '_').ToUpperInvariant();

        /// <summary>
        /// ASCII letters, digits, inner spaces and a few plain marks as they are; every other byte of the UTF-8 as %XX,
        /// and a space at either end too (an INI reader trims them)
        /// </summary>
        public static string Encode(string value)
        {
            var sb = new StringBuilder(value.Length);
            var bytes = Encoding.UTF8.GetBytes(value);
            for (var i = 0; i < bytes.Length; i++)
            {
                var b = bytes[i];
                var c = (char)b;
                if (c == ' ' && (i == 0 || i == bytes.Length - 1))
                {
                    sb.Append("%20");
                    continue;
                }

                // Quotes, $, braces and the like may mean something to CSP's INI reader: only plain marks go as they are
                if (b < 0x80 && (char.IsAsciiLetterOrDigit(c) || c is ' ' or '.' or '!' or '?' or '-' or '_'))
                    sb.Append(c);
                else
                    sb.Append('%').Append(b.ToString("X2"));
            }

            return sb.ToString();
        }
    }
}
