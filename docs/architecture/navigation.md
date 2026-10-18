# Navigation System

## Core Concept
Single main window hosts interchangeable screens. Navigation is state transition, not window management.

## Screen Model
- Each screen = ViewModel + View pair
- Screens are self-contained, no knowledge of other screens
- Lifecycle hooks: `Enter()`, `Exit()`

## NavigationService Pattern

### The Flow
How the screens hang together is data: the mods' `flow\*.json` (base game: `Content\streetcorsa\flow\flow.json`),
read once at start-up into `FlowBook` (`Navigation/FlowBook.cs`). It holds:
- the screens by id, each of a kind the program has (`ScreenKinds`), with its `exits`: its ways out by name (`back`,
  `bought`, `car_ad`...), each to a screen id;
- `home` (where a game starts and where a way out that leads nowhere goes) and `menu` (the main screen);
- the hub screens' tiles (the garage's buttons) and the main menu's cards;
- `endDay` per screen, and the system each screen belongs to (`ScreenKinds.SystemOf` when it names none). A screen of
  a system switched off in the rules is not there, nor are the exits and tiles to it.

The screens never name each other. See `docs/modding.md`, "flow", for the file's format and every exit.

### Going and Leaving
Every call returns `bool`: false when the player did not get there (callers that don't care use it as a statement).

```
Go(id, gameState, args)                  the screen of that flow id, built by its kind
Leave(this, "back", gameState, args)     a screen's way out, by name
GoFrom(this, id, gameState)              a hub's tile: a screen it names itself
GoHome(gameState, args)                  home
GoHomeLater(gameState)                   home, once the current navigation is over (for a screen sent home from Enter)
ScreenReadyLater(screen), EndDayLater    the same for a scripted screen's ready and the day's end, while the asker is on show
GoToMenu(card)                           the main menu, optionally with a card open
GoRace(gameState, intent)                the race screen
NavigateToStart()                        the main menu under the title, at startup
```

- `FlowArgs` (a record) carries what a screen is told besides its id: `ShopId`, `DealerId`, `Car`, `Race`,
  `SkipAnimation`, `Arrival` (the car out of the lot, or driven home from the street, the diner or the gas station: `DrivingHome.Arrival`; it drives in when it would run), `EndDay`, `Returning` (back from a race), `Card`, `Intro`. A
  kind that must be told what to show (`ScreenKinds.NeedsWhat`: car viewing, dealer lot, part shop, race) is not gone
  to without it, and no tile can open it.
- A way out that leads to no open screen goes home (from anywhere but home), or with no game in play to the main
  menu. `CanLeave(this, exit)` says whether it leads anywhere: a button for such a way out is shown only then.
- `LeaveFrom(id, exit, ...)` is `Leave` for a screen known by its id only (an overlay).
- Overlay kinds (`racing_hub`, `career`) open as dialogs over the screen on show; it is not left.
- `IdOf(screen)` and `CurrentId` give a screen's flow id; each screen also knows the one before it.
- `CanEndDayHere`: the window's End Day button, shown where the flow says `endDay` and the screen has none of its own.
  It goes home with `FlowArgs.EndDay` and the garage ends the day.

### Races
`GoRace(gameState, intent)` sets `intent.ReturnTo` to the screen on show (unless already set) and goes to the first
screen of kind `race`. After the race the race screen goes back there, or where that screen's `after_race` exit leads.
A car towed home goes home; so does a street race that ran past the night. Home failing, the main menu. The racing
hub's races come back to the screen it was opened over.

### Leave and Enter Scripts
A screen with the player's car on a 3D scene (the garage, the car lot, the gas station) implements `IScriptedScreen`.
Its view sets `PlayScreenScript`; the screen calls `ScreenReady(this)` once its own show is over, and the
`screen.<id>.enter` script plays (once). `Leave` and `GoFrom` go through `Depart`: the `screen.<id>.leave` script plays
first and the screen goes once it lets it; a way out that then goes nowhere calls `PutBackAfterLeave`.

The main screen's cards (New Game, Load Game, Settings) are the one exception: they open over the main screen instead of
replacing it, so `NavigationService` only builds them (`CreateNewGameCard(back)` and the rest) and the main menu hosts
them. It opens each through `SafeOpenCard`, which enters it and reports a failure the way `SafeNavigate` does, and
calls its `Exit()` itself; its `Resume()` opens the card again after a failed start or load. See `docs/screens/main-screen.md`.

### How It Works
1. Screen calls `_navigationService.Leave(this, "back", gameState)` (or `Go`, `GoRace`...)
2. `Leave` looks up where the exit leads in the flow; `Go` checks the screen is there and open
3. `Go` switches on the screen's kind and creates the ViewModel, passing the services it needs from its own fields
   and what `FlowArgs` says
4. It goes through `SafeNavigate(name, create, id)`, which records the screen's flow id and calls `NavigateTo(screen)`
5. Previous screen's `Exit()` called
6. New screen's `Enter()` called
7. View resolved via DataTemplate in App.xaml

### Services
NavigationService is constructed once in `App.xaml.cs` with every service a screen needs (plus a callback that sets
`App.CurrentGameState`), and hands them to the screens' constructors. Screens never look services up through
`(App)Application.Current`: a screen's dependencies are all in its constructor.

### Failure Handling (`SafeNavigate`)
- A screen whose constructor throws is never shown.
- A screen whose `Enter()` throws is exited again and the previous screen is put back with `Resume()`, never entered
  again.
- Either way the error is logged, an InformationDialog tells the player (the screen's name from `menu.screen.<id>`,
  else `menu.screen.<kind>`), and the call returns false.
- A screen id not in the flow, of a system switched off, or not told what it needs is not gone to (logged).
- An `async void Enter()` is only covered up to its first `await`, so each one guards its own body. Loading work
  (lists, files) runs in `Enter()`, not in the constructor.

## Rules

| Rule | Reason |
|------|--------|
| Screens never instantiate other screens | Decoupling |
| No `new ScreenViewModel()` in screen code | `NavigationService.Go` builds them |
| Screens leave by a named exit, never by another screen's id | The flow (a mod) decides where it leads |
| Building screens only in `NavigationService.Go` | Single source of truth |
| Pass what a screen shows in `FlowArgs` | One way in for every screen |

## Adding a Screen to the Flow
A screen of a kind the program has needs no code: an entry under `screens` in a `flow\*.json`, and exits or tiles
that lead to it.

## Adding a New Kind of Screen

1. Create `Screens/[Name]/[Name]ScreenViewModel.cs`
   - Inherit `BaseScreenViewModel`
   - Constructor takes NavigationService + dependencies (services, never `App` lookups)

2. Create `Screens/[Name]/[Name]ScreenView.xaml`
   - UserControl with UI

3. Create code-behind `.xaml.cs`
   - Just `InitializeComponent()`

4. Add DataTemplate in `App.xaml`:
   ```xml
   <DataTemplate DataType="{x:Type ns:[Name]ScreenViewModel}">
       <ns:[Name]ScreenView/>
   </DataTemplate>
   ```

5. Add a constant to `ScreenKinds` in `FlowBook.cs` and put it in `ScreenKinds.All`. If it belongs to a system, add
   it to `SystemOf`; if it must be told what to show, to `NeedsWhat`; if it opens over the screen, to `IsOverlay`; if
   the day cannot be ended from it, to `CanEndDay`.

6. Add a case to `NavigationService.Go` (add a constructor parameter and field for any service it needs that is not
   there yet; anything it is told goes in `FlowArgs`):
   ```csharp
   case ScreenKinds.[Name]:
       return SafeNavigate(name, () => new Screens.[Name].[Name]ScreenViewModel(
           this, _dialogService, game, ...), id);
   ```

7. Add it to `Content\streetcorsa\flow\flow.json` under `screens`, with its `exits`, and the exits or tiles that
   lead to it. The screen leaves with `Leave(this, "[exit]", gameState)`.

## Anti-Patterns
- Multiple windows for navigation
- WPF Frame/Page navigation
- Code-behind navigation logic
- Generic factories or service locators
- UI driving state instead of ViewModel

## Files
- `Navigation/NavigationService.cs` (and `FlowArgs`)
- `Navigation/FlowBook.cs` (`ScreenKinds`, tiles, cards)
- `Navigation/IScriptedScreen.cs`
- `Content/streetcorsa/flow/flow.json`
- `Navigation/BaseScreenViewModel.cs`
- `Navigation/IScreen.cs`

- The screen-bound `*Later`s run on the UI thread once what runs there now is over, only while the asking screen is
  still on show. Anything else that has to wait for the UI thread's queue (a property told again after a binding pushed
  a value the code refused, as Settings does) goes through `Helpers.UiThread.Later`, not the navigation.
