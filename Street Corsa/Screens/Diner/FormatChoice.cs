namespace StreetCorsa.Screens.Diner
{
    /// <summary>A race the diner offers on a track: its format id and name</summary>
    public sealed record FormatChoice(string Id, string Name)
    {
        public override string ToString() => Name;
    }
}
