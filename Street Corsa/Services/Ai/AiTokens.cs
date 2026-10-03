namespace StreetCorsa.Services.Ai
{
    /// <summary>
    /// How many tokens a text takes, as the game guesses it before asking: no model's own count, which differs from one
    /// to the next, but near enough to keep a request inside its context window. Ollama's real count is logged against it.
    /// </summary>
    public static class AiTokens
    {
        /// <summary>
        /// Characters a token, a little under what was measured (llama3.1 and gemma3 read about 3.25 of the game's English
        /// to a token), so the guess runs high rather than low
        /// </summary>
        public const double CharsPerToken = 3.0;

        /// <summary>What every message costs besides its words: the role and the template's marks around it</summary>
        public const int PerMessage = 8;

        /// <summary>Room kept free in the window besides the answer, for a guess that ran low</summary>
        public const int Margin = 256;

        public static int Estimate(string? text) => string.IsNullOrEmpty(text) ? 0 : (int)Math.Ceiling(text.Length / CharsPerToken);

        /// <summary>A message of the talk, with what it costs to send it</summary>
        public static int Estimate(AiTurn turn) => Estimate(turn.Text) + PerMessage;

        /// <summary>The whole request: the system prompt and every message sent</summary>
        public static int Estimate(AiTextRequest request) =>
            Estimate(request.System) + PerMessage + request.Messages.Sum(Estimate);
    }
}
