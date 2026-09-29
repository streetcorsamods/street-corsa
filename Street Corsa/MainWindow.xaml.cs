using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using StreetCorsa.Dialogs;
using StreetCorsa.Navigation;

namespace StreetCorsa
{
    public partial class MainWindow : System.Windows.Window
    {
        public NavigationService NavigationService { get; }
        public DialogService DialogService { get; }

        // What had the keyboard on the screen last, to give it back when the dialogs are done. Weak: a screen left
        // behind must not stay in memory for the element of it that had the keyboard last.
        private WeakReference<IInputElement>? _screenFocus;

        public MainWindow()
        {
            InitializeComponent();

            var app = (App)System.Windows.Application.Current;
            NavigationService = app.NavigationService;
            DialogService = app.DialogService;

            DataContext = this;

            ScreenHost.PreviewGotKeyboardFocus += (_, e) => _screenFocus = e.NewFocus is { } focus ? new WeakReference<IInputElement>(focus) : null;
            DialogService.PropertyChanged += OnDialogChanged;
        }

        /// <summary>
        /// The keyboard goes into a dialog when it opens (the screen behind is disabled, and would otherwise keep it
        /// nowhere), and back to what had it on the screen once the last dialog closes. After the layout, so the
        /// dialog's buttons are there to take it.
        /// </summary>
        private void OnDialogChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(DialogService.CurrentDialog)) return;

            var dialog = DialogService.CurrentDialog;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                if (!ReferenceEquals(dialog, DialogService.CurrentDialog)) return;

                if (dialog != null)
                {
                    DialogHost.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
                }
                else if (_screenFocus?.TryGetTarget(out var focus) == true && focus is UIElement { IsVisible: true, IsEnabled: true } element
                         && ScreenHost.IsAncestorOf(element))
                {
                    Keyboard.Focus(element);
                }
            });
        }
    }
}
