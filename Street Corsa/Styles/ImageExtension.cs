using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Markup;

namespace StreetCorsa.Styles;

/// <summary>
/// One of the game's pictures in XAML, by name: <c>UriSource="{theme:Image backgrounds/main}"</c> on a BitmapImage,
/// or <c>Source="{theme:Image misc/license}"</c> on an Image, with
/// <c>xmlns:theme="clr-namespace:StreetCorsa.Styles"</c>. A mod's picture of that name stands in for the game's
/// (<see cref="ThemeImages"/>). A BitmapImage in a resource dictionary is still made only when first shown.
/// </summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class ImageExtension : MarkupExtension
{
    public ImageExtension()
    {
    }

    public ImageExtension(string name)
    {
        Name = name;
    }

    [ConstructorArgument("name")]
    public string Name { get; set; } = string.Empty;

    // The designer or the game: it does not change while the process runs
    private static readonly bool InDesigner = DesignerProperties.GetIsInDesignMode(new DependencyObject());

    public override object? ProvideValue(IServiceProvider serviceProvider)
    {
        // A Uri for a property that takes one (BitmapImage.UriSource); the picture itself for anything else, a Setter's
        // Value or an Image's Source alike
        var target = serviceProvider.GetService(typeof(IProvideValueTarget)) as IProvideValueTarget;
        var wantsUri = target?.TargetProperty switch
        {
            DependencyProperty property => typeof(Uri).IsAssignableFrom(property.PropertyType),
            PropertyInfo property => typeof(Uri).IsAssignableFrom(property.PropertyType),
            _ => false
        };
        if (InDesigner) return wantsUri ? ThemeImages.GameUri(Name) : null;
        return wantsUri ? ThemeImages.Uri(Name) : ThemeImages.Load(Name);
    }
}
