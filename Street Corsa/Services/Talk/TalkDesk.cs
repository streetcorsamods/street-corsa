using StreetCorsa.Logging;
using StreetCorsa.Models.GameState;
using StreetCorsa.Models.Race;
using StreetCorsa.Parts;
using StreetCorsa.Rules;
using StreetCorsa.Screens.Shared;
using StreetCorsa.Services.Ai;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Services.Talk
{
    /// <summary>
    /// Where talks begin (<see cref="Shared"/>, started with the app): who the person behind an ad, a lot or a racer is,
    /// and what the talks need from the rest of the game (the AI, the race record, the cars' and parts' names). Talking
    /// is there only while the player's AI is (<see cref="IsAvailable"/>).
    /// </summary>
    public sealed class TalkDesk
    {
        private static TalkDesk _shared = new(new NoAiText());

        private readonly Func<string, string, Task<List<ProcessedRaceSession>>>? _races;
        private readonly Func<string, string> _carName;
        private readonly Func<PartsCatalog?> _parts;
        private readonly Action<Action> _onGameThread;
        private readonly Func<AiPrompts> _prompts;
        private readonly Func<TalkRules> _rules;
        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger("Talk");
        private readonly Func<string, DealerSalesman?> _salesmen;

        /// <summary>A person's portrait from the game's data (a dealer's salesman's); null when there is none</summary>
        public string? PortraitOf(Person person) =>
            person.Kind == PersonKind.Salesman && person.Key.StartsWith("dealer:", StringComparison.Ordinal)
                ? _salesmen(person.Key["dealer:".Length..])?.PortraitFile
                : null;

        /// <param name="text">The player's AI</param>
        /// <param name="races">The player's races of a save against one person: by the save's name and the person's</param>
        /// <param name="carName">A car's name by its id</param>
        /// <param name="parts">The parts catalog</param>
        /// <param name="onGameThread">Runs what changes the game on its thread (a memory written in the background)</param>
        /// <param name="salesmen">A dealer's salesman as its mod data gives him, by the dealer's id; none, and they are made up</param>
        public TalkDesk(IAiText text, Func<string, string, Task<List<ProcessedRaceSession>>>? races = null, Func<string, string>? carName = null,
            Func<PartsCatalog?>? parts = null, Action<Action>? onGameThread = null, Func<AiPrompts>? prompts = null, Func<TalkRules>? rules = null,
            Func<string, DealerSalesman?>? salesmen = null)
        {
            _salesmen = salesmen ?? (_ => null);
            Text = text;
            _races = races;
            _carName = carName ?? (id => id);
            _parts = parts ?? (() => null);
            _onGameThread = onGameThread ?? (change => change());
            _prompts = prompts ?? (() => AiPrompts.Installed);
            _rules = rules ?? (() => RuleBook.Current.Ai.Talk);
        }

        public static TalkDesk Shared => _shared;

        public static TalkDesk Start(TalkDesk desk) => _shared = desk;

        public IAiText Text { get; }

        /// <summary>Talking can begin: the player's AI is on and answering</summary>
        public bool IsAvailable => Text.IsAvailable;

        internal TalkRules Rules => _rules();

        /// <summary>The draws of a talk (how far a seller will come down)</summary>
        internal Random Random { get; init; } = Random.Shared;

        internal AiPrompts Prompts => _prompts();

        internal PartsCatalog? Parts => _parts();

        internal string CarName(string id) => _carName(id);

        internal async Task<IReadOnlyList<ProcessedRaceSession>> RacesOf(string saveName, string personName) =>
            _races == null || string.IsNullOrEmpty(saveName) ? [] : await _races(saveName, personName).ConfigureAwait(true);

        /// <summary>
        /// The seller of a car: a rival's own ad is the rival; a dealer's car (<paramref name="dealerId"/>) the dealer's
        /// salesman, on the lot; anyone else's a private seller, on the phone or at the curb
        /// </summary>
        public Conversation ForCar(GameState game, UsedCarListingViewModel car, string? dealerId, TalkPlace place)
        {
            Forget(game);
            var subject = new CarSubject(car);
            if (car.RivalAd is { } ad && game.Racers.Find(ad.RivalName) is Opponent rival)
                return new Conversation(this, game, TalkPeople.Rival(game, rival, Prompts), rival, place, subject);
            if (!string.IsNullOrWhiteSpace(dealerId))
                return new Conversation(this, game, TalkPeople.Salesman(game, dealerId, _salesmen(dealerId), Prompts, Rules), null, TalkPlace.Lot, subject);
            return new Conversation(this, game, TalkPeople.Seller(game, TalkPeople.CarKey(car.Listing), prompts: Prompts, rules: Rules), null, place, subject);
        }

        /// <summary>A dealer's salesman as the player walks onto the lot: the cars on it, none picked yet</summary>
        public Conversation ForLot(GameState game, string dealerId, IReadOnlyList<UsedCarListingViewModel> lot)
        {
            Forget(game);
            return new Conversation(this, game, TalkPeople.Salesman(game, dealerId, _salesmen(dealerId), Prompts, Rules), null, TalkPlace.Lot, null, lot);
        }

        /// <summary>The seller of a part in the paper, on the phone: a rival who pulled it off a car, or whoever placed the ad</summary>
        public Conversation ForPart(GameState game, PartAd ad, string partName)
        {
            Forget(game);
            var subject = new PartSubject(ad, partName);
            if (ad.SellerRival is { } name && game.Racers.Find(name) is Opponent rival)
                return new Conversation(this, game, TalkPeople.Rival(game, rival, Prompts), rival, TalkPlace.Phone, subject);
            return new Conversation(this, game, TalkPeople.Seller(game, TalkPeople.PartKey(ad), ad.SellerName, Prompts, Rules), null, TalkPlace.Phone, subject);
        }

        /// <summary>A racer, at the diner or pulled up alongside</summary>
        public Conversation ForRival(GameState game, Opponent rival, TalkPlace place)
        {
            Forget(game);
            return new Conversation(this, game, TalkPeople.Rival(game, rival, Prompts), rival, place, null);
        }

        private void Forget(GameState game)
        {
            game.People ??= [];
            TalkPeople.ForgetAll(game, Rules);
        }

        /// <summary>What the person makes of a talk, asked in the background and kept with them</summary>
        internal void Remember(Conversation talk)
        {
            var person = talk.Person;
            var transcript = string.Join("\n", talk.Turns.Select(turn =>
                GameText.Say(turn.Mine ? "chat.transcript.them" : "chat.transcript.player", ("name", person.Name), ("text", turn.Text))));
            var values = new TextValues
            {
                ["name"] = person.Name,
                ["player"] = talk.PlayerName,
                ["talk"] = transcript
            };
            var language = AiWriter.Language;
            var rules = Rules;
            var date = talk.Date;
            _ = Task.Run(async () =>
            {
                try
                {
                    var note = NoteOf(await Text.WriteAsync("chat_memory", values).ConfigureAwait(false));
                    if (note != null) _onGameThread(() => TalkPeople.Remember(person, date, note, language, rules));
                }
                catch (Exception ex)
                {
                    _logger.Warning("Could not keep what {Person} makes of the talk: {Error}", person.Name, ex.Message);
                }
            });
        }

        /// <summary>The note in what the AI wrote: its first line that is not a heading ("Note to self:")</summary>
        public static string? NoteOf(string? written) =>
            written?.Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.Length > 0 && !line.EndsWith(':'));

        /// <summary>A text AI that is never there: talking is off until the app starts the desk</summary>
        private sealed class NoAiText : IAiText
        {
            public bool IsAvailable => false;

            public Task<string?> WriteAsync(string use, TextValues values, AiPriority priority = AiPriority.Background, CancellationToken cancellationToken = default) =>
                Task.FromResult<string?>(null);

            public Task<string?> ChatAsync(string use, TextValues values, IReadOnlyList<AiTurn> turns, CancellationToken cancellationToken = default) =>
                Task.FromResult<string?>(null);

            public Task<AiTestResult> TestAsync(Services.Settings.AiSettings settings, string? apiKey, CancellationToken cancellationToken = default) =>
                Task.FromResult(new AiTestResult(false, string.Empty));
        }
    }
}
