namespace StreetCorsa.Dialogs
{
    /// <summary>
    /// Whether a dialog is up, for code that has no <see cref="DialogService"/> at hand: the window-level key
    /// handlers of screens and controls, which must leave the keys alone while a dialog is in front
    /// </summary>
    public static class ModalState
    {
        /// <summary>True while the dialog service shows a dialog; set on the UI thread by the service only</summary>
        public static bool IsOpen { get; internal set; }
    }
}
