using System.Globalization;

namespace StreetCorsa.Tools;

/// <summary>
/// A builder's command line: the words that are not options (the word after an option that takes a value is that
/// option's, so <c>--scale 0.112 prepared</c> is never taken for the folder), options by name, flags (options without a
/// value, named up front), and number options checked before anything is built, so a mistyped number (<c>7OO</c>) is
/// refused rather than built as the default. Linked into the builders that take options (diner, station, street, props).
/// </summary>
internal sealed class BuilderArgs
{
    private readonly string[] _args;

    /// <param name="args">The command line as Main gets it</param>
    /// <param name="flags">The options that take no value (<c>--flip</c>, <c>--mirror</c>)</param>
    public BuilderArgs(string[] args, params string[] flags)
    {
        _args = args;
        var positional = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal)) positional.Add(args[i]);
            else if (!flags.Contains(args[i])) i++;
        }

        Positional = positional;
    }

    /// <summary>The words that are neither options nor their values, in order</summary>
    public IReadOnlyList<string> Positional { get; }

    /// <summary>Whether a flag is given</summary>
    public bool Flag(string name) => _args.Contains(name);

    /// <summary>An option's value; null when it is not given, or given last with nothing after it</summary>
    public string? Option(string name)
    {
        var i = Array.IndexOf(_args, name);
        return i >= 0 && i + 1 < _args.Length ? _args[i + 1] : null;
    }

    /// <summary>Every value of an option given more than once (<c>--node a=x --node b=y</c>), in order</summary>
    public IReadOnlyList<string> Options(string name)
    {
        var values = new List<string>();
        for (var i = 0; i + 1 < _args.Length; i++)
        {
            if (_args[i] == name) values.Add(_args[++i]);
        }

        return values;
    }

    /// <summary>A number option; null when it is not given (one given that is no number is refused by <see cref="NumbersOk"/>)</summary>
    public float? Number(string name) => Parse(Option(name));

    /// <summary>
    /// Whether every one of these options that is given is a finite number; the first that is not is told on the error
    /// stream, with the usage
    /// </summary>
    public bool NumbersOk(string usage, params string[] names)
    {
        foreach (var name in names)
        {
            if (!_args.Contains(name) || Parse(Option(name)) != null) continue;

            Console.Error.WriteLine($"{name} takes a number, not {Option(name) ?? "nothing"}");
            Console.Error.WriteLine(usage);
            return false;
        }

        return true;
    }

    private static float? Parse(string? text) =>
        text != null && float.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var value)
            && float.IsFinite(value)
            ? value
            : null;
}
