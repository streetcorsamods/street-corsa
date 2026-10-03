using StreetCorsa.Models.Catalog;
using StreetCorsa.Models.GameState;
using StreetCorsa.Mods;
using StreetCorsa.Rules;
using StreetCorsa.Screens.Newspaper;
using StreetCorsa.Screens.Shared;
using StreetCorsa.Services.Ai;
using StreetCorsa.Services.Market;
using StreetCorsa.Services.Storage;
using StreetCorsa.Services.Talk;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Tests;

/// <summary>
/// The player's own car ads in the paper (ideas batch 6): set among the others in a place that does not move, and the
/// buyer who called talked up on the phone, the game deciding how far they go (a hard ceiling drawn once per offer)
/// </summary>
public class PlayerAdsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly TalkRules Rules = new();

    private static AiPrompts BasePrompts() => AiPrompts.Load(ModSet.BaseOnly(RepoPaths.BaseContent));

    // ----- where the player's ads go -----

    [Fact]
    public void The_players_ads_go_among_the_others_in_the_same_place_every_time()
    {
        var others = Enumerable.Range(0, 60).Select(i => $"ad {i:00}").ToList();
        var mine = Enumerable.Range(0, 5).Select(i => (Key: new Guid(i + 1, 7, 7, 0, 1, 2, 3, 4, 5, 6, 7), Ad: $"mine {i}")).ToList();

        var first = ClassifiedOrder.Mix(others, mine);
        var again = ClassifiedOrder.Mix(others, Enumerable.Reverse(mine));

        Assert.Equal(first, again);
        Assert.Equal(65, first.Count);
        Assert.Equal(others, first.Where(a => a.StartsWith("ad", StringComparison.Ordinal)));

        // Nowhere special: not all at the top, not in a block
        var places = mine.Select(m => first.IndexOf(m.Ad)).Order().ToList();
        Assert.NotEqual([0, 1, 2, 3, 4], places);
        Assert.True(places[^1] - places[0] > 5, string.Join(",", places));
    }

    [Fact]
    public void The_players_ad_alone_in_the_paper_is_the_paper()
    {
        var mine = (Key: Guid.NewGuid(), Ad: "mine");
        Assert.Equal(["mine"], ClassifiedOrder.Mix(Array.Empty<string>(), [mine]));
    }

    // ----- what a buyer pays -----

    [Fact]
    public void The_ceiling_comes_from_the_first_offer_never_past_the_asking_price_or_a_rivals_money()
    {
        Assert.Equal(1100m, Haggle.Ceiling(1000m, 0.1, null, 1500m, null, Rules));
        Assert.Equal(1050m, Haggle.Ceiling(1000m, 0.1, new Temperament { Give = 0.5 }, 1500m, null, Rules));
        Assert.Equal(1120m, Haggle.Ceiling(1000m, 0.5, null, 1500m, null, Rules));
        Assert.Equal(1080m, Haggle.Ceiling(1000m, 0.1, null, 1080m, null, Rules));
        Assert.Equal(1040m, Haggle.Ceiling(1000m, 0.1, null, 1500m, 1040m, Rules));

        // Never under what they offered, whatever the ad or their pocket says
        Assert.Equal(1000m, Haggle.Ceiling(1000m, 0.1, null, 900m, 500m, Rules));
        Assert.Equal(1000m, Haggle.Ceiling(1000m, 0, null, 1500m, null, Rules));
    }

    [Fact]
    public void An_asking_price_is_paid_met_halfway_stood_firm_on_or_walked_away_from()
    {
        Func<decimal, decimal> round = p => Haggle.RoundDown(p, 25);

        // Asked no more than they offered: they keep to their offer
        Assert.Equal(new HaggleOutcome(HaggleAnswer.Accept, 900m, 1000m), Haggle.DecideAsk(900m, 1000m, 1100m, 0, Rules, round));
        Assert.Equal(new HaggleOutcome(HaggleAnswer.Accept, 1080m, 1080m), Haggle.DecideAsk(1080m, 1000m, 1100m, 0, Rules, round));
        Assert.Equal(new HaggleOutcome(HaggleAnswer.Counter, 1200m, 1100m), Haggle.DecideAsk(1200m, 1000m, 1100m, 0, Rules, round));
        Assert.Equal(new HaggleOutcome(HaggleAnswer.Counter, 1150m, 1075m), Haggle.DecideAsk(1150m, 1000m, 1100m, 0, Rules, round));
        Assert.Equal(HaggleAnswer.Firm, Haggle.DecideAsk(1200m, 1100m, 1100m, 0, Rules, round).Answer);

        // Far over, or pushed again and again: they want no more of it
        Assert.Equal(HaggleAnswer.Walk, Haggle.DecideAsk(1400m, 1000m, 1100m, 0, Rules, round).Answer);
        Assert.Equal(HaggleAnswer.Walk, Haggle.DecideAsk(1200m, 1000m, 1100m, Rules.MaxCounters, Rules, round).Answer);
    }

    [Fact]
    public void Each_buyer_draws_their_own_limit_and_some_will_not_go_up_at_all()
    {
        var random = new Random(11);
        var draws = Enumerable.Range(0, 2000).Select(_ => Haggle.DrawMostRaise(Rules, random)).ToList();

        Assert.All(draws, d => Assert.InRange(d, 0, Rules.MostRaise));
        Assert.InRange(draws.Count(d => d == 0) / 2000.0, Rules.FirmShare - 0.04, Rules.FirmShare + 0.04);
        Assert.True(draws.Distinct().Count() > 1000);
    }

    [Theory]
    [InlineData("I'd take $1,150 for it, no less.", true)]
    [InlineData("Your ad says $1,500, and I said $1,000.", false)]
    [InlineData("A thousand's my offer, $1,000 and not a cent more.", false)]
    public void A_buyer_the_ai_has_go_up_by_itself_is_caught(string answer, bool higher)
    {
        Assert.Equal(higher, Haggle.NamesHigherPrice(answer, 1000m, [1500m]));
    }

    // ----- a call to the buyer -----

    private static GameState Game()
    {
        var game = new GameState { SaveName = "test" };
        game.Player.Name = "Kid";
        return game;
    }

    private static (CarSaleAd Ad, UsedCarListingViewModel Car) Ad(GameState game, decimal asking = 1500m, decimal offer = 1000m, string? rival = null)
    {
        var car = new Car("bel_air") { OdometerKM = 60_000 };
        game.Player.Cars.Add(car);
        var ad = new CarSaleAd
        {
            CarInstanceId = car.InstanceId,
            AskingPrice = asking,
            PostedDate = game.Date,
            EngineSummary = "283 V8",
            Description = "One owner, never raced.",
            Offer = new CarOffer { BuyerName = rival ?? "Gary", RivalName = rival, Amount = offer, Expires = game.Date.AddDays(3) }
        };
        game.NewspaperAds.PlayerCars.Add(ad);
        var shown = new UsedCarListingViewModel
        {
            Listing = new UsedCarListing
            {
                CarDefinitionId = "bel_air", Price = asking, Mileage = 60_000, Condition = 0.7f, EngineSummary = "283 V8",
                Description = ad.Description, ListedDate = ad.PostedDate
            },
            CarDefinition = new CarDefinition { Id = "bel_air", Brand = "Chevrolet", Name = "Bel Air", Year = 1957 }
        };
        return (ad, shown);
    }

    private static (TalkDesk Desk, FakeAiText Ai) Desk(string? answer = "Sure thing.")
    {
        var ai = new FakeAiText((_, _) => Task.FromResult(answer));
        return (new TalkDesk(ai, prompts: BasePrompts, rules: () => Rules), ai);
    }

    [Fact]
    public async Task A_buyer_talked_up_raises_the_offer_and_takes_the_ask_and_the_ai_is_told()
    {
        var game = Game();
        var (ad, car) = Ad(game);
        ad.Offer!.MostOver = 0.1;
        var (desk, ai) = Desk();
        var talk = desk.ForBuyer(game, ad, car);

        var ceiling = Haggle.Ceiling(1000m, 0.1, BasePrompts().TemperamentOf(talk.Person.Temperament), 1500m, null, Rules);
        await talk.OpenAsync(Ct);
        await talk.SayAsync($"I want {ceiling:0} for it.", Ct);

        Assert.Equal(PersonKind.Buyer, talk.Person.Kind);
        Assert.Equal("Gary", talk.Person.Name);
        Assert.Equal("chat_buyer", ai.Chats[^1].Use);
        Assert.Equal(HaggleAnswer.Accept, talk.LastOutcome!.Answer);
        Assert.Equal(ceiling, ad.Offer.Amount);
        Assert.Equal(1000m, ad.Offer.FirstAmount);
        Assert.True(talk.IsAgreed);
        var facts = (string)ai.Chats[^1].Values["facts"]!;
        Assert.Contains("283 V8", facts);
        Assert.Contains("One owner", facts);
        Assert.Contains(GameText.Say("chat.deal.buyer.accept", ("offer", new Helpers.Money(ceiling)), ("price", new Helpers.Money(ceiling))), facts);
        Assert.Contains("Kid", ai.Chats[0].Turns[0].Text);
    }

    [Fact]
    public async Task However_often_the_player_asks_the_offer_never_goes_past_the_hard_ceiling()
    {
        var game = Game();
        var (ad, car) = Ad(game);
        var offer = ad.Offer!;
        var (desk, _) = Desk();

        // Asked a little more again and again, the buyer forgetting the player (made again) in between
        for (var i = 0; i < 30 && ad.Offer != null; i++)
        {
            var talk = desk.ForBuyer(game, ad, car);
            await talk.SayAsync($"Make it ${Math.Round(offer.Amount * 1.05m)}", Ct);
            talk.End();
            game.People.Clear();
        }

        Assert.Equal(1000m, offer.FirstAmount);
        Assert.InRange(offer.MostOver!.Value, 0, Rules.MostRaise);
        Assert.True(offer.Amount <= 1000m * (1m + (decimal)Rules.MostRaise), $"{offer.Amount}");
    }

    [Fact]
    public async Task A_buyer_pushed_too_far_walks_away_and_the_offer_is_gone()
    {
        var game = Game();
        var (ad, car) = Ad(game);
        ad.Offer!.MostOver = 0;
        var (desk, ai) = Desk();
        var talk = desk.ForBuyer(game, ad, car);

        await talk.SayAsync("I need $1,450 for it.", Ct);

        Assert.Equal(HaggleAnswer.Walk, talk.LastOutcome!.Answer);
        Assert.Null(ad.Offer);
        Assert.False(talk.IsAgreed);
        Assert.Contains(GameText.Say("chat.deal.buyer.walk", ("offer", new Helpers.Money(1450m)), ("price", new Helpers.Money(1000m))),
            (string)ai.Chats[^1].Values["facts"]!);

        // Gone is gone: taking it now agrees to nothing
        await talk.SayAsync("Okay, deal.", Ct);
        Assert.False(talk.IsAgreed);
        Assert.Contains(GameText.Say("chat.fact.offer_gone"), (string)ai.Chats[^1].Values["facts"]!);
    }

    [Fact]
    public async Task A_buyer_who_will_not_go_up_stands_firm_and_taking_the_offer_shakes_on_it()
    {
        var game = Game();
        var (ad, car) = Ad(game);
        ad.Offer!.FirstAmount = 1000m;
        ad.Offer.MostOver = 0;
        var (desk, _) = Desk();
        var talk = desk.ForBuyer(game, ad, car);

        await talk.SayAsync("Would you go $1,100?", Ct);
        Assert.Equal(HaggleAnswer.Firm, talk.LastOutcome!.Answer);
        Assert.Equal(1000m, ad.Offer.Amount);
        Assert.False(talk.IsAgreed);

        await talk.SayAsync("You got a deal", Ct);
        Assert.Equal(HaggleAnswer.Shake, talk.LastOutcome!.Answer);
        Assert.True(talk.IsAgreed);
        Assert.Equal(1000m, ad.Offer.Amount);
    }

    [Fact]
    public async Task A_rival_who_called_talks_as_the_racer_and_never_pays_past_their_money()
    {
        var game = Game();
        var rival = new Opponent("Johnny Reyes", 24, Gender.Male, 95, 40) { Nickname = "Flash", Money = 1030m };
        game.Racers.AddRacer(rival);
        var (ad, car) = Ad(game, rival: "Johnny Reyes");
        ad.Offer!.MostOver = Rules.MostRaise;
        var (desk, ai) = Desk();
        var talk = desk.ForBuyer(game, ad, car);

        await talk.SayAsync("I want 1,100 bucks for it.", Ct);

        Assert.Same(rival, talk.Rival);
        Assert.Equal("chat_rival", ai.Chats[^1].Use);
        Assert.True(ad.Offer!.Amount <= 1030m, $"{ad.Offer.Amount}");
    }

    [Fact]
    public async Task An_offer_the_ai_raises_by_itself_is_asked_again_and_then_held_by_the_game()
    {
        var game = Game();
        var (ad, car) = Ad(game);
        var (desk, ai) = Desk(answer: "Tell you what, I'll go $1,250.");
        var talk = desk.ForBuyer(game, ad, car);

        var said = await talk.SayAsync("How's the weather?", Ct);

        Assert.Equal(2, ai.Chats.Count);
        Assert.Contains(GameText.Say("chat.fact.offer_warning", ("price", new Helpers.Money(1000m))), (string)ai.Chats[1].Values["facts"]!);
        Assert.Equal(GameText.Say("chat.deal.buyer.held", ("price", new Helpers.Money(1000m))), said);
        Assert.Equal(1000m, ad.Offer!.Amount);
    }

    // ----- the save -----

    [Fact]
    public void A_players_ad_and_its_buyer_round_trip_through_the_save()
    {
        using var temp = new TempDir();
        using var repository = new GameStateRepository(null, new SaveDatabase(temp.Combine("Saves")));
        var game = GameState.CreateNew("Kid");
        var (ad, _) = Ad(game);
        ad.IsModified = true;
        ad.AiPolished = true;
        ad.Offer!.FirstAmount = 1000m;
        ad.Offer.MostOver = 0.07;
        ad.Offer.Id = Guid.NewGuid();
        game.People.Add(new Person { Key = "buyer:x", Kind = PersonKind.Buyer, Name = "Gary", Temperament = "tire_kicker" });

        repository.Save(game, "Ads");
        var loaded = repository.Load("Ads")!;

        var back = Assert.Single(loaded.NewspaperAds.PlayerCars);
        Assert.Equal("283 V8", back.EngineSummary);
        Assert.True(back.IsModified);
        Assert.Equal("One owner, never raced.", back.Description);
        Assert.True(back.AiPolished);
        Assert.Equal(1000m, back.Offer!.FirstAmount);
        Assert.Equal(0.07, back.Offer.MostOver);
        Assert.Equal(ad.Offer.Id, back.Offer.Id);
        Assert.Equal(PersonKind.Buyer, Assert.Single(loaded.People).Kind);
    }

    [Fact]
    public void An_ad_from_before_ads_had_these_reads_with_nothing_drawn()
    {
        var ad = new CarSaleAd { Offer = new CarOffer() };
        Assert.Null(ad.Description);
        Assert.False(ad.AiPolished);
        Assert.Null(ad.Offer.FirstAmount);
        Assert.Null(ad.Offer.MostOver);
    }
}

/// <summary>The player's own ad gets its words as the others do: alone, since the ad writer is the whole app's</summary>
[Collection(AdWriterCollection.Name)]
public class PlayerAdWordsTests
{
    private sealed class Writer : IAdWriter
    {
        public int Asked { get; private set; }

        public bool CanImprove => true;

        public void Improve(AdFacts facts, string written, Action<string> rewrite)
        {
            Asked++;
            rewrite("Clean as a whistle, " + ((CarAdFacts)facts).Engine + ".");
        }
    }

    [Fact]
    public void The_players_ad_gets_its_words_once_with_the_engine_said_and_the_ai_rewrites_them()
    {
        var car = new Car("bel_air") { OdometerKM = 30_000 };
        var ad = new CarSaleAd { CarInstanceId = car.InstanceId, AskingPrice = 1200m };
        var engine = new EngineDescription("327 V8", 250, true);
        EngineDescription? Engine() => engine;

        try
        {
            AdBlurbs.Writer = null;
            Assert.True(AdBlurbs.Ensure(ad, car, null, () => null, Engine));
            var own = ad.Description;
            Assert.False(string.IsNullOrWhiteSpace(own));
            Assert.Equal("327 V8", ad.EngineSummary);
            Assert.True(ad.IsModified);

            // Written once: shown again with the same engine in the car, it keeps its words
            Assert.False(AdBlurbs.Ensure(ad, car, null, () => null, Engine));
            Assert.Equal(own, ad.Description);

            // The paper offers it to the writer as it opens; what comes back takes the words' place, once
            var writer = new Writer();
            AdBlurbs.Writer = writer;
            AdBlurbs.Polish(ad, car, null, () => null);
            AdBlurbs.Polish(ad, car, null, () => null);
            Assert.Equal(1, writer.Asked);
            Assert.Equal("Clean as a whistle, 327 V8.", ad.Description);
            Assert.True(ad.AiPolished);

            // Parts unknown for now: the ad stays as it is
            Assert.False(AdBlurbs.Ensure(ad, car, null, () => null, () => null));
            Assert.Equal("327 V8", ad.EngineSummary);
        }
        finally
        {
            AdBlurbs.Writer = null;
        }
    }

    [Fact]
    public void The_players_ad_is_written_anew_when_the_engine_in_the_car_changed()
    {
        var car = new Car("bel_air") { OdometerKM = 30_000 };
        var ad = new CarSaleAd { CarInstanceId = car.InstanceId, AskingPrice = 1200m, EngineSummary = "283 V8", Description = "Clean as a whistle, 283 V8.", AiPolished = true };

        try
        {
            AdBlurbs.Writer = null;
            Assert.True(AdBlurbs.Ensure(ad, car, null, () => null, () => new EngineDescription("409 V8", 340, true)));
            Assert.Equal("409 V8", ad.EngineSummary);
            Assert.True(ad.IsModified);
            Assert.False(ad.AiPolished);
            Assert.DoesNotContain("283", ad.Description);

            // And offered to the AI again, in the same session
            var writer = new Writer();
            AdBlurbs.Writer = writer;
            AdBlurbs.Polish(ad, car, null, () => null);
            Assert.Equal(1, writer.Asked);
            Assert.Equal("Clean as a whistle, 409 V8.", ad.Description);
        }
        finally
        {
            AdBlurbs.Writer = null;
        }
    }

    // ----- one offer, one deal -----

    [Fact]
    public void A_new_offer_from_a_caller_of_the_same_name_is_a_new_deal()
    {
        var ad = new CarSaleAd { Offer = new CarOffer { Id = Guid.NewGuid(), BuyerName = "Gary" } };
        var first = TalkPeople.BuyerKey(ad);
        Assert.Equal(first, TalkPeople.BuyerKey(ad));

        ad.Offer = new CarOffer { Id = Guid.NewGuid(), BuyerName = "Gary" };
        Assert.NotEqual(first, TalkPeople.BuyerKey(ad));

        // An offer from before offers were told apart keeps its old key, so its deal is not lost
        ad.Offer = new CarOffer { BuyerName = "Gary" };
        Assert.Equal($"buyer:{ad.AdId}:Gary", TalkPeople.BuyerKey(ad));
    }

    [Fact]
    public void An_offer_stands_until_the_end_of_its_last_day_and_not_after()
    {
        var day = new DateTime(1962, 5, 4, 10, 0, 0);
        var ad = new CarSaleAd { Offer = new CarOffer { Expires = day } };
        Assert.Same(ad.Offer, ad.OfferOn(day));
        Assert.Null(ad.OfferOn(day.AddMinutes(1)));
        Assert.Null(new CarSaleAd().OfferOn(day));
    }
}
