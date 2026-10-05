namespace StreetCorsa.Screens.Newspaper
{
    /// <summary>
    /// The order the paper sets its ads in: no order at all, as a paper's classifieds run, but the same each time it is
    /// opened, so an ad does not jump about between reads. Each one's place is drawn from its own id; the player's own
    /// ads go in among the others the same way.
    /// </summary>
    public static class ClassifiedOrder
    {
        /// <summary><paramref name="ads"/> in an order drawn from each one's <paramref name="id"/>: the same for the same ads</summary>
        public static IEnumerable<T> Shuffled<T>(IEnumerable<T> ads, Func<T, string> id) =>
            ads.OrderBy(a => Helpers.StableHash.Fnv(id(a))).ThenBy(id, StringComparer.Ordinal);

        /// <summary>
        /// <paramref name="others"/> in their order, with each of <paramref name="mine"/> put in at the place its key gives
        /// it: the same place for the same ads, whatever order the ads come in
        /// </summary>
        public static List<T> Mix<T>(IEnumerable<T> others, IEnumerable<(Guid Key, T Ad)> mine)
        {
            var all = others.ToList();
            foreach (var (key, ad) in mine.OrderBy(m => m.Key))
                all.Insert((int)(Helpers.StableHash.Of(key.ToString("N")) % (uint)(all.Count + 1)), ad);
            return all;
        }
    }
}
