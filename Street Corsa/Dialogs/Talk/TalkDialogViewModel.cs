using StreetCorsa.Screens.Shared;
using StreetCorsa.Services.Talk;
using StreetCorsa.Services.Text;
using StreetCorsa.ViewModels;

namespace StreetCorsa.Dialogs.Talk
{
    /// <summary>
    /// A talk in a window of its own: a call about an ad, a word with a racer at the diner or from the car. The other one
    /// begins as it opens; hanging up ends the talk (the person keeps what they make of it), and <paramref name="closed"/>
    /// runs after, for the screen to show a price the talk changed. With <paramref name="buy"/>, a price shaken on can be
    /// bought at once: the window closes first, then the purchase runs (after the dialog behind is back, if one was).
    /// </summary>
    public sealed class TalkDialogViewModel : BaseDialogViewModel
    {
        private readonly Action? _closed;
        private readonly Action? _buy;
        private bool _buying;

        public TalkDialogViewModel(DialogService dialogService, TalkViewModel talk, Action? closed = null, Action? buy = null)
            : base(dialogService)
        {
            _closed = closed;
            _buy = buy;
            Talk = talk;
            if (buy != null)
            {
                talk.Buy = () =>
                {
                    _buying = true;
                    Close();
                };
            }

            CloseCommand = new RelayCommand(Close);
        }

        public TalkViewModel Talk { get; }

        /// <summary>
        /// A talk in its window, a rival's with their portrait; with <paramref name="jumpQueue"/> over the dialog that is up,
        /// which comes back when the talk is over
        /// </summary>
        public static TalkDialogViewModel Show(DialogService dialogService, Conversation conversation, Action? closed = null, bool jumpQueue = false,
            Action? buy = null)
        {
            var portrait = conversation.Rival is { } rival ? RacersOut.PortraitOf(rival) : null;
            var dialog = new TalkDialogViewModel(dialogService, new TalkViewModel(conversation, portrait), closed, buy);
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

            // After the dialog queue has moved on: a dialog this one jumped is back up by then, and the purchase's own
            // questions come after it
            if (_buying && _buy != null) System.Windows.Application.Current?.Dispatcher.BeginInvoke(_buy);
        }
    }
}
