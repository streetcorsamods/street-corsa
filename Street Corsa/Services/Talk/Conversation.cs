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

        /// <summary>Who the person talks to</summary>
        public string PlayerName => _game.Player.Name;

        /// <summary>What is talked about now; null at the diner, or a salesman before a car is picked</summary>
        public TalkSubject? Subject { get; private set; }

        /// <summary>A salesman's cars</summary>
        public IReadOnlyList<UsedCarListingViewModel> Lot { get; }

        /// <summary>The prompt asked: chat_seller, chat_salesman, chat_rival, chat_buyer (a rival buying is still a rival)</summary>
        public string Use => Person.Kind switch
        {
            PersonKind.Salesman => "chat_salesman",
            PersonKind.Rival => "chat_rival",
            PersonKind.Buyer => "chat_buyer",
            _ => "chat_seller"
        };

        /// <summary>The player is the one selling: the person answered the player's ad</summary>
        public bool PlayerSells => Subject is BuyerSubject;

        /// <summary>The player said something: the talk is worth remembering</summary>
        public bool PlayerSpoke { get; private set; }

        /// <summary>The answer to the last offer, if any</summary>
        public HaggleOutcome? LastOutcome { get; private set; }

        /// <summary>A price was talked over (the price may have changed): the screens show the new one</summary>
        public event Action<HaggleOutcome>? Dealt;

        // The thing the player and the person have shaken on, at its price now; null when nothing is agreed
        private string? _agreedThing;

        /// <summary>
        /// The player and the person agreed on the price of what is talked about now (an offer taken, or the player taking
        /// the price as it stands): it can be bought (or, the player selling, sold) at it
        /// </summary>
        public bool IsAgreed => Subject != null && _agreedThing == Subject.Thing && Subject is not BuyerSubject { IsGone: true };

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
                TalkPlace.Phone when PlayerSells => "chat.stage.buyer_phone",
                TalkPlace.Phone => "chat.stage.phone",
                TalkPlace.Lot => Subject == null ? "chat.stage.lot" : "chat.stage.lot_car",
                TalkPlace.Curb => "chat.stage.curb",
                TalkPlace.Diner => "chat.stage.diner",
                _ => "chat.stage.cruise"
            };
            return await AskAsync(GameText.Say(key, ("thing", Subject?.Name ?? string.Empty), ("player", _game.Player.Name)), null, cancellationToken).ConfigureAwait(true);
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

        /// <summary>
        /// The talk is about <paramref name="subject"/> now, with nothing said of it: the player speaks of it before the
        /// person has gone on about it (an offer typed as soon as the car is picked is for that car)
        /// </summary>
        public void Look(TalkSubject subject)
        {
            if (Subject?.Thing != subject.Thing) Focus(subject);
        }

        /// <summary>What the player says: an offer in it is answered by the rules first, then the person speaks</summary>
        public Task<string?> SayAsync(string text, CancellationToken cancellationToken = default)
        {
            text = text.Trim();
            if (text.Length == 0) return Task.FromResult<string?>(null);

            PlayerSpoke = true;
            _playerSaid.Add(text);
            var outcome = Subject switch
            {
                null => null,
                BuyerSubject { IsGone: true } => null,
                BuyerSubject buyer => Ask(text, buyer) ?? Agree(text, buyer),
                _ => Offer(text, Subject) ?? Agree(text, Subject)
            };
            return AskAsync(text, outcome, cancellationToken);
        }

        /// <summary>The talk is over: what the person makes of it is written in the background and kept with them</summary>
        public void End()
        {
            if (!PlayerSpoke) return;

            // What the game keeps of it, whatever the AI makes of it: an insult, and the last offer that came to nothing
            if (_insulted) TalkPeople.Happened(Person, new PersonEvent { Date = _game.Date, Kind = "insulted", What = _insultedOver }, _desk.Rules);
            if (_lastOffer is { } last && _agreedThing != last.Thing)
                TalkPeople.Happened(Person, new PersonEvent { Date = _game.Date, Kind = last.Asked ? "asked" : "offered", What = last.What, Amount = last.Amount },
                    _desk.Rules);
            _desk.Remember(this);
        }

        // An offer this talk was an insult (a buyer asked too much and walked), over what; the last offer made, and on what
        private bool _insulted;
        private string _insultedOver = string.Empty;
        private (string Thing, string What, decimal Amount, bool Asked)? _lastOffer;

        private void Focus(TalkSubject subject)
        {
            Subject = subject;
            // The price before any talk, kept on the ad the first time: every later floor is worked out from it, whoever
            // is asked (a seller who has forgotten the player is made again, and starts from the ad's first price too)
            subject.FirstAsked ??= subject.Price;
            subject.MostOff ??= subject is BuyerSubject ? Haggle.DrawMostRaise(_desk.Rules, _desk.Random) : Haggle.DrawMostOff(_desk.Rules, _desk.Random);
            TalkPeople.Deal(Person, subject.Thing, subject.FirstAsked.Value, _game.Date);
        }

        /// <summary>The player takes the price as it stands ("you got a deal"): shaken on; null when the line says no such thing</summary>
        private HaggleOutcome? Agree(string text, TalkSubject subject)
        {
            var words = GameText.Installed;
            if (!Haggle.AgreesIn(text, words.Lines("chat.agree_words"), words.Lines("chat.agree_alone"), words.Lines("chat.agree_not"))) return null;

            var outcome = new HaggleOutcome(HaggleAnswer.Shake, subject.Price, subject.Price);
            _agreedThing = subject.Thing;
            _logger.Information("{Player} took {Person}'s price for {Thing}: {Price}", _game.Player.Name, Person.Name, subject.Thing, subject.Price);
            LastOutcome = outcome;
            Dealt?.Invoke(outcome);
            return outcome;
        }

        /// <summary>
        /// The buyer's answer, by the rules, to a price the player asks in <paramref name="text"/> for their car: the offer
        /// raised by it, or gone with the buyer; null when the line asks none
        /// </summary>
        private HaggleOutcome? Ask(string text, BuyerSubject subject)
        {
            var rules = _desk.Rules;
            var words = GameText.Installed;
            if (Haggle.OfferIn(text, subject.Price, [.. words.Lines("chat.offer_words"), .. words.Lines("chat.ask_words")], rules, UnitWords) is not { } ask)
                return null;

            _offers.Add(ask);
            _lastOffer = (subject.Thing, subject.Name, ask, true);

            var deal = TalkPeople.Deal(Person, subject.Thing, subject.FirstAsked ?? subject.Price, _game.Date);
            var temperament = _desk.Prompts.TemperamentOf(Person.Temperament);
            var ceiling = Haggle.Ceiling(subject.FirstAsked ?? deal.Asked, subject.MostOff ?? 0, temperament, subject.Asking,
                Rival?.Money, rules);
            var outcome = Haggle.DecideAsk(ask, subject.Price, ceiling, deal.Counters, rules, subject.Round);

            deal.Date = _game.Date;
            if (outcome.Answer != HaggleAnswer.Accept) deal.Counters++;
            if (outcome.Answer == HaggleAnswer.Walk)
            {
                subject.Walk();
                Insulted(subject);
            }
            else if (outcome.Price != subject.Price)
            {
                subject.Price = outcome.Price;
                deal.Agreed = outcome.Price;
            }

            _agreedThing = outcome.Answer == HaggleAnswer.Accept ? subject.Thing : null;

            _logger.Information("{Player} asked {Ask} of {Person} for {Thing}: {Answer}, offer now {Price}",
                _game.Player.Name, ask, Person.Name, subject.Thing, outcome.Answer, outcome.Price);
            LastOutcome = outcome;
            Dealt?.Invoke(outcome);
            return outcome;
        }

        /// <summary>The rules' answer to an offer in <paramref name="text"/>, the price changed by it; null when it holds none</summary>
        private HaggleOutcome? Offer(string text, TalkSubject subject)
        {
            var rules = _desk.Rules;
            if (Haggle.OfferIn(text, subject.Price, GameText.Installed.Lines("chat.offer_words"), rules, UnitWords) is not { } offer) return null;

            _offers.Add(offer);
            _lastOffer = (subject.Thing, subject.Name, offer, false);

            var deal = TalkPeople.Deal(Person, subject.Thing, subject.FirstAsked ?? subject.Price, _game.Date);
            var temperament = _desk.Prompts.TemperamentOf(Person.Temperament);
            var days = Math.Max(0, (int)(_game.Date.Date - subject.OnSaleSince.Date).TotalDays);
            var floor = Math.Max(Haggle.Floor(deal.Asked, Person.Kind, temperament, days, Rival?.IsBroke == true, rules),
                Haggle.HardFloor(subject.FirstAsked ?? deal.Asked, subject.MostOff ?? rules.MostDiscount, rules));
            var outcome = Haggle.Decide(offer, subject.Price, floor, deal.Counters, rules, subject.Round);
            if (outcome.Answer == HaggleAnswer.Insulted) Insulted(subject);

            deal.Date = _game.Date;
            if (outcome.Answer != HaggleAnswer.Accept) deal.Counters++;
            if (outcome.Price != subject.Price)
            {
                subject.Price = outcome.Price;
                deal.Agreed = outcome.Price;
            }

            // Taken, it is agreed; anything else and the player has not agreed to what stands now
            _agreedThing = outcome.Answer == HaggleAnswer.Accept ? subject.Thing : null;

            _logger.Information("{Player} offered {Offer} to {Person} for {Thing}: {Answer}, price now {Price}",
                _game.Player.Name, offer, Person.Name, subject.Thing, outcome.Answer, outcome.Price);
            LastOutcome = outcome;
            Dealt?.Invoke(outcome);
            return outcome;
        }

        private void Insulted(TalkSubject subject)
        {
            _insulted = true;
            _insultedOver = subject.Name;
        }

        private async Task<string?> AskAsync(string playerTurn, HaggleOutcome? outcome, CancellationToken cancellationToken)
        {
            // Two of the player's in a row (the AI gave no answer to the first) go as one: the APIs want them taking turns
            if (_turns.Count > 0 && !_turns[^1].Mine)
                _turns[^1] = new AiTurn(false, _turns[^1].Text + "\n" + playerTurn);
            else
                _turns.Add(new AiTurn(false, playerTurn));

            // The sections once a turn: the fact sheet and the sums the person may name both come of them
            var sections = Sections(outcome);
            var values = Values(sections);
            var said = await _desk.Text.ChatAsync(Use, values, Window(), cancellationToken).ConfigureAwait(true);

            // The AI must not come down by itself (a buyer: go up): a price past the one that stands, that nothing it was
            // told gives, and it is asked again with a word on it; the same again and the game says the price itself.
            // A sum far under the price (so much down, what gas costs) is small talk, as an offer that low is no offer
            if (said != null && Subject is { } subject)
            {
                var known = KnownSums(sections);
                var buyer = subject is BuyerSubject;
                var least = subject.Price * (decimal)_desk.Rules.OfferLowest;
                bool Strays(string line) => buyer
                    ? Haggle.NamesHigherPrice(line, subject.Price, known, UnitWords)
                    : Haggle.NamesLowerPrice(line, subject.Price, known, UnitWords, least);
                if (Strays(said))
                {
                    _logger.Information("{Person} named a price past {Price} by themselves: asked again", Person.Name, subject.Price);
                    values["facts"] = Facts(sections, GameText.Say(buyer ? "chat.fact.offer_warning" : "chat.fact.price_warning", ("price", new Helpers.Money(subject.Price))));
                    said = await _desk.Text.ChatAsync(Use, values, Window(), cancellationToken).ConfigureAwait(true);
                    if (said != null && Strays(said))
                        said = GameText.Say(buyer ? "chat.deal.buyer.held" : "chat.deal.held", ("price", new Helpers.Money(subject.Price)));
                }
            }
            // A salesman going over his lot names only its prices: one made up is asked again, once (a sum far under the
            // cheapest car is small talk)
            else if (said != null && Person.Kind == PersonKind.Salesman
                     && Haggle.NamesUnknownSum(said, KnownSums(sections), UnitWords, Lot.Count == 0 ? 0 : Lot.Min(c => c.Listing.Price) * (decimal)_desk.Rules.OfferLowest))
            {
                _logger.Information("{Person} named a price that is no car's on the lot: asked again", Person.Name);
                values["facts"] = Facts(sections, GameText.Say("chat.fact.lot_price_warning"));
                said = await _desk.Text.ChatAsync(Use, values, Window(), cancellationToken).ConfigureAwait(true) ?? said;
            }

            if (said == null) return null;

            _turns.Add(new AiTurn(true, said));
            if (Person.Kind == PersonKind.Salesman && Deal is { } deal) TalkPeople.Said(deal, said, _desk.Rules);
            SumUpOlder();
            return said;
        }

        // What the player typed this talk, and the offers read in it: the sums in them are theirs to be named back
        private readonly List<string> _playerSaid = [];
        private readonly List<decimal> _offers = [];

        private static IReadOnlyList<string> UnitWords => GameText.Installed.Lines("chat.unit_words");

        /// <summary>
        /// The sums the person may name: those of the facts they were given (the lot, what was first asked), the player's,
        /// and the player's offers ("would you take 1200" is one with no dollar sign on it). Not the other cars for sale in
        /// town, the town's talk, the paper or what happened before (an offer that came to nothing): the person may name
        /// them, but never as the price of their own; nor the AI's summary of the talk, which may get a figure wrong (the
        /// player's own are known anyway), nor what things cost these days (the lore)
        /// </summary>
        private List<decimal> KnownSums(IReadOnlyList<FactSection> sections) =>
            Haggle.SumsIn(TalkFacts.Sheet(sections.Where(s => s.Key is not ("market" or "earlier" or "lore" or "town" or "news" or "events")), int.MaxValue),
                    UnitWords)
                .Concat(_playerSaid.SelectMany(line => Haggle.SumsIn(line, UnitWords)))
                .Concat(_offers)
                .Concat(Deal is { } deal ? [deal.Asked] : Array.Empty<decimal>())
                .ToList();

        /// <summary>
        /// The newest turns the rules keep, from a player's turn on, and the older ones not summed up yet (the summary is
        /// asked in the background, and may be late or never come: never more than twice the turns kept, though)
        /// </summary>
        private List<AiTurn> Window()
        {
            var keep = Math.Max(1, _desk.Told.TurnsKept);
            var start = Math.Max(Math.Min(_turns.Count - keep, _summed), _turns.Count - keep * 2);
            var kept = _turns.Skip(Math.Max(0, start)).ToList();
            while (kept.Count > 1 && kept[0].Mine) kept.RemoveAt(0);
            return kept;
        }

        // What was said before the turns sent, summed up by the AI; how many turns from the first it covers
        private string? _summary;
        private int _summed;
        private bool _summing;

        /// <summary>What was said earlier in the talk, summed up, once its turns are no longer sent; null before</summary>
        public string? Summary => _summary;

        /// <summary>
        /// The turns fallen out of the ones the rules keep, not summed up yet, are near what is still sent of them (twice the
        /// turns kept): they are summed up with the summary before them, in the background, and sent with the facts from then
        /// on. Late and in one go, not every few turns: the summary takes the AI's one place and, a prompt of its own, makes a
        /// local model read the talk's whole prompt again for the next line. It ends after one of the person's turns, so the
        /// turns sent after it begin with the player's.
        /// </summary>
        private void SumUpOlder()
        {
            var rules = _desk.Told;
            var keep = Math.Max(1, rules.TurnsKept);
            var outside = _turns.Count - keep;
            if (outside > _summed && outside < _turns.Count && _turns[outside].Mine) outside++;
            var least = Math.Max(Math.Max(1, rules.SummaryTurns), keep - Math.Max(1, rules.SummaryTurns));
            if (_summing || outside - _summed < least) return;

            _summing = true;
            _ = SumUpAsync(_turns.Skip(_summed).Take(outside - _summed).ToList(), outside);
        }

        private async Task SumUpAsync(List<AiTurn> turns, int upTo)
        {
            try
            {
                var values = new TextValues
                {
                    ["name"] = Person.Name,
                    ["player"] = _game.Player.Name,
                    ["earlier"] = _summary ?? string.Empty,
                    ["talk"] = TalkDesk.Transcript(Person, turns)
                };
                var written = await _desk.Text.WriteAsync("chat_summary", values).ConfigureAwait(true);
                if (TalkDesk.NoteOf(written) is { } summary)
                {
                    _summary = summary;
                    _summed = upTo;
                }
            }
            catch (Exception ex)
            {
                // The turns are still sent, up to twice the rules' many: nothing is lost yet
                _logger.Warning("Could not sum up the start of the talk with {Person}: {Error}", Person.Name, ex.Message);
            }
            finally
            {
                _summing = false;
            }
        }

        // The factory's engine parts of each car talked about, looked up once a talk
        private readonly Dictionary<string, IReadOnlySet<string>?> _factories = new(StringComparer.OrdinalIgnoreCase);

        private IReadOnlySet<string>? Factory(string carId)
        {
            if (!_factories.TryGetValue(carId, out var parts)) _factories[carId] = parts = _desk.FactoryParts(carId);
            return parts;
        }

        private async Task LoadRacesAsync()
        {
            if (_sessionsLoaded) return;
            _sessionsLoaded = true;
            try
            {
                _sessions = await _desk.RacesOf(_game.SaveName, Person.Name).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                // Without the record the person only says they do not remember
                _logger.Warning("Could not read the races for a talk: {Error}", ex.Message);
            }
        }

        /// <summary>What the prompt is filled with: who the person is, where, and the fact sheet</summary>
        public TextValues Values(HaggleOutcome? outcome = null) => Values(Sections(outcome));

        private TextValues Values(IReadOnlyList<FactSection> sections)
        {
            var temperament = _desk.Prompts.TemperamentOf(Person.Temperament);
            return new TextValues
            {
                ["name"] = Person.Name,
                ["nickname"] = Rival?.Nickname ?? string.Empty,
                ["age"] = Person.Age,
                ["sex"] = GameText.Say(Person.Gender == Gender.Female ? "chat.sex.woman" : "chat.sex.man"),
                ["temperament"] = temperament?.Words ?? string.Empty,
                ["place"] = GameText.Say(PlayerSells ? "chat.place.buyer_phone" : $"chat.place.{Place.ToString().ToLowerInvariant()}"),
                ["player"] = _game.Player.Name,
                ["examples"] = TalkFacts.Examples(_desk.Prompts.Prompt(Use)?.Examples ?? [], Person.Name),
                ["facts"] = Facts(sections)
            };
        }

        /// <summary>
        /// The fact sheet for the next answer, cut to the rules' <see cref="Rules.TalkRules.FactTokens"/> and to half the
        /// context window, when the provider has one the game sets
        /// </summary>
        public string Facts(HaggleOutcome? outcome = null) => Facts(Sections(outcome));

        /// <summary>The sheet of <paramref name="sections"/>, with a <paramref name="warning"/> last under a heading of its own when given</summary>
        private string Facts(IReadOnlyList<FactSection> sections, string? warning = null)
        {
            var budget = _desk.Told.FactTokens;
            if (_desk.Text.ContextTokens is { } context) budget = Math.Min(budget, context / 2);
            return TalkFacts.Sheet(warning == null ? sections : [.. sections, new FactSection("warning", [warning], FactSection.Always)], budget);
        }

        /// <summary>
        /// The fact sheet's sections, what stays the same through the talk first and the deal, which changes with every
        /// offer, last: a local model reads again only what changed from the end of the last request on
        /// </summary>
        public IReadOnlyList<FactSection> Sections(HaggleOutcome? outcome = null)
        {
            var rules = _desk.Told;
            var today = _game.Date;
            var sections = new List<FactSection> { new("now", [.. TalkFacts.Today(_game, _desk.CarName)], FactSection.Always) };
            // What everybody knows these days, the same all day: what they may make small talk of
            sections.Add(new("lore", [.. TalkFacts.Lore(_desk.Prompts.Lore, TalkPeople.KindWord(Person.Kind), today.Year, rules.LoreTold,
                TalkPeople.Seeded($"{Person.Key}:{today:yyyy-MM-dd}"))], FactSection.Lore));
            if (Rival != null) sections.Add(new("you", [.. TalkFacts.Rival(Rival, _desk.CarName)], FactSection.Always));
            sections.Add(new("feeling", [.. TalkFacts.Feeling(TalkPeople.Feeling(Person, today, rules))], FactSection.Always));
            // A racer sizes up what the player drives
            if (Rival != null && TalkFacts.CarDriven(_game) is { } playersCar)
                sections.Add(new("player_car", [.. TalkFacts.Build(playersCar.Parts, _desk.Parts, Factory(playersCar.DefinitionId), rules.BuildPartsPerKind)],
                    FactSection.PlayersCar, HeadLines: 1));

            var thing = Subject switch
            {
                CarSubject car => TalkFacts.Car(car.Car, car.Price, today, car.OnSaleSince),
                PartSubject part => TalkFacts.Part(part.Ad, part.Name, _desk.Parts, today),
                BuyerSubject buyer => TalkFacts.PlayersCar(buyer.Car, buyer.Asking, today, buyer.OnSaleSince),
                _ => []
            };
            sections.Add(new("thing", [.. thing], FactSection.Always));
            if (Subject switch { CarSubject car => car.Car, BuyerSubject buyer => buyer.Car, _ => null } is { } subjectCar)
                sections.Add(new("build", [.. TalkFacts.Build(subjectCar.Listing.Parts, _desk.Parts, Factory(subjectCar.Listing.CarDefinitionId), rules.BuildPartsPerKind)],
                    FactSection.Build, HeadLines: 1));
            // A rival's own car, but not again when it is the car for sale talked about
            if (Rival?.Cars.FirstOrDefault() is { } rivalCar
                && !(Subject is CarSubject { Car.RivalAd.CarInstanceId: var forSaleId } && forSaleId == rivalCar.InstanceId))
                sections.Add(new("your_car", [.. TalkFacts.Build(rivalCar.Parts, _desk.Parts, Factory(rivalCar.DefinitionId), rules.BuildPartsPerKind)],
                    FactSection.OwnCar, HeadLines: 1));
            // What else is for sale in town: a seller's or buyer's sense of the price
            if (Subject switch { CarSubject car => car.Car, BuyerSubject buyer => buyer.Car, _ => null } is { } forSale)
                sections.Add(new("market", [.. TalkFacts.Market(forSale.Listing, _game.UsedCarMarket ?? [], _desk.CarName, rules.MarketCarsTold)],
                    FactSection.Market, HeadLines: 1));

            // A salesman met over one car (Car Viewing) is not given his lot, and so is not told it is empty either; the
            // lot is what he talks about until a car is picked, and the rest of it after
            if (Person.Kind == PersonKind.Salesman && (Lot.Count > 0 || Subject == null))
                sections.Add(new("lot", [.. TalkFacts.Lot(Lot, rules.LotCarsTold)], Subject == null ? FactSection.Lot : FactSection.OtherCars, HeadLines: 1));
            sections.Add(new("races", [.. TalkFacts.RacesWithPlayer(Person.Name, _sessions, today, rules.RacesTold)], FactSection.Races, HeadLines: 1));
            if (Rival != null)
                sections.Add(new("rival_races", [.. TalkFacts.RivalRaces(Person.Name, _game.RivalRaces ?? [], today, _desk.CarName, rules.RivalRacesTold)], FactSection.RivalRaces));
            sections.Add(new("memories", [.. TalkFacts.Memories(TalkPeople.Remembered(Person, today, rules), today)], FactSection.Memories, HeadLines: 1));
            sections.Add(new("events", [.. TalkFacts.Events(TalkPeople.Events(Person, today, rules), today, _desk.CarName)], FactSection.Events, HeadLines: 1));
            // The town's talk and the paper: everybody hears it
            sections.Add(new("town", [.. TalkFacts.Town(_game, rules.StreetTalkTold)], FactSection.Town, HeadLines: 1));
            sections.Add(new("news", [.. TalkFacts.News(_game, rules.NewsTold)], FactSection.News, HeadLines: 1));
            // What changes as the talk goes on, last: the start of a long talk, what was said of the thing, the deal
            sections.Add(new("earlier", [.. TalkFacts.Earlier(_summary)], FactSection.Earlier));
            sections.Add(new("said", [.. TalkFacts.Said(Deal)], FactSection.Said, HeadLines: 1));

            IEnumerable<string> dealLines = Subject switch
            {
                BuyerSubject sold when Deal is { } offered => TalkFacts.Offer(sold.Price, offered.Asked, sold.IsGone, outcome),
                not null when Deal is { } deal => TalkFacts.Deal(Subject.Price, deal.Asked, outcome),
                _ => []
            };
            sections.Add(new("deal", [.. dealLines], FactSection.Always));
            return sections;
        }
    }
}
