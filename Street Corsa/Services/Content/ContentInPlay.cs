using StreetCorsa.Logging;
using StreetCorsa.Mods;

namespace StreetCorsa.Services.Content
{
    /// <summary>
    /// The mods' content the game plays in, switched at run time. The intro, the main screen and Settings run on the
    /// core alone, read at start-up; a game is played in its setting, whose content (the core's, the setting's and the
    /// add-ons') is read and built when it starts or is loaded, behind a loader, and the core comes back when the game
    /// is left for the main screen. What is read from the content is kept in <see cref="ContentCache{T}"/>s, read
    /// again after a switch; what the services build from it is dropped by the steps given here.
    /// </summary>
    public sealed class ContentInPlay
    {
        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger(LogCategory.Startup);
        private readonly Func<ModSet, Task> _build;
        private readonly Action<ModSet> _onShow;

        /// <param name="build">
        /// Off the UI thread, once the content is in play: what the game reads and builds from it before it can be
        /// played (the parts catalog, the cars' prices, the books)
        /// </param>
        /// <param name="onShow">On the UI thread, once it is built: the look, the sounds, the problems told</param>
        public ContentInPlay(Func<ModSet, Task> build, Action<ModSet> onShow)
        {
            _build = build;
            _onShow = onShow;
        }

        /// <summary>A game's content is being read: true as it starts, false once it is done</summary>
        public event Action<bool>? LoadingChanged;

        /// <summary>A game's content, not the core alone, is in play</summary>
        public bool IsGame { get; private set; }

        // The loader is up: from a game's start until its first screen is on show (Shown), or it is given up (Drop)
        private bool _loading;

        /// <summary>
        /// Reads and builds the content of a game in <paramref name="setting"/> (by id; the first installed for none or
        /// one not installed), the loader up meanwhile and after, until the game's first screen is on show
        /// (<see cref="Shown"/>). False when it could not: the core is in play again and the loader down.
        /// </summary>
        public async Task<bool> PlayAsync(string? setting)
        {
            SetLoading(true);
            try
            {
                var mods = await Task.Run(async () =>
                {
                    var played = ModSet.PlayedIn(setting);
                    Use(played);
                    await _build(played);
                    return played;
                });

                IsGame = true;
                _onShow(mods);
                _logger.Information("Playing in {Setting}: {Mods}", mods.Setting?.ToString() ?? "the bare core", string.Join(", ", mods.Mods));
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "The content of the setting {Setting} could not be read", setting ?? "(none)");
                Drop();
                return false;
            }
        }

        /// <summary>A screen is on show: the loader of a game's start comes down over it</summary>
        public void Shown() => SetLoading(false);

        /// <summary>
        /// A game's start given up after its content was read (it could not be made or opened, the player backed out):
        /// the core in play again, the loader down
        /// </summary>
        public void Drop()
        {
            PlayCore();
            SetLoading(false);
        }

        private void SetLoading(bool loading)
        {
            if (_loading == loading) return;
            _loading = loading;
            LoadingChanged?.Invoke(loading);
        }

        /// <summary>The core alone in play again: a game left for the main screen. Nothing is done when it already is.</summary>
        public void PlayCore()
        {
            if (!IsGame && ReferenceEquals(ModSet.Installed, _core)) return;

            var core = _core ??= ModSet.CoreOf(ModSet.DefaultRoot);
            Use(core);
            IsGame = false;
            _onShow(core);
            _logger.Information("The core alone is in play again");
        }

        // The core as the start-up read it: the main screen's content does not change while the game runs
        private ModSet? _core;

        /// <summary>The core the start-up plays, kept to come back to</summary>
        public void StartWithCore()
        {
            _core = ModSet.Installed;
        }

        private static void Use(ModSet mods)
        {
            ModSet.Play(mods);
            StreetCorsa.Parts.Cars.CarBook.Current = null;
        }
    }
}
