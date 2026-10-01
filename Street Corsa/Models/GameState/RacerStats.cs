namespace StreetCorsa.Models.GameState
{
    public class RacerStats
    {
        public int Wins { get; set; }
        public int Losses { get; set; }
        public int Races { get; set; }

        // Drag race specific stats
        public int DragWins { get; set; }
        public int DragLosses { get; set; }
        public int DragRaces { get; set; }

        // Road race specific stats
        public int RoadWins { get; set; }
        public int RoadLosses { get; set; }
        public int RoadRaces { get; set; }
        public decimal TotalEarnings { get; set; }
        public decimal TotalLosses { get; set; }
        public int PinkSlipsWon { get; set; }
        public int PinkSlipsLost { get; set; }
        public int CarsOwned { get; set; }
        public int CarsSold { get; set; }

        /// <summary>
        /// Reputation score (0-100) based on race performance
        /// Higher reputation indicates a more feared/respected racer. A new racer starts neutral.
        /// </summary>
        public int Reputation { get; set; } = 50;

        /// <summary>
        /// Bonus reputation earned from event victories
        /// Added on top of calculated reputation
        /// </summary>
        public int EventReputationBonus { get; set; }

        /// <summary>
        /// Races called off for jumping the start. No contest, so neither a win nor a loss, but everybody saw it:
        /// each one costs <see cref="FalseStartPenalty"/> reputation, up to <see cref="MaxFalseStartPenalty"/>
        /// </summary>
        public int FalseStarts { get; set; }

        public static int FalseStartPenalty => Rules.RuleBook.Current.Reputation.FalseStartPenalty;
        public static int MaxFalseStartPenalty => Rules.RuleBook.Current.Reputation.MaxFalseStartPenalty;

        /// <summary>Times the police caught this racer after a street race. The fines and the impound grow with it.</summary>
        public int PoliceBusts { get; set; }

        /// <summary>
        /// Times this racer got away from the police. The street hears about it: each one is worth
        /// <see cref="PoliceEscapeBonus"/> reputation, up to <see cref="MaxPoliceEscapeBonus"/>
        /// </summary>
        public int PoliceEscapes { get; set; }

        public static int PoliceEscapeBonus => Rules.RuleBook.Current.Reputation.PoliceEscapeBonus;
        public static int MaxPoliceEscapeBonus => Rules.RuleBook.Current.Reputation.MaxPoliceEscapeBonus;

        [LiteDB.BsonIgnore]
        public double WinRate => Races > 0 ? (double)Wins / Races : 0.0;

        [LiteDB.BsonIgnore]
        public decimal NetEarnings => TotalEarnings - TotalLosses;

        /// <summary>
        /// Calculate reputation based on current stats, by the rules' <c>reputation</c>: in the base game
        /// Base(50) + WinRate Bonus(0-30) + PinkSlip Bonus(0-15) - Loss Penalty(0-20), clamped to 0-100
        /// </summary>
        public int CalculateReputation()
        {
            var rules = Rules.RuleBook.Current.Reputation;
            var reputation = rules.Base;

            if (Races > 0)
            {
                // Win rate bonus: none at no wins, the whole of it at every race won
                reputation += (int)(WinRate * rules.WinRateBonus);

                // Pink slip wins are prestigious, and losses hurt, each up to their most
                reputation += Math.Min(PinkSlipsWon * rules.PinkSlipWin, rules.MaxPinkSlipWins);
                reputation -= Math.Min(PinkSlipsLost * rules.PinkSlipLoss, rules.MaxPinkSlipLosses);

                // A racer who keeps losing loses reputation faster; one who keeps winning is a star
                if (Races >= rules.LoserRaces && WinRate < rules.LoserWinRate) reputation -= rules.LoserPenalty;
                if (Races >= rules.StarRaces && WinRate > rules.StarWinRate) reputation += rules.StarBonus;
            }

            // Add event reputation bonus
            reputation += EventReputationBonus;

            // Jumping the start: no contest, but a racer known for it is not taken seriously
            reputation -= Math.Min(FalseStarts * FalseStartPenalty, MaxFalseStartPenalty);

            // Outrunning the police: the street talks about it
            reputation += Math.Min(PoliceEscapes * PoliceEscapeBonus, MaxPoliceEscapeBonus);

            // Clamp to valid range
            return Math.Clamp(reputation, 0, 100);
        }

        /// <summary>
        /// Get reputation tier description
        /// </summary>
        public string GetReputationTier()
        {
            return Reputation switch
            {
                >= 90 => "Legendary",
                >= 75 => "Elite",
                >= 60 => "Respected",
                >= 40 => "Average",
                >= 25 => "Struggling",
                _ => "Unknown"
            };
        }
    }
}
