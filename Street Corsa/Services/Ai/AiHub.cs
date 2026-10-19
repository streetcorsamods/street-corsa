using StreetCorsa.Models.GameState;
using StreetCorsa.Services.Settings;

namespace StreetCorsa.Services.Ai
{
    /// <summary>
    /// The player's AI for the whole app, like <see cref="Audio.AudioHub"/> for sound: the text service, ComfyUI and what
    /// the game writes with them, all on the settings in use (read at every request, so a change in Settings takes at once)
    /// and the game's one HTTP client. Started by the app; until then (the tests, a harness) it is a hub with the AI off.
    /// </summary>
    public sealed class AiHub
    {
        private static AiHub _shared = new(() => new AiSettings(), () => null);

        public static AiHub Shared => _shared;

        public IAiText Text { get; }

        public IAiImages Images { get; }

        public AiWriter Writer { get; }

        private readonly AiTextService _text;
        private readonly ComfyImageService _images;

        // The shows playing (a car on the move in a scene): everything of the AI's that works this machine's card in the
        // background waits for the last to end
        private readonly AiShows _shows = new();

        public AiHub(Func<AiSettings> settings, Func<GameState?> currentGame, Action<Action>? onGameThread = null, Func<string, string>? carName = null)
        {
            Text = _text = new AiTextService(settings, AiHttp.Shared, shows: _shows);
            Images = _images = new ComfyImageService(settings, AiHttp.Shared, shows: _shows);
            Writer = new AiWriter(Text, Images, currentGame, onGameThread, carName);
        }

        /// <summary>A race starts or is over: a local model lets go of the GPU for it (<see cref="AiTextService.SetRacing"/>)</summary>
        public void SetRacing(bool racing) => _text.SetRacing(racing);

        /// <summary>
        /// A car starts moving in a scene, or stops: an AI on this machine is asked nothing of the background's meanwhile, no
        /// words, no picture, no model loaded again (<see cref="AiShows"/>)
        /// </summary>
        public void SetShowing(bool showing) => _shows.Set(showing);

        /// <summary>A text provider on this machine is writing an answer right now (a frame that came late says so in the log)</summary>
        public bool IsWriting => _text.InFlight > 0;

        /// <summary>A ComfyUI on this machine is painting a picture right now (a frame that came late says so in the log)</summary>
        public bool IsPainting => _images.InFlight > 0;

        /// <summary>The app's hub, from start-up on</summary>
        public static AiHub Start(Func<AiSettings> settings, Func<GameState?> currentGame, Action<Action> onGameThread, Func<string, string>? carName = null) =>
            _shared = new AiHub(settings, currentGame, onGameThread, carName);
    }
}
