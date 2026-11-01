# Time System

## Purpose
Game time progression with scheduled tasks that execute at intervals.

## Core Components

### GameState.Date
The in-game date and time. Advances when the player does things (`GameAction` tariffs in
`Services/Time/GameAction.cs`). The day runs from 8:00 to 22:00 (`GameTimeService`); time spent past 22:00 ends
the day and the game picks up at 8:00 the next morning.

### Ending the day
The garage's calendar panel has an **End Day** button: after asking, `GameTimeService.EndDayAsync` skips the
rest of the day to the next morning, the night's tasks run, and the game is saved.

### GameTimeScheduler
Executes registered tasks when time advances.

### IScheduledTask
Interface for tasks that run on schedule.

| Property | Type | Purpose |
|----------|------|---------|
| TaskId | string | Unique identifier |
| IntervalDays | int | How often to run |
| ExecuteAsync | method | Task logic |

## Task Execution Flow
1. Time advances from `previousDate` to `newDate`
2. Scheduler checks each registered task
3. If `daysSinceLastRun >= IntervalDays`: execute
4. Update `ScheduledTaskState.LastExecuted`

## ScheduledTaskState (Persisted)
Stored in `GameState.ScheduledTasks`:
- `TaskId` - Which task
- `LastExecuted` - When it last ran

## Current Tasks

In the order they run each morning, which is the order `App.xaml.cs` registers them in. The order matters: the
businesses open, close and restock before the rivals shop, the rivals shop and tune before they race each other,
and the two that put engines together on a worker thread come last, so whatever comes before them is done by the
time the player carries on.

| Task | Id | Interval | Purpose |
|------|----|----------|---------|
| DealerReviewTask | `dealer_review` | 1 day | Closing-down sales mark down or shut; now and then a lot closes or opens (`DealerLifeService`) |
| PartShopReviewTask | `partshop_review` | 1 day | The same for the part shops, and their deliveries come in (`PartShopLifeService`) |
| OpponentReviewTask | `opponent_review` | 1 day | Rivals buy, repair, tune, go broke and come back (`OpponentLifeService`) |
| RaceSimulatorTask | `race_simulator` | 1 day | Rivals race each other |
| EventGenerationTask | `event_generation` | 1 day | Clears expired invitations and opens new ones; counts `DaysPlayed` |
| CarAdsReviewTask | `car_ads_review` | 1 day | Buyers answer the player's car ads |
| MarketRefreshTask | `market_refresh` | 1 day | Tops the open lots back up |
| PartsAdsRefreshTask | `parts_ads_refresh` | 1 day | Turns over the used parts ads, and brings what the player ordered by mail |

A task works on the day it is given (`ExecuteAsync`'s `date`), not on `GameState.Date`: when several days turn
over at once, each day's run is stamped with its own morning. What a task says (the paper, the diner's talk) is
written even when a later step of its day fails, and a task that throws is due again the next day.

## Adding a New Task

1. Create class implementing `IScheduledTask`:
   ```csharp
   public class MyTask : IScheduledTask
   {
       public string TaskId => "my_task";
       public int IntervalDays => 1;
       public Task ExecuteAsync(GameState state, DateTime date) { ... }
   }
   ```

2. Register in `App.xaml.cs`:
   ```csharp
   Scheduler.RegisterTask(new MyTask(dependencies));
   ```

## Time Advancement
Activities that advance time call `IGameTimeService.SpendTimeAsync` (a `GameAction` or minutes), which runs the
scheduler once for each day that turns over. Nothing calls the scheduler directly.

## Files
- `Services/Scheduler/GameTimeScheduler.cs`
- `Services/Scheduler/IGameTimeScheduler.cs`
- `Services/Scheduler/IScheduledTask.cs`
- `Services/Scheduler/Tasks/*.cs`
- `Services/Time/GameTimeService.cs`
- `Models/GameState/ScheduledTaskState.cs`

## Travel

Visiting a dealer costs that dealer's `travelHours` (the setting's `dealers/dealers.json`), spent through
`App.SpendTimeAsync(int minutes)` rather than a `GameAction`, because the cost is per dealer and not a
fixed tariff. The map checks `GetRemainingMinutesToday` first and asks before a trip that would run past
the end of the day. See `docs/screens/dealer-lot.md`.
