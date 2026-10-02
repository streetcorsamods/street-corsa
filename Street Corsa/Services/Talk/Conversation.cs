using StreetCorsa.Logging;
using StreetCorsa.Models.GameState;
using StreetCorsa.Models.Race;
using StreetCorsa.Screens.Shared;
using StreetCorsa.Services.Ai;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Services.Talk
{
    /// <summary>Where a talk happens: what the person is told it is, and how they begin</summary>
    public enum TalkPlace
    {
        /// <summary>The player phones about an ad</summary>
        Phone,

        /// <summary>On a dealer's lot: the salesman comes over</summary>
        Lot,

        /// <summary>At the seller's curb, come to see the car</summary>
        Curb,

        /// <summary>At the diner's tables</summary>
        Diner,

        /// <summary>Pulled up alongside on the street</summary>
        Cruise
    }

    /// <summary>
    /// One talk with one person: the turns so far, what is talked about (it can change: a salesman follows the player round
    /// the lot), and what the game knows, given to the AI with every turn as the only facts it may use (<see cref="TalkFacts"/>).
    /// An offer the player makes is decided here, by the rules (<see cref="Haggle"/>), before the AI is asked, and the AI
    /// is told the answer to say. Everything runs on the game's thread: the AI is awaited, nothing else.
    /// </summary>
    public sealed class Conversation
    {
        private readonly TalkDesk _desk;
        private readonly GameState _game;
        private readonly List<AiTurn> _turns = [];
        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger("Talk");
        private IReadOnlyList<ProcessedRaceSession> _sessions = [];
        private bool _sessionsLoaded;

        internal Conversation(TalkDesk desk, GameState game, Person person, Opponent? rival, TalkPlace place, TalkSubject? subject,
            IReadOnlyList<UsedCarListingViewModel>? lot = null)
        {
            _desk = desk;
            _game = game;
            Person = person;
            Rival = rival;
            Place = place;
            Lot = lot ?? [];
            if (subject != null) Focus(subject);
        }

        public Person Person { get; }

        /// <summary>The racer, when the person is one</summary>
        public Opponent? Rival { get; }

        public TalkPlace Place { get; }

        /// <summary>Game time now: when the talk is had</summary>
        public DateTime Date => _game.Date;

        /// <summary>What is talked about now; null at the diner, or a salesman before a car is picked</summary>
        public TalkSubject? Subject { get; private set; }

        /// <summary>A salesman's cars</summary>
        public IReadOnlyList<UsedCarListingViewModel> Lot { get; }

        /// <summary>The prompt asked: chat_seller, chat_salesman, chat_rival</summary>
        public string Use => Person.Kind switch
        {
            PersonKind.Salesman => "chat_salesman",
            PersonKind.Rival => "chat_rival",
            _ => "chat_seller"
        };

        /// <summary>The player said something: the talk is worth remembering</summary>
        public bool PlayerSpoke { get; private set; }

        /// <summary>The answer to the last offer, if any</summary>
        public HaggleOutcome? LastOutcome { get; private set; }

        /// <summary>A price was talked over (the price may have changed): the screens show the new one</summary>
        public event Action<HaggleOutcome>? Dealt;

        /// <summary>The deal over the thing talked about now</summary>
        public PersonDeal? Deal => Subject == null ? null : Person.DealFor(Subject.Thing);

        /// <summary>The turns as sent: the player's and the stage lines, and the person's</summary>
        public IReadOnlyList<AiTurn> Turns => _turns;

        /// <summary>The person begins: picks up the phone, comes over on the lot, looks up at the diner</summary>
        public async Task<string?> OpenAsync(CancellationToken cancellationToken = default)
        {
            await LoadRacesAsync().ConfigureAwait(true);
            var key = Place switch
            {
                TalkPlace.Phone => "chat.stage.phone",
                TalkPlace.Lot => Subject == null ? "chat.stage.lot" : "chat.stage.lot_car",
                TalkPlace.Curb => "chat.stage.curb",
                TalkPlace.Diner => "chat.stage.diner",
                _ => "chat.stage.cruise"
            };
            return await AskAsync(GameText.Say(key, ("thing", Subject?.Name ?? string.Empty)), null, cancellationToken).ConfigureAwait(true);
        }

        /// <summary>
        /// The talk turns to <paramref name="subject"/> (a salesman, as the player picks a car on the lot): the person
        /// goes on about it unasked, something new when they have talked about it before
        /// </summary>
        public Task<string?> TurnToAsync(TalkSubject subject, CancellationToken cancellationToken = default)
        {
            var before = Person.DealFor(subject.Thing) is { Said.Count: > 0 };
            Focus(subject);
            return AskAsync(GameText.Say(before ? "chat.stage.picked_again" : "chat.stage.picked", ("thing", subject.Name)), null, cancellationToken);
        }

        /// <summary>Back from a thing to the rest (the lot): nothing is said, the next words are about the lot</summary>
        public void TurnAway() => Subject = null;

        /// <summary>What the player says: an offer in it is answered by the rules first, then the person speaks</summary>
        public Task<string?> SayAsync(string text, CancellationToken cancellationToken = default)
        {
            text = text.Trim();
            if (text.Length == 0) return Task.FromResult<string?>(null);

            PlayerSpoke = true;
            var outcome = Subject == null ? null : Offer(text, Subject);
            return AskAsync(text, outcome, cancellationToken);
        }

        /// <summary>The talk is over: what the person makes of it is written in the background and kept with them</summary>
        public void End()
        {
            if (!PlayerSpoke) return;
            _desk.Remember(this);
        }

        private void Focus(TalkSubject subject)
        {
            Subject = subject;
            // The price before any talk, kept the first time: every later floor is worked out from it
            TalkPeople.Deal(Person, subject.Thing, subject.Price);
        }

        /// <summary>The rules' answer to an offer in <paramref name="text"/>, the price changed by it; null when it holds none</summary>
        private HaggleOutcome? Offer(string text, TalkSubject subject)
        {
            var rules = _desk.Rules;
            if (Haggle.OfferIn(text, subject.Price, GameText.Installed.Lines("chat.offer_words"), rules) is not { } offer) return null;

            var deal = TalkPeople.Deal(Person, subject.Thing, subject.Price);
            var temperament = _desk.Prompts.TemperamentOf(Person.Temperament);
            var days = Math.Max(0, (int)(_game.Date.Date - subject.OnSaleSince.Date).TotalDays);
            var floor = Haggle.Floor(deal.Asked, Person.Kind, temperament, days, Rival?.IsBroke == true, rules);
            var outcome = Haggle.Decide(offer, subject.Price, floor, deal.Counters, rules, subject.Round);

            deal.Date = _game.Date;
            if (outcome.Answer != HaggleAnswer.Accept) deal.Counters++;
            if (outcome.Price != subject.Price)
            {
                subject.Price = outcome.Price;
                deal.Agreed = outcome.Price;
            }

            _logger.Information("{Player} offered {Offer} to {Person} for {Thing}: {Answer}, price now {Price}",
                _game.Player.Name, offer, Person.Name, subject.Thing, outcome.Answer, outcome.Price);
            LastOutcome = outcome;
            Dealt?.Invoke(outcome);
            return outcome;
        }

        private async Task<string?> AskAsync(string playerTurn, HaggleOutcome? outcome, CancellationToken cancellationToken)
        {
            // Two of the player's in a row (the AI gave no answer to the first) go as one: the APIs want them taking turns
            if (_turns.Count > 0 && !_turns[^1].Mine)
                _turns[^1] = new AiTurn(false, _turns[^1].Text + "\n" + playerTurn);
            else
                _turns.Add(new AiTurn(false, playerTurn));

            var said = await _desk.Text.ChatAsync(Use, Values(outcome), Window(), cancellationToken).ConfigureAwait(true);
            if (said == null) return null;

            _turns.Add(new AiTurn(true, said));
            if (Person.Kind == PersonKind.Salesman && Deal is { } deal) TalkPeople.Said(deal, said, _desk.Rules);
            return said;
        }

        /// <summary>The newest turns the rules keep, from a player's turn on</summary>
        private List<AiTurn> Window()
        {
            var kept = _turns.TakeLast(Math.Max(1, _desk.Rules.TurnsKept)).ToList();
            while (kept.Count > 1 && kept[0].Mine) kept.RemoveAt(0);
            return kept;
        }

        private async Task LoadRacesAsync()
        {
            if (_sessionsLoaded) return;
            _sessionsLoaded = true;
            try
            {
                _sessions = await _desk.RacesOf(_game.SaveName).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                // Without the record the person only says they do not remember
                _logger.Warning("Could not read the races for a talk: {Error}", ex.Message);
            }
        }

        /// <summary>What the prompt is filled with: who the person is, where, and the fact sheet</summary>
        public TextValues Values(HaggleOutcome? outcome = null)
        {
            var temperament = _desk.Prompts.TemperamentOf(Person.Temperament);
            return new TextValues
            {
                ["name"] = Person.Name,
                ["nickname"] = Rival?.Nickname ?? string.Empty,
                ["age"] = Person.Age,
                ["sex"] = GameText.Say(Person.Gender == Gender.Female ? "chat.sex.woman" : "chat.sex.man"),
                ["temperament"] = temperament?.Words ?? string.Empty,
                ["place"] = GameText.Say($"chat.place.{Place.ToString().ToLowerInvariant()}"),
                ["player"] = _game.Player.Name,
                ["facts"] = Facts(outcome)
            };
        }

        /// <summary>The fact sheet for the next answer</summary>
        public string Facts(HaggleOutcome? outcome = null)
        {
            var rules = _desk.Rules;
            var today = _game.Date;
            var lines = new List<string>(TalkFacts.Today(_game));
            if (Rival != null) lines.AddRange(TalkFacts.Rival(Rival, _desk.CarName));

            switch (Subject)
            {
                case CarSubject car:
                    lines.AddRange(TalkFacts.Car(car.Car, car.Price, today, car.OnSaleSince));
                    break;
                case PartSubject part:
                    lines.AddRange(TalkFacts.Part(part.Ad, part.Name, _desk.Parts, today));
                    break;
            }

            if (Person.Kind == PersonKind.Salesman) lines.AddRange(TalkFacts.Lot(Lot, rules.LotCarsTold));
            lines.AddRange(TalkFacts.RacesWithPlayer(Person.Name, _sessions, today, rules.RacesTold));
            if (Rival != null) lines.AddRange(TalkFacts.RivalRaces(Person.Name, _game.RivalRaces ?? [], today, _desk.CarName, rules.RivalRacesTold));
            lines.AddRange(TalkFacts.Memories(TalkPeople.Remembered(Person, today, rules), today));
            lines.AddRange(TalkFacts.Said(Deal));
            if (Subject != null && Deal is { } deal) lines.AddRange(TalkFacts.Deal(Subject.Price, deal.Asked, outcome));
            return TalkFacts.Block(lines);
        }
    }
}
