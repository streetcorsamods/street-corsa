namespace StreetCorsa.Screens.Newspaper
{
    /// <summary>
    /// Where the player's own ads go among the paper's others: nowhere special, as the paper set them, but the same place
    /// each time it is opened, so an ad does not jump about between reads. Each one's place is drawn from its own id.
    /// </summary>
    public static class ClassifiedOrder
    {
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
