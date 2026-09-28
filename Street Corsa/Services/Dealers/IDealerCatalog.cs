using StreetCorsa.Models.GameState;

namespace StreetCorsa.Services.Dealers
{
    /// <summary>
    /// The dealers as authored: where they are, what they look like inside, and what they stock.
    /// </summary>
    public interface IDealerCatalog
    {
        /// <summary>Every dealer, in the order the file lists them</summary>
        IReadOnlyList<DealerDefinition> All { get; }

        /// <summary>One dealer by its id, or null if the file does not have it</summary>
        DealerDefinition? Get(string id);

        /// <summary>
        /// The room a showroom gives a lot to work with. A showroom nobody has measured comes back as a
        /// cautious small one rather than null, so a lot is never laid out on guesswork.
        /// </summary>
        ShowroomSpec GetShowroom(string id);

        /// <summary>The picture the pins stand on (the last mod's); null when no mod has one</summary>
        DealerMapSpec? Map { get; }

        /// <summary>
        /// Every dealer as a new game carries it (open, or waiting to open). A game under way is brought up to the
        /// dealers with <see cref="DealerRoster.Sync"/>, which keeps how each one's business is going.
        /// </summary>
        List<DealerLocation> ToLocations();
    }
}
