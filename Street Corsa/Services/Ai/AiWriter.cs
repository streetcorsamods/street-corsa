using System.IO;
using StreetCorsa.Configuration;
using StreetCorsa.Helpers;
using StreetCorsa.Logging;
using StreetCorsa.Models.GameState;
using StreetCorsa.Services.Market;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Services.Ai
{
    /// <summary>
    /// What the player's AI adds to the game, always on top of the game's own words and in the background: the diner's
    /// street talk and the paper's pieces told again (kept in the save next to the originals), a dealer's word on a car,
    /// an ad's blurb made livelier, and a portrait for a new face on the street. Nothing waits on it; whatever fails leaves
    /// the game's own. What it writes goes into the game state on the game's thread (<c>onGameThread</c>), so a save in
    /// progress never sees a list change under it.
    ///
    /// The prompts are the mods' (<see cref="AiPrompts"/>): <see cref="StreetTalkUse"/>, <see cref="NewsUse"/>,
    /// <see cref="DealerTakeUse"/>, <see cref="AdUse"/>, <see cref="PortraitLookUse"/>, and the ComfyUI workflow
    /// <see cref="PortraitUse"/>.
    /// </summary>
    public sealed class AiWriter : IAdWriter
    {
        public const string StreetTalkUse = "street_talk";
        public const string NewsUse = "news";
        public const string DealerTakeUse = "dealer_take";
        public const string AdUse = "ad";
        public const string PortraitLookUse = "portrait_look";
        public const string PortraitUse = "portrait";

        /// <summary>The folder under %AppData%\StreetCorsa the portraits go in, one folder per save</summary>
        public const string PortraitsFolder = "AiPortraits";

        private readonly IAiText _text;
        private readonly IAiImages _images;
        private readonly Func<GameState?> _currentGame;
        private readonly Action<Action> _onGameThread;
        private readonly Func<string, string> _carName;
        private readonly string _portraitsRoot;
        private readonly Random _random;
        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger("Ai");

        private readonly object _lock = new();
        private readonly Dictionary<string, Task<string?>> _writing = new(StringComparer.Ordinal);
        private readonly HashSet<Task> _background = [];

        /// <param name="currentGame">The game in play, where a dealer's word and an ad's blurb are kept</param>
        /// <param name="onGameThread">Runs a change to the game state where the game changes it (the UI thread); at once when not given</param>
        /// <param name="carName">A car's name by its catalog id; the id when not given</param>
        /// <param name="portraitsRoot">Where the portraits go; %AppData%\StreetCorsa\AiPortraits when not given</param>
        public AiWriter(IAiText text, IAiImages images, Func<GameState?> currentGame, Action<Action>? onGameThread = null,
            Func<string, string>? carName = null, string? portraitsRoot = null, Random? random = null, Func<StreetCorsa.Rules.AiRules>? rules = null)
        {
            _rules = rules ?? (() => StreetCorsa.Rules.RuleBook.Current.Ai);
            _text = text;
            _images = images;
            _currentGame = currentGame;
            _onGameThread = onGameThread ?? (change => change());
            _carName = carName ?? (id => id);
            _portraitsRoot = portraitsRoot ?? Path.Combine(AppSettings.AppDataPath, PortraitsFolder);
            _random = random ?? new Random();
        }

        private readonly Func<StreetCorsa.Rules.AiRules> _rules;

        private StreetCorsa.Rules.AiRules Rules => _rules();

        /// <summary>The text AI can be asked now (the dealer's word button shows only then)</summary>
        public bool IsAvailable => _text.IsAvailable;

        /// <summary>The language the game speaks now: what the AI writes is kept with it, and shown only in it</summary>
        public static string Language => GameText.Language is { Length: > 0 } language ? language : StreetCorsa.Rules.GameFile.Installed.Language;

        /// <summary>Everything started in the background so far, done (for the tests)</summary>
        public Task WhenIdle()
        {
            lock (_lock) return Task.WhenAll(_background.ToList());
        }

        private void Background(Func<Task> work)
        {
            var task = Task.Run(async () =>
            {
                try
                {
                    await work().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.Warning("AI background work failed: {Error}", ex.Message);
                }
            });
            lock (_lock) _background.Add(task);
            task.ContinueWith(t => { lock (_lock) _background.Remove(t); }, TaskScheduler.Default);
        }

        private bool Chance(double chance)
        {
            lock (_random) return chance >= 1 || _random.NextDouble() < chance;
        }

        // ----- the diner's talk and the paper -----

        /// <summary>New lines of street talk (<see cref="Opponents.OpponentLifeService.AddTalk"/>): each told again in the AI's words</summary>
        public void StreetTalkAdded(GameState game, IReadOnlyList<StreetTalkItem> items)
        {
            if (!_text.IsAvailable) return;
            var language = Language;
            foreach (var item in items.Where(i => i.AiText == null && i.Text.Length > 0 && Chance(Rules.StreetTalkChance)).ToList())
            {
                Background(async () =>
                {
                    var said = await _text.WriteAsync(StreetTalkUse, new TextValues { ["line"] = item.Text }).ConfigureAwait(false);
                    if (said != null) _onGameThread(() => { item.AiText = said; item.AiLanguage = language; });
                });
            }
        }

        /// <summary>New pieces in the paper (<see cref="News.NewsWriter.Add"/>): each written again, a headline and its body</summary>
        public void NewsAdded(GameState game, IReadOnlyList<NewsArticle> articles)
        {
            if (!_text.IsAvailable) return;
            var language = Language;
            foreach (var article in articles.Where(a => a.AiBody == null && a.Body.Length > 0 && Chance(Rules.NewsChance)).ToList())
            {
                Background(async () =>
                {
                    var written = await _text.WriteAsync(NewsUse, new TextValues { ["headline"] = article.Headline, ["body"] = article.Body }).ConfigureAwait(false);
                    if (SplitPiece(written) is not { } piece) return;
                    _onGameThread(() => { article.AiHeadline = piece.Headline; article.AiBody = piece.Body; article.AiLanguage = language; });
                });
            }
        }

        /// <summary>A piece as the AI writes it: the headline on the first line, the body after; null without both</summary>
        public static (string Headline, string Body)? SplitPiece(string? written)
        {
            if (string.IsNullOrWhiteSpace(written)) return null;
            var lines = written.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            if (lines.Count < 2) return null;

            var headline = Unlabelled(lines[0]);
            var body = Unlabelled(string.Join(" ", lines.Skip(1)));
            return headline.Length == 0 || body.Length == 0 ? null : (headline, body);
        }

        /// <summary>A label a model puts before a part ("Headline:", "BODY -") taken off</summary>
        private static string Unlabelled(string text)
        {
            var colon = text.IndexOf(':');
            if (colon is > 0 and <= 12 && text[..colon].All(c => char.IsLetter(c) || c == ' ')) text = text[(colon + 1)..];
            return text.Trim().Trim('"').Trim();
        }

        // ----- a dealer's word, an ad's blurb -----

        /// <summary>
        /// The dealer's word on a car on the lot (or a private seller's, with no dealer): written once per listing and kept
        /// in the save, in the language the game speaks. Null when the AI is off or fails: the button says nothing.
        /// </summary>
        public Task<string?> DealerTakeAsync(UsedCarListing listing, DealerLocation? dealer)
        {
            var values = new TextValues
            {
                ["car"] = _carName(listing.CarDefinitionId),
                ["price"] = new Money(listing.Price),
                ["mileage"] = listing.Mileage,
                ["condition"] = (int)Math.Round(listing.Condition * 100),
                ["engine"] = listing.EngineSummary ?? string.Empty,
                ["power"] = listing.PowerHp is { } hp ? (int)Math.Round(hp) : null,
                ["modified"] = listing.IsModified ? "yes" : "no",
                ["dealer"] = dealer?.Name ?? string.Empty,
                ["specialty"] = dealer?.Specialization ?? string.Empty,
                ["private"] = dealer == null ? "yes" : "no",
                ["days"] = _currentGame() is { } game ? (int)(game.Date.Date - listing.ListedDate.Date).TotalDays : 0
            };
            return KeptAsync("dealer_" + listing.Id, DealerTakeUse, values, AiPriority.Now);
        }

        /// <summary>
        /// An ad's blurb told livelier by the AI, keeping its facts: <paramref name="kind"/> is what is for sale ("car",
        /// "part"), <paramref name="baseBlurb"/> the game's own blurb, <paramref name="facts"/> what the prompt may name
        /// beyond them (the mods' prompt says which; money and units as in the game's text). With <paramref name="key"/>
        /// the answer is kept in the save and given again for that key. Null when the AI is off or fails: the game's blurb stands.
        /// </summary>
        public Task<string?> ImproveAdAsync(string kind, string baseBlurb, IReadOnlyDictionary<string, object?>? facts = null, string? key = null)
        {
            var values = new TextValues();
            foreach (var (name, value) in facts ?? new Dictionary<string, object?>()) values[name] = value;
            values["kind"] = kind;
            values["blurb"] = baseBlurb;
            return key == null ? WriteOnceAsync(null, AdUse, values, AiPriority.Background) : KeptAsync("ad_" + key, AdUse, values, AiPriority.Background);
        }

        // The ads waiting for the AI, asked one at a time: a new game makes a hundred at once, and the line the player's
        // own requests share would otherwise be full of them
        private readonly Queue<(string Kind, string Written, Dictionary<string, object?> Values, Action<string> Rewrite)> _adDesk = new();
        private bool _deskRunning;

        /// <summary>
        /// The paper's writer (<see cref="AdBlurbs.Writer"/>): an ad's blurb, offered as the ad is made, comes back livelier
        /// when the AI answers, on the game's thread; the ad keeps it, so it is never kept twice. The ads wait their turn
        /// at a desk of their own and go to the AI one by one, as background work.
        /// </summary>
        public bool CanImprove => _text.IsAvailable;

        public void Improve(AdFacts facts, string written, Action<string> rewrite)
        {
            if (!_text.IsAvailable) return;

            var (kind, values) = facts switch
            {
                CarAdFacts car => ("car", new Dictionary<string, object?>
                {
                    ["engine"] = car.Engine ?? string.Empty,
                    ["parts"] = string.Join(", ", car.Standouts.Select(s => s.Part))
                }),
                PartAdFacts part => ("part", new Dictionary<string, object?> { ["part"] = part.Part }),
                _ => ("car", new Dictionary<string, object?>())
            };
            lock (_lock)
            {
                _adDesk.Enqueue((kind, written, values, rewrite));
                if (_deskRunning) return;
                _deskRunning = true;
            }

            Background(WorkTheDeskAsync);
        }

        /// <summary>The ads at the desk, one after the other, until none waits (or the AI is turned off: they keep the game's words)</summary>
        private async Task WorkTheDeskAsync()
        {
            while (true)
            {
                (string Kind, string Written, Dictionary<string, object?> Values, Action<string> Rewrite) ad;
                lock (_lock)
                {
                    if (_adDesk.Count == 0 || !_text.IsAvailable)
                    {
                        _adDesk.Clear();
                        _deskRunning = false;
                        return;
                    }

                    ad = _adDesk.Dequeue();
                }

                try
                {
                    if (await ImproveAdAsync(ad.Kind, ad.Written, ad.Values).ConfigureAwait(false) is { Length: > 0 } better)
                        _onGameThread(() => ad.Rewrite(better));
                }
                catch (Exception ex)
                {
                    _logger.Warning("An ad was not rewritten: {Error}", ex.Message);
                }
            }
        }

        /// <summary>What was kept for <paramref name="key"/> in the game in play (in the language spoken), else written and kept</summary>
        private Task<string?> KeptAsync(string key, string use, TextValues values, AiPriority priority)
        {
            var game = _currentGame();
            var language = Language;
            if (game?.AiWords?.FirstOrDefault(w => w.Key == key && w.Language == language) is { } kept) return Task.FromResult<string?>(kept.Text);
            if (!_text.IsAvailable) return Task.FromResult<string?>(null);

            return WriteOnceAsync(key, use, values, priority, said =>
            {
                if (game == null) return;
                _onGameThread(() =>
                {
                    game.AiWords ??= [];
                    game.AiWords.RemoveAll(w => w.Key == key);
                    game.AiWords.Add(new AiWords { Key = key, Text = said, Language = language, Date = game.Date });
                    var over = game.AiWords.Count - Math.Max(1, Rules.MaxKept);
                    if (over > 0) game.AiWords.RemoveRange(0, over);
                });
            });
        }

        /// <summary>One request per key at a time: asked twice while it is written, the same answer comes to both</summary>
        private Task<string?> WriteOnceAsync(string? key, string use, TextValues values, AiPriority priority, Action<string>? keep = null)
        {
            if (!_text.IsAvailable) return Task.FromResult<string?>(null);
            if (key == null) return _text.WriteAsync(use, values, priority);

            lock (_lock)
            {
                if (_writing.TryGetValue(key, out var running)) return running;
                var task = WriteAndKeepAsync();
                _writing[key] = task;
                return task;
            }

            async Task<string?> WriteAndKeepAsync()
            {
                try
                {
                    var said = await _text.WriteAsync(use, values, priority).ConfigureAwait(false);
                    if (said != null) keep?.Invoke(said);
                    return said;
                }
                finally
                {
                    lock (_lock) _writing.Remove(key);
                }
            }
        }

        // ----- portraits -----

        /// <summary>The file a rival's painted portrait is (or would be) in, for a save</summary>
        public string PortraitFile(string saveName, Guid opponentId) =>
            Path.Combine(_portraitsRoot, SafeName(saveName), opponentId.ToString("N") + ".png");

        /// <summary>The portrait ComfyUI painted for a rival in the game in play; null when there is none</summary>
        public string? PortraitOf(Opponent opponent)
        {
            if (_currentGame() is not { SaveName.Length: > 0 } game) return null;
            var file = PortraitFile(game.SaveName, opponent.OpponentId);
            return File.Exists(file) ? file : null;
        }

        /// <summary>
        /// A new face on the street (<see cref="Opponents.OpponentLifeService"/>): with ComfyUI on, a portrait is painted for
        /// them in the background (the text AI, if on, describes how they look first), shown in place of their own from then on
        /// </summary>
        public void NewFace(GameState game, Opponent opponent)
        {
            if (!_images.IsAvailable || !Rules.PaintNewFaces || opponent.IsKing || game.SaveName.Length == 0) return;
            var file = PortraitFile(game.SaveName, opponent.OpponentId);
            if (File.Exists(file)) return;

            var values = new TextValues
            {
                ["name"] = opponent.Name,
                ["nickname"] = opponent.Nickname,
                ["age"] = opponent.Age,
                ["sex"] = opponent.Gender == Gender.Female ? "woman" : "man",
                ["biography"] = opponent.Biography ?? string.Empty,
                ["aggression"] = opponent.Aggression
            };
            Background(async () =>
            {
                values["look"] = await _text.WriteAsync(PortraitLookUse, values).ConfigureAwait(false) ?? string.Empty;
                var picture = await _images.PaintAsync(PortraitUse, values).ConfigureAwait(false);
                if (picture == null || picture.Length == 0) return;

                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                SafeFile.WriteAllBytes(file, picture);
                _logger.Information("Portrait painted for {Name}: {File}", opponent.Name, file);
            });
        }

        private static string SafeName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var safe = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
            return safe.Length == 0 ? "_" : safe;
        }

        // ----- what is shown -----

        /// <summary>A line of street talk as the diner shows it: the AI's, when it wrote one in the language spoken</summary>
        public static string Said(StreetTalkItem item) => item.AiText is { Length: > 0 } ai && item.AiLanguage == Language ? ai : item.Text;

        /// <summary>A piece's headline as the paper prints it: the AI's, when it wrote one in the language spoken</summary>
        public static string Headline(NewsArticle article) => ByAi(article) ? article.AiHeadline! : article.Headline;

        public static string Body(NewsArticle article) => ByAi(article) ? article.AiBody! : article.Body;

        private static bool ByAi(NewsArticle article) =>
            article.AiHeadline is { Length: > 0 } && article.AiBody is { Length: > 0 } && article.AiLanguage == Language;
    }
}
