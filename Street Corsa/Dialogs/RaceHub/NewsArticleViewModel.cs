using StreetCorsa.Models.GameState;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Dialogs.RaceHub
{
    /// <summary>A piece of the racing hub's Street News; the first is the lead story, in bigger type</summary>
    public sealed class NewsArticleViewModel(NewsArticle article, DateTime today, bool isLead)
    {
        public string Headline { get; } = Services.Ai.AiWriter.Headline(article);
        public string Body { get; } = Services.Ai.AiWriter.Body(article);
        public bool IsLead { get; } = isLead;

        public string DateDisplay { get; } = (today.Date - article.Date.Date).Days switch
        {
            <= 0 => GameText.Say("paper.today"),
            1 => GameText.Say("paper.yesterday"),
            _ => GameText.Date(article.Date, "format.weekday_date")
        };
    }
}
