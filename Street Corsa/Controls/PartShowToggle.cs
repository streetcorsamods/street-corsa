using System.Windows.Media;
using StreetCorsa.Parts;
using StreetCorsa.Services.Text;
using StreetCorsa.ViewModels;

namespace StreetCorsa.Controls
{
    /// <summary>
    /// One of the parts view's buttons that put a kind of part out of sight (<see cref="PartShow"/>): the engine, the
    /// gearbox, the brakes, the springs and shocks. In sight, the button is lit; the car's viewport hides the parts of the
    /// kinds that are not.
    /// </summary>
    public sealed class PartShowToggle : ObservableObject
    {
        public PartShowToggle(PartShow show)
        {
            Show = show;
            Name = GameText.Installed.Has($"part_show.{show.Id}") ? GameText.Say($"part_show.{show.Id}") : show.Id;
            Tip = GameText.Say("garage.view.part_show.tip", ("kind", Name));
        }

        public PartShow Show { get; }

        public string Id => Show.Id;

        /// <summary>What the button stands for, said where its icon is missing</summary>
        public string Name { get; }

        public string Tip { get; }

        /// <summary>Read when a button first shows it (the theme's pictures are the running app's)</summary>
        public ImageSource? Icon => Show.Icon is { Length: > 0 } icon ? Styles.ThemeImages.Icon(icon, 128) : null;

        private bool _isShown = true;

        /// <summary>The parts of its kinds are in sight</summary>
        public bool IsShown
        {
            get => _isShown;
            set => SetProperty(ref _isShown, value);
        }

        /// <summary>The buttons of the mods' shows, every kind in sight</summary>
        public static IReadOnlyList<PartShowToggle> ForInstalled() =>
            PartGroups.Current.Shows.Select(s => new PartShowToggle(s)).ToList();
    }
}
