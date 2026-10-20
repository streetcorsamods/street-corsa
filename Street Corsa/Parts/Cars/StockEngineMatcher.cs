namespace StreetCorsa.Parts.Cars;

public sealed record EngineMatch(RatedBuild Build, double Score);

/// <summary>
/// Picks the engine build that best stands for the engine an Assetto Corsa car really has: same corporate
/// family first, then the words the names share (a 427 is a 427), then how close the power comes.
/// A suggestion, kept on the car's profile where it can be overruled.
/// </summary>
public static class StockEngineMatcher
{
    // The installed mods' figures, not the career's: the match is stored in catalog.db, which every career shares
    private static EngineMatchRules Figures => EngineRules.Program.Matcher;
    private static double SameFamily => Figures.SameFamily;
    private static double OtherFamily => Figures.OtherFamily;
    private static double SharedNumber => Figures.SharedNumber;
    private static double SharedWord => Figures.SharedWord;
    private static double PowerWeight => Figures.PowerWeight;
    private static double FactoryBuildBonus => Figures.FactoryBuildBonus;

    /// <param name="carId">The car's AC folder name: a make the mods give it (cars.json) gives its family</param>
    /// <param name="bhp">Power the car's own data claims, if any</param>
    public static List<EngineMatch> Rank(EngineBuildIndex index, string? carId, string brand, string name, double? bhp)
    {
        var carName = $"{brand} {name}";
        var family = CarBook.Current.FamilyOfCar(carId, brand, name);
        var tokens = MakeFamilies.Tokens(carName);

        return index.Runnable
            .Select(build => new EngineMatch(build, Score(build, family, tokens, bhp)))
            .OrderByDescending(m => m.Score)
            .ToList();
    }

    /// <summary>
    /// The best factory engine: a build of speed-shop parts is never one (it stays in <see cref="Rank"/>, to be picked
    /// by hand), nor one of another family while <see cref="EngineMatchRules.RefuseOtherFamily"/> holds: a car whose
    /// family has no build installed gets no engine (null) rather than the wrong one. A car of no known family takes
    /// the best there is.
    /// </summary>
    public static RatedBuild? Best(EngineBuildIndex index, string? carId, string brand, string name, double? bhp)
    {
        var family = CarBook.Current.FamilyOfCar(carId, brand, name);
        return Rank(index, carId, brand, name, bhp).FirstOrDefault(m => !m.Build.Build.Aftermarket && !IsForeign(m.Build, family))?.Build;
    }

    /// <summary>
    /// A build of another family than the car's, when both are known and the rules refuse one. A build's family is
    /// its block's; a build named for the car's own make (the Volvo Amazon's, of Ford six parts) is the car's own
    /// whatever its block.
    /// </summary>
    private static bool IsForeign(RatedBuild build, string family) =>
        Figures.RefuseOtherFamily && family.Length > 0 && build.Family.Length > 0 && family != build.Family
        && MakeFamilies.FamilyOf(build.Build.Name) != family;

    private static double Score(RatedBuild build, string family, IReadOnlySet<string> tokens, double? bhp)
    {
        var score = 0.0;
        if (family.Length > 0 && build.Family.Length > 0) score += family == build.Family ? SameFamily : OtherFamily;

        foreach (var token in tokens)
        {
            if (build.NameTokens.Contains(token)) score += char.IsDigit(token[0]) ? SharedNumber : SharedWord;
        }

        if (bhp is > 0 && build.PowerHp > 0) score -= PowerWeight * Math.Abs(Math.Log(build.PowerHp / bhp.Value));
        if (build.Build.Origin == EngineBuild.OriginCar) score += FactoryBuildBonus;
        return score;
    }

    /// <summary>The number in a power figure as car data writes it: "430bhp", "335 hp", "250 cv"</summary>
    /// <remarks>The one spec parser, <see cref="AcSpecs.ParsePower"/>: kept here for the callers that know it by this name</remarks>
    public static double? ParsePower(string? text) => AcSpecs.ParsePower(text);
}
