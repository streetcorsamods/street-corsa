namespace StreetCorsa.Models.GameState
{
    public class Car
    {
        public Guid InstanceId { get; set; }
        public string DefinitionId { get; set; } // Reference to catalog (AC car folder)
        public string SkinId { get; set; } = "default"; // Selected skin/livery

        /// <summary>
        /// The colour the car was painted in the paint booth, #RRGGBB, over its skin's own (Looks.CarPaint); null in its
        /// skin's colours
        /// </summary>
        public string? PaintColour { get; set; }

        // Condition & Mileage (Street Rod style)
        public double OdometerKM { get; set; }
        public double EngineHealth { get; set; } // 0.0 to 1.0
        public double TransmissionHealth { get; set; } // 0.0 to 1.0
        public double BodyCondition { get; set; } // 0.0 to 1.0
        public double TireCondition { get; set; } // 0.0 to 1.0

        /// <summary>
        /// Body damage by zone as Assetto Corsa keeps it: front, rear, left, right, each the collision speed in km/h
        /// the zone has taken. It goes back into AC at the start of every race, scratches and dents with it, and
        /// only a body shop takes it off (<see cref="Parts.Cars.CarCondition"/>).
        /// </summary>
        public double[] BodyDamageKmh { get; set; } = new double[4];

        /// <summary>
        /// The fuel in the tank, litres, as its last race left it; null for a full tank (a car that has not raced since
        /// it was bought or filled up). It goes into the next race as it is, and the gas station fills it up for money.
        /// </summary>
        public double? FuelLitres { get; set; }

        /// <summary>What the tank holds, litres, as AC reported it after a race; null until the car has raced</summary>
        public double? FuelTankLitres { get; set; }

        /// <summary>The body's dirt, 0 clean to 1 filthy: it builds up race after race, and a wash takes it off</summary>
        public double BodyDirt { get; set; }

        /// <summary>
        /// Rust through the paint, 0 none to 1 a body more rust than steel: it comes with a used car, grows a little every
        /// day, faster the more there is, and a body shop takes it off (<see cref="Parts.Cars.CarLooks"/>)
        /// </summary>
        public double Rust { get; set; }

        /// <summary>Worn paint, 0 as new to 1 chipped, scratched and faded all over; a respray takes it off with the rust</summary>
        public double PaintWear { get; set; }

        /// <summary>A cracked windscreen, 0 sound to 1 cracked right across: from a hit at the front, or as the car came</summary>
        public double GlassCracks { get; set; }

        /// <summary>
        /// Which rust and which scratches the car has (the pattern its wear layer is drawn with): the same for the car in
        /// every scene and race, carried over from its ad when it is bought; 0 on a car from an older save (its id stands in)
        /// </summary>
        public int LookSeed { get; set; }

        /// <summary>
        /// The oil in the engine, 1 full to 0 dry; null for full (a car that has not raced since it was bought or topped
        /// up). Every race burns a little, a worn engine more; the gas station tops it up
        /// (<see cref="Parts.Cars.CarCondition.OilLeft"/>).
        /// </summary>
        public double? OilLevel { get; set; }

        /// <summary>
        /// Parts mounted on the car itself, each with everything that is mounted on it in turn.
        /// <see cref="PartInstance.ParentSlot"/> says where on the car: the engine goes on <see cref="PartInstance.CarEngineSlot"/>.
        /// </summary>
        public List<PartInstance> Parts { get; set; }

        /// <summary>
        /// False until the car has been given the parts it left the factory with. Tells a car that never had
        /// any (an older save) from one whose engine has been pulled.
        /// </summary>
        public bool HasPartsAssigned { get; set; }

        /// <summary>False until the car has been given the wheels, brakes, springs and shocks it left the factory with</summary>
        public bool HasRunningGearAssigned { get; set; }

        /// <summary>
        /// What its running gear has been brought up to (<see cref="StreetCorsa.Parts.Cars.RunningGearFactory.GearVersion"/>):
        /// 0 for a car of an older save, which gets its rear axle and its period brakes once (step 31), 1 one that gets its
        /// leaf springs, torsion bars and sway bars once, 2 one whose rear brakes, springs, shocks and bar go onto the rear
        /// axle once, 3 one whose leaf springs (and an independent rear's running gear) go back onto the car's own slots;
        /// what the player took off or put on since stays as it is (moved with the rear axle's)
        /// </summary>
        public int GearVersion { get; set; }

        /// <summary>Whether the car has been given the rear axle it left the factory with (step 31 on)</summary>
        [LiteDB.BsonIgnore]
        public bool HasAxleAssigned => GearVersion >= 1;

        /// <summary>Whether the car has been given the sway bars it left the factory with (step 31 phase 3 on)</summary>
        [LiteDB.BsonIgnore]
        public bool HasSwayBarsAssigned => GearVersion >= 2;

        [LiteDB.BsonIgnore]
        public PartInstance? Engine => Parts.FirstOrDefault(p => p.ParentSlot == PartInstance.CarEngineSlot);

        /// <summary>
        /// Horsepower on the dyno, as the car was last looked at: what its engine makes, tuning and wear included. Null
        /// until somebody has put it on the dyno; 0 when it does not run. The rivals choose, tune and race by it.
        /// </summary>
        public double? PowerHp { get; set; }

        // Purchase/Sale Info
        public decimal PurchasePrice { get; set; }

        /// <summary>
        /// The car is in the police impound (its owner was busted after a street race), and can be collected from
        /// this date on, in game time, for <see cref="ImpoundFee"/>. Null when it is not impounded. An impounded car
        /// goes nowhere: it does not race, run or sell until it is collected.
        /// </summary>
        public DateTime? ImpoundedUntil { get; set; }

        /// <summary>What collecting the car from the impound costs; 0 when it is not impounded</summary>
        public decimal ImpoundFee { get; set; }

        [LiteDB.BsonIgnore]
        public bool IsImpounded => ImpoundedUntil != null;

        /// <summary>
        /// When the car changed hands, in game time (1970s). Whoever creates the car sets it from the game's
        /// date; the real clock has no place in the game world.
        /// </summary>
        public DateTime PurchaseDate { get; set; }

        /// <summary>
        /// Which of the player's warehouses the car is kept in, from 1, and its bay there, from 0; null until the car lot
        /// has found it a place (a car just bought, an older save). A car keeps its place until it changes hands
        /// (<see cref="ClearPlace"/>, <see cref="Services.CarLot.CarStorage"/>).
        /// </summary>
        public int? Warehouse { get; set; }

        /// <inheritdoc cref="Warehouse"/>
        public int? Bay { get; set; }

        /// <summary>
        /// The car changes hands, and gives up its place in the warehouses: a car that comes back to the player later
        /// takes the first free bay like any other, not the one it had before
        /// </summary>
        public void ClearPlace()
        {
            Warehouse = null;
            Bay = null;
        }

        /// <summary>Who has owned the car and how it has raced; see <see cref="CarHistory"/></summary>
        public CarHistory History { get; set; } = new();

        public Car()
        {
            InstanceId = Guid.NewGuid();
            DefinitionId = string.Empty;
            OdometerKM = 0;
            EngineHealth = 1.0;
            TransmissionHealth = 1.0;
            BodyCondition = 1.0;
            TireCondition = 1.0;
            Parts = [];
            PurchasePrice = 0m;
        }

        public Car(string definitionId) : this()
        {
            DefinitionId = definitionId;
        }

        /// <summary>
        /// The car copied for a worker thread: its figures and a copy of its part tree (the parts' ids kept). Made on the
        /// UI thread before the car goes to <c>Task.Run</c>, because the workbench may change the car's own parts while
        /// the worker reads them. Only the history is shared, for reading: nothing else written to the copy reaches the car.
        /// </summary>
        public Car Snapshot() => new(DefinitionId)
        {
            InstanceId = InstanceId,
            SkinId = SkinId,
            PaintColour = PaintColour,
            OdometerKM = OdometerKM,
            EngineHealth = EngineHealth,
            TransmissionHealth = TransmissionHealth,
            BodyCondition = BodyCondition,
            TireCondition = TireCondition,
            BodyDamageKmh = BodyDamageKmh?.ToArray() ?? new double[4],
            FuelLitres = FuelLitres,
            FuelTankLitres = FuelTankLitres,
            BodyDirt = BodyDirt,
            Rust = Rust,
            PaintWear = PaintWear,
            GlassCracks = GlassCracks,
            LookSeed = LookSeed,
            OilLevel = OilLevel,
            Parts = Parts.Select(p => StreetCorsa.Parts.Cars.PartTrees.Clone(p, keepIds: true)).ToList(),
            HasPartsAssigned = HasPartsAssigned,
            HasRunningGearAssigned = HasRunningGearAssigned,
            GearVersion = GearVersion,
            PowerHp = PowerHp,
            PurchasePrice = PurchasePrice,
            ImpoundedUntil = ImpoundedUntil,
            ImpoundFee = ImpoundFee,
            PurchaseDate = PurchaseDate,
            Warehouse = Warehouse,
            Bay = Bay,
            History = History
        };
    }
}
