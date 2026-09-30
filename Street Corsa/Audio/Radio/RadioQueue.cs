namespace StreetCorsa.Audio.Radio;

/// <summary>What happens at the end: stop there, go round again, or play the same song over</summary>
public enum RadioRepeat
{
    Off,
    All,
    One
}

/// <summary>
/// The order a list's songs play in. In order, or shuffled: every song once before any comes round again, songs of
/// one artist kept apart where the list allows, and a new round never starting with the song the last one ended on.
/// Songs are known by their paths; the artist of each, for keeping them apart, comes from <c>artistOf</c>.
/// </summary>
public sealed class RadioQueue(Random? random = null, Func<string, string?>? artistOf = null)
{
    private readonly Random _random = random ?? new Random();
    private readonly Func<string, string?> _artistOf = artistOf ?? (_ => null);
    private List<string> _songs = [];
    private List<int> _order = [];
    private int _position = -1;

    public bool Shuffle { get; private set; }

    public RadioRepeat Repeat { get; set; }

    /// <summary>The list, in its own order</summary>
    public IReadOnlyList<string> Songs => _songs;

    /// <summary>The song on now; null before the first and after the last</summary>
    public string? Current => _position >= 0 && _position < _order.Count ? _songs[_order[_position]] : null;

    /// <summary>The song that comes after this one when it plays out, without moving on; null when the radio would stop</summary>
    public string? Upcoming
    {
        get
        {
            if (_order.Count == 0 || _position < 0) return null;
            if (Repeat == RadioRepeat.One) return Current;
            if (_position + 1 < _order.Count) return _songs[_order[_position + 1]];

            // A new round is shuffled anew: its first song is not known yet
            return Repeat == RadioRepeat.All && !Shuffle ? _songs[_order[0]] : null;
        }
    }

    /// <summary>A list to play, from <paramref name="start"/> (any song when shuffling and none is given, the first otherwise)</summary>
    public void Load(IEnumerable<string> songs, string? start, bool shuffle)
    {
        _songs = songs.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        Shuffle = shuffle;
        var from = start == null ? -1 : _songs.FindIndex(s => string.Equals(s, start, StringComparison.OrdinalIgnoreCase));
        Arrange(from, avoidFirst: null);
    }

    /// <summary>Shuffling on or off; the song on now plays on, and the rest follow it in the new order</summary>
    public void SetShuffle(bool shuffle)
    {
        if (shuffle == Shuffle) return;
        Shuffle = shuffle;
        var current = _position >= 0 && _position < _order.Count ? _order[_position] : -1;
        Arrange(current, avoidFirst: null);
    }

    /// <summary>
    /// The next song: when the last one played out (<paramref name="playedOut"/>), or the player skipped. At the end
    /// of the list a skip goes round again; a song that played out stops the radio there unless it repeats.
    /// </summary>
    public string? Next(bool playedOut)
    {
        if (_order.Count == 0) return null;
        if (playedOut && Repeat == RadioRepeat.One && Current != null) return Current;

        _position++;
        if (_position < _order.Count) return Current;

        if (playedOut && Repeat == RadioRepeat.Off)
        {
            _position = _order.Count;
            return null;
        }

        var last = _songs[_order[^1]];
        Arrange(-1, avoidFirst: last);
        return Current;
    }

    /// <summary>The song before; the first stays the first, unless the list goes round</summary>
    public string? Previous()
    {
        if (_order.Count == 0) return null;
        if (_position > 0) _position = Math.Min(_position, _order.Count) - 1;
        else if (Repeat == RadioRepeat.All) _position = _order.Count - 1;
        else _position = 0;
        return Current;
    }

    /// <summary>
    /// The order anew. Shuffled: <paramref name="from"/> first (a random song when none), the rest at random. In
    /// order: the list as it is, playing from <paramref name="from"/> (the first when none).
    /// </summary>
    private void Arrange(int from, string? avoidFirst)
    {
        var order = Enumerable.Range(0, _songs.Count).ToList();
        if (!Shuffle)
        {
            _order = order;
            _position = _songs.Count == 0 ? -1 : Math.Max(0, from);
            return;
        }

        for (var i = order.Count - 1; i > 0; i--)
        {
            var j = _random.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }

        if (from >= 0)
        {
            order.Remove(from);
            order.Insert(0, from);
        }
        else if (avoidFirst != null && order.Count > 1 && string.Equals(_songs[order[0]], avoidFirst, StringComparison.OrdinalIgnoreCase))
        {
            var other = 1 + _random.Next(order.Count - 1);
            (order[0], order[other]) = (order[other], order[0]);
        }

        SpreadArtists(order);
        _order = order;
        _position = order.Count == 0 ? -1 : 0;
    }

    /// <summary>
    /// Keeps an artist's songs from following each other: the order is built again, each place taking the first song
    /// left (in the shuffled order) that is not by the artist just before, except that an artist with more than half
    /// the songs left has to go now, or the end would be theirs back to back. The first place is never touched: the
    /// song picked to start with, or the one that keeps a new round from starting with the last round's end.
    /// </summary>
    private void SpreadArtists(List<int> order)
    {
        if (order.Count < 3) return;

        var left = order.Skip(1).ToList();
        var spread = new List<int>(order.Count) { order[0] };
        while (left.Count > 0)
        {
            var before = Artist(spread[^1]);
            var crowded = left.Select(Artist).OfType<string>()
                .GroupBy(a => a, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(g => g.Count() > left.Count / 2)?.Key;

            var pick = crowded != null && !Same(crowded, before)
                ? left.FindIndex(i => Same(Artist(i), crowded))
                : left.FindIndex(i => Artist(i) == null || !Same(Artist(i), before));
            if (pick < 0) pick = 0;

            spread.Add(left[pick]);
            left.RemoveAt(pick);
        }

        order.Clear();
        order.AddRange(spread);

        static bool Same(string? a, string? b) => a != null && b != null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    private string? Artist(int index) => _artistOf(_songs[index]) is { Length: > 0 } artist ? artist : null;
}
