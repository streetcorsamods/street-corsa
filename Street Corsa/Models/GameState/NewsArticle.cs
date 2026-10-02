namespace StreetCorsa.Models.GameState
{
    /// <summary>
    /// A piece in the newspaper, written from a race when it was settled (<see cref="Services.News.NewsWriter"/>):
    /// the paper prints the latest, the weightiest first
    /// </summary>
    public class NewsArticle
    {
        /// <summary>The day it happened, in game time</summary>
        public DateTime Date { get; set; }

        public string Headline { get; set; } = string.Empty;

        public string Body { get; set; } = string.Empty;

        /// <summary>How much of a story it is: the player's pink slips and the King lead the page, a rival's wreck is a filler</summary>
        public int Weight { get; set; }

        /// <summary>The piece written again by the player's AI (<see cref="Services.Ai.AiWriter"/>); null when it was not</summary>
        public string? AiHeadline { get; set; }

        public string? AiBody { get; set; }

        /// <summary>The language the AI wrote it in: shown only while the game speaks it, else the game's own piece</summary>
        public string? AiLanguage { get; set; }
    }
}
