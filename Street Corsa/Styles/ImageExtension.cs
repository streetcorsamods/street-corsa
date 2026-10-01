using System.ComponentModel;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;

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
        var target = serviceProvider.GetService(typeof(IProvideValueTarget)) as IProvideValueTarget;
        var wantsPicture = target?.TargetProperty is DependencyProperty property && typeof(ImageSource).IsAssignableFrom(property.PropertyType);
        if (InDesigner) return wantsPicture ? null : ThemeImages.GameUri(Name);
        return wantsPicture ? ThemeImages.Load(Name) : ThemeImages.Uri(Name);
    }
}
