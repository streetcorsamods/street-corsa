namespace StreetCorsa.Dialogs
{
    /// <summary>
    /// Whether a dialog or an overlay (the radio's panel) is up, for code that has no <see cref="DialogService"/> at
    /// hand: the window-level key handlers of screens and controls, which must leave the keys alone while something is
    /// in front
    /// </summary>
    public static class ModalState
    {
        /// <summary>True while a dialog or an overlay is in front of the screen</summary>
        public static bool IsOpen => DialogOpen || OverlayOpen;

        /// <summary>True while the dialog service shows a dialog; set on the UI thread by the service only</summary>
        internal static bool DialogOpen { get; set; }

        /// <summary>True while the radio's panel is open; set on the UI thread by the panel only</summary>
        public static bool OverlayOpen { get; internal set; }
    }
}
