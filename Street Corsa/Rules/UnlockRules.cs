namespace StreetCorsa.Rules;

/// <summary>
/// What a career of runs for the score opens as it goes (<c>"unlocks"</c> in the rules): the cars' tiers by the banked
/// score (<see cref="Services.Career.ChallengeCareer"/>), the medals a Challenge's road is worth, and the tier of a car
/// its setting gives none
/// </summary>
public sealed class UnlockRules
{
    /// <summary>
    /// The banked score each tier opens at, tier 1 first (<see cref="Parts.Cars.CarEntry.Tier"/>): <c>[0, 25000, 80000]</c>
    /// opens tier 1 from the start, tier 2 at 25,000 and tier 3 at 80,000; a tier past the list opens with its last. None:
    /// nothing is held back by score, every car is open.
    /// </summary>
    public long[] Tiers { get; set; } = [];

    /// <summary>
    /// A road's medals, each a share of its reference score: the road's distance at the speed its AI line is driven at,
    /// scored as the format scores (km × km/h × 10). The AI line is a racing driver's on an empty road, so a medal asks
    /// for a share of it (New Forest's line goes at 90 km/h, Highlands' long layout at 178).
    /// </summary>
    public double Bronze { get; set; } = 0.3;

    public double Silver { get; set; } = 0.42;

    public double Gold { get; set; } = 0.55;

    /// <summary>
    /// The tier of a car the setting puts in none (a car of the player's own), by its power to weight: below the first
    /// figure (hp per tonne) tier 1, below the second tier 2, and so on; above the last, the tier after it. A car whose
    /// figures cannot be read is put in the last.
    /// </summary>
    public double[] TierHpPerTonne { get; set; } = [180, 260, 330, 490];

    /// <summary>The medals rising and within reason; the tiers' scores and the bands not falling</summary>
    internal void Check(string path, List<string> problems)
    {
        Figures.Within(this, path, nameof(Bronze), 0.01, 5, problems);
        Figures.Within(this, path, nameof(Silver), 0.01, 5, problems);
        Figures.Within(this, path, nameof(Gold), 0.01, 5, problems);
        if (Silver < Bronze || Gold < Silver)
        {
            problems.Add(FormattableString.Invariant(
                $"{path}: the medals' shares are not bronze to gold rising ({Bronze}, {Silver}, {Gold}); the core's are used"));
            var core = new UnlockRules();
            (Bronze, Silver, Gold) = (core.Bronze, core.Silver, core.Gold);
        }

        // Read whole, never filled in: a list in the rules is taken as it is written
        if (Tiers.Length > Parts.Cars.CarEntry.LastTier)
        {
            problems.Add($"{path}.tiers names {Tiers.Length} tiers; a car's tier goes to {Parts.Cars.CarEntry.LastTier}, the rest are left out");
            Tiers = Tiers.Take(Parts.Cars.CarEntry.LastTier).ToArray();
        }

        for (var i = 0; i < Tiers.Length; i++)
        {
            var least = i == 0 ? 0 : Tiers[i - 1];
            if (Tiers[i] >= least) continue;
            problems.Add(FormattableString.Invariant($"{path}.tiers[{i}] ({Tiers[i]}) is below the tier before it; it opens with it ({least})"));
            Tiers[i] = least;
        }

        for (var i = 0; i < TierHpPerTonne.Length; i++)
        {
            var least = i == 0 ? 0 : TierHpPerTonne[i - 1];
            if (double.IsFinite(TierHpPerTonne[i]) && TierHpPerTonne[i] >= least) continue;
            problems.Add(FormattableString.Invariant($"{path}.tierHpPerTonne[{i}] ({TierHpPerTonne[i]}) is no figure above the one before it; the core's bands are used"));
            TierHpPerTonne = new UnlockRules().TierHpPerTonne;
            break;
        }
    }
}
