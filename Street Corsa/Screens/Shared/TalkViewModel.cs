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

        // The talk's token, kept: it is still asked for after End let go of its source (cancelled first, so it reads as such)
        private readonly CancellationToken _token;
        private readonly TalkLineViewModel _thinking = new(string.Empty, GameText.Say("chat.ui.thinking"), false);
        private Task _current = Task.CompletedTask;
        private bool _opened;
        private bool _ended;
        private int _pick;
        private bool _pickPending;
        private TalkSubject? _picked;
        private string _draft = string.Empty;
        private bool _isWaiting;

        public TalkViewModel(Conversation talk, string? portrait = null)
        {
            Talk = talk;
            _token = _cancel.Token;
            Portrait = portrait ?? TalkDesk.Shared.PortraitOf(talk.Person);
            SendCommand = new AsyncRelayCommand(SendAsync, () => CanSend, name: "Talk.Send");
            BuyCommand = new RelayCommand(OnBuy, () => CanBuy);
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
            TalkPlace.Shop or TalkPlace.MachineShop => "chat.ui.at_counter",
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

        /// <summary>The price of what is talked about now (the buyer's offer, the player selling); empty with nothing for sale in the talk</summary>
        public string PriceLine => Talk.Subject switch
        {
            BuyerSubject { IsGone: true } => string.Empty,
            BuyerSubject buyer => GameText.Say("chat.ui.offer_now", ("price", new Helpers.Money(buyer.Price))),
            { } subject => GameText.Say("chat.ui.price_now", ("price", new Helpers.Money(subject.Price))),
            _ => string.Empty
        };

        /// <summary>"Down from $1,600" once the seller has come down; "Up from $1,200" once a buyer has come up</summary>
        public string DealLine => Talk.Subject is { } subject && Talk.Deal is { } deal
            ? subject is BuyerSubject buyer
                ? !buyer.IsGone && buyer.Price > deal.Asked ? GameText.Say("chat.ui.offer_up", ("asked", new Helpers.Money(deal.Asked))) : string.Empty
                : subject.Price < deal.Asked ? GameText.Say("chat.ui.deal_down", ("asked", new Helpers.Money(deal.Asked))) : string.Empty
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

        /// <summary>What the box says before anything is typed: a clerk is asked for parts, everybody else about the thing and its price</summary>
        public string Hint => GameText.Say(Talk.Place == TalkPlace.Shop ? "chat.ui.hint_clerk" : "chat.ui.hint");

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

        /// <summary>
        /// The deal can be closed: a thing for sale can always be bought at its price as it stands (a shake the AI did not
        /// take in still leaves the button there); the player's own car is sold to a buyer only at a price agreed, the ad's
        /// own buttons being there for the rest
        /// </summary>
        public bool CanBuy => Buy != null && !_ended && Talk.Subject is { } subject && (subject is not BuyerSubject || Talk.IsAgreed);

        /// <summary>"Buy it for $1,325"; "Sell it for $1,325" to a buyer</summary>
        public string BuyLabel => Talk.Subject is { } subject
            ? GameText.Say(subject is BuyerSubject ? "chat.ui.sell" : "chat.ui.buy", ("price", new Helpers.Money(subject.Price)))
            : string.Empty;

        public RelayCommand BuyCommand { get; }

        /// <summary>They have said something in this talk: the AI is answering (none yet, and it may not be there at all)</summary>
        public bool HasAnswered { get; private set; }

        /// <summary>The other one begins (once)</summary>
        public Task OpenAsync()
        {
            if (_opened) return _current;
            _opened = true;
            return Run(() => Talk.OpenAsync(_token), "chat.ui.no_answer");
        }

        /// <summary>
        /// A car picked on the lot (null: none, back to the lot): asked about once it has stayed picked a moment and the
        /// answer before it is in; picked again before that, only the last is asked about
        /// </summary>
        public async Task PickAsync(TalkSubject? subject, TimeSpan settle)
        {
            var mine = ++_pick;
            _picked = subject;
            _pickPending = true;
            if (settle > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(settle, _token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }

            await WhenIdleAsync();
            if (mine != _pick || _ended) return;

            _pickPending = false;
            if (subject == null)
            {
                Talk.TurnAway();
                RefreshPrice();
                return;
            }

            if (Talk.Subject?.Thing == subject.Thing) return;

            // The talk is about it at once, before the salesman has gone on about it: Buy is for this car at its price
            Talk.Look(subject);
            RefreshPrice();
            await Run(() => Talk.TurnToAsync(subject, _token), null);
            RefreshPrice();
        }

        private async Task SendAsync()
        {
            var text = Draft.Trim();
            if (text.Length == 0 || IsWaiting || _ended) return;

            Draft = string.Empty;
            SettlePick();
            Lines.Add(new TalkLineViewModel(GameText.Say("chat.ui.you"), text, true));
            await Run(() => Talk.SayAsync(text, _token), "chat.ui.dead_line");
        }

        /// <summary>The thing the screen shows is the one bought, even when the talk has not turned to it yet</summary>
        private void OnBuy()
        {
            SettlePick();
            if (!CanBuy) return;

            Talk.Take();
            Buy?.Invoke();
        }

        /// <summary>
        /// A car picked and not yet talked about (the pick settling, or an answer still out when it was picked): the talk
        /// is about it now, unsaid, so what the player says or buys is for the car on screen; its pitch is not asked for
        /// </summary>
        private void SettlePick()
        {
            if (!_pickPending) return;
            _pickPending = false;
            _pick++;
            if (_picked == null) Talk.TurnAway();
            else Talk.Look(_picked);
            RefreshPrice();
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
            finally
            {
                _cancel.Dispose();
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
                HasAnswered = true;
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
