using StreetCorsa.Models.Catalog;
using StreetCorsa.Models.GameState;
using StreetCorsa.Models.Race;
using StreetCorsa.Mods;
using StreetCorsa.Rules;
using StreetCorsa.Screens.Shared;
using StreetCorsa.Services.Ai;
using StreetCorsa.Services.Talk;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Tests;

/// <summary>
/// Talking to people (step 26): the person behind an ad, the facts the AI is given, haggling decided by the rules,
/// memories that fade, a salesman who does not say the same twice, and the providers sending a talk's turns
/// </summary>
public class TalkTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static AiPrompts BasePrompts() => AiPrompts.Load(ModSet.BaseOnly(RepoPaths.BaseContent));

    private static readonly IReadOnlyList<string> OfferWords = ["take", "give", "offer", "how about", "bucks"];

    // ----- what the player offers -----

    [Theory]
    [InlineData("Would you take 1200 for it?", 1200)]
    [InlineData("I'll give you $1,200.", 1200)]
    [InlineData("1.2k, cash", 1200)]
    [InlineData("how about 900 bucks", 900)]
    [InlineData("$1300", 1300)]
    [InlineData("I'll give you 3,000 for it", 3000)]
    public void An_offer_is_read_from_what_the_player_types(string text, int expected)
    {
        Assert.Equal(expected, Haggle.OfferIn(text, 1500m, OfferWords, Rules));
    }

    [Theory]
    [InlineData("Is it a 1957?")]
    [InlineData("How many miles, 1200?")]
    [InlineData("I'll give you 20")]
    [InlineData("Does it run?")]
    public void A_year_a_mileage_or_a_silly_sum_is_no_offer(string text)
    {
        Assert.Null(Haggle.OfferIn(text, 1500m, OfferWords, Rules));
    }

    // ----- what the seller answers -----

    private static readonly TalkRules Rules = new();

    [Fact]
    public void The_floor_comes_from_what_was_first_asked_by_kind_temperament_and_time_on_sale()
    {
        Assert.Equal(1275m, Haggle.Floor(1500m, PersonKind.PrivateSeller, null, 0, false, Rules));
        Assert.Equal(1380m, Haggle.Floor(1500m, PersonKind.Rival, null, 0, false, Rules));
        Assert.Equal(1320m, Haggle.Floor(1500m, PersonKind.Rival, null, 0, broke: true, Rules));
        Assert.Equal(1200m, Haggle.Floor(1500m, PersonKind.PrivateSeller, null, Rules.LongOnSaleDays, false, Rules));
        Assert.Equal(1500m, Haggle.Floor(1500m, PersonKind.PrivateSeller, new Temperament { Give = 0 }, 0, false, Rules));
    }

    [Fact]
    public void An_offer_is_taken_met_halfway_turned_down_or_stood_firm_on()
    {
        Func<decimal, decimal> round = p => Haggle.RoundUp(p, 25);

        Assert.Equal(new HaggleOutcome(HaggleAnswer.Accept, 1300m, 1300m), Haggle.Decide(1300m, 1500m, 1275m, 0, Rules, round));
        Assert.Equal(new HaggleOutcome(HaggleAnswer.Accept, 1600m, 1500m), Haggle.Decide(1600m, 1500m, 1275m, 0, Rules, round));
        Assert.Equal(new HaggleOutcome(HaggleAnswer.Counter, 1100m, 1300m), Haggle.Decide(1100m, 1500m, 1275m, 0, Rules, round));
        Assert.Equal(HaggleAnswer.Insulted, Haggle.Decide(500m, 1500m, 1275m, 0, Rules, round).Answer);
        Assert.Equal(HaggleAnswer.Firm, Haggle.Decide(1100m, 1500m, 1275m, Rules.MaxCounters, Rules, round).Answer);

        // Never under the floor, however the halves fall
        var counter = Haggle.Decide(1000m, 1300m, 1275m, 0, Rules, round);
        Assert.Equal(HaggleAnswer.Counter, counter.Answer);
        Assert.Equal(1275m, counter.Price);
    }

    private static readonly IReadOnlyList<string> AgreeWords = ["got a deal", "it's a deal", "i'll take it"];
    private static readonly IReadOnlyList<string> AgreeAlone = ["deal", "sold", "ok"];

    [Theory]
    [InlineData("yo got a deal", true)]
    [InlineData("You got a deal!", true)]
    [InlineData("Deal.", true)]
    [InlineData("ok", true)]
    [InlineData("Alright, I'll take it", true)]
    [InlineData("What's the deal with the engine?", false)]
    [InlineData("Is it sold?", false)]
    [InlineData("I won't take it for that", false)]
    public void Taking_the_price_as_it_stands_is_read_from_what_the_player_types(string text, bool agrees)
    {
        Assert.Equal(agrees, Haggle.AgreesIn(text, AgreeWords, AgreeAlone));
    }

    [Theory]
    [InlineData("Tell you what, $1,300 and she's yours.", true)]
    [InlineData("I could do 1,250 bucks.", true)]
    [InlineData("Only 57,166 miles on her, a '57 with a 283.", false)]
    [InlineData("The Fairlane over there is $900, but this one's $1,325.", false)]
    [InlineData("You offered $1,000? No dice.", false)]
    public void An_answer_that_comes_down_by_itself_is_caught(string answer, bool lower)
    {
        Assert.Equal(lower, Haggle.NamesLowerPrice(answer, 1325m, [900m, 1000m, 1450m]));
    }

    // ----- a talk -----

    private static GameState Game()
    {
        var game = new GameState { SaveName = "test" };
        game.Player.Name = "Kid";
        return game;
    }

    private static UsedCarListingViewModel Car(decimal price = 1500m, string? dealer = null) => new()
    {
        Listing = new UsedCarListing
        {
            CarDefinitionId = "bel_air", Price = price, Mileage = 80_000, Condition = 0.7f, DealerLocation = dealer ?? string.Empty,
            EngineSummary = "283 V8", Description = "Grandma's car, church on Sundays", ListedDate = GameState.GetStartingDateTime()
        },
        CarDefinition = new CarDefinition { Id = "bel_air", Brand = "Chevrolet", Name = "Bel Air", Year = 1957 }
    };

    private static (TalkDesk Desk, FakeAiText Ai) Desk(List<ProcessedRaceSession>? races = null, string? answer = "Sure thing.")
    {
        var ai = new FakeAiText((_, _) => Task.FromResult(answer));
        var desk = new TalkDesk(ai, (_, _) => Task.FromResult(races ?? []), id => id == "bel_air" ? "Chevrolet Bel Air" : id,
            prompts: BasePrompts, rules: () => Rules);
        return (desk, ai);
    }

    [Fact]
    public async Task An_offer_changes_the_price_by_the_rules_and_the_ai_is_told_what_to_say()
    {
        var game = Game();
        var car = Car();
        car.Listing.MostOff = Rules.MostDiscount; // the seller's temperament is the limit here, not the ad's draw
        var (desk, ai) = Desk();
        var talk = desk.ForCar(game, car, null, TalkPlace.Phone);

        // The least this seller takes, by their temperament: offered, it is taken
        var floor = Haggle.RoundUp(Haggle.Floor(1500m, PersonKind.PrivateSeller, BasePrompts().TemperamentOf(talk.Person.Temperament), 0, false, Rules), 25);
        await talk.OpenAsync(Ct);
        await talk.SayAsync($"Would you take {floor:0}?", Ct);

        Assert.Equal(PersonKind.PrivateSeller, talk.Person.Kind);
        Assert.Equal(HaggleAnswer.Accept, talk.LastOutcome!.Answer);
        Assert.Equal(floor, car.Listing.Price);
        Assert.Equal(1500m, talk.Deal!.Asked);
        var facts = (string)ai.Chats[^1].Values["facts"]!;
        Assert.Contains(GameText.Say("chat.deal.accept", ("offer", new Helpers.Money(floor)), ("price", new Helpers.Money(floor))), facts);
        Assert.Contains("283 V8", facts);
        Assert.Contains("Grandma's car", facts);
    }

    [Fact]
    public async Task Asking_again_and_again_never_brings_the_price_under_the_first_floor()
    {
        var game = Game();
        var car = Car();
        var (desk, _) = Desk();

        for (var i = 0; i < 6; i++)
        {
            var talk = desk.ForCar(game, car, null, TalkPlace.Phone);
            await talk.SayAsync("I'll give you $1,000", Ct);
            talk.End();
        }

        // Taking the price as it stands shakes on it and changes nothing
        var price = car.Listing.Price;
        var shake = desk.ForCar(game, car, null, TalkPlace.Phone);
        Assert.False(shake.IsAgreed);
        await shake.SayAsync("yo got a deal", Ct);
        Assert.Equal(HaggleAnswer.Shake, shake.LastOutcome!.Answer);
        Assert.True(shake.IsAgreed);
        Assert.Equal(price, car.Listing.Price);

        Assert.True(car.Listing.Price >= Haggle.Floor(1500m, PersonKind.PrivateSeller, BasePrompts().TemperamentOf(TalkPeople.Find(game, TalkPeople.CarKey(car.Listing))!.Temperament), 0, false, Rules));
        Assert.Single(game.People);
    }

    [Fact]
    public async Task However_often_and_whoever_is_asked_a_price_never_goes_under_the_hard_floor()
    {
        var game = Game();
        var car = Car();
        var (desk, _) = Desk();

        // Asked again and again, the seller forgetting the player (made again) in between: the ad keeps its first price
        for (var i = 0; i < 30; i++)
        {
            var talk = desk.ForCar(game, car, null, TalkPlace.Phone);
            await talk.SayAsync($"I'll give you ${Math.Round(car.Listing.Price * 0.8m)}", Ct);
            talk.End();
            game.People.Clear();
        }

        Assert.Equal(1500m, car.Listing.FirstAsked);
        var mostOff = car.Listing.MostOff!.Value;
        Assert.InRange(mostOff, 0, Rules.MostDiscount);
        Assert.True(car.Listing.Price >= Haggle.HardFloor(1500m, mostOff, Rules), $"{car.Listing.Price}");
        Assert.Equal(1125m, Haggle.HardFloor(1500m, 0.9, Rules));
    }

    [Fact]
    public void Each_seller_draws_their_own_limit_under_the_hard_floor_and_some_will_not_budge()
    {
        var random = new Random(7);
        var draws = Enumerable.Range(0, 2000).Select(_ => Haggle.DrawMostOff(Rules, random)).ToList();

        Assert.All(draws, d => Assert.InRange(d, 0, Rules.MostDiscount));
        Assert.InRange(draws.Count(d => d == 0) / 2000.0, Rules.FirmShare - 0.04, Rules.FirmShare + 0.04);
        Assert.True(draws.Distinct().Count() > 1000);
    }

    [Fact]
    public async Task A_seller_who_will_not_budge_stands_firm_however_the_player_asks()
    {
        var game = Game();
        var car = Car();
        car.Listing.FirstAsked = 1500m;
        car.Listing.MostOff = 0;
        var (desk, _) = Desk();

        for (var i = 0; i < 5; i++)
        {
            var talk = desk.ForCar(game, car, null, TalkPlace.Phone);
            await talk.SayAsync("Would you take 1400?", Ct);
            Assert.NotEqual(HaggleAnswer.Accept, talk.LastOutcome!.Answer);
        }

        Assert.Equal(1500m, car.Listing.Price);
    }

    [Fact]
    public async Task A_price_the_ai_comes_down_to_by_itself_is_asked_again_and_then_held_by_the_game()
    {
        var game = Game();
        var car = Car();
        var (desk, ai) = Desk(answer: "Tell you what, pal, $1,100 and she's yours.");
        var talk = desk.ForCar(game, car, "dealer_1", TalkPlace.Lot);

        var said = await talk.SayAsync("yo got a deal", Ct);

        Assert.Equal(2, ai.Chats.Count);
        Assert.Contains(GameText.Say("chat.fact.price_warning", ("price", new Helpers.Money(1500m))), (string)ai.Chats[1].Values["facts"]!);
        Assert.Equal(GameText.Say("chat.deal.held", ("price", new Helpers.Money(1500m))), said);
        Assert.Equal(1500m, car.Listing.Price);
        Assert.True(talk.IsAgreed);
    }

    [Fact]
    public async Task The_talk_goes_as_turns_from_the_players_first_and_two_of_the_players_in_a_row_go_as_one()
    {
        var game = Game();
        var (desk, ai) = Desk(answer: null);
        var talk = desk.ForCar(game, Car(), null, TalkPlace.Phone);

        await talk.OpenAsync(Ct);
        await talk.SayAsync("Hello?", Ct);

        var turns = ai.Chats[^1].Turns;
        var only = Assert.Single(turns);
        Assert.False(only.Mine);
        Assert.Contains("Hello?", only.Text);
        Assert.Contains("[", only.Text);
    }

    [Fact]
    public async Task A_person_who_never_raced_the_player_is_told_so_and_one_who_did_is_told_how_it_went()
    {
        var game = Game();
        var rival = new Opponent("Johnny Reyes", 24, Gender.Male, 95, 80) { Nickname = "Flash" };
        game.Racers.AddRacer(rival);
        var races = new List<ProcessedRaceSession>
        {
            new() { OpponentName = "Johnny Reyes", PlayerWon = false, GameDate = game.Date.AddDays(-6), RaceType = "DragRace", WasPinkSlip = true }
        };
        var (desk, ai) = Desk(races);

        await desk.ForRival(game, rival, TalkPlace.Diner).OpenAsync(Ct);
        var rivalFacts = (string)ai.Chats[^1].Values["facts"]!;
        await desk.ForCar(game, Car(), null, TalkPlace.Phone).OpenAsync(Ct);
        var sellerFacts = (string)ai.Chats[^1].Values["facts"]!;

        Assert.Contains(GameText.Say("chat.fact.race_won", ("when", GameText.Say("chat.when.days", ("days", 6))),
            ("kind", GameText.Say("chat.fact.kind_drag")), ("pink", GameText.Say("chat.fact.pink"))), rivalFacts);
        Assert.Contains(GameText.Say("chat.fact.no_races"), sellerFacts);
        Assert.Equal("chat_rival", ai.Chats[0].Use);
        Assert.Equal("Flash", ai.Chats[0].Values["nickname"]);
    }

    [Fact]
    public async Task Rivals_know_their_races_with_each_other_and_the_record_keeps_only_the_newest()
    {
        var game = Game();
        var rival = new Opponent("Johnny Reyes", 24, Gender.Male, 95, 80);
        game.Racers.AddRacer(rival);
        var rules = new TalkRules { RivalRacesKept = 3 };
        TalkPeople.RecordRivalRaces(game, Enumerable.Range(0, 5).Select(i => new RivalRaceRecord
        {
            Date = game.Date.AddDays(-i), Winner = "Johnny Reyes", Loser = $"Racer {i}", RaceType = "Drag"
        }), rules);
        var (desk, ai) = Desk();

        await desk.ForRival(game, rival, TalkPlace.Cruise).OpenAsync(Ct);

        Assert.Equal(3, game.RivalRaces.Count);
        Assert.Equal("Racer 2", game.RivalRaces[0].Loser);
        Assert.Contains("Racer 4", (string)ai.Chats[^1].Values["facts"]!);
    }

    [Fact]
    public async Task What_a_person_makes_of_a_talk_is_kept_and_forgotten_in_time()
    {
        var game = Game();
        var ai = new FakeAiText((use, _) => Task.FromResult<string?>(use == "chat_memory" ? "The kid offered 900 for the Bel Air." : "Hello."));
        var desk = new TalkDesk(ai, prompts: BasePrompts, rules: () => Rules);
        var talk = desk.ForCar(game, Car(), null, TalkPlace.Phone);

        await talk.SayAsync("Would you take 900?", Ct);
        talk.End();
        var person = talk.Person;
        for (var i = 0; i < 50 && person.Memories.Count == 0; i++) await Task.Delay(20, Ct);

        var memory = Assert.Single(person.Memories);
        Assert.Contains("900", memory.Text);
        Assert.Single(TalkPeople.Remembered(person, game.Date.AddDays(Rules.ForgetDaysSeller), Rules));
        Assert.Empty(TalkPeople.Remembered(person, game.Date.AddDays(Rules.ForgetDaysSeller + 1), Rules));

        // A talk with nothing said by the player is not remembered
        var asked = ai.Asked.Count(a => a.Use == "chat_memory");
        var quiet = desk.ForCar(game, Car(), null, TalkPlace.Phone);
        await quiet.OpenAsync(Ct);
        quiet.End();
        await Task.Delay(50, Ct);
        Assert.Equal(asked, ai.Asked.Count(a => a.Use == "chat_memory"));
    }

    [Fact]
    public async Task A_salesman_goes_on_about_a_car_unasked_and_says_something_new_when_the_player_comes_back_to_it()
    {
        var game = Game();
        var first = Car(1500m, "dealer_1");
        var second = Car(900m, "dealer_1");
        second.Listing.Id = "second";
        var said = 0;
        var ai = new FakeAiText((_, _) => Task.FromResult<string?>($"Pitch {++said}"));
        var desk = new TalkDesk(ai, prompts: BasePrompts, rules: () => Rules);
        var talk = desk.ForLot(game, "dealer_1", [first, second]);

        await talk.OpenAsync(Ct);
        await talk.TurnToAsync(new CarSubject(first), Ct);
        await talk.TurnToAsync(new CarSubject(second), Ct);
        await talk.TurnToAsync(new CarSubject(first), Ct);

        Assert.Equal(PersonKind.Salesman, talk.Person.Kind);
        Assert.Equal("chat_salesman", ai.Chats[0].Use);
        Assert.Contains("[", ai.Chats[0].Turns[0].Text);
        var back = ai.Chats[^1];
        Assert.Contains(GameText.Say("chat.stage.picked_again", ("thing", new CarSubject(first).Name)), back.Turns[^1].Text);
        Assert.Contains("Pitch 2", (string)back.Values["facts"]!);
        Assert.DoesNotContain("Pitch 3", (string)back.Values["facts"]!);

        // The next visit, the salesman still knows what he said about it
        var again = desk.ForLot(game, "dealer_1", [first, second]);
        await again.TurnToAsync(new CarSubject(first), Ct);
        Assert.Contains(GameText.Say("chat.stage.picked_again", ("thing", new CarSubject(first).Name)), ai.Chats[^1].Turns[^1].Text);
        Assert.Same(talk.Person, again.Person);
    }

    [Fact]
    public void The_same_ad_has_the_same_seller_and_a_rival_keeps_their_temperament()
    {
        var game = Game();
        var listing = new UsedCarListing { Id = "ad-1" };
        var first = TalkPeople.Seller(game, TalkPeople.CarKey(listing), prompts: BasePrompts(), rules: Rules);
        game.People.Clear();
        var again = TalkPeople.Seller(game, TalkPeople.CarKey(listing), prompts: BasePrompts(), rules: Rules);

        Assert.Equal(first.Name, again.Name);
        Assert.Equal(first.Temperament, again.Temperament);
        Assert.False(string.IsNullOrEmpty(first.Temperament));

        var hothead = new Opponent("Butch", 30, Gender.Male, 95, 90);
        var person = TalkPeople.Rival(game, hothead, BasePrompts());
        Assert.True(BasePrompts().TemperamentOf(person.Temperament)!.Fits("rival", 90));
    }

    [Fact]
    public async Task A_dealers_salesman_is_who_its_data_says_with_his_portrait()
    {
        var game = Game();
        var given = new DealerSalesman { Name = "Cal Whitaker", Age = 45, Sex = "man", Temperament = "old_timer", PortraitFile = @"C:\cal.png" };
        var desk = new TalkDesk(new FakeAiText((_, _) => Task.FromResult<string?>("Howdy!")), prompts: BasePrompts, rules: () => Rules,
            salesmen: id => id == "dealer_1" ? given : null);

        // Met before the data gave him: he becomes the data's man
        var before = TalkPeople.Salesman(game, "dealer_1", prompts: BasePrompts(), rules: Rules);
        var talk = desk.ForLot(game, "dealer_1", [Car(1500m, "dealer_1")]);

        Assert.Same(before, talk.Person);
        Assert.Equal("Cal Whitaker", talk.Person.Name);
        Assert.Equal(45, talk.Person.Age);
        Assert.Equal("old_timer", talk.Person.Temperament);
        Assert.Equal(@"C:\cal.png", desk.PortraitOf(talk.Person));
        Assert.Null(desk.PortraitOf(desk.ForLot(game, "dealer_2", []).Person));
        await Task.CompletedTask;
    }

    [Fact]
    public void Every_base_game_dealer_has_a_salesman_with_a_portrait_a_temperament_and_a_name_of_his_own()
    {
        var dealers = new StreetCorsa.Services.Dealers.DealerCatalog(ModSet.BaseOnly(RepoPaths.BaseContent)).All;
        var prompts = BasePrompts();

        Assert.NotEmpty(dealers);
        foreach (var dealer in dealers)
        {
            var salesman = dealer.Salesman;
            Assert.True(salesman != null, dealer.Id);
            Assert.False(string.IsNullOrWhiteSpace(salesman!.Name), dealer.Id);
            Assert.True(File.Exists(salesman.PortraitFile), $"{dealer.Id}: {salesman.Portrait}");
            Assert.True(prompts.TemperamentOf(salesman.Temperament)?.Fits("salesman", 50) == true, $"{dealer.Id}: {salesman.Temperament}");
        }

        Assert.Equal(dealers.Count, dealers.Select(d => d.Salesman!.Name).Distinct().Count());
    }

    // ----- the providers -----

    private static readonly AiTextRequest Talk = new("You are Walt.", string.Empty, 80, 0.9,
        [new AiTurn(false, "[A customer walks onto your lot.]"), new AiTurn(true, "Howdy, pal!"), new AiTurn(false, "Does it run?")]);

    [Fact]
    public async Task Ollama_is_sent_the_turns_and_the_games_tuning()
    {
        var http = new FakeHttp((_, _) => Task.FromResult(FakeHttp.Json("""{ "message": { "content": "Like a top." } }""")));
        var provider = AiProviders.Create(http.Client(), AiProviders.Ollama, null, "streetcorsa", null, new OllamaRules());

        Assert.Equal("Like a top.", await provider.CompleteAsync(Talk, Ct));

        var json = Assert.Single(http.Sent).Json!;
        var messages = json["messages"]!.AsArray();
        Assert.Equal(new[] { "system", "user", "assistant", "user" }, messages.Select(m => m!["role"]!.GetValue<string>()));
        Assert.Equal("Does it run?", messages[3]!["content"]!.GetValue<string>());
        Assert.Equal(8192, json["options"]!["num_ctx"]!.GetValue<int>());
        Assert.Equal("10m", json["keep_alive"]!.GetValue<string>());
    }

    [Fact]
    public async Task Anthropic_is_sent_the_turns_with_the_system_apart()
    {
        var http = new FakeHttp((_, _) => Task.FromResult(FakeHttp.Json("""{ "content": [ { "type": "text", "text": "Sure does." } ], "stop_reason": "end_turn" }""")));
        var provider = AiProviders.Create(http.Client(), AiProviders.Anthropic, null, null, "sk-test");

        await provider.CompleteAsync(Talk, Ct);

        var json = Assert.Single(http.Sent).Json!;
        Assert.Equal("You are Walt.", json["system"]!.GetValue<string>());
        Assert.Equal(new[] { "user", "assistant", "user" }, json["messages"]!.AsArray().Select(m => m!["role"]!.GetValue<string>()));
    }

    [Fact]
    public async Task The_games_model_is_made_in_ollama_from_a_base_with_its_examples()
    {
        var http = new FakeHttp((_, _) => Task.FromResult(FakeHttp.Json("""{ "status": "success" }""")));
        var ollama = new OllamaTextProvider(http.Client(), "http://localhost:11434", null, null);
        var model = BasePrompts().Ollama!;
        var examples = model.Examples.SelectMany(e => new[] { new AiTurn(false, e.Player), new AiTurn(true, e.Answer) }).ToList();

        await ollama.CreateModelAsync(model.Name, model.From, model.SystemText, model.Parameters, examples, Ct);

        var sent = Assert.Single(http.Sent);
        Assert.Equal("http://localhost:11434/api/create", sent.Uri.ToString());
        Assert.Equal("streetcorsa", sent.Json!["model"]!.GetValue<string>());
        Assert.Equal(model.From, sent.Json!["from"]!.GetValue<string>());
        Assert.Equal(examples.Count, sent.Json!["messages"]!.AsArray().Count);
        Assert.False(string.IsNullOrWhiteSpace(sent.Json!["system"]!.GetValue<string>()));
    }

    // ----- the review's cases: what is no offer, no deal, and what the AI may say back -----

    private static IReadOnlyList<string> Lines(string key) => GameText.Installed.Lines(key);

    [Theory]
    [InlineData("Is this the 1957 for sale?")]
    [InlineData("What about the 1958?")]
    [InlineData("Only 5k miles?")]
    [InlineData("How about the 283 cubes?")]
    public void A_year_or_a_measure_is_no_offer_with_the_base_games_words(string text)
    {
        Assert.Null(Haggle.OfferIn(text, 1800m, Lines("chat.offer_words"), Rules, Lines("chat.unit_words")));
    }

    [Theory]
    [InlineData("I'll give you 1950", 1950)]
    [InlineData("How about 1500?", 1500)]
    [InlineData("would you take 1200?", 1200)]
    public void An_offer_that_looks_like_a_year_is_one_with_an_offer_word_before_it(string text, int expected)
    {
        Assert.Equal(expected, Haggle.OfferIn(text, 2000m, Lines("chat.offer_words"), Rules, Lines("chat.unit_words")));
    }

    [Theory]
    [InlineData("Sure", false)]
    [InlineData("yes", false)]
    [InlineData("Deal?", false)]
    [InlineData("I never agreed to that", false)]
    [InlineData("It's not a deal", false)]
    [InlineData("Deal!", true)]
    [InlineData("You got a deal, mister", true)]
    public void Only_a_plain_yes_to_the_price_is_a_deal_with_the_base_games_words(string text, bool agrees)
    {
        Assert.Equal(agrees, Haggle.AgreesIn(text, Lines("chat.agree_words"), Lines("chat.agree_alone"), Lines("chat.agree_not")));
    }

    [Theory]
    [InlineData("I'd let her go for 1.2 grand", 1200)]
    [InlineData("call it 1.2k", 1200)]
    [InlineData("1,250 bucks", 1250)]
    public void A_sum_with_k_or_grand_on_it_is_a_sum(string text, int expected)
    {
        Assert.Equal([(decimal)expected], Haggle.SumsIn(text, Lines("chat.unit_words")));
    }

    [Fact]
    public void A_mileage_is_no_sum()
    {
        Assert.Empty(Haggle.SumsIn("only 5k miles on her", Lines("chat.unit_words")));
    }

    [Fact]
    public void A_price_a_step_over_the_floor_still_comes_down()
    {
        Func<decimal, decimal> round = p => Haggle.RoundUp(p, 25);

        // Halfway rounds to $1,350, the floor to $1,400, over the price: the counter is the floor itself
        Assert.Equal(new HaggleOutcome(HaggleAnswer.Counter, 1300m, 1380m), Haggle.Decide(1300m, 1395m, 1380m, 0, Rules, round));
    }

    [Fact]
    public async Task The_players_own_offer_said_back_is_no_price_come_down_by_itself()
    {
        var game = Game();
        var car = Car();
        car.Listing.FirstAsked = 1500m;
        car.Listing.MostOff = 0;
        var (desk, ai) = Desk(answer: "$1,200? Can't do it, friend.");
        var talk = desk.ForCar(game, car, null, TalkPlace.Phone);

        var said = await talk.SayAsync("would you take 1200?", Ct);

        Assert.Single(ai.Chats);
        Assert.Equal("$1,200? Can't do it, friend.", said);
    }

    [Fact]
    public void A_salesman_met_over_one_car_is_not_told_his_lot_is_empty()
    {
        var (desk, _) = Desk();
        var talk = desk.ForCar(Game(), Car(dealer: "dealer_1"), "dealer_1", TalkPlace.Curb);

        Assert.Equal(PersonKind.Salesman, talk.Person.Kind);
        Assert.DoesNotContain(GameText.Say("chat.fact.lot_empty"), talk.Facts());
    }

    [Fact]
    public void A_deal_not_talked_over_is_forgotten_with_the_memories()
    {
        var game = Game();
        var person = TalkPeople.Salesman(game, "dealer_1", rules: Rules);
        TalkPeople.Deal(person, "car:old", 1500m, game.Date);
        TalkPeople.Deal(person, "car:new", 1500m, game.Date.AddDays(Rules.ForgetDaysSalesman));

        TalkPeople.Forget(person, game.Date.AddDays(Rules.ForgetDaysSalesman + 1), Rules);

        Assert.Equal(["car:new"], person.Deals.Select(d => d.Thing));
    }

    [Fact]
    public void A_seller_named_by_the_ad_is_of_the_names_sex()
    {
        var game = Game();
        for (var i = 0; i < 20; i++)
            Assert.Equal(Gender.Male, TalkPeople.Seller(game, $"part:{i}", "Frank", rules: Rules).Gender);
        Assert.Equal(Gender.Female, TalkPeople.Seller(game, "part:doris", "Doris Kemper", rules: Rules).Gender);
    }

    [Fact]
    public void A_mod_salesman_with_a_name_and_no_sex_is_a_man_and_with_neither_keeps_who_he_was()
    {
        var game = Game();
        var named = TalkPeople.Salesman(game, "dealer_named", new DealerSalesman { Name = "Cal Whitaker" }, rules: Rules);
        Assert.Equal(Gender.Male, named.Gender);

        var drawn = TalkPeople.Salesman(game, "dealer_drawn", rules: Rules);
        var again = TalkPeople.Salesman(game, "dealer_drawn", new DealerSalesman { Temperament = "old_timer" }, rules: Rules);
        Assert.Same(drawn, again);
        Assert.Equal(TalkPeople.Salesman(Game(), "dealer_drawn", rules: Rules).Gender, again.Gender);
    }

    // ----- the base game's content -----

    [Fact]
    public void The_base_game_has_the_talk_prompts_temperaments_for_every_kind_and_an_ollama_model()
    {
        var prompts = BasePrompts();

        Assert.Empty(prompts.Problems);
        foreach (var use in new[] { "chat_seller", "chat_salesman", "chat_rival", "chat_memory" })
            Assert.NotNull(prompts.Prompt(use));
        foreach (var kind in new[] { "seller", "salesman", "rival" })
            Assert.Contains(prompts.Temperaments, t => t.Fits(kind, 50));
        for (var aggression = 0; aggression <= 100; aggression += 5)
            Assert.Contains(prompts.Temperaments, t => t.Fits("rival", aggression));
        Assert.NotNull(prompts.Ollama);
        Assert.Contains("{facts}", prompts.Prompt("chat_salesman")!.Prompt);
    }

    [Fact]
    public void The_talk_rules_are_in_the_base_games_rules()
    {
        var book = RuleBook.Read(ModMerge.ContentFiles(ModSet.BaseOnly(RepoPaths.BaseContent), ContentTypes.Rules, []), []);
        Assert.Equal(new TalkRules().ForgetDaysRival, book.Ai.Talk.ForgetDaysRival);
        Assert.Equal(new TalkRules().DiscountSalesman, book.Ai.Talk.DiscountSalesman);
        Assert.Equal(new OllamaRules().KeepAlive, book.Ai.Ollama.KeepAlive);
    }
}
