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
    public void The_line_names_each_site_with_its_licence_and_leaves_the_games_own_out()
    {
        var line = IconCredits.Line(IconCredits.Load());

        Assert.Contains("from www.freeicons.io (CC BY 3.0, https://creativecommons.org/licenses/by/3.0/)", line);
        Assert.Contains("by Delapouite from game-icons.net (CC BY 3.0", line);
        Assert.DoesNotContain("Street Corsa", line);
    }

    [Fact]
    public void Every_icon_is_under_an_open_licence()
    {
        // CC BY 3.0 or 4.0 (not NC, ND or SA), public domain, or the game's own CC BY 4.0: what a fork may ship as it is
        static bool Open(string licence) =>
            licence.StartsWith("CC BY 3.0,", StringComparison.Ordinal) || licence.StartsWith("CC BY 4.0,", StringComparison.Ordinal)
            || licence.Equals("public domain", StringComparison.OrdinalIgnoreCase);

        Assert.All(IconCredits.Load(), r => Assert.True(Open(r.Licence), $"{r.Icon}: {r.Licence}"));
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
