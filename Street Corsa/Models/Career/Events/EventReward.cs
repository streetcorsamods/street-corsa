using StreetCorsa.Services.Text;

namespace StreetCorsa.Models.Career.Events
{
    /// <summary>
    /// Defines rewards given for completing a race event
    /// </summary>
    public class EventReward
    {
        /// <summary>
        /// Cash reward amount
        /// </summary>
        public decimal Cash { get; set; }

        /// <summary>
        /// Reputation points gained
        /// </summary>
        public int Reputation { get; set; }

        /// <summary>
        /// Optional special item reward (e.g., rare part ID)
        /// </summary>
        public string? SpecialItem { get; set; }

        /// <summary>
        /// Description of the special item if any
        /// </summary>
        public string? SpecialItemDescription { get; set; }

        /// <summary>
        /// A field's purse, place by place (the first is the winner's): what each place takes of the pot, and the
        /// reputation it brings. Empty: the winner takes it all, as in a race of two. The special item is the winner's.
        /// </summary>
        public List<PlaceReward> Places { get; set; } = [];

        /// <summary>
        /// Create an empty reward
        /// </summary>
        public EventReward() { }

        /// <summary>
        /// Create a cash and reputation reward
        /// </summary>
        public EventReward(decimal cash, int reputation = 0)
        {
            Cash = cash;
            Reputation = reputation;
        }

        /// <summary>
        /// Create a reward with all parameters
        /// </summary>
        public EventReward(decimal cash, int reputation, string specialItem, string itemDescription)
        {
            Cash = cash;
            Reputation = reputation;
            SpecialItem = specialItem;
            SpecialItemDescription = itemDescription;
        }

        /// <summary>
        /// The reward as this game pays it: the cash scaled by the save's <see cref="GameState.GameRules.RacePrizeMultiplier"/>,
        /// to the nearest $5. A copy: the event's definition is shared by every game.
        /// </summary>
        public EventReward ScaledBy(double prizeMultiplier) => new()
        {
            Cash = Math.Round(GameState.GameRules.Scale(Cash, prizeMultiplier) / 5) * 5,
            Reputation = Reputation,
            SpecialItem = SpecialItem,
            SpecialItemDescription = SpecialItemDescription,
            Places = Places
        };

        /// <summary>
        /// What place <paramref name="place"/> (1, the winner) takes of a field's <paramref name="pot"/>: its share of it,
        /// or its own cash when it names one, to the nearest $5, and its reputation. Without <see cref="Places"/> the
        /// winner takes the pot and <see cref="Reputation"/>, the rest nothing. The special item is not in it.
        /// </summary>
        public EventReward ForPlace(int place, decimal pot)
        {
            if (Places.Count == 0)
                return place == 1 ? new EventReward(pot, Reputation) : new EventReward();
            if (place < 1 || place > Places.Count) return new EventReward();

            var mine = Places[place - 1];
            var cash = mine.Cash ?? pot * (decimal)Math.Clamp(mine.Share ?? 0, 0, 1);
            return new EventReward(Math.Max(0m, Math.Round(cash / 5) * 5), mine.Reputation);
        }

        /// <summary>
        /// Get a human-readable description of the reward
        /// </summary>
        public string GetDescription()
        {
            var parts = new List<string>();

            if (Cash > 0)
                parts.Add(GameText.Say("event.reward.cash", ("cash", Cash)));

            if (Reputation > 0)
                parts.Add(GameText.Say("event.reward.reputation", ("reputation", Reputation)));

            if (!string.IsNullOrEmpty(SpecialItemDescription))
                parts.Add(SpecialItemDescription);
            else if (!string.IsNullOrEmpty(SpecialItem))
                parts.Add(GameText.Say("event.reward.special", ("item", SpecialItem)));

            return parts.Count > 0 ? string.Join(GameText.Say("event.reward.and"), parts) : GameText.Say("event.reward.none");
        }
    }

    /// <summary>One place of a field's purse (<see cref="EventReward.Places"/>)</summary>
    public class PlaceReward
    {
        /// <summary>The share of the pot, 0 to 1</summary>
        public double? Share { get; set; }

        /// <summary>A sum of its own instead of a share (in the game's money, before the difficulty's scale)</summary>
        public decimal? Cash { get; set; }

        /// <summary>The reputation it brings; less than nothing takes some away (last place)</summary>
        public int Reputation { get; set; }
    }
}
