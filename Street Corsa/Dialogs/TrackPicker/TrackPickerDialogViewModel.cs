using StreetCorsa.Screens.Shared;
using StreetCorsa.Services.Text;
using StreetCorsa.ViewModels;

namespace StreetCorsa.Dialogs.TrackPicker
{
    /// <summary>
    /// Where the car goes on its own (a free run, a test-and-tune), picked from cards: a search box over a scrolling
    /// grid of the tracks, the one picked last time first and picked already. A click picks a card, a click on it again
    /// (or Go, or Enter) goes there; Back (Esc) goes back to where the picker was opened from. The picker closes before
    /// either answer runs: dialogs never nest.
    /// </summary>
    public sealed class TrackPickerDialogViewModel : BaseDialogViewModel
    {
        private readonly DialogService _dialogService;
        private readonly IReadOnlyList<PracticeTrack> _tracks;
        private readonly Dictionary<PracticeTrack, TrackPickerCardViewModel> _cards;
        private readonly PracticeTrack? _last;
        private readonly Action<PracticeTrack> _picked;
        private readonly Action _back;
        private bool _answered;

        private string _searchText = string.Empty;
        private IReadOnlyList<TrackPickerCardViewModel> _shown = [];
        private TrackPickerCardViewModel? _selected;

        /// <param name="tracks">The tracks to pick from, in the order they are shown</param>
        /// <param name="last">The one picked last time (or the one to offer first): shown first and picked already</param>
        /// <param name="picked">Where the car goes, once the picker has closed</param>
        /// <param name="back">Back where the picker was opened from, once it has closed</param>
        public TrackPickerDialogViewModel(
            DialogService dialogService,
            string title,
            string note,
            IReadOnlyList<PracticeTrack> tracks,
            PracticeTrack? last,
            Action<PracticeTrack> picked,
            Action back)
        {
            _dialogService = dialogService;
            Title = title;
            Note = note;
            _tracks = tracks;
            _last = last;
            _picked = picked;
            _back = back;
            _cards = tracks.ToDictionary(t => t, t => new TrackPickerCardViewModel(t, ReferenceEquals(t, last)));

            PickCommand = new RelayCommand<TrackPickerCardViewModel>(OnPick, card => card != null);
            GoCommand = new RelayCommand(OnGo, () => Selected != null);
            BackCommand = new RelayCommand(OnBack);

            Refilter();
            Selected = last != null && _cards.TryGetValue(last, out var lastCard) ? lastCard : null;
        }

        public string Title { get; }

        /// <summary>What the picker is for, over the cards</summary>
        public string Note { get; }

        public RelayCommand<TrackPickerCardViewModel> PickCommand { get; }
        public RelayCommand GoCommand { get; }
        public RelayCommand BackCommand { get; }

        /// <summary>The search, as it is typed: the cards narrow down with every letter</summary>
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value ?? string.Empty)) Refilter();
            }
        }

        /// <summary>The cards the search finds</summary>
        public IReadOnlyList<TrackPickerCardViewModel> Cards
        {
            get => _shown;
            private set
            {
                _shown = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasCards));
                OnPropertyChanged(nameof(CountDisplay));
            }
        }

        public bool HasCards => _shown.Count > 0;

        /// <summary>"12 of 340"</summary>
        public string CountDisplay => GameText.Say("trackpicker.count", ("shown", _shown.Count), ("all", _tracks.Count));

        /// <summary>The card picked; Go takes it</summary>
        public TrackPickerCardViewModel? Selected
        {
            get => _selected;
            private set
            {
                if (ReferenceEquals(_selected, value)) return;
                if (_selected != null) _selected.IsSelected = false;
                _selected = value;
                if (_selected != null) _selected.IsSelected = true;
                OnPropertyChanged();
                RelayCommand.RaiseCanExecuteChanged();
            }
        }

        private void Refilter()
        {
            Cards = PracticeTracks.Filter(_tracks, _searchText, _last).Select(t => _cards[t]).ToList();

            // What Enter would take must be in sight: a pick the search hid gives way to the first card found
            if (_selected != null && !_shown.Contains(_selected)) Selected = _shown.FirstOrDefault();
            else if (_selected == null && !string.IsNullOrWhiteSpace(_searchText)) Selected = _shown.FirstOrDefault();
        }

        private void OnPick(TrackPickerCardViewModel? card)
        {
            if (card == null) return;
            if (ReferenceEquals(card, Selected)) OnGo();
            else Selected = card;
        }

        private void OnGo()
        {
            if (Selected is not { } card || _answered) return;
            _answered = true;
            _dialogService.Withdraw(this);
            _picked(card.Track);
        }

        private void OnBack()
        {
            if (_answered) return;
            _answered = true;
            _dialogService.Withdraw(this);
            _back();
        }
    }
}
