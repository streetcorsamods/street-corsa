using StreetCorsa.Services.Station;

namespace StreetCorsa.Services.Scripting
{
    /// <summary>
    /// The stage a screen's scripts play on: its one car (by its role, and the script's own car when a step names
    /// none), the scene's marks and paths, the screen's curtain if it has one (none: a fade is done at once), and how its
    /// camera goes with the car (a screen whose camera is a seat ignores it)
    /// </summary>
    public sealed class SceneStage(IScriptCar car, string role, SceneMarks marks,
        Func<float, TimeSpan, FadeEase, Task>? fade = null, Action<CameraMode>? camera = null) : IScriptStage
    {
        public const string Player = "player";
        public const string Rival = "rival";

        public IScriptCar? Car(string? named) => named == null || named.Equals(role, StringComparison.OrdinalIgnoreCase) ? car : null;

        public MarkSpec? Mark(string name) => marks.Marks.TryGetValue(name, out var mark) ? mark : null;

        public IReadOnlyList<GroundPoint>? Path(string name) => marks.Paths.TryGetValue(name, out var path) ? path : null;

        public Task Fade(float opacity, TimeSpan over, FadeEase ease) => fade?.Invoke(opacity, over, ease) ?? Task.CompletedTask;

        public void Camera(IScriptCar of, CameraMode mode) => camera?.Invoke(mode);
    }
}
