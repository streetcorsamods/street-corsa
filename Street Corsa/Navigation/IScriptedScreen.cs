using StreetCorsa.Services.Scripting;

namespace StreetCorsa.Navigation
{
    /// <summary>
    /// A screen whose view has the player's car on a 3D scene (the garage, the car lot, the gas station): the flow's own
    /// moments play there as the car's scripts, <c>screen.&lt;id&gt;.enter</c> once the screen is up and its own show is
    /// over (the screen tells <see cref="NavigationService.ScreenReady"/>), <c>screen.&lt;id&gt;.leave</c> as the player
    /// leaves it by a way out (the screen goes once the script lets it).
    /// </summary>
    public interface IScriptedScreen : IScreen
    {
        /// <summary>
        /// Set by the view while its scene is up: plays the script for a trigger with the screen's car, told the moment;
        /// done once the script lets the screen go on. Null when no script plays for it (or there is no scene).
        /// </summary>
        Func<string, ScriptFacts, Task?>? PlayScreenScript { get; set; }

        /// <summary>The moment, for a script: the game's clock, the light of the day</summary>
        ScriptFacts ScriptFacts { get; }

        /// <summary>A leave script played and the way out went nowhere after all: the car back where it stood</summary>
        void PutBackAfterLeave();
    }
}
