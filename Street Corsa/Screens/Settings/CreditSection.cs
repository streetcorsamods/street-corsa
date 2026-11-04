namespace StreetCorsa.Screens.Settings
{
    /// <summary>One block of the credits screen: what was made (the heading) and whose work it is, a line each</summary>
    public sealed record CreditSection(string Heading, IReadOnlyList<string> Lines);
}
