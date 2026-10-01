using System.Runtime.CompilerServices;

namespace StreetCorsa.Services.Text
{
    /// <summary>
    /// The code the tools share with the game says its words through a seam; in the game, the seam is the game's
    /// text, from the moment the program loads (the tests' too)
    /// </summary>
    internal static class GameTextHookup
    {
#pragma warning disable CA2255 // the program's own start, not a library's
        [ModuleInitializer]
#pragma warning restore CA2255
        internal static void PointTheSeamsAtTheText()
        {
            StreetCorsa.Parts.Cars.PartWords.Source = GameText.Say;
        }
    }
}
