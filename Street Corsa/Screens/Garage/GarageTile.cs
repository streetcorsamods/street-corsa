using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using StreetCorsa.Navigation;

namespace StreetCorsa.Screens.Garage
{
    /// <summary>
    /// One of the round buttons round the car, as the flow has it (<see cref="FlowTile"/>): its picture, its tooltip, what
    /// it does, and its size and place in the column of its spot
    /// </summary>
    public sealed class GarageTile
    {
        private const double SmallSize = 64;
        private const double LargeSize = 80;
        private const double SmallPadding = 16;
        private const double LargePadding = 18;

        /// <summary>The room between two tiles of a spot, one under the other</summary>
        private const double Gap = 16;

        public GarageTile(string id, FlowTile tile, string? text, ImageSource? icon, ICommand command, bool first)
        {
            Id = id;
            Text = text;
            Icon = icon;
            Command = command;
            Size = tile.IsLarge ? LargeSize : SmallSize;
            var padding = tile.Padding ?? (tile.IsLarge ? LargePadding : SmallPadding);
            Padding = new Thickness(padding);
            Margin = new Thickness(0, first ? 0 : Gap, 0, 0);
            IsRadio = string.Equals(tile.To, TileActions.Radio, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Its id in the flow</summary>
        public string Id { get; }

        /// <summary>Its tooltip</summary>
        public string? Text { get; }

        public ImageSource? Icon { get; }

        public ICommand Command { get; }

        /// <summary>Its width and height</summary>
        public double Size { get; }

        /// <summary>The room round its icon</summary>
        public Thickness Padding { get; }

        /// <summary>The room above it: none for the first of its spot</summary>
        public Thickness Margin { get; }

        /// <summary>The radio: the radio's toasts come out of it</summary>
        public bool IsRadio { get; }
    }
}
