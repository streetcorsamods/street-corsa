using StreetCorsa.Screens.Shared;
using StreetCorsa.Services.Talk;
using StreetCorsa.Services.Text;
using StreetCorsa.ViewModels;

namespace StreetCorsa.Dialogs.Talk
{
    /// <summary>
    /// A talk in a window of its own: a call about an ad, a word with a racer at the diner or from the car. The other one
    /// begins as it opens; hanging up ends the talk (the person keeps what they make of it), and <paramref name="closed"/>
    /// runs after, for the screen to show a price the talk changed.
    /// </summary>
    public sealed class TalkDialogViewModel : BaseDialogViewModel
    {
        private readonly DialogService _dialogService;
        private readonly Action? _closed;

        public TalkDialogViewModel(DialogService dialogService, TalkViewModel talk, Action? closed = null)
        {
            _dialogService = dialogService;
            _closed = closed;
            Talk = talk;
            CloseCommand = new RelayCommand(() => _dialogService.CloseDialog());
        }

        public TalkViewModel Talk { get; }

        /// <summary>
        /// A talk in its window, a rival's with their portrait; with <paramref name="jumpQueue"/> over the dialog that is up,
        /// which comes back when the talk is over
        /// </summary>
        public static TalkDialogViewModel Show(DialogService dialogService, Conversation conversation, Action? closed = null, bool jumpQueue = false)
        {
            var portrait = conversation.Rival is { } rival ? RacersOut.PortraitOf(rival) : null;
            var dialog = new TalkDialogViewModel(dialogService, new TalkViewModel(conversation, portrait), closed);
            dialogService.ShowDialog(dialog, jumpQueue);
            return dialog;
        }

        /// <summary>"Hang up" on the phone, "So long" face to face</summary>
        public string CloseLabel => GameText.Say(Talk.Talk.Place == TalkPlace.Phone ? "chat.ui.hang_up" : "chat.ui.goodbye");

        public RelayCommand CloseCommand { get; }

        /// <summary>One talk with a person at a time</summary>
        public override string? DuplicateKey => $"talk:{Talk.Talk.Person.Key}";

        public override void OnOpened() => _ = Talk.OpenAsync();

        public override void OnClosed()
        {
            Talk.End();
            _closed?.Invoke();
        }
    }
}
