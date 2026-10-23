namespace StreetCorsa.Screens.Settings
{
    /// <summary>A setting the game can play: a setting mod installed</summary>
    public sealed record SettingChoice(string Id, string Name)
    {
        public override string ToString() => Name;
    }
}
