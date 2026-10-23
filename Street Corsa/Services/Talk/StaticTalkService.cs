using StreetCorsa.Services.Text;

namespace StreetCorsa.Services.Talk
{
    /// <summary>
    /// What racers say, from the mods' text (<see cref="GameText"/>, the "talk.*" pools): which pool fits the moment and
    /// the racer is decided here, the lines are the mods'
    /// </summary>
    public class StaticTalkService : ITalkService
    {
        /// <summary>Reputation apart for a racer to talk up or down to the player; pink slips are talked of at 15</summary>
        private static int RepGap => Rules.RuleBook.Current.Street.TalkReputationGap;
        private static int PinkSlipRepGap => Rules.RuleBook.Current.Street.TalkPinkSlipReputationGap;

        /// <summary>Aggression of a hothead; a bold one takes pink slips at 60</summary>
        private static int Aggressive => Rules.RuleBook.Current.Street.TalkAggressive;
        private static int Bold => Rules.RuleBook.Current.Street.TalkBold;

        private readonly Random _random = new();
        // The words given, or those of the content in play
        private readonly GameText? _given;
        private GameText _text => _given ?? GameText.Installed;

        public StaticTalkService(GameText? text = null)
        {
            _given = text;
        }

        public bool IsAvailable => true;

        public Task<string> GetMessageAsync(TalkContext context)
        {
            var lines = PoolsFor(context).SelectMany(_text.Lines).ToList();
            var values = new TextValues
            {
                ["car"] = CarOf(context),
                ["trackName"] = context.SelectedTrackName ?? GameText.Say("rivals.that_track")
            };
            return Task.FromResult(lines.Count == 0 ? string.Empty : GameText.Fill(lines[_random.Next(lines.Count)], values));
        }

        /// <summary>The car a line talks about: the grudge's, else the player's</summary>
        private static string CarOf(TalkContext context) =>
            context.Grudge != null
                ? context.GrudgeCarName ?? GameText.Say("rivals.car")
                : context.PlayerCar is { } playerCar ? Catalog.CarNames.Short(playerCar) : GameText.Say("rivals.car");

        /// <summary>The pools the lines are drawn from, all of them together</summary>
        private static List<string> PoolsFor(TalkContext context)
        {
            var repDiff = context.Opponent.Stats.Reputation - context.Player.Stats.Reputation;
            var aggressive = context.Opponent.Aggression >= Aggressive;

            return context.Trigger switch
            {
                TalkTrigger.OpponentSelected when context.Grudge != null => Grudge(context, aggressive),
                TalkTrigger.PulledUp when context.Grudge != null => Grudge(context, aggressive),
                TalkTrigger.PulledUp => PulledUp(context, aggressive),
                TalkTrigger.BetTypeChanged when context.IsPinkSlipBet && context.Grudge != null =>
                    ["talk.rematch", context.PlayerHasGrudgeCar ? "talk.rematch.has_car" : "talk.rematch.lost_car"],
                TalkTrigger.OpponentSelected => [Greeting(repDiff, aggressive)],
                TalkTrigger.TrackSelected => [aggressive ? "talk.track.aggressive" : "talk.track"],
                TalkTrigger.BetTypeChanged when context.IsPinkSlipBet => [PinkSlip(repDiff, context.Opponent.Aggression >= Bold)],
                TalkTrigger.ChallengeAccepted => [aggressive ? "talk.accepted.aggressive" : "talk.accepted"],
                TalkTrigger.ChallengeRejected => [repDiff > RepGap ? "talk.rejected.stronger" : "talk.rejected"],
                _ => [Greeting(repDiff, aggressive)]
            };
        }

        private static string Greeting(int repDiff, bool aggressive)
        {
            var standing = repDiff > RepGap ? "stronger" : repDiff < -RepGap ? "weaker" : "even";
            return $"talk.greeting.{standing}" + (aggressive ? ".aggressive" : "");
        }

        /// <summary>A rival who has pulled up beside the player's car in the street, window down</summary>
        private static List<string> PulledUp(TalkContext context, bool aggressive)
        {
            if (context.Opponent.IsBoss) return [Opponents.RacerWords.Key("talk.pulled_up.king", context.Opponent)];
            // Any racer may have lines of his own (RacerWords)
            string Own(string key) => Opponents.RacerWords.Key(key, context.Opponent);
            if (context.IsPinkSlipBet) return [Own("talk.pulled_up.pink_slip")];

            return aggressive ? [Own("talk.pulled_up"), Own("talk.pulled_up.aggressive")] : [Own("talk.pulled_up")];
        }

        /// <summary>A rival who lost a pink slip to the player, and wants a rematch</summary>
        private static List<string> Grudge(TalkContext context, bool aggressive)
        {
            var pools = new List<string> { "talk.grudge", context.PlayerHasGrudgeCar ? "talk.grudge.has_car" : "talk.grudge.lost_car" };
            if (aggressive) pools.Add("talk.grudge.aggressive");
            return pools;
        }

        private static string PinkSlip(int repDiff, bool bold) =>
            repDiff > PinkSlipRepGap ? "talk.pink_slip.stronger"
            : repDiff < -PinkSlipRepGap ? bold ? "talk.pink_slip.weaker.bold" : "talk.pink_slip.weaker"
            : "talk.pink_slip.even";
    }
}
