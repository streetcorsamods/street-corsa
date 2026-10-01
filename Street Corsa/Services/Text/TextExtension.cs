using System.ComponentModel;
using System.Windows;
using System.Windows.Markup;

namespace StreetCorsa.Services.Text
{
    /// <summary>
    /// A word of the game's text in XAML: <c>Text="{t:Text garage.end_day}"</c>, with
    /// <c>xmlns:t="clr-namespace:StreetCorsa.Services.Text"</c>. It is the pool's first line in the player's language
    /// (<see cref="GameText.Say"/>), read as the view loads; the designer shows the key.
    /// </summary>
    [MarkupExtensionReturnType(typeof(string))]
    public sealed class TextExtension : MarkupExtension
    {
        public TextExtension()
        {
        }

        public TextExtension(string key)
        {
            Key = key;
        }

        [ConstructorArgument("key")]
        public string Key { get; set; } = string.Empty;

        // The designer or the game: it does not change while the process runs
        private static readonly bool InDesigner = DesignerProperties.GetIsInDesignMode(new DependencyObject());

        public override object ProvideValue(IServiceProvider serviceProvider) => InDesigner ? Key : GameText.Say(Key);
    }
}
