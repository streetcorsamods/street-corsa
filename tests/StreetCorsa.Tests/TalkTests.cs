using StreetCorsa.Models.Catalog;
using StreetCorsa.Models.GameState;
using StreetCorsa.Models.Race;
using StreetCorsa.Mods;
using StreetCorsa.Parts;
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

    private static (TalkDesk Desk, FakeAiText Ai) Desk(List<ProcessedRaceSession>? races = null, string? answer = "Sure thing.", TalkRules? rules = null)
    {
        var ai = new FakeAiText((_, _) => Task.FromResult(answer));
        var desk = new TalkDesk(ai, (_, _) => Task.FromResult(races ?? []), id => id == "bel_air" ? "Chevrolet Bel Air" : id,
            prompts: BasePrompts, rules: () => rules ?? Rules);
        return (desk, ai);
    }

    [Fact]
    public async Task A_car_for_sale_can_be_bought_from_the_talk_without_a_shake_at_its_price_as_it_stands()
    {
        var game = Game();
        var car = Car();
        var (desk, _) = Desk();
        var talk = new TalkViewModel(desk.ForCar(game, car, null, TalkPlace.Phone), portrait: "") { Buy = () => { } };

        await talk.OpenAsync();

        Assert.False(talk.Talk.IsAgreed);
        Assert.True(talk.CanBuy);
        Assert.Contains("1,500", talk.BuyLabel);

        talk.End();
        Assert.False(talk.CanBuy);
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
        Assert.Equal(16384, json["options"]!["num_ctx"]!.GetValue<int>());
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

    // ----- the fact sheet's sections and budget -----

    private static string Heading(string key) => GameText.Say("chat.section", ("heading", GameText.Say($"chat.section.{key}")));

    [Fact]
    public void The_fact_sheet_goes_under_headings_what_stays_the_same_first_and_the_deal_last()
    {
        var (desk, _) = Desk();
        var talk = desk.ForCar(Game(), Car(), null, TalkPlace.Phone);

        var facts = talk.Facts();

        var order = new[] { "now", "thing", "races", "memories", "deal" }.Select(key => facts.IndexOf(Heading(key), StringComparison.Ordinal)).ToList();
        Assert.All(order, at => Assert.True(at >= 0));
        Assert.Equal(order.OrderBy(at => at), order);
        Assert.EndsWith(GameText.Say("chat.fact.price", ("price", new Helpers.Money(1500m)), ("asked", new Helpers.Money(1500m))), facts);
    }

    [Fact]
    public void A_sheet_too_long_loses_the_least_needed_lines_first_and_never_the_thing_or_the_deal()
    {
        var thing = new FactSection("thing", ["For sale: a 1957 Chevrolet Bel Air, the price $1,500."], FactSection.Always);
        var others = new FactSection("rival_races", ["Yesterday you beat Hank in a drag race.", "3 days ago Lou beat you in a road race.", "6 days ago you beat Sal in a drag race."], FactSection.RivalRaces);
        var races = new FactSection("races", ["You have raced the player 3 times. The latest:", "- yesterday, a drag race: you won.", "- 5 days ago, a road race: the player won."], FactSection.Races, HeadLines: 1);
        var deal = new FactSection("deal", ["The price is $1,500."], FactSection.Always);

        // Room for all but the other racers' races: they go, line by line and then the heading, before any race with the player
        var withoutOthers = TalkFacts.Sheet([thing, races, deal], int.MaxValue);
        Assert.Equal(withoutOthers, TalkFacts.Sheet([thing, others, races, deal], AiTokens.Estimate(withoutOthers)));

        // Less room: the races with the player go from the oldest, their head line last of all
        var headOnly = TalkFacts.Sheet([thing, races with { Lines = races.Lines.Take(1).ToList() }, deal], int.MaxValue);
        Assert.Equal(headOnly, TalkFacts.Sheet([thing, others, races, deal], AiTokens.Estimate(headOnly)));

        // No room at all: what is always kept is still there, over the budget
        var kept = TalkFacts.Sheet([thing, others, races, deal], 1);
        Assert.Equal(TalkFacts.Sheet([thing, deal], int.MaxValue), kept);
        Assert.StartsWith(Heading("thing"), kept);
    }

    [Fact]
    public void The_fact_sheet_keeps_to_the_rules_budget_and_to_half_the_context_window()
    {
        var game = Game();
        var car = Car();
        var (roomy, _) = Desk();
        Assert.Contains(GameText.Say("chat.fact.no_memories"), roomy.ForCar(game, car, null, TalkPlace.Phone).Facts());

        // Half a small window holds the car and the deal, not what the seller remembers
        var (small, ai) = Desk();
        ai.ContextTokens = 2 * AiTokens.Estimate(TalkFacts.Sheet(small.ForCar(game, car, null, TalkPlace.Phone).Sections().Where(s => s.Priority == FactSection.Always), int.MaxValue));
        var facts = small.ForCar(game, car, null, TalkPlace.Phone).Facts();
        Assert.DoesNotContain(GameText.Say("chat.fact.no_memories"), facts);
        Assert.Contains(Heading("deal"), facts);

        // And the rules' own budget, with no window known
        var (tight, _) = Desk(rules: new TalkRules { FactTokens = 1 });
        Assert.DoesNotContain(Heading("memories"), tight.ForCar(game, car, null, TalkPlace.Phone).Facts());
    }

    // ----- what is on a car -----

    [Fact]
    public void A_cars_build_is_told_part_by_part_the_engine_first_the_same_part_once_and_what_the_factory_put_on()
    {
        var catalog = TestCars.Catalog.Value;
        var car = TestCars.WithRealEngine(catalog);
        var engineParts = car.Parts.SelectMany(p => p.SelfAndDescendants()).Select(p => p.DefinitionId).ToList();
        var swapped = engineParts.First(id => PartKinds.GroupOf(catalog.Get(id)!) == PartGroups.Camshafts);
        var factory = engineParts.Where(id => id != swapped).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var tyre = catalog.Parts.Values.First(p => PartKinds.GroupOf(p) == PartGroups.Tyres);
        for (var i = 0; i < 4; i++) car.Parts.Add(new PartInstance(tyre.Id) { Wear = 0.9 - i * 0.1 });

        var lines = TalkFacts.Build(car.Parts, catalog, factory).ToList();

        Assert.Equal(GameText.Say("chat.fact.build"), lines[0]);
        Assert.StartsWith("- " + PartKinds.GroupName(PartGroups.Blocks) + ":", lines[1]);
        // The same part on every corner is one line, with how many and the worst of them, after the engine's
        var tyreLine = Assert.Single(lines, l => l.Contains(Services.Market.AdBlurbs.NameOf(tyre) + GameText.Say("chat.fact.build_count", ("count", 4)) + ","));
        Assert.Contains(ConditionDisplay.Of(0.6).Sentence, tyreLine);
        Assert.True(lines.FindIndex(l => l.StartsWith("- " + PartKinds.GroupName(PartGroups.Camshafts), StringComparison.Ordinal)) < lines.IndexOf(tyreLine));
        // The cam not the factory's says so; the rest say they came from it
        var camLine = Assert.Single(lines, l => l.Contains(Services.Market.AdBlurbs.NameOf(catalog.Get(swapped)!)));
        Assert.EndsWith(GameText.Say("chat.fact.build_aftermarket"), camLine);
        Assert.EndsWith(GameText.Say("chat.fact.build_stock"), lines[1]);
        // The engine's many small accessories are one line, all from the factory
        var accessories = Assert.Single(lines, l => l.StartsWith("- " + PartKinds.GroupName(PartGroups.Accessories) + ":", StringComparison.Ordinal));
        Assert.Contains(GameText.Say("chat.fact.build_stock_all"), accessories);

        // The factory not known: no word on where a part came from; no catalog: nothing at all
        Assert.DoesNotContain(TalkFacts.Build(car.Parts, catalog, null), l => l.EndsWith(GameText.Say("chat.fact.build_stock"), StringComparison.Ordinal));
        Assert.Empty(TalkFacts.Build(car.Parts, null, factory));
    }

    [Fact]
    public void A_car_for_sale_and_a_rivals_own_car_are_told_part_by_part()
    {
        var catalog = TestCars.Catalog.Value;
        var game = Game();
        var car = Car();
        car.Listing.Parts = TestCars.WithRealEngine(catalog).Parts;
        var rival = new Opponent("Johnny Reyes", 24, Gender.Male, 95, 80) { Nickname = "Flash" };
        rival.Cars.Add(TestCars.WithRealEngine(catalog));
        game.Racers.AddRacer(rival);
        var ai = new FakeAiText((_, _) => Task.FromResult<string?>("Sure."));
        var desk = new TalkDesk(ai, carName: id => id, parts: () => catalog, prompts: BasePrompts, rules: () => Rules);

        Assert.Contains(Heading("build"), desk.ForCar(game, car, null, TalkPlace.Phone).Facts());
        var rivalFacts = desk.ForRival(game, rival, TalkPlace.Diner).Facts();
        Assert.Contains(Heading("your_car"), rivalFacts);
        Assert.Contains("- " + PartKinds.GroupName(PartGroups.Blocks) + ":", rivalFacts);
    }

    // ----- the town, the market, the player as seen -----

    private static UsedCarListing ForSale(string id, decimal price, string dealer = "") => new()
    {
        CarDefinitionId = id, Price = price, Mileage = 70_000, Condition = 0.6f, DealerLocation = dealer, ListedDate = GameState.GetStartingDateTime()
    };

    [Fact]
    public void Everybody_hears_the_town_and_reads_the_paper_and_a_seller_knows_what_else_is_for_sale()
    {
        var game = Game();
        game.StreetTalk = [new StreetTalkItem(game.Date.AddDays(-3), "Hank wrecked his Ford on Route 9."), new StreetTalkItem(game.Date.AddDays(-1), "The King has a new cam.")];
        game.News = [new NewsArticle { Date = game.Date.AddDays(-2), Headline = "Drag racing on Main", Body = "Police were called." }];
        var car = Car();
        game.UsedCarMarket = [ForSale("fairlane", 1450m), ForSale("bel_air", 1300m, "dealer_2"), ForSale("impala", 2600m), car.Listing];
        var (desk, _) = Desk();

        var facts = desk.ForCar(game, car, null, TalkPlace.Phone).Facts();

        // The newest first
        Assert.True(facts.IndexOf("The King has a new cam.", StringComparison.Ordinal) < facts.IndexOf("Hank wrecked", StringComparison.Ordinal));
        Assert.Contains("Drag racing on Main", facts);
        // The same model first, then the nearest in price; never the car itself
        var market = facts[facts.IndexOf(Heading("market"), StringComparison.Ordinal)..];
        Assert.True(market.IndexOf("$1,300", StringComparison.Ordinal) < market.IndexOf("$1,450", StringComparison.Ordinal));
        Assert.True(market.IndexOf("$1,450", StringComparison.Ordinal) < market.IndexOf("$2,600", StringComparison.Ordinal));
        Assert.DoesNotContain("$1,500", market[..market.IndexOf("\n\n", StringComparison.Ordinal)]);
        Assert.Contains(GameText.Say("chat.fact.market_dealer"), market);
    }

    [Fact]
    public async Task A_seller_who_names_another_cars_price_as_their_own_is_caught()
    {
        var game = Game();
        var car = Car();
        car.Listing.FirstAsked = 1500m;
        car.Listing.MostOff = 0;
        game.UsedCarMarket = [ForSale("bel_air", 1300m)];
        var (desk, ai) = Desk(answer: "Tell you what, $1,300 and she's yours.");

        var said = await desk.ForCar(game, car, null, TalkPlace.Phone).SayAsync("Does it run?", Ct);

        Assert.Equal(2, ai.Chats.Count);
        Assert.Equal(GameText.Say("chat.deal.held", ("price", new Helpers.Money(1500m))), said);
    }

    [Fact]
    public void People_know_the_players_name_on_the_street_and_what_they_drive_and_a_racer_knows_its_parts()
    {
        var catalog = TestCars.Catalog.Value;
        var game = Game();
        game.Player.Stats.Reputation = 80;
        var mine = TestCars.WithRealEngine(catalog);
        game.Player.Cars.Add(mine);
        game.Player.SelectedCarInstanceId = mine.InstanceId;
        var rival = new Opponent("Johnny Reyes", 24, Gender.Male, 95, 80) { Nickname = "Flash" };
        game.Racers.AddRacer(rival);
        var ai = new FakeAiText((_, _) => Task.FromResult<string?>("Sure."));
        var desk = new TalkDesk(ai, carName: id => id == "car_a" ? "1968 Dodge Dart" : id, parts: () => catalog, prompts: BasePrompts, rules: () => Rules);

        var sellerFacts = desk.ForCar(game, Car(), null, TalkPlace.Phone).Facts();
        Assert.Contains(game.Player.Stats.GetReputationTier(), sellerFacts);
        Assert.Contains(GameText.Say("chat.fact.player_car", ("player", "Kid"), ("car", "1968 Dodge Dart")), sellerFacts);
        Assert.DoesNotContain(Heading("player_car"), sellerFacts);

        var rivalFacts = desk.ForRival(game, rival, TalkPlace.Cruise).Facts();
        Assert.Contains(Heading("player_car"), rivalFacts);
    }

    [Fact]
    public async Task A_salesman_who_makes_up_a_price_on_his_lot_is_asked_again()
    {
        var game = Game();
        var answers = new Queue<string>(["The Bel Air's yours for $950, pal!", "The Bel Air's a steal at $1,500, pal!"]);
        var ai = new FakeAiText((_, _) => Task.FromResult<string?>(answers.Dequeue()));
        var desk = new TalkDesk(ai, carName: id => id, prompts: BasePrompts, rules: () => Rules);
        var talk = desk.ForLot(game, "dealer_1", [Car(1500m, "dealer_1")]);

        var said = await talk.OpenAsync(Ct);

        Assert.Equal("The Bel Air's a steal at $1,500, pal!", said);
        Assert.Equal(2, ai.Chats.Count);
        // Last, under a heading of its own: not read as one of the section's before it
        Assert.EndsWith(Heading("warning") + "\n" + GameText.Say("chat.fact.lot_price_warning"), (string)ai.Chats[1].Values["facts"]!);
        // Each car on the lot with its engine and miles
        Assert.Contains("283 V8", (string)ai.Chats[0].Values["facts"]!);
    }

    [Fact]
    public async Task Small_talk_of_money_down_or_what_gas_costs_is_no_price()
    {
        var game = Game();
        var (desk, ai) = Desk(answer: "Just $300 down and she's yours. Gas is $0.30 a gallon, she sips it.");
        var said = await desk.ForCar(game, Car(), null, TalkPlace.Phone).SayAsync("What would it take?", Ct);
        Assert.Equal("Just $300 down and she's yours. Gas is $0.30 a gallon, she sips it.", said);
        Assert.Single(ai.Chats);

        var answers = new FakeAiText((_, _) => Task.FromResult<string?>("Only $40 a month on any car here, pal. Gas is $0.30!"));
        var lot = new TalkDesk(answers, carName: id => id, prompts: BasePrompts, rules: () => Rules).ForLot(game, "dealer_1", [Car(1500m, "dealer_1")]);
        await lot.OpenAsync(Ct);
        Assert.Single(answers.Chats);
    }

    [Fact]
    public async Task A_sum_from_the_towns_talk_or_the_paper_is_no_price_the_seller_may_come_down_to()
    {
        var game = Game();
        game.StreetTalk = [new StreetTalkItem(game.Date.AddDays(-1), "Word is Hank let his Ford go for $900.")];
        game.News = [new NewsArticle { Date = game.Date.AddDays(-1), Headline = "Coupe sold", Body = "A coupe went for $1,100 at auction." }];
        var car = Car();
        car.Listing.FirstAsked = 1500m;
        car.Listing.MostOff = 0;

        foreach (var answer in new[] { "Tell you what, $900 and it's yours.", "Make it $1,100 and we're done." })
        {
            var (desk, ai) = Desk(answer: answer);
            var said = await desk.ForCar(game, car, null, TalkPlace.Phone).SayAsync("Does it run?", Ct);
            Assert.Equal(2, ai.Chats.Count);
            Assert.Equal(GameText.Say("chat.deal.held", ("price", new Helpers.Money(1500m))), said);
        }
    }

    [Fact]
    public void A_rivals_car_for_sale_is_told_once_not_again_as_their_own()
    {
        var catalog = TestCars.Catalog.Value;
        var game = Game();
        var own = TestCars.WithRealEngine(catalog);
        var rival = new Opponent("Johnny Reyes", 24, Gender.Male, 95, 80) { Nickname = "Flash" };
        rival.Cars.Add(own);
        game.Racers.AddRacer(rival);
        var car = Car();
        car.Listing.Parts = own.Parts;
        car.RivalAd = new RivalCarAd { RivalName = rival.Name, CarInstanceId = own.InstanceId, AskingPrice = 1500m, PostedDate = game.Date };
        var desk = new TalkDesk(new FakeAiText((_, _) => Task.FromResult<string?>("Sure.")), carName: id => id, parts: () => catalog,
            prompts: BasePrompts, rules: () => Rules);

        var keys = desk.ForCar(game, car, null, TalkPlace.Phone).Sections().Select(s => s.Key).ToList();
        Assert.Contains("build", keys);
        Assert.DoesNotContain("your_car", keys);
    }

    [Fact]
    public void A_piece_in_the_paper_is_told_as_the_paper_shows_it()
    {
        var game = Game();
        game.News = [new NewsArticle { Date = game.Date, Headline = "Drag racing on Main", Body = "Police were called.", AiBody = "The cops came.", AiLanguage = AiWriter.Language }];
        var (desk, _) = Desk();

        var facts = desk.ForCar(game, Car(), null, TalkPlace.Phone).Facts();

        // No AI headline: the paper shows the piece as the game wrote it, and so the person read it
        Assert.Contains("Police were called.", facts);
        Assert.DoesNotContain("The cops came.", facts);
    }

    [Fact]
    public void A_talk_too_long_for_the_window_loses_its_oldest_turns_and_starts_with_the_players()
    {
        var line = new string('x', 300);
        var turns = Enumerable.Range(0, 9).Select(i => new AiTurn(i % 2 == 1, line + i)).ToList();
        var each = AiTokens.Estimate(turns[0]);

        // Room for the system prompt, the answer and about three turns and a half
        var context = AiTokens.Estimate("You are Walt.") + AiTokens.PerMessage + 80 + AiTokens.Margin + each * 7 / 2;
        var fitted = AiTextService.Fit(turns, "You are Walt.", 80, context);

        Assert.Equal(turns[^1], fitted[^1]);
        Assert.False(fitted[0].Mine);
        Assert.Equal(turns.Skip(turns.Count - fitted.Count), fitted);
        Assert.InRange(fitted.Count, 1, 3);

        // No window known: every turn goes, as the provider keeps its own
        Assert.Same(turns, AiTextService.Fit(turns, "You are Walt.", 80, null));
        // Even a window too small for one: the player's last line still goes
        Assert.Equal(new[] { turns[^1] }, AiTextService.Fit(turns, "You are Walt.", 80, 10));
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

    // ----- memory and feelings -----

    [Fact]
    public async Task A_long_talk_sums_up_the_turns_that_fall_out_and_sends_the_summary_with_the_facts()
    {
        var game = Game();
        var rules = new TalkRules { TurnsKept = 4, SummaryTurns = 2 };
        var ai = new FakeAiText((use, _) => Task.FromResult<string?>(use == "chat_summary" ? "The kid asked about the paint.\nThen the tires." : "Sure thing."));
        var desk = new TalkDesk(ai, prompts: BasePrompts, rules: () => rules);
        var talk = desk.ForCar(game, Car(), null, TalkPlace.Phone);

        await talk.SayAsync("What color is it?", Ct);
        await talk.SayAsync("How are the tires?", Ct);
        Assert.DoesNotContain(ai.Asked, a => a.Use == "chat_summary");
        await talk.SayAsync("Does the radio work?", Ct);

        // Two turns fell out of the four kept: summed up, the lines kept as one
        var asked = Assert.Single(ai.Asked, a => a.Use == "chat_summary");
        Assert.Contains("What color is it?", (string)asked.Values["talk"]!);
        Assert.DoesNotContain("Does the radio work?", (string)asked.Values["talk"]!);
        Assert.Equal("The kid asked about the paint. Then the tires.", talk.Summary);

        await talk.SayAsync("Any rust?", Ct);
        var last = ai.Chats[^1];
        Assert.Contains("The kid asked about the paint.", (string)last.Values["facts"]!);
        Assert.DoesNotContain(last.Turns, t => t.Text.Contains("What color"));
        Assert.Equal("Any rust?", last.Turns[^1].Text);
    }

    [Fact]
    public async Task A_price_the_summary_got_wrong_is_no_price_the_seller_may_come_down_to()
    {
        var game = Game();
        var rules = new TalkRules { TurnsKept = 2, SummaryTurns = 1 };
        var ai = new FakeAiText((use, values) => Task.FromResult<string?>(use == "chat_summary" ? "Frank came down to $600."
            : ((string)values["facts"]!).Contains("$600") ? "Like I said, $600." : "Sure thing."));
        var desk = new TalkDesk(ai, prompts: BasePrompts, rules: () => rules);
        var talk = desk.ForCar(game, Car(), null, TalkPlace.Phone);

        await talk.SayAsync("What color is it?", Ct);
        await talk.SayAsync("How are the tires?", Ct);
        Assert.NotNull(talk.Summary);

        var said = await talk.SayAsync("Any rust?", Ct);
        Assert.Equal(GameText.Say("chat.deal.held", ("price", new Helpers.Money(1500m))), said);
    }

    [Fact]
    public async Task The_summary_is_asked_late_and_in_one_go_not_every_few_turns()
    {
        var game = Game();
        var rules = new TalkRules { TurnsKept = 10, SummaryTurns = 2 };
        var ai = new FakeAiText((use, _) => Task.FromResult<string?>(use == "chat_summary" ? "They talked." : "Sure thing."));
        var desk = new TalkDesk(ai, prompts: BasePrompts, rules: () => rules);
        var talk = desk.ForCar(game, Car(), null, TalkPlace.Phone);

        for (var i = 0; i < 8; i++) await talk.SayAsync($"Question {i}?", Ct);
        Assert.DoesNotContain(ai.Asked, a => a.Use == "chat_summary");

        await talk.SayAsync("Question 8?", Ct);
        var asked = Assert.Single(ai.Asked, a => a.Use == "chat_summary");
        Assert.Contains("Question 3?", (string)asked.Values["talk"]!);
        Assert.DoesNotContain("Question 4?", (string)asked.Values["talk"]!);
    }

    [Fact]
    public async Task With_an_odd_number_of_turns_kept_the_summary_still_takes_the_persons_answer()
    {
        var game = Game();
        var rules = new TalkRules { TurnsKept = 3, SummaryTurns = 1 };
        var answered = 0;
        var ai = new FakeAiText((use, _) => Task.FromResult<string?>(use == "chat_summary" ? "They talked." : $"Answer {++answered}."));
        var desk = new TalkDesk(ai, prompts: BasePrompts, rules: () => rules);
        var talk = desk.ForCar(game, Car(), null, TalkPlace.Phone);

        await talk.SayAsync("One?", Ct);
        await talk.SayAsync("Two?", Ct);
        var asked = Assert.Single(ai.Asked, a => a.Use == "chat_summary");
        Assert.Contains("Answer 1.", (string)asked.Values["talk"]!);

        await talk.SayAsync("Three?", Ct);
        var sent = ai.Chats[^1].Turns;
        Assert.False(sent[0].Mine);
        Assert.Equal("Two?", sent[0].Text);
    }

    [Fact]
    public async Task Turns_not_summed_up_yet_are_still_sent_up_to_twice_the_turns_kept()
    {
        var game = Game();
        var rules = new TalkRules { TurnsKept = 2, SummaryTurns = 1 };
        var ai = new FakeAiText((use, _) => Task.FromResult<string?>(use == "chat_summary" ? null : "Sure thing."));
        var desk = new TalkDesk(ai, prompts: BasePrompts, rules: () => rules);
        var talk = desk.ForCar(game, Car(), null, TalkPlace.Phone);

        foreach (var line in new[] { "One?", "Two?", "Three?", "Four?" }) await talk.SayAsync(line, Ct);

        Assert.Null(talk.Summary);
        Assert.Equal(["Three?", "Sure thing.", "Four?"], ai.Chats[^1].Turns.Select(t => t.Text));
    }

    [Fact]
    public async Task An_insulting_offer_and_the_last_offer_are_kept_and_the_seller_is_cooler_next_time()
    {
        var game = Game();
        var (desk, ai) = Desk();
        var car = Car();
        var talk = desk.ForCar(game, car, null, TalkPlace.Phone);

        await talk.SayAsync("I'll give you 300", Ct);
        Assert.Equal(HaggleAnswer.Insulted, talk.LastOutcome!.Answer);
        talk.End();

        Assert.Equal(["insulted", "offered"], talk.Person.Events.Select(e => e.Kind));
        Assert.Equal(300m, talk.Person.Events[1].Amount);
        Assert.Equal(-1, TalkPeople.Feeling(talk.Person, game.Date, Rules));

        var again = desk.ForCar(game, car, null, TalkPlace.Phone);
        Assert.Same(talk.Person, again.Person);
        await again.OpenAsync(Ct);
        var facts = (string)ai.Chats[^1].Values["facts"]!;
        Assert.Contains(GameText.Say("chat.fact.feeling.cool"), facts);
        Assert.Contains(Heading("events"), facts);
        Assert.Contains("$300", facts);
        // How they feel is only how they talk: the price stands as it was
        Assert.Equal(1500m, again.Subject!.Price);
    }

    [Fact]
    public void How_a_person_feels_is_what_they_still_remember_added_up_from_minus_two_to_two()
    {
        var game = Game();
        var person = TalkPeople.Seller(game, "car:1", rules: Rules);
        Assert.Equal(0, TalkPeople.Feeling(person, game.Date, Rules));
        Assert.Empty(TalkFacts.Feeling(0));

        for (var i = 0; i < 4; i++) TalkPeople.Happened(person, new PersonEvent { Date = game.Date, Kind = "bought", Amount = 100m }, Rules);
        Assert.Equal(2, TalkPeople.Feeling(person, game.Date, Rules));
        TalkPeople.Happened(person, new PersonEvent { Date = game.Date, Kind = "offered", Amount = 50m }, Rules);
        Assert.Equal(2, TalkPeople.Feeling(person, game.Date, Rules));

        // Forgotten in time, as the memories are
        Assert.Equal(0, TalkPeople.Feeling(person, game.Date.AddDays(Rules.ForgetDaysSeller + 1), Rules));
        TalkPeople.Forget(person, game.Date.AddDays(Rules.ForgetDaysSeller + 1), Rules);
        Assert.Empty(person.Events);

        // Never more than the rules keep, the oldest dropped
        var few = new TalkRules { EventsKept = 2 };
        foreach (var kind in new[] { "bought", "beaten", "sold" }) TalkPeople.Happened(person, new PersonEvent { Date = game.Date, Kind = kind }, few);
        Assert.Equal(["beaten", "sold"], person.Events.Select(e => e.Kind));
    }

    [Fact]
    public void A_rival_remembers_a_pink_slip_lost_and_a_stranger_never_talked_to_is_nobody()
    {
        var game = Game();
        var rival = new Opponent("Johnny Reyes", 24, Gender.Male, 95, 80);
        game.Racers.AddRacer(rival);

        TalkPeople.Happened(game, rival, new PersonEvent { Date = game.Date, Kind = "beaten" });
        TalkPeople.Happened(game, rival, new PersonEvent { Date = game.Date, Kind = "pink_lost" });
        var person = TalkPeople.Rival(game, rival);
        Assert.Equal(-2, TalkPeople.Feeling(person, game.Date, Rules));
        Assert.Equal([GameText.Say("chat.fact.feeling.sore")], TalkFacts.Feeling(-2));

        TalkPeople.Happened(game, new PersonEvent { Date = game.Date, Kind = "sold", CarId = "bel_air", Amount = 900m }, "buyer:nobody", "car:nobody");
        Assert.Single(game.People);

        // A seller talked to is found by any of the keys given
        var seller = TalkPeople.Seller(game, "car:x", rules: Rules);
        TalkPeople.Happened(game, new PersonEvent { Date = game.Date, Kind = "bought", CarId = "bel_air", Amount = 900m }, "dealer:", "car:x");
        var lines = TalkFacts.Events(seller.Events, game.Date, id => id == "bel_air" ? "Chevrolet Bel Air" : id).ToList();
        Assert.Equal(GameText.Say("chat.fact.events"), lines[0]);
        Assert.Contains("Chevrolet Bel Air", lines[1]);
        Assert.Contains("$900", lines[1]);
    }

    [Fact]
    public void A_note_of_several_lines_is_kept_whole_without_its_heading()
    {
        Assert.Equal("Kid offered 900. Seemed keen.", TalkDesk.NoteOf("Note to self:\nKid offered 900.\n\nSeemed keen.\n"));
        Assert.Null(TalkDesk.NoteOf("Note to self:\n"));
        Assert.Null(TalkDesk.NoteOf(null));
    }

    // ----- what everybody knows, and how people talk -----

    private static readonly LoreTopic[] Topics =
    [
        new() { Id = "town", Lines = ["Town 1.", "Town 2.", "Town 3."] },
        new() { Id = "racing", For = ["rival"], Lines = ["Racing 1.", "Racing 2."] },
        new() { Id = "sixties", To = 1965, Lines = ["Sixties 1."] },
        new() { Id = "seventy", From = 1970, To = 1970, Lines = ["Seventy 1.", "Seventy 2."] }
    ];

    [Fact]
    public void Lore_is_what_the_kind_knows_in_the_years_it_is_true_a_line_of_each_topic_in_turn_and_no_more_than_told()
    {
        var seller = TalkFacts.Lore(Topics, "seller", 1970, 3, new Random(1)).ToList();
        Assert.Equal(3, seller.Count);
        Assert.DoesNotContain(seller, l => l.StartsWith("Racing") || l.StartsWith("Sixties"));
        // A line of each topic before a second of any
        Assert.Contains(seller.Take(2), l => l.StartsWith("Seventy"));
        Assert.Contains(seller.Take(2), l => l.StartsWith("Town"));

        var rival = TalkFacts.Lore(Topics, "rival", 1963, 10, new Random(1)).ToList();
        Assert.Equal(6, rival.Count);
        Assert.Contains("Sixties 1.", rival);
        Assert.DoesNotContain(rival, l => l.StartsWith("Seventy"));

        // The same draw, the same lines: a talk's sheet stays the same all day
        Assert.Equal(TalkFacts.Lore(Topics, "rival", 1963, 4, new Random(7)), TalkFacts.Lore(Topics, "rival", 1963, 4, new Random(7)));
    }

    [Fact]
    public async Task People_are_told_what_everybody_knows_and_the_prompts_examples_in_their_kinds_voice()
    {
        var game = Game();
        var (desk, ai) = Desk();
        var talk = desk.ForLot(game, "dealer_1", [Car(1500m, "dealer_1")]);

        await talk.OpenAsync(Ct);

        var values = ai.Chats[^1].Values;
        Assert.Contains(Heading("lore"), (string)values["facts"]!);
        var examples = (string)values["examples"]!;
        Assert.StartsWith(GameText.Say("chat.examples"), examples);
        Assert.Contains($"{talk.Person.Name}: ", examples);
        Assert.Equal(BasePrompts().Prompt("chat_salesman")!.Examples[0].Answer,
            BasePrompts().Prompt("chat_salesman")!.Examples.Select(e => e.Answer).First(examples.Contains));

        // Told the same lines all day, other lines another day
        var sections = talk.Sections();
        Assert.Equal(sections.Single(s => s.Key == "lore").Lines, talk.Sections().Single(s => s.Key == "lore").Lines);
    }

    [Fact]
    public void The_base_game_knows_its_times_for_every_kind_and_has_examples_for_every_talk()
    {
        var prompts = BasePrompts();
        Assert.Empty(prompts.Problems);
        foreach (var kind in new[] { "seller", "salesman", "rival", "buyer" })
            Assert.True(TalkFacts.Lore(prompts.Lore, kind, GameState.GetStartingDateTime().Year, 8, new Random(1)).Count() >= 6, kind);
        foreach (var use in new[] { "chat_seller", "chat_salesman", "chat_rival", "chat_buyer" })
        {
            Assert.NotEmpty(prompts.Prompt(use)!.Examples);
            Assert.Contains("{examples}", prompts.Prompt(use)!.Prompt);
            // No figure in an example: a model copies them
            Assert.DoesNotContain(prompts.Prompt(use)!.Examples, e => e.Answer.Any(char.IsDigit));
        }

        // Never a price in the lore: what is for sale is the game's to say
        Assert.DoesNotContain(prompts.Lore.SelectMany(t => t.Lines), l => l.Contains('$'));
    }

    // ----- how much is sent -----

    [Fact]
    public void The_talk_detail_caps_what_is_told_and_full_is_the_rules_as_they_are()
    {
        var rules = new TalkRules();
        Assert.Same(rules, TalkDetail.Apply(rules, TalkDetail.Full));
        Assert.Same(rules, TalkDetail.Apply(rules, "nonsense"));

        var light = TalkDetail.Apply(rules, TalkDetail.Light);
        Assert.Equal((1500, 12, 4), (light.FactTokens, light.TurnsKept, light.MemoriesGiven));
        Assert.Equal(rules.DiscountPrivate, light.DiscountPrivate);
        Assert.Equal(40, rules.TurnsKept);

        // A mod's rules already under the cap stay as they are
        var small = TalkDetail.Apply(new TalkRules { FactTokens = 800 }, TalkDetail.Normal);
        Assert.Equal(800, small.FactTokens);

        var prompts = BasePrompts();
        var tokens = TalkDetail.All.Select(d => TalkDetail.MostTokensALine(TalkDetail.Apply(rules, d), prompts)).ToList();
        Assert.True(tokens[0] < tokens[1] && tokens[1] < tokens[2], string.Join(", ", tokens));
        // Ollama's window halves the sheet
        Assert.True(TalkDetail.MostTokensALine(rules, prompts, 4096) < tokens[2]);
        // Twice the turns kept go until the older ones are summed up
        Assert.Equal(2 * 10 * rules.TurnTokens,
            TalkDetail.MostTokensALine(new TalkRules { TurnsKept = 20 }, prompts) - TalkDetail.MostTokensALine(new TalkRules { TurnsKept = 10 }, prompts));

        // A mod sets the caps in the rules
        var modded = TalkDetail.Apply(new TalkRules { DetailLight = new TalkDetailCaps { FactTokens = 700, TurnsKept = 5, MemoriesGiven = 2 } }, TalkDetail.Light);
        Assert.Equal((700, 5, 2), (modded.FactTokens, modded.TurnsKept, modded.MemoriesGiven));
    }

    [Fact]
    public async Task A_light_talk_tells_fewer_memories_and_turns_but_forgets_nothing_it_keeps()
    {
        var game = Game();
        var ai = new FakeAiText((use, _) => Task.FromResult<string?>(use == "chat_memory" ? "A new note." : "Sure thing."));
        var desk = new TalkDesk(ai, prompts: BasePrompts, rules: () => Rules, detail: () => TalkDetail.Light);
        var talk = desk.ForCar(game, Car(), null, TalkPlace.Phone);
        for (var i = 0; i < 9; i++) TalkPeople.Remember(talk.Person, game.Date, $"Note {i}.", "en", Rules);

        for (var i = 0; i < 10; i++) await talk.SayAsync($"Question {i}?", Ct);

        var last = ai.Chats[^1];
        Assert.True(last.Turns.Count <= 12 * 2, $"{last.Turns.Count} turns");
        var facts = (string)last.Values["facts"]!;
        Assert.Contains("Note 8.", facts);
        Assert.DoesNotContain("Note 4.", facts);

        talk.End();
        for (var i = 0; i < 50 && talk.Person.Memories.Count < 10; i++) await Task.Delay(20, Ct);
        Assert.Equal(10, talk.Person.Memories.Count);
    }

    // ----- the base game's content -----

    [Fact]
    public void The_base_game_has_the_talk_prompts_temperaments_for_every_kind_and_an_ollama_model()
    {
        var prompts = BasePrompts();

        Assert.Empty(prompts.Problems);
        foreach (var use in new[] { "chat_seller", "chat_salesman", "chat_rival", "chat_memory", "chat_summary" })
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
