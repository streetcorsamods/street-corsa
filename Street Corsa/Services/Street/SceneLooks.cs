namespace StreetCorsa.Services.Street
{
    /// <summary>
    /// A scene's looks: what its model keeps under nodes named <see cref="Prefix"/> (a diner's roof sign with its name,
    /// the trucks in the Hi-Way Grill's lot, the Starlite's neon) is shown only when the scene names the group
    /// (<c>"looks": ["hiway"]</c> shows every node named LOOK_HIWAY...); the rest of them are hidden. So one street
    /// model wears a different look for each diner on it. tools/Shared/PreparedScene.cs writes the nodes.
    /// </summary>
    public static class SceneLooks
    {
        /// <summary>What a look group's nodes are named after, with the group's name in capitals after it</summary>
        public const string Prefix = "LOOK_";

        /// <summary>The node prefixes of the groups named (any case, blanks left out): "hiway" is LOOK_HIWAY</summary>
        public static IReadOnlyList<string> Prefixes(IEnumerable<string?>? names) =>
            (names ?? []).Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => Prefix + n!.Trim().ToUpperInvariant()).Distinct().ToList();
    }
}
