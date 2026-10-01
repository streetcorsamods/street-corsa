namespace StreetCorsa.Screens.Settings
{
    /// <summary>Units the player can read: "imperial", "metric", or null for the game's own</summary>
    public sealed record UnitsChoice(string? Id, string Name)
    {
        public override string ToString() => Name;
    }
}
