using StreetCorsa.Helpers;
using StreetCorsa.Services.Diner;
using StreetCorsa.Services.Text;
using StreetCorsa.ViewModels;

namespace StreetCorsa.Dialogs.RaceHub
{
    /// <summary>
    /// A diner on the racing hub's list: its name and class, a line about the place, who is in tonight, the stakes, and
    /// whether its regulars will race the player's car; its button drives there
    /// </summary>
    public sealed class DinerCardViewModel
    {
        public DinerCardViewModel(Models.Diners.Diner diner, DinerBook book, int racersIn, bool snubs, Action<string> go, Func<bool> canGo)
        {
            Diner = diner;
            Name = book.NameOf(diner);
            About = book.AboutOf(diner);
            RacersIn = racersIn;
            Snubs = snubs;
            GoCommand = new RelayCommand(() => go(diner.Id), canGo);
        }

        public Models.Diners.Diner Diner { get; }

        public string Id => Diner.Id;

        public string Name { get; }

        public string About { get; }

        public bool HasAbout => About.Length > 0;

        /// <summary>The cars the place takes: any at the first diner, from its floor up at the others</summary>
        public string ClassDisplay => Diner.IsFirst
            ? GameText.Say("racehub.diner.class.any")
            : GameText.Say("racehub.diner.class", ("power", Measure.Power(Diner.PowerFrom).ToString("N0", null)));

        /// <summary>How many regulars are out tonight</summary>
        public int RacersIn { get; }

        public string RacersInDisplay => RacersIn == 0 ? GameText.Say("racehub.diner.racers_in.none")
            : GameText.Say(RacersIn == 1 ? "racehub.diner.racers_in.one" : "racehub.diner.racers_in.other", ("count", RacersIn));

        /// <summary>The stakes are up here: so many times the house limits</summary>
        public bool HasStakes => Diner.Stakes > 1m;

        public string StakesDisplay => GameText.Say("racehub.diner.stakes", ("stakes", Diner.Stakes));

        /// <summary>The player's car is below the place's class: the regulars will not race it (the place is open all the same)</summary>
        public bool Snubs { get; }

        public string SnubDisplay => GameText.Say("racehub.diner.snub");

        public RelayCommand GoCommand { get; }
    }
}
