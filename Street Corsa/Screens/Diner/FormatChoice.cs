namespace StreetCorsa.Screens.Diner
{
    /// <summary>A race the diner offers on a track: its format id and name</summary>
    public sealed record FormatChoice(string Id, string Name)
    {
        /// <summary>The id without its mod's namespace, for the format's own text keys</summary>
        public string LocalId => Mods.ModIds.Local(Id);

        public override string ToString() => Name;
    }
}
