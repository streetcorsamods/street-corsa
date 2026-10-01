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
            // A unit's words: "{n} mph" in unit.mph, its name alone in unit.mph.name; a mod without them still shows the
            // figure, with the unit's id
            Helpers.Locale.UnitWords = (unit, number) =>
                GameText.Installed.Has($"unit.{unit}") ? GameText.Say($"unit.{unit}", ("n", number)) : $"{number} {unit}";
            Helpers.Locale.UnitName = unit => GameText.Installed.Has($"unit.{unit}.name") ? GameText.Say($"unit.{unit}.name") : unit;
        }
    }
}
