using StreetCorsa.Parts.Cars;
using StreetCorsa.Parts.Export;
using StreetCorsa.Parts.Logic;

namespace StreetCorsa.Parts.Sounds;

/// <summary>The engine a sound is wanted for</summary>
/// <param name="BlockId">The engine block part; the pin key, and what keeps the choice the same race after race</param>
/// <param name="Cylinders">0 when unknown</param>
/// <param name="Family">gm, ford, mopar; empty when unknown</param>
/// <param name="LimiterRpm">How far the engine is driven (its redline, or its cut when that comes first); 0 when unknown</param>
/// <param name="PartIds">Every part of the engine, the block among them; a pin on one of them (a hot cam, headers) competes with the block's</param>
public sealed record SoundRequest(string BlockId, int Cylinders, string Family, double LimiterRpm, IReadOnlyList<string>? PartIds = null);

public sealed record SoundChoice(SoundEntry Sound, double Score, string Reason);

/// <summary>
/// Picks the sound for an engine. Cylinders decide (a six must not get a V8 bank), the family counts, and the
/// bank's rev ceiling must reach the limiter: below it by more than the margin the bank runs out of samples
/// and the engine sounds like a recording slowed down; above it, the tighter the better, because a bank made
/// for 8,500 rpm idles too low at 700. A curated sound beats a harvested one on equal terms. Among equals the
/// block id decides, always the same way, so a car sounds the same every race until its engine changes and
/// different blocks spread over equal sounds. A pin in sounds.json settles it before any of that: the block's pin, or
/// one on a part the engine carries, so a hot cam or headers can sound unlike the stock engine. Between pins the sound
/// with the higher <see cref="SoundEntry.Tune"/> wins, a part's over the block's on equal tunes, so mild headers never
/// tame a hot block. A part's pin counts only on an engine its sound suits (cylinders and family, where both are
/// known): a part that fits any make (a carb, a blower) must not carry one make's sound into another's engine.
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
        Pins(library, request).FirstOrDefault(c => library.Plays(c.Sound)) ?? Rank(library, request).FirstOrDefault(c => library.Plays(c.Sound));

    /// <summary>The sound the engine's own pins give it; null when nothing is pinned</summary>
    public static SoundChoice? Pinned(SoundLibrary library, SoundRequest request) => Pins(library, request).FirstOrDefault();

    /// <summary>
    /// Every sound the engine's pins give it, best first: the higher tune, a part's before the block's on equal tunes,
    /// the part id after that, always the same way. A part pinned to a sound that does not suit this engine is left out.
    /// </summary>
    public static IEnumerable<SoundChoice> Pins(SoundLibrary library, SoundRequest request)
    {
        var parts = (request.PartIds ?? Array.Empty<string>())
            .Where(p => p != null && !string.Equals(p, request.BlockId, StringComparison.OrdinalIgnoreCase))
            .Select(p => (Part: p, IsBlock: false));
        var block = new[] { (Part: request.BlockId, IsBlock: true) }.Where(b => b.Part != null);

        return parts.Concat(block)
            .Select(p => (p.Part, p.IsBlock, Sound: library.Overrides.Pins.TryGetValue(p.Part, out var id) ? library.Get(id) : null))
            .Where(p => p.Sound != null && (p.IsBlock || Suits(p.Sound, request)))
            .OrderByDescending(p => p.Sound!.Tune)
            .ThenBy(p => p.IsBlock)
            .ThenBy(p => p.Part, StringComparer.OrdinalIgnoreCase)
            .Select(p => new SoundChoice(p.Sound!, double.MaxValue,
                p.IsBlock ? "pinned in " + SoundLibrary.SoundsJson : $"its {p.Part} pinned in {SoundLibrary.SoundsJson}"))
            .ToList();
    }

    // The cylinders and the family, where both sides say: what Score holds a sound to before anything else
    private static bool Suits(SoundEntry sound, SoundRequest request) =>
        (sound.Cylinders is not { } cylinders || request.Cylinders <= 0 || cylinders == request.Cylinders)
        && (sound.Family.Length == 0 || request.Family.Length == 0 || string.Equals(sound.Family, request.Family, StringComparison.OrdinalIgnoreCase));

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

        // The bank has to reach as far as the engine is driven, not the few rpm past its redline it stops at
        var limiter = report?.DrivenToRpm ?? 0;
        if (limiter <= 0 && block != null) limiter = block.Number(Logic.EngineAssembly.Specs.RevLimit);

        return new SoundRequest(blockId, cylinders, family, limiter, partIds?.ToList());
    }

    /// <summary>
    /// The sound a car races with, or null when it should keep its own: no sound at all, or the choice is the bank the
    /// car already ships. The sound belongs to the engine, not to the car: an engine with a pinned sound (the game's
    /// own engines) takes it into any car, whatever bank the car ships. A car's own bank stands only for an engine
    /// with nothing pinned that plays, when the car runs the block it came with (<paramref name="factoryBlock"/>). A
    /// sound that does not play (<paramref name="plays"/>, else the library's own <see cref="SoundLibrary.Plays"/>: a
    /// bank silent in FMOD, or one it cannot open) is passed over at every step for the next, pins included.
    /// </summary>
    public static CarSound? ForCar(SoundLibrary library, SoundRequest request, string carId, out SoundChoice? choice,
        bool factoryBlock = false, Func<SoundEntry, bool>? plays = null)
    {
        plays ??= library.Plays;

        // Lazily, best first: only as many sounds are asked whether they play as it takes to find one
        choice = Pins(library, request).FirstOrDefault(c => plays(c.Sound));

        if (choice == null && factoryBlock && library.All.FirstOrDefault(s => s.IsOwnSoundOf(carId)) is { } own && plays(own))
            choice = new SoundChoice(own, double.MaxValue, "the block it came with");

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
