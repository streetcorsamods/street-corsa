using System.Collections.ObjectModel;
using StreetCorsa.Logging;
using StreetCorsa.Models.GameState;
using StreetCorsa.Services.Talk;
using StreetCorsa.Services.Text;
using StreetCorsa.ViewModels;

namespace StreetCorsa.Screens.Shared
{
    /// <summary>One line of a talk as shown: who said it, and a note of the game's ("The line went dead.")</summary>
    public sealed class TalkLineViewModel(string who, string text, bool fromPlayer, bool isNote = false)
    {
        public string Who { get; } = who;

        public string Text { get; } = text;

        public bool FromPlayer { get; } = fromPlayer;

        public bool IsNote { get; } = isNote;

        public bool FromThem => !FromPlayer && !IsNote;
    }

    /// <summary>
    /// A talk as the talk panel shows it (<c>Controls/TalkPanel</c>), in a window or on a screen: the lines, what the
    /// player types, the "..." while the other one thinks, the price as it stands. One request at a time: the player
    /// waits for the answer before saying more, and a car picked on the lot while one is out is asked about after it,
    /// only the last one picked (<see cref="PickAsync"/>).
    /// </summary>
    public sealed class TalkViewModel : ObservableObject
    {
        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger("Talk");
        private readonly CancellationTokenSource _cancel = new();
        private readonly TalkLineViewModel _thinking = new(string.Empty, GameText.Say("chat.ui.thinking"), false);
        private Task _current = Task.CompletedTask;
        private bool _opened;
        private bool _ended;
        private int _pick;
        private string _draft = string.Empty;
        private bool _isWaiting;

        public TalkViewModel(Conversation talk, string? portrait = null)
        {
            Talk = talk;
            Portrait = portrait ?? TalkDesk.Shared.PortraitOf(talk.Person);
            SendCommand = new AsyncRelayCommand(SendAsync, () => CanSend, name: "Talk.Send");
            BuyCommand = new RelayCommand(() => Buy?.Invoke(), () => CanBuy);
            talk.Dealt += _ =>
            {
                RefreshPrice();
                PriceChanged?.Invoke();
            };
        }

        public Conversation Talk { get; }

        public ObservableCollection<TalkLineViewModel> Lines { get; } = [];

        /// <summary>Who the player talks to: "Big Walt Kroger", "Johnny "Flash" Reyes"</summary>
        public string Title => Talk.Rival is { Nickname.Length: > 0 } rival ? $"{Talk.Person.Name} \"{rival.Nickname}\"" : Talk.Person.Name;

        /// <summary>Where: on the phone, on the lot...</summary>
        public string Subtitle => GameText.Say(Talk.Place switch
        {
            TalkPlace.Phone => "chat.ui.on_phone",
            TalkPlace.Lot => "chat.ui.on_lot",
            TalkPlace.Curb => "chat.ui.at_curb",
            TalkPlace.Diner => "chat.ui.at_diner",
            _ => "chat.ui.on_street"
        });

        private bool _isOpen = true;

        /// <summary>The talk shown, or shut away by the player (on the lot, its tab); shut, it still goes on</summary>
        public bool IsOpen
        {
            get => _isOpen;
            set
            {
                if (SetProperty(ref _isOpen, value) && value) HasUnread = false;
            }
        }

        private bool _hasUnread;

        /// <summary>They said something while the talk was shut: the tab calls for the player until it is opened</summary>
        public bool HasUnread
        {
            get => _hasUnread;
            private set => SetProperty(ref _hasUnread, value);
        }

        /// <summary>Their portrait, when there is one: a rival's, a dealer's salesman's</summary>
        public string? Portrait { get; }

        public bool HasPortrait => !string.IsNullOrEmpty(Portrait);

        /// <summary>The price of what is talked about now; empty with nothing for sale in the talk</summary>
        public string PriceLine => Talk.Subject is { } subject ? GameText.Say("chat.ui.price_now", ("price", new Helpers.Money(subject.Price))) : string.Empty;

        /// <summary>"Down from $1,600" once the seller has come down</summary>
        public string DealLine => Talk.Subject is { } subject && Talk.Deal is { } deal && subject.Price < deal.Asked
            ? GameText.Say("chat.ui.deal_down", ("asked", new Helpers.Money(deal.Asked)))
            : string.Empty;

        /// <summary>The price changed in the talk: the screen shows the new one</summary>
        public event Action? PriceChanged;

        public string Draft
        {
            get => _draft;
            set
            {
                if (!SetProperty(ref _draft, value)) return;
                OnPropertyChanged(nameof(CanSend));
                OnPropertyChanged(nameof(ShowsHint));
            }
        }

        /// <summary>Nothing typed yet: the hint shows in the box</summary>
        public bool ShowsHint => Draft.Length == 0;

        /// <summary>The other one is thinking of an answer</summary>
        public bool IsWaiting
        {
            get => _isWaiting;
            private set
            {
                if (SetProperty(ref _isWaiting, value)) OnPropertyChanged(nameof(CanSend));
            }
        }

        public bool CanSend => !IsWaiting && !_ended && Draft.Trim().Length > 0;

        public AsyncRelayCommand SendCommand { get; }

        private Action? _buy;

        /// <summary>What buying the thing talked about is where the talk is (the screen's own purchase); none: no Buy</summary>
        public Action? Buy
        {
            get => _buy;
            set
            {
                _buy = value;
                OnPropertyChanged(nameof(CanBuy));
            }
        }

        /// <summary>A price is agreed: the deal can be closed</summary>
        public bool CanBuy => Buy != null && Talk.IsAgreed && !_ended;

        /// <summary>"Buy it for $1,325"</summary>
        public string BuyLabel => Talk.Subject is { } subject ? GameText.Say("chat.ui.buy", ("price", new Helpers.Money(subject.Price))) : string.Empty;

        public RelayCommand BuyCommand { get; }

        /// <summary>The other one begins (once)</summary>
        public Task OpenAsync()
        {
            if (_opened) return _current;
            _opened = true;
            return Run(() => Talk.OpenAsync(_cancel.Token), "chat.ui.no_answer");
        }

        /// <summary>
        /// A car picked on the lot (null: none, back to the lot): asked about once it has stayed picked a moment and the
        /// answer before it is in; picked again before that, only the last is asked about
        /// </summary>
        public async Task PickAsync(TalkSubject? subject, TimeSpan settle)
        {
            var mine = ++_pick;
            if (settle > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(settle, _cancel.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }

            await WhenIdleAsync();
            if (mine != _pick || _ended) return;

            if (subject == null)
            {
                Talk.TurnAway();
                RefreshPrice();
                return;
            }

            if (Talk.Subject?.Thing == subject.Thing) return;
            await Run(() => Talk.TurnToAsync(subject, _cancel.Token), null);
            RefreshPrice();
        }

        private async Task SendAsync()
        {
            var text = Draft.Trim();
            if (text.Length == 0 || IsWaiting || _ended) return;

            Draft = string.Empty;
            Lines.Add(new TalkLineViewModel(GameText.Say("chat.ui.you"), text, true));
            await Run(() => Talk.SayAsync(text, _cancel.Token), "chat.ui.dead_line");
        }

        /// <summary>The talk is over: nothing more is asked, and the person keeps what they make of it</summary>
        public void End()
        {
            if (_ended) return;
            _ended = true;
            OnPropertyChanged(nameof(CanSend));
            _cancel.Cancel();
            try
            {
                Talk.End();
            }
            catch (Exception ex)
            {
                _logger.Warning("Ending the talk with {Person} failed: {Error}", Talk.Person.Name, ex.Message);
            }
        }

        private async Task WhenIdleAsync()
        {
            while (!_current.IsCompleted) await _current;
        }

        private Task Run(Func<Task<string?>> ask, string? failKey)
        {
            var run = RunAsync(ask, failKey);
            _current = run;
            return run;
        }

        private async Task RunAsync(Func<Task<string?>> ask, string? failKey)
        {
            IsWaiting = true;
            Lines.Add(_thinking);
            string? said = null;
            try
            {
                said = await ask();
            }
            catch (Exception ex)
            {
                _logger.Warning("A talk with {Person} failed: {Error}", Talk.Person.Name, ex.Message);
            }
            finally
            {
                Lines.Remove(_thinking);
                IsWaiting = false;
            }

            if (_ended) return;
            if (said != null)
            {
                Lines.Add(new TalkLineViewModel(Talk.Person.Name, said, false));
                if (!IsOpen) HasUnread = true;
            }
            else if (failKey != null)
                Lines.Add(new TalkLineViewModel(string.Empty, GameText.Say(failKey), false, isNote: true));
        }

        private void RefreshPrice()
        {
            OnPropertyChanged(nameof(PriceLine));
            OnPropertyChanged(nameof(DealLine));
            OnPropertyChanged(nameof(CanBuy));
            OnPropertyChanged(nameof(BuyLabel));
            RelayCommand.RaiseCanExecuteChanged();
        }
    }
}
