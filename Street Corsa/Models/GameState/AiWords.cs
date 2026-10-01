namespace StreetCorsa.Models.GameState
{
    /// <summary>
    /// Words the player's AI wrote for something with no place of its own for them (<see cref="GameState.AiWords"/>): a
    /// dealer's word on a car ("dealer_&lt;listing id&gt;"), an ad's blurb ("ad_&lt;key&gt;")
    /// </summary>
    public class AiWords
    {
        public string Key { get; set; } = string.Empty;

        public string Text { get; set; } = string.Empty;

        /// <summary>The language it was written in: used only while the game speaks it</summary>
        public string Language { get; set; } = string.Empty;

        /// <summary>Game time it was written</summary>
        public DateTime Date { get; set; }
    }
}
