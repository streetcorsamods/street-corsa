namespace StreetCorsa.Screens.Settings
{
    /// <summary>A language the game can speak: a folder in the mods' <c>text</c>, or the game's own</summary>
    public sealed record LanguageChoice(string Id, string Name, bool IsGameLanguage)
    {
        public override string ToString() => Name;
    }
}
