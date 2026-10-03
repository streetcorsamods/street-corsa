using StreetCorsa.Rules;
using StreetCorsa.Services.Ai;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Services.Talk
{
    /// <summary>
    /// How much a talk sends with each line, as the player sets it (Settings' AI section, <see cref="Settings.AiSettings.TalkDetail"/>):
    /// a paid provider is billed by the token, a slow one reads every token. Light and normal cap what the rules give (the
    /// fact sheet, the turns, the memories); full is the rules as they are. It never changes what a person may say or how
    /// they haggle, only how much they are told.
    /// </summary>
    public static class TalkDetail
    {
        public const string Light = "light";
        public const string Normal = "normal";
        public const string Full = "full";

        public static IReadOnlyList<string> All { get; } = [Light, Normal, Full];

        /// <summary>A detail the game knows; anything else is full</summary>
        public static string Normalize(string? detail) =>
            All.FirstOrDefault(d => d.Equals(detail?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? Full;

        /// <summary>The most a detail sends: fact sheet tokens, turns, memories (full: no cap)</summary>
        public static (int FactTokens, int Turns, int Memories) Caps(string? detail) => Normalize(detail) switch
        {
            Light => (1500, 12, 4),
            Normal => (3000, 24, 6),
            _ => (int.MaxValue, int.MaxValue, int.MaxValue)
        };

        /// <summary><paramref name="rules"/> capped by the detail; the same rules at full</summary>
        public static TalkRules Apply(TalkRules rules, string? detail)
        {
            if (Normalize(detail) == Full) return rules;
            var (facts, turns, memories) = Caps(detail);
            return rules.With(Math.Min(rules.FactTokens, facts), Math.Min(rules.TurnsKept, turns), Math.Min(rules.MemoriesGiven, memories));
        }

        /// <summary>A turn of a talk, about: a line or two of the player's and the person's</summary>
        public const int TokensATurn = 45;

        /// <summary>
        /// The most a line of a talk sends, about (tokens): the persona and the longest talk prompt, a full fact sheet and the
        /// turns kept. Most lines send far less: a seller's sheet is a few hundred tokens
        /// </summary>
        public static int MostTokensALine(TalkRules rules, AiPrompts prompts, int? context = null)
        {
            var prompt = new[] { "chat_seller", "chat_salesman", "chat_rival", "chat_buyer" }
                .Select(use => prompts.Prompt(use))
                .Where(p => p != null)
                .Select(p => AiTokens.Estimate(p!.Prompt) + p.Examples.Sum(e => AiTokens.Estimate(e.Player) + AiTokens.Estimate(e.Answer)))
                .DefaultIfEmpty(0)
                .Max();
            var facts = context is { } window ? Math.Min(rules.FactTokens, window / 2) : rules.FactTokens;
            return AiTokens.Estimate(prompts.Persona) + prompt + facts + rules.TurnsKept * TokensATurn;
        }

        /// <summary>The detail's name in the player's words</summary>
        public static string Name(string detail) => GameText.Say($"settings.ai.detail.{Normalize(detail)}");
    }
}
