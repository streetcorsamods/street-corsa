using System.Globalization;
using System.Text.RegularExpressions;
using StreetCorsa.Helpers;
using StreetCorsa.Mods;
using StreetCorsa.Rules;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Tests;

/// <summary>
/// The tests that change the game's locale, which every word the game writes reads: run on their own, after the
/// others, so no other test reads a currency or a unit one of them set
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LocaleCollection
{
    public const string Name = "Locale";
}

/// <summary>
/// Money, units, numbers and dates as the player reads them (step E4): in the game's currency and culture from its
/// game.json, in the units the game or the player picks; the text names what a number is
/// </summary>
[Collection(LocaleCollection.Name)]
public partial class LocaleTests : IDisposable
{
    private readonly CultureInfo _culture = Locale.Culture;
    private readonly MoneyStyle _money = Locale.Money;
    private readonly UnitSystem _units = Locale.Units;

    public void Dispose()
    {
        Locale.Culture = _culture;
        Locale.Money = _money;
        Locale.Units = _units;
    }

    [Fact]
    public void The_base_game_writes_whole_dollars_with_the_sign_in_front()
    {
        Assert.Equal("$1,234", Money.Text(1234m));
        Assert.Equal("-$1,234", Money.Text(-1234m));
        Assert.Equal("$12.50", Money.Text(12.5m, 2));
        Assert.Equal("$0", new Money(0).ToString());
    }

    [Fact]
    public void A_game_writes_its_own_currency_in_its_own_culture()
    {
        Locale.Culture = CultureInfo.GetCultureInfo("de-DE");
        Locale.Money = new MoneyStyle { Symbol = "€", After = true, Space = true, Decimals = 2 };

        Assert.Equal("1.234,50 €", Money.Text(1234.5m));
        Assert.Equal("-3,00 €", Money.Text(-3m));

        Locale.Culture = CultureInfo.GetCultureInfo("ja-JP");
        Locale.Money = new MoneyStyle { Symbol = "¥" };
        Assert.Equal("¥1,500,000", Money.Text(1_500_000m));
    }

    [Fact]
    public void A_figure_is_kept_metric_and_read_in_the_players_units()
    {
        Locale.Units = UnitSystem.Imperial;
        Assert.Equal(62.137, Measure.Speed(100).Value, 3);
        Assert.Equal(212, Measure.Temperature(100).Value, 6);
        Assert.Equal(1, Measure.Oil(0.946352946).Value, 6);
        Assert.Equal(1, Measure.Fuel(3.785411784).Value, 6);
        Assert.Equal(2.20462, Measure.Mass(1).Value, 5);
        Assert.Equal(100, Measure.SpeedFromMph(100).Value, 9);
        Assert.Equal("mph", Measure.Speed(1).Unit);

        Locale.Units = UnitSystem.Metric;
        Assert.Equal(160.934, Measure.SpeedFromMph(100).Value, 3);
        Assert.Equal(100, Measure.Speed(100).Value);
        Assert.Equal("kmh", Measure.Speed(1).Unit);
    }

    [Fact]
    public void A_figure_writes_its_units_words_from_the_text()
    {
        Locale.Units = UnitSystem.Imperial;
        Assert.Equal("104 mph", Measure.SpeedFromMph(104.2).ToString());
        Assert.Equal("212 °F", Measure.Temperature(100).ToString());
        Assert.Equal("1,243 mi", Measure.Distance(2000).ToString());

        Locale.Units = UnitSystem.Metric;
        Assert.Equal("167.69 km/h", Measure.SpeedFromMph(104.2).ToString("0.00", null));
        Assert.Equal("100 °C", Measure.Temperature(100).ToString());
        Assert.Equal("km/h", Measure.NameOf(Quantity.Speed));
    }

    [Fact]
    public void The_text_names_what_a_number_is()
    {
        Locale.Units = UnitSystem.Imperial;
        var values = new TextValues { ["price"] = 1500m, ["peak"] = 120.0, ["left"] = 7.57, ["tank"] = 60.0, ["kmh"] = 50, ["name"] = "Johnny" };

        Assert.Equal("$1,500 for Johnny", GameText.Fill("{price:money} for {name}", values));
        Assert.Equal("248 °F", GameText.Fill("{peak:temp}", values));
        Assert.Equal("2.0 of 16 gal", GameText.Fill("{left:fuel.number:0.0} of {tank:fuel:0}", values));
        Assert.Equal("a 31 mph hit", GameText.Fill("a {kmh:speed} hit", values));
        // A format that names no kind is a number format, as before
        Assert.Equal("1,500.00", GameText.Fill("{price:N2}", values));
        // A kind on a value that is no number writes the value
        Assert.Equal("Johnny", GameText.Fill("{name:money}", values));

        Locale.Units = UnitSystem.Metric;
        Assert.Equal("7.6 of 60 l", GameText.Fill("{left:fuel.number:0.0} of {tank:fuel:0}", values));
    }

    [Fact]
    public void Numbers_and_dates_are_written_in_the_games_culture()
    {
        Locale.Culture = CultureInfo.GetCultureInfo("es-ES");
        var values = new TextValues { ["x"] = 12345.5, ["when"] = new DateTime(1965, 6, 3) };

        Assert.Equal("12.345,5", GameText.Fill("{x:N1}", values));
        Assert.Equal("junio", GameText.Fill("{when:MMMM}", values));
        Assert.Equal("3 junio 1965", GameText.Date(new DateTime(1965, 6, 3), "format.date.day_month_year"));
    }

    [Fact]
    public void The_oil_short_reads_in_litres_in_metric()
    {
        Locale.Units = UnitSystem.Metric;
        Assert.Equal("a litre", StreetCorsa.Parts.Cars.GasStation.OilShort(0.7));
        Assert.Equal("6 litres", StreetCorsa.Parts.Cars.GasStation.OilShort(6.2));
    }

    [Fact]
    public void The_race_mode_gets_the_speeds_unit()
    {
        var text = GameText.Load(ModSet.Load(RepoPaths.Content));

        Locale.Units = UnitSystem.Metric;
        var lines = RaceText.IniLines(text).ToList();
        Assert.Contains("SPEED_PER_MPH=1.609344", lines);
        Assert.Contains("UNIT_SPEED=%7Bn%7D km%2Fh", lines);

        Locale.Units = UnitSystem.Imperial;
        Assert.Contains("SPEED_PER_MPH=1", RaceText.IniLines(text));
    }

    [Fact]
    public void The_base_games_locale_is_us_dollars_and_imperial()
    {
        var locale = GameFile.Load(ModSet.Load(RepoPaths.Content)).Locale;

        Assert.Equal("en-US", locale.Culture);
        Assert.Equal("$", locale.Money.Symbol);
        Assert.Equal(0, locale.Money.Decimals);
        Assert.Equal(UnitSystem.Imperial, locale.UnitSystem);
    }

    [Fact]
    public void A_locale_that_cannot_be_used_is_told_and_the_base_games_is_kept()
    {
        var problems = new List<string>();
        var locale = GameLocale.Checked(new GameLocale { Culture = "xx-NOPE-1", Units = "furlongs", Money = new MoneyStyle { Decimals = 9 } }, problems);

        Assert.Equal("en-US", locale.Culture);
        Assert.Equal("imperial", locale.Units);
        Assert.Equal(0, locale.Money.Decimals);
        Assert.Equal(3, problems.Count);
    }

    [Theory]
    [InlineData("qq-QQ")]
    [InlineData("usa")]
    [InlineData("en_US")]
    [InlineData("")]
    public void A_culture_dotnet_does_not_list_is_no_culture(string name) => Assert.Null(Locale.FindCulture(name));

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("ja-JP")]
    public void A_listed_culture_is_found(string name) => Assert.Equal(name, Locale.FindCulture(name)?.Name);

    [Fact]
    public void Money_reads_alike_however_the_text_names_it()
    {
        var values = new TextValues { ["price"] = new Money(1500m), ["raw"] = -1500m, ["litres"] = 7.570823456123 };

        Assert.Equal("$1,500", GameText.Fill("{price}", values));
        Assert.Equal("$1,500", GameText.Fill("{price:money}", values));
        Assert.Equal("1,500", GameText.Fill("{price:money.number}", values));
        Assert.Equal("-1,500", GameText.Fill("{raw:money.number}", values));
        Assert.Equal("$1,500.00", GameText.Fill("{price:N2}", values));
        // A fraction with no format, as older translations name the gas station's litres
        Assert.Equal("7.57 litros", GameText.Fill("{litres} litros", values));
    }

    [Fact]
    public void A_date_outside_the_cultures_calendar_still_writes()
    {
        Locale.Culture = CultureInfo.GetCultureInfo("ar-SA");
        Assert.Equal("0001-01-01", GameText.Date(default, "format.date"));
    }

    [Fact]
    public void The_race_mode_gets_the_games_decimal_mark()
    {
        var text = GameText.Load(ModSet.Load(RepoPaths.Content));

        Assert.Contains("DECIMAL=.", RaceText.IniLines(text));
        Locale.Culture = CultureInfo.GetCultureInfo("de-DE");
        Assert.Contains("DECIMAL=%2C", RaceText.IniLines(text));
        Assert.Contains("UNIT_SPEED_NAME=MPH", RaceText.IniLines(text));
    }

    [GeneratedRegex(@"\$\{")]
    private static partial Regex DollarPlaceholder();

    [GeneratedRegex(@"\{\w+:([\w.]+)(?::[^{}]*)?\}")]
    private static partial Regex KindPlaceholder();

    [Fact]
    public void No_text_writes_the_dollar_sign_itself_and_every_kind_it_names_is_known()
    {
        var wrong = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(RepoPaths.Content, "streetcorsa", "text"), "*.json", SearchOption.AllDirectories))
        {
            // The pools, not the _comment that tells of {name:format}
            var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file), documentOptions: new() { CommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true })!.AsObject();
            node.Remove("_comment");
            var json = node.ToJsonString();
            if (DollarPlaceholder().IsMatch(json)) wrong.Add($"{Path.GetFileName(file)}: a ${{...}}, which is {{...:money}} now");
            foreach (Match match in KindPlaceholder().Matches(json))
            {
                var kind = match.Groups[1].Value;
                if (!IsKnownKindOrFormat(kind)) wrong.Add($"{Path.GetFileName(file)}: {match.Value}");
            }
        }

        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }

    /// <summary>
    /// A kind ("money", "fuel.number"), or a .NET format a kind cannot be mistaken for: a standard one ("N0", "d"), a
    /// custom number ("0.0") or a date written in the date letters alone ("dddd", "MMMM"). A word of other letters
    /// ("mony", "tmep") is a kind misspelt, which would write itself as a .NET format.
    /// </summary>
    private static bool IsKnownKindOrFormat(string kind)
    {
        if (kind.EndsWith(".number", StringComparison.Ordinal)) return GameText.Kinds.ContainsKey(kind[..^7]);
        if (GameText.Kinds.ContainsKey(kind)) return true;
        if (StandardFormat().IsMatch(kind)) return true;
        return !kind.Any(char.IsLetter) || kind.All(c => !char.IsLetter(c) || "dMyhHmstfFgzK".Contains(c));
    }

    [GeneratedRegex(@"^[A-Za-z]\d{0,2}$")]
    private static partial Regex StandardFormat();

    [Theory]
    [InlineData("money", true)]
    [InlineData("fuel.number", true)]
    [InlineData("N0", true)]
    [InlineData("0.0", true)]
    [InlineData("dddd", true)]
    [InlineData("MMMM", true)]
    [InlineData("mony", false)]
    [InlineData("tmep", false)]
    [InlineData("fule.number", false)]
    public void A_misspelt_kind_is_told_from_a_format(string kind, bool known) =>
        Assert.Equal(known, IsKnownKindOrFormat(kind));

    [Fact]
    public void No_screen_writes_a_dollar_sign_or_a_unit_itself()
    {
        // The log keeps English: a line that goes to it is no screen's
        var pattern = new Regex(@"\$""[^""]*(\$\{|\} ?(km|mph|kg|HP|hp|Nm|litres|°C)\b)");
        var wrong = new List<string>();
        foreach (var file in Directory.EnumerateFiles(RepoPaths.GameProject, "*.cs", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains("CarCatalogEditor")))
        foreach (var (line, number) in File.ReadLines(file).Select((l, i) => (l, i + 1)))
        {
            if (pattern.IsMatch(line) && !line.Contains("_logger.") && !line.Contains("Logger.") && !line.Contains("problem"))
                wrong.Add($"{Path.GetFileName(file)}:{number}: {line.Trim()}");
        }

        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }
}
