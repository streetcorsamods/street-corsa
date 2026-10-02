using StreetCorsa.Models.GameState;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.ViewModels;

namespace StreetCorsa.Dialogs.PartPreview
{
    /// <summary>
    /// A part for sale, to look at before it is bought: the part in 3D (with whatever is bolted on it, as it is sold),
    /// its name, its condition and the price line the seller gave. Buy only when there is something to buy it with;
    /// the dialog closes first and then the purchase runs, so whatever the purchase asks or tells comes after it.
    /// </summary>
    public sealed class PartPreviewDialogViewModel : BaseDialogViewModel
    {
        private readonly DialogService _dialogService;
        private readonly Action? _buy;
        private readonly Func<Services.Talk.Conversation>? _talk;
        private readonly Action? _talked;
        private string _priceLine;

        /// <param name="talk">The seller, to call about it; none when there is nobody to call</param>
        /// <param name="talked">After a call, for the screen behind</param>
        public PartPreviewDialogViewModel(DialogService dialogService, PartsCatalog catalog, PartInstance part, string title, string priceLine, Action? buy,
            Func<Services.Talk.Conversation>? talk = null, Action? talked = null)
        {
            _dialogService = dialogService;
            _buy = buy;
            _talk = talk;
            _talked = talked;
            _priceLine = priceLine;
            Catalog = catalog;
            Part = part;
            Title = title;

            var definition = catalog.Get(part.DefinitionId);
            PartName = definition == null ? part.DefinitionId : definition.DisplayName ?? definition.Name;
            Group = definition == null ? string.Empty : PartKinds.GroupName(PartKinds.GroupOf(definition));
            Condition = PartTrees.Describe(part);

            BuyCommand = new RelayCommand(OnBuy, () => _buy != null);
            CloseCommand = new RelayCommand(() => _dialogService.CloseDialog());
            CallCommand = new RelayCommand(OnCall, () => CanCall);
        }

        /// <summary>The seller can be called while the player's AI is on</summary>
        public bool CanCall => _talk != null && Services.Talk.TalkDesk.Shared.IsAvailable;

        public RelayCommand CallCommand { get; }

        /// <summary>The call over this preview; the preview comes back when it is over, with the price as it then is</summary>
        private void OnCall()
        {
            if (_talk == null) return;

            var conversation = _talk();
            Talk.TalkDialogViewModel.Show(_dialogService, conversation, () =>
            {
                if (conversation.Subject is { } subject) PriceLine = Helpers.Money.Text(subject.Price);
                _talked?.Invoke();
            }, jumpQueue: true, buy: _buy == null ? null : OnBuy);
        }

        public PartsCatalog Catalog { get; }

        public PartInstance Part { get; }

        /// <summary>What the dialog is headed: the ad's line, or the part's name</summary>
        public string Title { get; }

        /// <summary>The part as the catalog names it</summary>
        public string PartName { get; }

        /// <summary>Which shelf it goes on: "Carburetors"</summary>
        public string Group { get; }

        /// <summary>Its condition and what comes with it: "85%, with 3 more parts"</summary>
        public string Condition { get; }

        /// <summary>What it costs, as the seller puts it; empty for none</summary>
        public string PriceLine
        {
            get => _priceLine;
            private set
            {
                if (SetProperty(ref _priceLine, value)) OnPropertyChanged(nameof(HasPriceLine));
            }
        }

        public bool HasPriceLine => !string.IsNullOrWhiteSpace(PriceLine);

        /// <summary>The name is said under the title only when the title is something else</summary>
        public bool ShowsPartName => !string.Equals(PartName, Title, StringComparison.Ordinal);

        public bool CanBuy => _buy != null;

        public RelayCommand BuyCommand { get; }

        public RelayCommand CloseCommand { get; }

        /// <summary>The same part is not previewed twice at once</summary>
        public override string? DuplicateKey => $"part-preview:{Part.InstanceId}";

        private void OnBuy()
        {
            // Closed first: the purchase's own question and its answer come after this one, not under it
            _dialogService.CloseDialog();
            _buy?.Invoke();
        }
    }
}
