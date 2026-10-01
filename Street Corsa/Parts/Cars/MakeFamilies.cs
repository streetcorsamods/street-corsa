namespace StreetCorsa.Parts.Cars;

/// <summary>
/// Which corporate family a car or an engine belongs to, from the words of its name, as the mods' makes say
/// (<see cref="CarBook"/>). Engines stay in the family: a Pontiac may turn up with a Chevrolet V8, never with a Hemi.
/// </summary>
public static class MakeFamilies
{
    /// <summary>Family of whatever the text names, "" when no word gives it away. The first word that tells wins.</summary>
    public static string FamilyOf(string text) => CarBook.Current.FamilyOf(text);

    /// <summary>Lower-case words and numbers of a name that are worth comparing</summary>
    public static IReadOnlySet<string> Tokens(string text) => CarBook.Current.Tokens(text);
}
