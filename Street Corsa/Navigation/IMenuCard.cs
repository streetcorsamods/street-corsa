namespace StreetCorsa.Navigation
{
    /// <summary>
    /// A main-screen card that has a say in how it goes: a screen of its own inside it to step back from first, or a
    /// question to ask before what is typed in it is lost
    /// </summary>
    public interface IMenuCard
    {
        /// <summary>Escape: the card steps back one screen, closing itself (through its back action) once it is on its first</summary>
        void StepBack();

        /// <summary>Another card opens in its place: <paramref name="go"/> runs once the card may go, at once or once the player agreed; never when not</summary>
        void Leave(Action go);
    }
}
