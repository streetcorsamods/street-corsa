using StreetCorsa.Parts.Cars;
using StreetCorsa.Parts.Export;
using StreetCorsa.Parts.Logic;

namespace StreetCorsa.Parts.Sounds;

/// <summary>The engine a sound is wanted for</summary>
/// <param name="BlockId">The engine block part; the pin key, and what keeps the choice the same race after race</param>
/// <param name="Cylinders">0 when unknown</param>
/// <param name="Family">gm, ford, mopar; empty when unknown</param>
/// <param name="LimiterRpm">Where the engine stops; 0 when unknown</param>
/// <param name="PartIds">Every part of the engine, the block among them; a pin on one of them (a hot cam, headers) beats the block's</param>
public sealed record SoundRequest(string BlockId, int Cylinders, string Family, double LimiterRpm, IReadOnlyList<string>? PartIds = null);

public sealed record SoundChoice(SoundEntry Sound, double Score, string Reason);

/// <summary>
/// Picks the sound for an engine. Cylinders decide (a six must not get a V8 bank), the family counts, and the
/// bank's rev ceiling must reach the limiter: below it by more than the margin the bank runs out of samples
/// and the engine sounds like a recording slowed down; above it, the tighter the better, because a bank made
/// for 8,500 rpm idles too low at 700. A curated sound beats a harvested one on equal terms. Among equals the
/// block id decides, always the same way, so a car sounds the same every race until its engine changes and
/// different blocks spread over equal sounds. A pin in sounds.json settles it before any of that: a pin on a part
/// the engine carries beats the block's pin, so a hot cam or headers can sound unlike the stock engine; between
/// two pinned parts the sound with the higher <see cref="SoundEntry.Tune"/> wins.
/// </summary>
public static class SoundMatcher
{
    /// <summary>How far below the limiter a bank's ceiling may sit before the sound is judged wrong (Content Manager's figure)</summary>
    public const double RevMargin = 500;

    private const double CylindersMatch = 3, CylindersUnknown = 1, CylindersMismatch = -6;
    private const double FamilyMatch = 2, FamilyUnknown = 0.5, FamilyMismatch = -1;
    private const double CeilingUnknown = -1.5, CeilingSurplusPerThousandRpm = 0.5, CeilingSurplusMax = 2;
    private const double CeilingShortMin = 2, CeilingShortMax = 4, CeilingShortSpan = 1500;
    private const double CuratedBonus = 1;

    public static SoundChoice? Choose(SoundLibrary library, SoundRequest request) =>
        Pinned(library, request) ?? Rank(library, request).FirstOrDefault();

    /// <summary>The sound the engine's own pins give it (a part's first, then the block's); null when nothing is pinned</summary>
    public static SoundChoice? Pinned(SoundLibrary library, SoundRequest request)
    {
        if (PinnedPart(library, request) is { } part) return part;

        if (library.Overrides.Pins.TryGetValue(request.BlockId, out var pinned) && library.Get(pinned) is { } pin)
            return new SoundChoice(pin, double.MaxValue, "pinned in " + SoundLibrary.SoundsJson);

        return null;
    }

    // The pinned part with the hottest sound; the part id breaks a tie, always the same way
    private static SoundChoice? PinnedPart(SoundLibrary library, SoundRequest request)
    {
        var best = (request.PartIds ?? Array.Empty<string>())
            .Where(p => !string.Equals(p, request.BlockId, StringComparison.OrdinalIgnoreCase))
            .Select(p => (Part: p, Sound: library.Overrides.Pins.TryGetValue(p, out var id) ? library.Get(id) : null))
            .Where(p => p.Sound != null)
            .OrderByDescending(p => p.Sound!.Tune)
            .ThenBy(p => p.Part, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        return best.Sound == null ? null : new SoundChoice(best.Sound, double.MaxValue, $"its {best.Part} pinned in {SoundLibrary.SoundsJson}");
    }

    /// <summary>Every sound of the library, best first</summary>
    public static IReadOnlyList<SoundChoice> Rank(SoundLibrary library, SoundRequest request) =>
        library.All.Select(sound => Score(sound, request))
            .OrderByDescending(c => c.Score)
            .ThenByDescending(c => Spread(request.BlockId, c.Sound.Id))
            .ToList();

    public static SoundChoice Score(SoundEntry sound, SoundRequest request)
    {
        var score = 0.0;
        var reasons = new List<string>();

        if (sound.Cylinders is { } cylinders && request.Cylinders > 0)
        {
            score += cylinders == request.Cylinders ? CylindersMatch : CylindersMismatch;
            reasons.Add(cylinders == request.Cylinders ? $"{cylinders} cyl" : $"{cylinders} cyl for a {request.Cylinders}");
        }
        else
        {
            score += CylindersUnknown;
            reasons.Add("cylinders unknown");
        }

        if (sound.Family.Length > 0 && request.Family.Length > 0)
        {
            var same = string.Equals(sound.Family, request.Family, StringComparison.OrdinalIgnoreCase);
            score += same ? FamilyMatch : FamilyMismatch;
            reasons.Add(same ? sound.Family : $"{sound.Family} for a {request.Family}");
        }
        else
        {
            score += FamilyUnknown;
            reasons.Add("family unknown");
        }

        if (sound.RpmMax is { } ceiling && request.LimiterRpm > 0)
        {
            var shortfall = request.LimiterRpm - ceiling;
            if (shortfall > RevMargin)
            {
                // Out of samples well before the limiter: the further, the worse, but never as bad as the wrong cylinders
                score -= CeilingShortMin + (CeilingShortMax - CeilingShortMin) * Math.Min(1, (shortfall - RevMargin) / CeilingShortSpan);
                reasons.Add($"runs out at {ceiling:0} rpm under a {request.LimiterRpm:0} limiter");
            }
            else if (shortfall > 0)
            {
                // A little short: within the margin, but an exact or higher ceiling is better
                score -= shortfall / RevMargin;
                reasons.Add($"{ceiling:0} rpm ceiling just under a {request.LimiterRpm:0} limiter");
            }
            else
            {
                score -= Math.Min(CeilingSurplusMax, -shortfall / 1000 * CeilingSurplusPerThousandRpm);
                reasons.Add($"{ceiling:0} rpm ceiling for a {request.LimiterRpm:0} limiter");
            }
        }
        else
        {
            score += CeilingUnknown;
            reasons.Add("ceiling unknown");
        }

        if (sound.Curated)
        {
            score += CuratedBonus;
            reasons.Add("curated");
        }

        return new SoundChoice(sound, score, string.Join(", ", reasons));
    }

    /// <summary>What an engine on the dyno asks for: its block, cylinders, family and limiter, and its parts for their pins</summary>
    public static SoundRequest RequestFor(PartsCatalog catalog, string blockId, EngineReport? report, IEnumerable<string>? partIds = null)
    {
        var block = catalog.Get(blockId);
        var cylinders = report?.Inputs?.Cylinders ?? 0;
        if (cylinders == 0 && block != null) cylinders = (int)block.Number("cylinders");

        var family = block == null ? "" : MakeFamilies.FamilyOf(block.DisplayName ?? block.Name);
        if (family.Length == 0) family = MakeFamilies.FamilyOf(PackOf(blockId));

        var limiter = report?.LimiterRpm ?? 0;
        if (limiter <= 0 && block != null) limiter = block.Number(Logic.EngineAssembly.Specs.RevLimit);

        return new SoundRequest(blockId, cylinders, family, limiter, partIds?.ToList());
    }

    /// <summary>
    /// The sound a car races with, or null when it should keep its own: no sound at all, or the choice is the bank the
    /// car already ships. The sound belongs to the engine, not to the car: an engine with a pinned sound (the game's
    /// own engines) takes it into any car, whatever bank the car ships. A car's own bank stands only for an engine
    /// with nothing pinned, when the car runs the block it came with (<paramref name="factoryBlock"/>). A sound that
    /// does not play (<paramref name="plays"/>: a bank silent in FMOD, or one it cannot open) is passed over at every
    /// step for the next.
    /// </summary>
    public static CarSound? ForCar(SoundLibrary library, SoundRequest request, string carId, out SoundChoice? choice,
        bool factoryBlock = false, Func<SoundEntry, bool>? plays = null)
    {
        plays ??= _ => true;

        choice = Pinned(library, request);
        if (choice != null && !plays(choice.Sound)) choice = null;

        if (choice == null && factoryBlock && library.All.FirstOrDefault(s => s.IsOwnSoundOf(carId)) is { } own && plays(own))
            choice = new SoundChoice(own, double.MaxValue, "the block it came with");

        // Lazily, best first: only as many sounds are asked whether they play as it takes to find one
        choice ??= Rank(library, request).FirstOrDefault(c => plays(c.Sound));

        if (choice == null || choice.Sound.IsOwnSoundOf(carId)) return null;
        return choice.Sound.ToCarSound();
    }

    // "engines/gm/GM_327_block" -> "gm", and so for a mod's "mymod:engines/gm/x"
    private static string PackOf(string partId)
    {
        var parts = Mods.ModIds.Local(partId).Split('/');
        return parts.Length > 2 ? parts[1] : "";
    }

    // FNV-1a over both ids: the same block always lands on the same sound, and blocks spread over equal sounds
    private static uint Spread(string blockId, string soundId) => Helpers.StableHash.Fnv(blockId + "\n" + soundId);
}
