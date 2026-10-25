using StreetCorsa.Mods;

namespace StreetCorsa.Tests;

/// <summary>
/// A made-up setting's career for the tests of the career services (the core has no event, milestone or victory of its
/// own): played over the core of the source tree as a setting is (<see cref="TestPlaces.Play"/>), so the services that
/// read the content in play find it. Only in <see cref="GlobalsCollection"/>.
/// </summary>
internal static class TestCareer
{
    public const string SettingId = "career_setting";

    public const string Victories = """
        { "victories": [
          { "id": "Legend", "name": "Local Legend", "description": "Reach 100 reputation.",
            "goal": [ { "type": "counter", "counter": "ReputationReached", "atLeast": 100, "step": "Reach {target} reputation", "todo": "Reach {target} reputation ({remaining} more needed)" } ],
            "progress": { "achieved": "Reputation: {ReputationReached}/100", "unlocked": "Reputation: {ReputationReached}/100", "locked": "Reputation: {ReputationReached}/100" } },
          { "id": "Crown", "name": "Take the Crown", "description": "Beat the boss in a pink slip race.",
            "unlock": [
              { "type": "counter", "counter": "TotalWins", "atLeast": 10, "step": "Win {target} races", "todo": "Win {target} races ({current}/{target})" },
              { "type": "counter", "counter": "ReputationReached", "atLeast": 50, "step": "Reach {target} reputation", "todo": "Reach {target} reputation ({current}/{target})" } ],
            "goal": [ { "type": "beatBoss", "step": "Beat the boss", "todo": "Beat the boss in a pink slip race" } ],
            "progress": { "achieved": "The crown is yours!", "unlocked": "The boss awaits", "locked": "Prove yourself ({TotalWins}/10 wins, {ReputationReached}/50 rep)" } },
          { "id": "Collector", "name": "Collector", "description": "Win 10 cars.",
            "goal": [ { "type": "counter", "counter": "PinkSlipWins", "atLeast": 10, "step": "Win {target} cars", "todo": "Win {remaining} more cars" } ],
            "progress": { "achieved": "Cars won: {PinkSlipWins}/10", "unlocked": "Cars won: {PinkSlipWins}/10", "locked": "Cars won: {PinkSlipWins}/10" } },
          { "id": "Domination", "name": "Domination", "description": "Beat every racer once.",
            "unlock": [ { "type": "counter", "counter": "ReputationReached", "atLeast": 75, "step": "Reach {target} reputation", "todo": "Reach {target} reputation ({current}/{target})" } ],
            "goal": [ { "type": "beatAll", "step": "Defeat all {total} opponents", "todo": "Defeat {remaining} more opponents" } ],
            "progress": { "achieved": "Domination!", "unlocked": "Opponents defeated: {defeated}/{total}", "locked": "Unlock at 75 reputation" } }
        ] }
        """;

    /// <summary>
    /// Seven events open to anybody (more than the paper shows at once), one that needs some reputation and one for Fords
    /// only
    /// </summary>
    public static string Events()
    {
        var open = Enumerable.Range(1, 7).Select(i =>
            $$"""{ "id": "open_{{i}}", "name": "Open {{i}}", "description": "Anybody.", "format": "{{(i % 2 == 0 ? "drag" : "circuit")}}", "minReputation": 0, "reward": { "cash": 100, "reputation": 1 }, "schedule": "Weekly" }""");
        const string known = """{ "id": "known_names", "name": "Known Names", "description": "Some reputation.", "format": "circuit", "minReputation": 30, "reward": { "cash": 500, "reputation": 5 }, "schedule": "Weekly" }""";
        const string fords = """{ "id": "ford_night", "name": "Ford Night", "description": "Fords only.", "format": "drag", "entryRequirements": { "type": "brand", "brand": "Ford" }, "minReputation": 0, "reward": { "cash": 300, "reputation": 2 }, "schedule": "Weekly" }""";
        return $$"""{ "events": [ {{string.Join(", ", open.Append(known).Append(fords))}} ] }""";
    }

    /// <summary>The core of the source tree and the made-up setting in <paramref name="temp"/>, the setting played</summary>
    public static ModSet Mods(TempDir temp)
    {
        temp.File($"{SettingId}/{ModSet.ManifestFileName}", $$"""{ "id": "{{SettingId}}", "type": "setting" }""");
        temp.File($"{SettingId}/victories/victories.json", Victories);
        temp.File($"{SettingId}/events/events.json", Events());
        return ModSet.Load(temp.Path, alsoFrom: [RepoPaths.BaseContent], setting: SettingId);
    }
}
