using System.ComponentModel;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Services.Content
{
    /// <summary>
    /// A step of a long wait, as a service reports it (<see cref="IProgress{T}"/>): a new step by its text key of
    /// ui_shell.json (its count unknown), or how far the step under way is (<see cref="Key"/> null)
    /// </summary>
    public readonly record struct LoadingStep(string? Key, int Done = 0, int Total = 0)
    {
        /// <summary>A new step, by its text key</summary>
        public static LoadingStep Named(string key) => new(key);

        /// <summary>How far the step under way is</summary>
        public static LoadingStep Counted(int done, int total) => new(null, done, total);
    }

    /// <summary>
    /// What a long wait is busy with, for the player: the step (a text key of ui_shell.json, said once) and, when the
    /// step knows how much it has to do, how far it is. Shown at the bottom of the title while the game starts and of
    /// the loader while a game's content is read (LoadingStatusView). Set from any thread; the bindings follow.
    /// </summary>
    public sealed class LoadingProgress : INotifyPropertyChanged
    {
        public static LoadingProgress Shared { get; } = new();

        /// <summary>
        /// The steps a service reports, onto <see cref="Shared"/>: handed to it only by a caller with a loader up, so a
        /// service called any other time tells nobody
        /// </summary>
        public static IProgress<LoadingStep> Reporter { get; } = new SharedReporter();

        // Straight from the reporting thread (no hop through the UI thread per car): the bindings follow from any thread
        private sealed class SharedReporter : IProgress<LoadingStep>
        {
            public void Report(LoadingStep value)
            {
                if (value.Key != null) Step(value.Key);
                else Count(value.Done, value.Total);
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private readonly object _lock = new();
        private string _text = string.Empty;
        private int _done;
        private int _total;

        /// <summary>The step under way, in the player's words; empty when nothing is</summary>
        public string Text { get { lock (_lock) return _text; } }

        /// <summary>How far the step is, 0 to 1; 0 while <see cref="IsIndeterminate"/></summary>
        public double Fraction { get { lock (_lock) return _total > 0 ? Math.Clamp((double)_done / _total, 0, 1) : 0; } }

        /// <summary>
        /// A step under way does not know how much it has to do: the bar runs without a measure. False when nothing is
        /// under way, so a hidden bar does not keep its endless animation (and WPF drawing frames) all session.
        /// </summary>
        public bool IsIndeterminate { get { lock (_lock) return _total <= 0 && _text.Length > 0; } }

        /// <summary>"120 / 552" while a step counts, else empty; written in the game's culture, as its other figures</summary>
        public string CountText
        {
            get
            {
                lock (_lock)
                    return _total > 0
                        ? string.Format(Helpers.Locale.Culture, "{0:N0} / {1:N0}", _done, _total)
                        : string.Empty;
            }
        }

        /// <summary>Something is under way</summary>
        public bool IsBusy { get { lock (_lock) return _text.Length > 0; } }

        /// <summary>A new step, by its text key; its count starts unknown</summary>
        public static void Step(string key) => Shared.Set(GameText.Say(key), 0, 0);

        /// <summary>How far the step under way is: <paramref name="done"/> of <paramref name="total"/></summary>
        public static void Count(int done, int total) => Shared.SetCount(done, total);

        /// <summary>Nothing under way any more</summary>
        public static void Clear() => Shared.Set(string.Empty, 0, 0);

        private void Set(string text, int done, int total)
        {
            lock (_lock)
            {
                if (_text == text && _done == done && _total == total) return;
                (_text, _done, _total) = (text, done, total);
            }

            Changed(nameof(Text), nameof(IsBusy), nameof(Fraction), nameof(IsIndeterminate), nameof(CountText));
        }

        // A count told for every car: the bar moves on whole percents, not 550 times
        private void SetCount(int done, int total)
        {
            lock (_lock)
            {
                if (_total == total && total > 0 && done < total && done * 100 / total == _done * 100 / total && done != 0) return;
                (_done, _total) = (done, total);
            }

            Changed(nameof(Fraction), nameof(IsIndeterminate), nameof(CountText));
        }

        private void Changed(params string[] names)
        {
            foreach (var name in names) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
