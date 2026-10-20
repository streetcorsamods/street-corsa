# Screens Index

## Screen Inventory

| Screen | Path | Purpose |
|--------|------|---------|
| MainMenu | `Screens/MainMenu/` | Main screen: the game's title while it loads, then cars filmed in the showrooms with the menu over them (see `main-screen.md`) |
| NewGame | `Screens/NewGame/` | Create new save (a card on the main screen) |
| LoadGame | `Screens/LoadGame/` | Load existing save (a card on the main screen) |
| Settings | `Screens/Settings/` | App settings (a card on the main screen) |
| CarCatalogEditor | `Screens/CarCatalogEditor/` | Edit car prices/precedence |
| Garage | `Screens/Garage/` | In-game hub: 3D garage with the selected car, overlay buttons to every other in-game screen. Under the car a row of round icon buttons: the key, then parts view (with stand and turn over while it is open), repairs, lights, doors, rotate. Turning the key starts the engine and the engine bar (rpm, tach, pedal) rises in the buttons' place; turning it back, or the engine dying, brings them back. Sell is the round button right of the car's name (a dot while in the paper, amber with an offer). Free run and test-and-tune are in the racing hub. The room is the game's own, its props placed by data (see `garage.md`) |
| CarLot | `Screens/CarLot/` | The player's cars parked in their warehouses in 3D: look them over and take one to the garage (see `car-lot.md`) |
| Diner | `Screens/Diner/` | Drive down Main Street into the diner's lot, the racers' cars parked out front; the diner (who is in, the word on the street, a racer's options, the race and a talk) opens in one window over the street (see `diner.md`) |
| Cruise | `Screens/Cruise/` | Sit at the curb on Main Street in your car; rivals pull up with an offer (see `cruise.md`) |
| GasStation | `Screens/Station/` | Drive to the gas station: fill up, top up the oil, a wash, the attendant's remarks (see `gas-station.md`) |
| PaintBooth | `Screens/PaintBooth/` | Roll the car into the paint booth, pick one of its make's colours or one of your own on the car, and watch the painter spray it; home to the garage in the new colour (see `paint-booth.md`) |
| Newspaper | `Screens/Newspaper/` | The classifieds, set as a period paper's ads under the masthead (the paper's name in the top band between the back button and the money card, the date line under the thick rule): used cars (every dealer's and racer's, and the player's own among them at a place drawn from the ad's id, written like the others and circled in pencil with the buyer's offer pencilled under it; clicked, `Dialogs/PlayerAd`: the car, the offer to take or turn down, "Call the buyer" with the AI on) and used parts, a page at a time as the player scrolls; an ad is bought from here or clicked to look at it |
| DealerMap | `Screens/DealerMap/` | The city, with a pin per car dealer (the garage's Car dealers button) or per part shop (Parts dealers: gold squares) |
| PartShop | `Screens/PartShop/` | Inside one part shop in 3D, its own stock on its shelves; the counter (a window over it) has its shelves or yard at today's prices, haggling at a junkyard, selling to it, and the clerk while the AI is on (see `part-shop.md`) |
| MachineShop | `Screens/PartShop/MachineShop*` | A machine shop from the part shops' map (the `part_shop` screen for a shop of kind `machine`): the player's parts on its machines in 3D; the counter lists what they have there and can bring, and the work on each (see `machine-shop.md`) |
| DealerLot | `Screens/DealerLot/` | One dealer's cars parked in 3D; click one to look at it |
| UsedParts | `Screens/UsedParts/` | The mail order (the part shops' map's button): new parts by mail, selling off the shelf |

## Navigation Flow

```
MainMenu (opens under the title; NewGame, LoadGame and Settings are cards on it, not screens)
         ├── NewGame card → Garage
         ├── LoadGame card → Garage
         └── Settings card → CarCatalogEditor → MainMenu with the Settings card open

Garage (hub) ←→ CarLot
  │
  ├──→ Racing hub dialog       [Go race!: invitations, Street News]
  │      ├──→ TrackPicker → AC free run (back in the garage)   [Free Run: any track, as cards with a search]
  │      ├──→ TrackPicker → RaceLoading (test-and-tune)        [Test & Tune: the drag strips]
  │      ├──→ EventEntry → RaceLoading
  │      └──→ Diner (challenges)  [a diner on the racing hub; four by level]
  │      └──→ Cruise ──→ RaceLoading ──→ Cruise   [Cruise the streets]
  ├──→ GasStation               [Gas station]
  ├──→ DealerMap → DealerLot    [Car dealers]
  ├──→ DealerMap → PartShop     [Parts dealers: the same map with the part shops]
  │              → UsedParts    [Mail order]
  └──→ Newspaper                [the classifieds]

Garage Quit button → Quit dialog → MainMenu or the desktop
Garage trophy button → Career dialog (victory paths, the record, milestones) over the garage
```

## Screen Structure
Each screen folder contains:
- `[Name]ScreenViewModel.cs` - Logic, commands, state
- `[Name]ScreenView.xaml` - UI layout
- `[Name]ScreenView.xaml.cs` - Code-behind (InitializeComponent only)

## NavigationService Methods

Screens are gone to by their id in the flow (`Content\streetcorsa\flow\flow.json`), never by a method per screen.

| Method | Target Screen |
|--------|---------------|
| NavigateToStart() | MainMenu under the title, at startup |
| GoToMenu(card) | MainMenu, optionally with a card open |
| Go(id, gameState, args) | The screen of that flow id, built by its kind; `FlowArgs` says what it shows (`DealerId`, `ShopId`, `Car`, `Race`...) |
| Leave(this, exit, gameState, args) | Where the screen's exit leads in the flow (`back`, `bought`, `car_ad`...); nowhere open: home |
| GoFrom(this, id, gameState) | A hub's tile: the screen it names (the racing hub and the career open as dialogs over it) |
| GoHome(gameState) / GoHomeLater(gameState) | Home (the garage); later: once the navigation under way is over |
| GoRace(gameState, intent) | RaceLoading; it comes back to the screen it was set up on (`intent.ReturnTo`) |
| CreateNewGameCard(back) | NewGame card (not a navigation) |
| CreateLoadGameCard(back) | LoadGame card (not a navigation) |
| CreateSettingsCard(back) | Settings card (not a navigation) |

| Flow id (base game) | Screen | Kind |
|---------------------|--------|------|
| main_menu | MainMenu | main_menu |
| garage | Garage (home) | garage |
| car_lot | CarLot | car_lot |
| racing_hub | The racing hub dialog | racing_hub |
| career | The career dialog | career |
| diner | Diner | diner |
| street | Cruise | street |
| gas_station | GasStation | gas_station |
| newspaper | Newspaper | newspaper |
| car_viewing | CarViewing | car_viewing |
| dealer_map | DealerMap | dealer_map |
| dealer_lot | DealerLot | dealer_lot |
| part_shops_map | DealerMap with the part shops | part_shops_map |
| part_shop | PartShop | part_shop |
| mail_order | UsedParts (the mail order) | mail_order |
| race | RaceLoading | race |
| sound_browser | SoundBrowser | sound_browser |
| car_catalog_editor | CarCatalogEditor | car_catalog_editor |

## Adding a New Screen

1. Create folder: `Screens/[Name]/`
2. Create ViewModel inheriting `BaseScreenViewModel`
3. Create View (UserControl XAML)
4. Add DataTemplate to `App.xaml`
5. Add a `ScreenKinds` constant (and to `ScreenKinds.All`) and a case for it in `NavigationService.Go`
6. Add it to `flow/flow.json` with its exits

See [Navigation System](../architecture/navigation.md) for details.
