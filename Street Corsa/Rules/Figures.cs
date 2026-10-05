using System.Globalization;

namespace StreetCorsa.Rules;

/// <summary>
/// Holds a figure of the rules to what the game can play by. A rules mod or a typo can give a figure the game divides
/// by as 0, a count it loops over in the millions, or a cost below nothing (which pays the player); such a figure is
/// the base game's instead, told in the problems. The base game's own figures are always inside their bounds.
/// </summary>
internal static class Figures
{
    /// <summary>The figure <paramref name="name"/> from <paramref name="least"/> to <paramref name="most"/>, both included</summary>
    public static void Within(object figures, string path, string name, double least, double most, List<string> problems) =>
        Hold(figures, path, name, value => value >= least && value <= most,
            FormattableString.Invariant($"from {least} to {most}"), problems);

    /// <summary>The figure <paramref name="name"/> above 0 and up to <paramref name="most"/>: one the game divides by</summary>
    public static void Above0(object figures, string path, string name, double most, List<string> problems) =>
        Hold(figures, path, name, value => value > 0 && value <= most,
            FormattableString.Invariant($"above 0 and up to {most}"), problems);

    /// <summary>Each of the costs <paramref name="names"/> from nothing to <paramref name="most"/></summary>
    public static void Money(object figures, string path, List<string> problems, double most, params string[] names)
    {
        foreach (var name in names) Within(figures, path, name, 0, most, problems);
    }

    private static void Hold(object figures, string path, string name, Func<double, bool> fits, string bounds, List<string> problems)
    {
        var type = figures.GetType();
        var property = type.GetProperty(name)!;
        var given = property.GetValue(figures)!;
        var value = Convert.ToDouble(given, CultureInfo.InvariantCulture);
        if (double.IsFinite(value) && fits(value)) return;

        var baseFigure = property.GetValue(Activator.CreateInstance(type));
        property.SetValue(figures, baseFigure);
        problems.Add(FormattableString.Invariant(
            $"{path}.{RuleBook.Camel(name)} ({given}) is not {bounds}; the base game's ({baseFigure}) is kept"));
    }
}
