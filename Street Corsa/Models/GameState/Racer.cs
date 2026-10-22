namespace StreetCorsa.Models.GameState
{
    public class Racer(RacerType type, string name)
    {
        public RacerType Type { get; set; } = type;
        public string Name { get; set; } = name;
        public RacerStats Stats { get; set; } = new RacerStats();
        public List<Car> Cars { get; set; } = [];
        /// <summary>Loose parts on the shelf; a part keeps whatever was mounted on it when it came off</summary>
        public List<PartInstance> Parts { get; set; } = [];
        public decimal Money { get; set; } = 0m;
        public RacerStatus Status { get; set; } = RacerStatus.ReadyToRace;

        // Parameterless constructor for LiteDB
        public Racer() : this(RacerType.AI, "Unknown")
        {
        }
    }

    public class Player : Racer
    {
        public Guid? SelectedCarInstanceId { get; set; }

        /// <summary>
        /// The engine on the garage's engine stand, block and all that is on it; null while the stand is empty.
        /// The internals come out only here (<see cref="Parts.Cars.EngineWork"/>).
        /// </summary>
        public PartInstance? EngineStand { get; set; }

        /// <summary>Parts ordered by mail and not here yet (<c>NewspaperPartsService.BuyNew</c>)</summary>
        public List<PartOrder> PartOrders { get; set; } = [];

        /// <summary>Parts left at a machine shop and not back yet (<c>MachineShopService.DropOff</c>)</summary>
        public List<MachineOrder> MachineWork { get; set; } = [];

        /// <summary>Engine kits on the shelf, their parts among <see cref="Racer.Parts"/> (roadmap step 40)</summary>
        public List<KitCrate> Crates { get; set; } = [];

        /// <summary>Names of the mail-order parts that came since the player was last in the garage, to be told of there</summary>
        public List<string> Delivered { get; set; } = [];

        /// <summary>What the machine shops brought back since the player was last in the garage, and where it is, to be told of there</summary>
        public List<string> MachineReturned { get; set; } = [];

        /// <summary>What a new player starts with (the rules' <c>start.money</c>)</summary>
        public static decimal StartingMoney => Rules.RuleBook.Current.Start.Money;

        public Player(string name) : base(RacerType.Player, name)
        {
            Money = StartingMoney;
        }

        // Parameterless constructor for LiteDB
        public Player() : base(RacerType.Player, "Player")
        {
            Money = StartingMoney;
        }
    }
}
