using System.Text.Json.Nodes;
using StreetCorsa.Converters;
using StreetCorsa.Styles;

namespace StreetCorsa.Tests;

/// <summary>The 2026-11-12 tech audit's fixes worker G took: a mod's colour text read one way by the theme and the crews</summary>
public sealed class Audit1112GTests
{
    [Theory]
    [InlineData("sc#1")]
    [InlineData("ContextColor file://share/profile.icc 1,0,0,0")]
    [InlineData("#GGGGGG")]
    [InlineData("plaid")]
    [InlineData("  ")]
    public void Colour_text_that_does_not_read_is_none_for_the_theme_and_the_crews_alike(string text)
    {
        Assert.Null(Colours.TryParse(text));
        Assert.Null(ThemeFile.ParseColor(JsonValue.Create(text)));
        Assert.Null(ColourToBrushConverter.BrushOf(text));
    }

    [Fact]
    public void A_colour_reads_the_same_for_the_theme_and_the_crews()
    {
        var red = System.Windows.Media.Color.FromRgb(0xB2, 0x22, 0x22);

        Assert.Equal(red, Colours.TryParse(" #B22222 "));
        Assert.Equal(red, ThemeFile.ParseColor(JsonValue.Create("#B22222")));
        Assert.Equal(red, Assert.IsType<System.Windows.Media.SolidColorBrush>(ColourToBrushConverter.BrushOf("#B22222")).Color);
    }
}
