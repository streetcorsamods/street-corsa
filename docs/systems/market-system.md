# Market System

## Purpose
Dynamic used car market with dealer locations, pricing based on condition, and daily refresh.

## Data Layers

| Layer | Model | Purpose |
|-------|-------|---------|
| 1 | CarDefinition | Immutable AC identity |
| 2 | CarProfile | BasePrice, DealerPrecedence |
| 3 | UsedCarListing | Car for sale |
| 4 | CarInstance | Player-owned car |

## UsedCarListing Properties
- `CarDefinitionId` - References catalog
- `Price` - Varies from BasePrice by condition
- `Condition` - 0.0 (broken) to 1.0 (perfect)
- `Mileage` - Odometer reading
- `SkinId` - Visual variant
- `DealerLocation` - Which dealer
- `ListedDate` - When listed
- `IsSold` - Purchase status

## Spawning Logic
Dealer by dealer, each filled to its own `stockLow`..`stockHigh`:
- Build the pool of cars that have a profile and a price, each with its rank (the share of installed cars
  it is dearer than)
- For each dealer, take the cars whose rank falls in its price band
- Draw from those weighted by `DealerPrecedence`, at most twice per model, until the lot is full

A dealer whose band matches nothing installed takes the nearest cars instead of standing empty.

Do **not** go back to spawning a whole market and cutting it to size — `.Take(n)` over a list built in
catalog order starves whole dealers.

## Daily Refresh
1. Remove sold listings older than 7 days
2. Remove unsold listings older than 14 days, except a car in a closing-down sale (`PriceBeforeSale` set): it stays
   until its lot shuts. A full respawn of the market (the map's stale-market check) keeps those cars too.
3. Top each open dealer back up to its own target (not one in its closing-down sale, closed or not open yet)

The save's rules can turn the daily refresh off (`GameRules.MarketRefreshEnabled`): the lots then keep what they have.

## Pricing
What a car is worth has one formula, `CarValuation` (`Services/Market/CarValuation.cs`), used by the market, an
opponent's car, a pink-slipped car going back on a lot and the pink-slip challenge logic:
- `CarProfile.BasePrice` × condition factor `0.5 + 0.6 × condition` (condition 0 = 50%, 1.0 = 110%)
- × its history (`HistoryFactor`, step 10): +1% a win and +2% a car won on a pink slip; −2% an owner beyond the
  second and −1% every 10,000 km past 80,000 (each at most −8%); all together within ±15%. A car with no history
  (an older save) is ×1.
- × how it looks (`LooksFactor`): a body all rust loses 30%, paint worn all over 8%, a cracked windscreen 2%, each as
  far as it goes (`market.rustLoss`, `paintWearLoss`, `crackedScreenLoss`)
- plus half of what the car's engine parts cost new beyond its factory build's, scaled by condition (`ValueOf`, with the
  parts catalog; without it the engine counts as the factory one)
- rounded to the nearest $100

A dealer's listing gets its looks rolled with it (`CarLooks.Roll`, `LookSource.Dealer`: a little rust and worn paint on
a rough car, washed, rarely a cracked windscreen) and is priced as it looks; a rival's own car is a private one
(`LookSource.Private`: more of everything, dirty), and its ad shows it as it is.

A listing's price is that times the dealer's random variation of ±20%, then times the save's
`GameRules.CarPriceMultiplier` (the difficulty: 0.85 Easy, 1 Normal, 1.15 Hard). The multiplier is on what sellers
ask, never on what a car is worth: a car the player sells fetches a share of its worth whatever the difficulty.

## Dealer Locations
Twenty dealers, defined in `Content/streetcorsa/dealers/dealers.json` and read by `DealerCatalog`. Twelve are open
when a game starts (Downtown Motors, Sunset Motors, Colorado Motors, Suburban Autos, Ocean Park Autos, Riverside Cars,
Vermont Auto Sales, Industrial Motors, Harbor Auto, Eastside Garage, Fairplex Auto Mart, Firestone Boulevard Motors);
the other eight (`"startsOpen": false`) wait to open. `GetDefaultDealers()` still hands out the original five when the
file cannot be read.

The save holds `DealerLocation`: Id, Name, Region, and how the business is going (`Status`, `OpenedOn`, `ClosesOn`,
`ClosedOn`, `Trade`). Map position, showroom, parking bays and stock character live in the JSON and are merged over the
save (`DealerRoster.Sync`), so new fields reach old saves; a save from before step 20 reads its dealers as open and
gets the new ones as the JSON says. See `docs/screens/dealer-lot.md`.

## Dealers Come and Go
`DealerLifeService` (the `dealer_review` task, daily, before the rivals' review) keeps about 60% of the dealers open
(`DealerRules`):
- **About every two months** (`DaysPerChange` 60, and not while a sale is on) a lot goes under or one opens, toward
  the target (12 of 20), never away from it and never under 40% open: short of lots with no premises free, or over
  with nobody allowed to close, nothing changes. A two-year run of the base game gives about five of each.
- **Who goes under** is weighted by how the lot is doing (`CloseWeight`): `Trade` counts the cars sold off it and
  halves every 30 days. A count runs from the last review up to, not including, this morning's: the rivals and the
  player buy after the review, at the time it counted to, so their sales count the next day. Cars standing unsold 10
  days or more count against a lot; a lot sold out has none standing. A lot that opened during the game gets
  120 days before it can close.
- **The closing-down sale:** the paper and the diner announce it; the lot takes 20% off every car (the listing keeps
  `PriceBeforeSale`), 35% in its last three days, and restocks nothing. The morning after its seventh day it closes and
  what is unsold goes with it. The map pin and the lot's header show the sale; the price card shows the old price
  struck through.
- **The rivals shop it** (`OpponentLifeService.ShopTheSales`): a racer with money goes round a sale on 35% of days,
  buys a car 10% stronger than theirs and puts the old one in the paper, or, in the last days, buys the deepest cut to
  sell on in the paper (`OpponentRules.WorthFlipping`: the least they would ask for it there, from what the car is
  worth, not the lot's marked price, beats the price by 5%). A flip needs a car of their own and room for a second;
  they keep driving their own and the flipped car goes in the paper. Two cars a day at most go to the rivals.
- **A drive that does not fit the day** to a lot on the last day of its sale is refused: the doors would be shut when
  the player got there. The words of the pin, the card and the lot header are in `text/dealers.json` (`dealer.*`).
- **Who opens:** a dealer that has never opened; once there are none, premises empty for 180 days open again under the
  old name. The market's refresh stocks the new lot the same morning.
- A game whose market does not refresh (`MarketRefreshEnabled`) keeps its dealers as they are; a sale under way still
  runs out. Trade-ins only go to an open lot.

## Which Dealer Gets a Car
Not random. Each dealer claims a slice of the market's price range (`priceBandLow`/`priceBandHigh`); a car
goes to one of the dealers whose slice covers where its base price sits, or to the nearest slice if none
does. `conditionCenter` then shifts the condition roll by how far that dealer's standard sits from the middle
of the range (the roll still leads, and one car in twelve is not shifted at all), so a cheap lot has rough
cars without making a good find impossible.

## Purchase Flow
`CarPurchaseService` owns this, shared by the paper's classifieds and the dealer lot (`Screens/Shared/PurchaseFlow.cs`
holds the confirmation and result dialogs).

1. Validate funds and availability
2. Claim the listing (`IsSold = true`) before anything is awaited, so a second confirm cannot buy it twice; it is
   released again if making the car fails
3. Create `CarInstance` from listing (a part pack that cannot be put together is logged; the car sells without it and
   `EnsurePartsAsync` fills it in later)
4. Add to `GameState.Player.Cars`
5. Deduct money
6. Spend `GameAction.BuyCar` time
7. Save game state

A car bought counts in `Player.Stats.CarsOwned`.

**A rival's car out of the paper** (step 12): the classifieds list the rivals' ads among the lots' cars, as listings made
up from the car for the page only (`RivalCarAds.AsListing`: the car's own figures and the engine as the ad described it
when posted, no dyno run). The row carries the ad (`UsedCarListingViewModel.RivalAd`), and buying it
(`CarPurchaseService.PurchaseFromRivalAsync`, called with the ad) pays the ad's price as it stands now to the rival and hands over that very
car from their garage (`RivalCarAds.HandOver`, `CarAcquisition.PrivateSale`); nothing is put together. See
`opponent-system.md`, "A bigger pool".

## The Classifieds
The newspaper (step 25) is the classifieds only, two sections set as a period paper's ads (`Screens/Newspaper`): every car
for sale, every dealer's and every rival's ad, sorted by make, with the player's own ads and their buyers boxed at the top;
and every used part ad. A section shows a page of ads (`NewspaperScreenViewModel.AdsPerPage`, a screen's worth, not a
rule) and another each time the player scrolls near its end (`AdPages`); an ad bought or gone leaves the rest in place.
There is no dealer filter: a dealer's own cars are on its lot (the dealer map). Clicking an ad goes to look at it.

**Ad blurbs** (`AdBlurbs`): every ad has a few words of its own (`Description` on `UsedCarListing`, `RivalCarAd` and
`PartAd`), written once when the ad is placed (`SpawnListingsAsync`, `RefreshMarketAsync`, `ListCar`,
`RivalCarAds.Post`, the paper's and the rivals' part ads) and kept in the save; an ad from an older save gets its words
the first time the paper shows it (`AdBlurbs.Ensure`). What the words are about is picked with some weight from the
ad's facts (`CarAdFacts`, `PartAdFacts`): a part worth naming off the car's part tree (a blower or turbo, nitrous,
injection or carbs, heads, a cam, an intake, headers: the last ones only on a car somebody has worked on), the engine,
"all original", the condition when it is very good or rough, low miles, a line of character; a car's ad now and then
adds its condition after. The lines are the `ads.car.*` and `ads.part.*` pools of `text\ads.json`. `AdBlurbs.Writer`
(`IAdWriter`) is the seam for a better writer (an AI): every ad written is offered to it with its facts, and what it
calls back with, on the UI thread, takes the built-in line's place; the game never waits on it.

## Selling Cars
`CarSaleService` (`ICarSaleService`), from the garage's Sell button (`Dialogs/SellCar`):
- **To a dealer**, on the spot: `DealerShare` (60%) of what the car is worth, one hour (`GameAction.SellCar`). The
  trade-in lot (`TradeInLocation`) puts it on sale at its worth times `CarPriceMultiplier`.
- **To the scrapyard**, the only buyer of a totaled car (`CarCondition.IsTotaled`): 15% of its worth, ±20% (the same
  price every time for the same car, rolled from its instance id). It leaves the game.
- **Through the paper**: an ad (`CarSaleAd` in `NewspaperAds.PlayerCars`) at the player's asking price ($100 up to
  three times the car's worth) costs $10 and half an hour (`GameAction.PlaceAd`) and runs 14 days. The car stays in the
  garage and can race. Each day (`CarAdsReviewTask`) a buyer may call: a 60% chance for a car asked at 90% of its worth
  or less, 12 points less for each tenth more, nobody at 140%; half that (`paperFreshChanceFactor`) for a car the player
  has had fewer than 14 days (`paperFreshDays`). Every buyer haggles: the offer is drawn between 85% (`paperHaggleShare`)
  and all of the asking price, never below 80% of the car's worth (`paperFloorShare`) nor over the asking price, so a
  car bought cheap and put straight back in the paper can still turn a profit, a thin one. An offer holds two days; the
  player takes it or turns it down in the Sell dialog or under "Your Ads" in the newspaper. The buyer sees the car
  before paying: one worth less than when they called (its engine or wheels taken off since) gets the offer cut in
  proportion (`CarOffer.ValueAtOffer`), or the buyer walks away when that leaves less than the least asking price. A
  buyer drives the car away: it leaves the game.

A sale refuses a car that is out racing (`GameState.PendingRace`), moves the selected car on to another, and counts in
`Player.Stats.CarsSold`.

## Car History
Every car keeps its history (`Car.History`, a `CarHistory`): the owners the game has seen, each with the date and how
they got it (`CarAcquisition`: Dealer, PinkSlip, PrivateSale, Unknown), the owners before anybody in the game had it
(`EarlierOwners`), and its races, wins and pink slips won, whoever drove it. The odometer is `Car.OdometerKM`.
- **Its best quarter mile** (step 11): `BestQuarterSeconds`, `BestQuarterMph` and `BestQuarterDate`, from any drag
  race's timeslip or test-and-tune pass, whoever drove it (`CarHistory.RecordQuarter`; a time under 5 s or over 60 s
  is a bad file, not a run). It shows with the history ("best 13.52 @ 104 mph") and is the dial-in a bracket race
  suggests. It does not move the price.
- It goes with the car everywhere: a pink slip (the player's races and the rivals'), a sale out of the paper to a rival,
  and through a dealer's lot (`ListCar` copies it onto `UsedCarListing.History`, `CarPurchaseService.CarFrom` copies it
  back and adds the buyer). A relisted car keeps its id (`UsedCarListing.CarInstanceId`): whoever buys it gets that
  very car back.
- Somebody who had the car twice (won it back, bought it back) is one owner (`OwnerCount`), for the price and the words.
- New stock comes with 1-3 earlier owners by its mileage; a rival's first car with a few more; the King's car with his
  record (it tops the ±15%).
- Older saves: `HistoryUpgrade` gives every car its present owner at load, and a car or listing nothing was known about
  the earlier owners its miles tell of (`EarlierOwnersFor`: none under 1,000 km, one more every 80,000 km).
- Shown on the dealer lot, the used car ads, the purchase question, the Sell dialog and the garage (the mileage's
  tooltip), always through `CarHistoryDisplay`.

## Cars Going Back on a Lot
`ListCar(car, price, location, listedDate)` turns a car into a listing with its parts (engine, running gear), the
seller's engine summary and whether it has been worked on. A pink-slipped car goes back this way at its `ValueOf` (times `CarPriceMultiplier`), to
`TradeInLocation(dealers)`: the roughest lot (lowest `conditionCenter`).

## Key Service
`UsedCarMarketService` (`IUsedCarMarketService`):
- `SpawnListingsAsync(dealers, date, priceMultiplier)` - Initial spawn
- `RefreshMarketAsync(listings, dealers, date, priceMultiplier)` - Daily refresh. Engines are put together only for the listings that
  make it into the market, on a worker thread; the listings are looked at and handed back on the calling thread
- `GetAvailableListings(listings)` - Filter unsold
- `ListCar(car, price, location, listedDate)` - A car (with its parts) as a listing
- `ValueOf(car)` - What the car is worth (`CarValuation`)
- `TradeInLocation(dealers)` - The dealer that takes in traded cars

## Files
- `Services/Market/UsedCarMarketService.cs`
- `Services/Market/CarPurchaseService.cs`
- `Services/Market/CarSaleService.cs`
- `Services/Scheduler/Tasks/CarAdsReviewTask.cs`
- `Dialogs/SellCar/SellCarDialogViewModel.cs`
- `Services/Market/CarValuation.cs`
- `Services/Market/AdBlurbs.cs`, `Content/streetcorsa/text/ads.json`
- `Screens/Newspaper/` (the classifieds)
- `Services/Dealers/DealerCatalog.cs`
- `Services/Dealers/DealerRoster.cs`, `DealerRules.cs`, `DealerLifeService.cs`
- `Services/Scheduler/Tasks/DealerReviewTask.cs`
- `Content/streetcorsa/dealers/dealers.json`
- `Services/Market/IUsedCarMarketService.cs`
- `Models/GameState/UsedCarListing.cs`
- `Models/GameState/DealerLocation.cs`
