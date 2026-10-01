using StreetCorsa.Parts.Cars;

namespace StreetCorsa.Tests;

/// <summary>The words of a race report's lines, for the tests that look for a line by what it says</summary>
internal static class ReportTexts
{
    public static IEnumerable<string> Texts(this ConditionReport report) => report.Select(l => l.Text);
}
