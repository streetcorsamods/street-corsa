using System.IO;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace StreetCorsa.Parts.Export;

/// <summary>
/// The ui_*.json files of Assetto Corsa content (ui_car.json, ui_skin.json, ui_track.json), read the one way every
/// reader in the game and the tools reads them. Mods write them loosely: comments, trailing commas, a key in capitals,
/// a year as 1969, 1969.0 or "1969", pit boxes as 20 or "20", tags as a list or a single string. This reads all of those
/// and hands back what could be read; a file that is not JSON at all, or larger than any real one, is an error the
/// caller reports, never an exception.
/// </summary>
public static class AcUiJson
{
    /// <summary>The largest ui_*.json read: a real one is a few kilobytes</summary>
    public const long MaxBytes = 1024 * 1024;

    /// <summary>The parsed file, or null with the reason (missing, unreadable, too large, not an object)</summary>
    public static JObject? TryRead(string path, out string? error) =>
        TryReadText(path, out error) is { } text ? TryParse(text, out error, Path.GetFileName(path)) : null;

    /// <summary>The file's text, or null with the reason (missing, unreadable, too large): for a reader that keeps the text too</summary>
    public static string? TryReadText(string path, out string? error)
    {
        var name = Path.GetFileName(path);
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists)
            {
                error = "no " + name;
                return null;
            }

            if (file.Length > MaxBytes)
            {
                error = $"{name} is {file.Length:N0} bytes, more than a description ever is";
                return null;
            }

            error = null;
            return File.ReadAllText(file.FullName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            error = ex.Message;
            return null;
        }
    }

    /// <param name="json">The file's text</param>
    /// <param name="error">Why it is no object, when it is not</param>
    /// <param name="fileName">The file the error names</param>
    public static JObject? TryParse(string json, out string? error, string fileName = "ui_car.json")
    {
        try
        {
            // Strings stay strings: by default a date-like one ("version": "2021-05-01") would come back as a Date
            // token, which GetString does not read. Numbers with a fraction are doubles, never decimals.
            using var reader = new JsonTextReader(new StringReader(json))
            {
                MaxDepth = 64,
                DateParseHandling = DateParseHandling.None,
                FloatParseHandling = FloatParseHandling.Double
            };
            var token = JToken.ReadFrom(reader, new JsonLoadSettings
            {
                CommentHandling = CommentHandling.Ignore,
                DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Replace
            });

            error = token is JObject ? null : fileName + " is not a JSON object";
            return token as JObject;
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return null;
        }
    }

    /// <summary>A property by its name as it is written, else in other capitals ("Name")</summary>
    private static JToken? Get(JToken? parent, string name) => (parent as JObject)?.GetValue(name, StringComparison.OrdinalIgnoreCase);

    /// <summary>A string property; a number or bool is given as its invariant text</summary>
    public static string? GetString(JToken? parent, string name) =>
        Get(parent, name) switch
        {
            JValue { Type: JTokenType.String } v => (string?)v.Value,
            JValue { Type: JTokenType.Integer or JTokenType.Float or JTokenType.Boolean } v =>
                Convert.ToString(v.Value, CultureInfo.InvariantCulture),
            _ => null
        };

    /// <summary>An integer property written as 1969, 1969.0 or "1969"; null when it is none of those or out of range</summary>
    public static int? GetInt(JToken? parent, string name)
    {
        switch (Get(parent, name))
        {
            case JValue { Type: JTokenType.Integer } v when v.Value is long l && l is >= int.MinValue and <= int.MaxValue:
                return (int)l;
            case JValue { Type: JTokenType.Float } v when v.Value is double d && double.IsFinite(d) && d == Math.Floor(d) && d is >= int.MinValue and <= int.MaxValue:
                return (int)d;
            case JValue { Type: JTokenType.String } v when int.TryParse((string?)v.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed):
                return parsed;
            default:
                return null;
        }
    }

    /// <summary>A list of strings written as an array (non-strings skipped) or as one string; empty when absent</summary>
    public static IReadOnlyList<string> GetStrings(JToken? parent, string name) =>
        Get(parent, name) switch
        {
            JArray array => array.OfType<JValue>().Where(v => v.Type == JTokenType.String)
                .Select(v => (string?)v.Value).Where(s => !string.IsNullOrEmpty(s)).Select(s => s!).ToList(),
            JValue { Type: JTokenType.String } v when !string.IsNullOrEmpty((string?)v.Value) => [(string)v.Value!],
            _ => []
        };

    /// <summary>A nested object ("specs"), or null</summary>
    public static JObject? GetObject(JToken? parent, string name) => Get(parent, name) as JObject;
}
