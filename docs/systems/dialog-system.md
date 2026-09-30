# Dialog System

## Core Principle
Dialogs are modal overlays within the main window. Never nested. Never windows.

## Mental Model
- Main Screen (game state)
- One Modal Dialog Overlay (optional)

That's it. Two layers maximum. A dialog requested while another is open waits in a FIFO queue and shows when the one in
front closes: it never replaces it (a "Save Warning" is not lost behind "Purchase Successful").

## Dialog Types

| Type | Purpose |
|------|---------|
| ConfirmationDialog | Yes/No decisions |
| InformationDialog | Read-only message, single OK |
| EventEntryDialog | Pick a car to enter an event with |

## DialogService
Central service for showing/hiding dialogs.

| Method | Purpose |
|--------|---------|
| ShowDialog(viewModel) | Display the dialog, or queue it behind the open one |
| ShowDialog(viewModel, jumpQueue: true) | Display it now, over the open one (which waits at the head of the queue) |
| Withdraw(viewModel) | Take a dialog away, whether it is up or still waiting |
| CloseDialog() | Hide the current dialog; the next queued one shows |
| CurrentDialog / IsDialogOpen | What is showing |

## Creating a Dialog

1. Create ViewModel in `Dialogs/[Name]/`:
   ```csharp
   public class MyDialogViewModel
   {
       public RelayCommand ConfirmCommand { get; }
       public RelayCommand CancelCommand { get; }
   }
   ```

2. Create View XAML:
   ```xml
   <UserControl x:Class="...MyDialogView">
       <!-- Dialog content -->
   </UserControl>
   ```

3. Add DataTemplate in `App.xaml`:
   ```xml
   <DataTemplate DataType="{x:Type dialogs:MyDialogViewModel}">
       <dialogs:MyDialogView/>
   </DataTemplate>
   ```

4. Show from screen:
   ```csharp
   var dialog = new MyDialogViewModel(_dialogService, ...);
   _dialogService.ShowDialog(dialog);
   ```

## Size
The card takes the size its dialog asks for. A dialog of text sets `MaxWidth="{StaticResource TextDialogMaxWidth}"`
(750) so its lines stay readable; the card scrolls one that grows taller than the window. A big dialog with its own
lists (the career) sets its Width and Height and binds MaxWidth/MaxHeight to `helpers:DialogRoom.Width`/`Height`,
the room the main window has for a dialog, so it fits a small or high-DPI screen whole and scrolls its own lists.

## Rules

| Do | Don't |
|----|-------|
| One dialog at a time | Nested dialogs |
| Inline confirmation states | Dialog opening dialog |
| Return results via callbacks (the dialog closes first, then calls back, so a callback may show the next dialog) | Native MessageBox |
| Dim background | Multiple modal windows |

## Escalation Rule
If dialog becomes complex → close it → navigate to a dedicated screen.

## Files
- `Dialogs/DialogService.cs`
- `Dialogs/Confirmation/ConfirmationDialogViewModel.cs`
- `Dialogs/Confirmation/ConfirmationDialogView.xaml`
- `Dialogs/Information/InformationDialogViewModel.cs`
- `Dialogs/Information/InformationDialogView.xaml`
