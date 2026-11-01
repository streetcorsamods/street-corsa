using StreetCorsa.Screens.Settings;

namespace StreetCorsa.Tests;

public class IconCreditsTests
{
    private static string IconsFolder => Path.Combine(RepoPaths.GameProject, "Assets", "Images", "Icons");

    [Fact]
    public void Every_icon_the_game_ships_has_its_row_and_every_row_its_icon()
    {
        var rows = IconCredits.Load();
        var icons = Directory.EnumerateFiles(IconsFolder, "*.png").Select(Path.GetFileNameWithoutExtension).Order().ToList();

        Assert.Equal(icons, rows.Select(r => r.Icon).Order().ToList());
        Assert.All(rows, r => Assert.True(r.Author.Length > 0 && r.Source.Length > 0 && r.Licence.Length > 0, r.Icon));
    }

    [Fact]
    public void The_built_in_list_is_the_file_beside_the_icons()
    {
        var file = IconCredits.Parse(File.ReadAllText(Path.Combine(IconsFolder, "credits.csv")));
        Assert.Equal(file, IconCredits.Load());
    }

    [Fact]
    public void The_line_names_each_site_with_its_licence_and_shows_a_set_credit_word_for_word()
    {
        var line = IconCredits.Line(IconCredits.Load());

        Assert.Contains("from www.freeicons.io (CC BY 3.0, https://creativecommons.org/licenses/by/3.0/)", line);
        Assert.Contains("Icon by dDara (https://www.magnific.com/icon/brake_950456)", line);
        Assert.Contains("Muhammad Shabraiz from Vecteezy.com", line);
        Assert.DoesNotContain("Street Corsa", line);
    }

    [Fact]
    public void Authors_of_one_site_and_licence_go_together_once_each()
    {
        var rows = IconCredits.Parse("""
            icon,author,source,licence,link,credit
            a,Ann,site.io,CC BY 3.0,,
            b,Bob,site.io,CC BY 3.0,,
            c,Ann,site.io,CC BY 3.0,,
            d,us,Street Corsa,CC BY 4.0,,
            e,Eve,other.com,its licence,,Icon by Eve (https://other.com/e)
            """);

        Assert.Equal(" · Icons by Ann and Bob from site.io (CC BY 3.0); Icon by Eve (https://other.com/e).", IconCredits.Line(rows));
        Assert.Equal("", IconCredits.Line(rows.Where(r => r.IsOurs).ToList()));
    }
}
