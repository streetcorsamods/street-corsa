using System.Windows.Media.Imaging;
using StreetCorsa.Logging;
using StreetCorsa.Mods;
using StreetCorsa.Rules;
using StreetCorsa.Screens.Shared;
using StreetCorsa.ViewModels;

namespace StreetCorsa.Screens.NewGame
{
    /// <summary>
    /// A setting installed, as the New Game screen lists it: its picture, its name and what it is, all the mod's own
    /// (mod.json), and the difficulty to start it on, the setting's presets and the figures it offers to adjust
    /// </summary>
    public sealed class SettingCard : ObservableObject
    {
        private GameRulesEditor? _rules;
        private bool _isConfigOpen;

        public SettingCard(ModInfo mod)
        {
            Mod = mod;
            Preview = Thumbnails.Load(mod.PreviewFile, 640);
        }

        public ModInfo Mod { get; }

        public string Name => string.IsNullOrWhiteSpace(Mod.Name) ? Mod.Id : Mod.Name;

        public string Description => Mod.Description;

        /// <summary>Its version and who made it: "0.1.0 · Street Corsa"</summary>
        public string Byline => string.Join("  ·  ", new[] { Mod.Version, Mod.Author }.Where(s => !string.IsNullOrWhiteSpace(s)));

        /// <summary>The mod's picture, null when it has none (the card shows the setting's name in its place)</summary>
        public BitmapImage? Preview { get; }

        public bool HasPreview => Preview != null;

        /// <summary>The difficulty and its figures are on show, under the card's row</summary>
        public bool IsConfigOpen
        {
            get => _isConfigOpen;
            set
            {
                if (SetProperty(ref _isConfigOpen, value)) OnPropertyChanged(nameof(OpenRules));
            }
        }

        /// <summary>The difficulty the career starts on, on the setting's presets: read the first time it is asked for</summary>
        public GameRulesEditor Rules => _rules ??= new GameRulesEditor(PresetsOf(Mod));

        /// <summary>
        /// <see cref="Rules"/> once the card is opened, null before: what the card's figures are bound to, so a card never
        /// opened never reads its setting's rules
        /// </summary>
        public GameRulesEditor? OpenRules => IsConfigOpen ? Rules : null;

        /// <summary>The difficulty presets of a setting's rules, read before its content is</summary>
        private static DifficultyRules PresetsOf(ModInfo setting)
        {
            try
            {
                return RuleBook.Of(ModSet.PlayedIn(setting.Id)).Difficulty;
            }
            catch (Exception ex)
            {
                AppLoggerFactory.CreateLogger(LogCategory.Save).Warning(ex, "The rules of the setting {Setting} could not be read for its presets", setting.Id);
                return RuleBook.Installed.Difficulty;
            }
        }
    }
}
