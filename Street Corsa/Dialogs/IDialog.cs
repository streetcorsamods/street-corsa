namespace StreetCorsa.Dialogs
{
    public interface IDialog
    {
        void OnOpened();
        void OnClosed();

        /// <summary>
        /// Two dialogs of the same type with the same key ask the same thing: the second is not shown while the
        /// first is up or waiting. Null for a dialog that is always shown.
        /// </summary>
        string? DuplicateKey { get; }

        /// <summary>A big dialog (the career): its card takes the width the dialog asks for, past the usual limit</summary>
        bool IsWide => false;
    }
}
