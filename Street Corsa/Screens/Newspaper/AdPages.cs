using System.Collections.ObjectModel;

namespace StreetCorsa.Screens.Newspaper
{
    /// <summary>
    /// A section of the classifieds, a page at a time: the first page when it is filled, another each time the player
    /// scrolls to the end of what is shown (<see cref="ShowMore"/>), until every ad is out. Only what is shown is laid
    /// out, so a market of hundreds of cars opens as fast as one of a dozen.
    /// </summary>
    public sealed class AdPages<T>(int pageSize) where T : class
    {
        private readonly List<T> _all = [];

        public int PageSize { get; } = Math.Max(1, pageSize);

        /// <summary>The ads out so far, in the section's order</summary>
        public ObservableCollection<T> Shown { get; } = [];

        /// <summary>Every ad of the section, out or not</summary>
        public int Count => _all.Count;

        public bool HasMore => Shown.Count < _all.Count;

        /// <summary>The section's ads anew: the first page of them shows</summary>
        public void Fill(IEnumerable<T> ads)
        {
            _all.Clear();
            _all.AddRange(ads);
            Shown.Clear();
            ShowMore();
        }

        /// <summary>The next page; false when every ad was out already</summary>
        public bool ShowMore()
        {
            if (!HasMore) return false;

            foreach (var ad in _all.Skip(Shown.Count).Take(PageSize).ToList()) Shown.Add(ad);
            return true;
        }

        /// <summary>An ad out of the paper (bought, gone): the rest stay where they are, and no page is lost</summary>
        public void Remove(T ad)
        {
            if (!_all.Remove(ad)) return;
            Shown.Remove(ad);
        }
    }
}
