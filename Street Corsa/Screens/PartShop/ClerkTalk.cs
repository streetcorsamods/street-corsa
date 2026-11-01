using StreetCorsa.Logging;
using StreetCorsa.Screens.Shared;
using StreetCorsa.Services.Talk;
using StreetCorsa.ViewModels;

namespace StreetCorsa.Screens.PartShop
{
    /// <summary>
    /// Whoever is behind a shop's counter, talked to while the player's AI is on: the clerk of a part shop, the machinist
    /// of a machine shop. They look up as the player comes in (<see cref="Start"/>: greeted before the counter is opened,
    /// the button's dot calls for the player); one who never says a word (the AI on in Settings but not answering) is let
    /// go, and the counter goes on without the talk.
    /// </summary>
    public sealed class ClerkTalk : ObservableObject
    {
        private readonly IAppLogger _logger;
        private readonly string _who;

        /// <param name="who">Who they are in the log ("clerk", "machinist")</param>
        public ClerkTalk(IAppLogger logger, string who)
        {
            _logger = logger;
            _who = who;
        }

        private TalkViewModel? _talk;

        public TalkViewModel? Talk
        {
            get => _talk;
            private set
            {
                if (SetProperty(ref _talk, value)) OnPropertyChanged(nameof(HasTalk));
            }
        }

        public bool HasTalk => Talk != null;

        /// <summary>The talk's panel shows (the counter is open); set as the counter opens and closes</summary>
        public bool IsOpen
        {
            set
            {
                if (Talk != null) Talk.IsOpen = value;
            }
        }

        /// <summary>
        /// The talk starts, once: <paramref name="conversation"/> made only when the AI is there to answer, its panel open
        /// or not with the counter. Nothing happens while one is on.
        /// </summary>
        public void Start(Func<Conversation> conversation, string? portrait, bool isOpen)
        {
            if (Talk != null || !TalkDesk.Shared.IsAvailable) return;

            var made = conversation();
            if (!TalkDesk.Shared.Answers(made)) return;
            var talk = new TalkViewModel(made, portrait) { IsOpen = isOpen };
            Talk = talk;
            _ = talk.OpenOrDropAsync(() => Talk == talk, () =>
            {
                _logger.Information("The {Who}'s AI did not answer: the counter goes on without the talk", _who);
                Talk = null;
            });
        }

        /// <summary>The stock changed (a deal, a regular's discount, the work priced): they talk of what is there now</summary>
        public void StockChanged() => Talk?.Talk.StockChanged();

        /// <summary>The player has left the counter: the talk ends</summary>
        public void End() => Talk?.End();
    }
}
