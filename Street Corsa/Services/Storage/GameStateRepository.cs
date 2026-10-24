using LiteDB;
using StreetCorsa.Logging;
using StreetCorsa.Models.GameState;
using StreetCorsa.Services.Opponents;
using System.IO;

namespace StreetCorsa.Services.Storage
{
    public class GameStateRepository : IGameStateRepository
    {
        private const string Collection = "gamestate";
        private const int BackupCount = 3;

        private readonly IOpponentInitializationService? _opponentInitializationService;
        private readonly Func<string, bool>? _isGameCar;

        // Saves backed up this session. The backups rotate once per session, at a save's first write: rotating on every
        // write (a race alone writes several times) would push a good copy out within a minute, before anybody could
        // notice that the save went bad.
        private readonly HashSet<string> _backedUp = new(StringComparer.OrdinalIgnoreCase);
        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger(LogCategory.Save);

        /// <param name="database">
        /// The saves' database owner to share with the race session repository; null makes one of its own.
        /// Either way this repository disposes it.
        /// </param>
        /// <param name="isGameCar">
        /// A car id the game plays with: in the catalog, installed, with a profile (an encrypted car mod or a race copy is
        /// not). A career's starting cars must be; null takes any car folder (tests).
        /// </param>
        public GameStateRepository(IOpponentInitializationService? opponentInitializationService = null, SaveDatabase? database = null,
            Func<string, bool>? isGameCar = null)
        {
            _opponentInitializationService = opponentInitializationService;
            _isGameCar = isGameCar;
            Database = database ?? new SaveDatabase();
        }

        /// <summary>The open database of the save in use, shared with the race session repository</summary>
        public SaveDatabase Database { get; }

        public GameState? Load(string saveName)
        {
            var dbPath = Database.PathOf(saveName);

            if (!File.Exists(dbPath))
                return null;

            try
            {
                var state = Database.Use(saveName, db => db.GetCollection<GameState>(Collection).FindById(1));

                // LastPlayedDate is left as it was saved; Save stamps it. (Stamping it here once made every
                // save "just now", back when the Load screen loaded each save to list it.)
                if (state != null)
                {
                    state.SaveName = saveName;

                    // A save from before the difficulty was picked plays on Normal
                    state.Rules ??= new GameRules();
                    state.NewspaperAds.PlayerCars ??= [];
                    state.NewspaperAds.RivalCars ??= [];
                    state.Racers.Departed ??= [];
                    state.StreetTalk ??= [];
                    state.Mods ??= [];
                    state.PartShops ??= [];
                    state.Player.PartOrders ??= [];
                    state.Player.MachineWork ??= [];
                    state.Player.Crates ??= [];
                    state.NewspaperAds.Kits ??= [];
                    foreach (var shop in state.PartShops)
                    {
                        shop.Kits ??= [];
                        shop.YardKits ??= [];
                    }
                    state.Player.Delivered ??= [];
                    state.Player.MachineReturned ??= [];
                    HistoryUpgrade.BringUpToDate(state);
                    Opponents.Bosses.BringUpToDate(state);
                }

                return state;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to load save '{saveName}': {ex.Message}", ex);
            }
        }

        public void Save(GameState state, string saveName) => Save(state, saveName, null);

        public void Save(GameState state, string saveName, Action<LiteDatabase>? sameTransaction)
        {
            var dbPath = Database.PathOf(saveName);

            try
            {
                // The session's first write to this save keeps what was there before it
                BackUpOnce(saveName, dbPath);

                state.LastPlayedDate = DateTime.Now;

                // Played with these from now on: a mod the player took out is warned of once, at the load before
                state.Mods = Mods.ModSet.Installed.References();
                Database.InTransaction(saveName, db =>
                {
                    db.GetCollection<GameState>(Collection).Upsert(state);
                    sameTransaction?.Invoke(db);
                });
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to save '{saveName}': {ex.Message}", ex);
            }
        }

        public bool Exists(string saveName) =>
            SaveDatabase.IsValidSaveName(saveName) && File.Exists(Database.PathOf(saveName));

        public void Delete(string saveName)
        {
            var dbPath = Database.PathOf(saveName);
            if (!File.Exists(dbPath))
                return;

            Database.Close(saveName);
            lock (_backedUp) _backedUp.Remove(saveName);

            // The save, LiteDB's journal and rebuild files next to it, and its backups
            foreach (var file in Database.FilesOf(saveName))
            {
                if (File.Exists(file))
                    File.Delete(file);
            }

            for (int i = 1; i <= BackupCount; i++)
            {
                var backupPath = $"{dbPath}.backup{i}";
                if (File.Exists(backupPath))
                    File.Delete(backupPath);
            }
        }

        public List<string> ListSaves()
        {
            if (!Directory.Exists(Database.SavesDirectory))
                return [];

            // Only real saves: not the catalog, not LiteDB's "-log"/"-tmp" side files, not backups
            return Directory.GetFiles(Database.SavesDirectory, "*.db")
                .Where(f => !Path.GetFileName(f).Contains(".backup", StringComparison.OrdinalIgnoreCase))
                .Select(f => Path.GetFileNameWithoutExtension(f))
                .Where(SaveDatabase.IsValidSaveName)
                .OrderByDescending(f => File.GetLastWriteTime(Path.Combine(Database.SavesDirectory, f + ".db")))
                .ToList();
        }

        public IReadOnlyList<SaveHeader> ListSaveHeaders()
        {
            var headers = new List<SaveHeader>();
            foreach (var saveName in ListSaves())
            {
                try
                {
                    // Only the fields the list shows are projected out of the document: the market, opponents
                    // and career are never mapped to objects. Peek reads the save in use through its open
                    // instance and any other save read-only, so listing never closes the game being played.
                    var doc = Database.Peek(saveName, db => db.GetCollection(Collection)
                        .Query()
                        .Where("_id = 1")
                        .Select("{ name: $.Player.Name, money: $.Player.Money, date: $.Date, played: $.LastPlayedDate }")
                        .FirstOrDefault());

                    if (doc == null)
                    {
                        _logger.Warning("Save {SaveName} holds no game; it is not listed", saveName);
                        continue;
                    }

                    headers.Add(new SaveHeader(
                        saveName,
                        doc["name"].IsString ? doc["name"].AsString : string.Empty,
                        doc["money"].IsNumber ? doc["money"].AsDecimal : 0m,
                        doc["date"].IsDateTime ? doc["date"].AsDateTime : default,
                        doc["played"].IsDateTime ? doc["played"].AsDateTime : default));
                }
                catch (Exception ex)
                {
                    // A corrupt save is left off the list, with a trace of why in the log
                    _logger.Warning(ex, "Save {SaveName} could not be read; it is not listed", saveName);
                }
            }
            return headers;
        }

        public GameState CreateNew(string saveName, string playerName, GameRules? rules = null)
        {
            // Throws on a name that cannot be a save before anything is made
            Database.PathOf(saveName);

            // Overwriting a save starts from nothing: the old game's race records (the dedup set) and its backups
            // would otherwise live on in the new game's file
            if (Exists(saveName))
            {
                _logger.Information("Save {SaveName} is overwritten by a new game: its files go first", saveName);
                Delete(saveName);
            }

            var state = GameState.CreateNew(playerName, rules, Career.CareerContent.Installed.OpenFromStart);
            state.SaveName = saveName;
            // Played in the setting this run plays, for good
            state.Setting = Mods.ModSet.Installed.Setting?.Id;

            // The new career plays by the rules it was just given, its rivals made by them too
            StreetCorsa.Rules.RuleBook.PlayBy(state);
            AddStartingCars(state);

            // Initialize opponents if service is available
            _opponentInitializationService?.InitializeOpponents(state);

            Save(state, saveName);
            return state;
        }

        /// <summary>
        /// The cars the rules start a career with (<c>start.cars</c>), in the player's garage, the first the one they
        /// drive. They come without parts: the garage gives a car its factory ones when it first looks at it. One that
        /// Assetto Corsa does not have, or that the game does not play with (an encrypted car mod, a race copy, a car the
        /// catalog has no profile for), is left out, and the mods' problems say so.
        /// </summary>
        private void AddStartingCars(GameState state)
        {
            foreach (var start in StreetCorsa.Rules.RuleBook.Current.Start.Cars ?? [])
            {
                if (start == null || string.IsNullOrWhiteSpace(start.Car)) continue;

                var id = start.Car.Trim();
                if (!Directory.Exists(Path.Combine(StreetCorsa.Configuration.AppSettings.Instance.CarsPath, id)))
                {
                    _logger.Warning("The rules start the career with the car {Car}, which Assetto Corsa does not have: left out", id);
                    Mods.ModProblems.Add("Rules", $"start.cars: {id} is no car in Assetto Corsa; left out");
                    continue;
                }

                if (_isGameCar != null && !_isGameCar(id))
                {
                    _logger.Warning("The rules start the career with the car {Car}, which the game does not play with: left out", id);
                    Mods.ModProblems.Add("Rules", $"start.cars: {id} is not a car the game plays with (an encrypted mod, a race copy, or one without a profile); left out");
                    continue;
                }

                var condition = double.IsFinite(start.Condition) ? Math.Clamp(start.Condition, 0.05, 1.0) : 0.7;
                var car = new Car(id)
                {
                    SkinId = string.IsNullOrWhiteSpace(start.Skin) ? "default" : start.Skin.Trim(),
                    OdometerKM = double.IsFinite(start.Mileage) ? Math.Max(0, start.Mileage) : 0,
                    EngineHealth = condition,
                    TransmissionHealth = condition,
                    BodyCondition = condition,
                    TireCondition = condition,
                    PurchaseDate = state.Date
                };
                // The first car is somebody's old one
                StreetCorsa.Parts.Cars.CarLooks.Give(car, StreetCorsa.Parts.Cars.CarLooks.Roll(condition, StreetCorsa.Parts.Cars.LookSource.Private, Random.Shared));
                car.History.ChangeHands(state.Player.Name, state.Date, CarAcquisition.Unknown);

                state.Player.Cars ??= [];
                state.Player.Cars.Add(car);
                state.Player.Stats.CarsOwned++;
                state.Player.SelectedCarInstanceId ??= car.InstanceId;
            }
        }

        /// <summary>Closes the open save; call it after the last save on exit</summary>
        public void Dispose() => Database.Dispose();

        private void BackUpOnce(string saveName, string dbPath)
        {
            lock (_backedUp)
            {
                if (_backedUp.Contains(saveName)) return;
            }

            if (CreateBackup(saveName, dbPath))
            {
                lock (_backedUp) _backedUp.Add(saveName);
            }
        }

        /// <summary>True when a backup was made; a save not on disk yet has nothing to back up, and a failure is logged</summary>
        private bool CreateBackup(string saveName, string dbPath)
        {
            if (!File.Exists(dbPath))
                return false;

            try
            {
                // Rotate backups: backup3 <- backup2 <- backup1 <- current
                var backup3 = $"{dbPath}.backup3";
                var backup2 = $"{dbPath}.backup2";
                var backup1 = $"{dbPath}.backup1";

                if (File.Exists(backup2))
                    File.Copy(backup2, backup3, true);

                if (File.Exists(backup1))
                    File.Copy(backup1, backup2, true);

                // The save may be open: copied through its owner, checkpointed and with no write in progress
                Database.CopyTo(saveName, backup1);
                return true;
            }
            catch (Exception ex)
            {
                // Backup failure shouldn't prevent saving; the next save tries again
                _logger.Warning(ex, "Could not back up save {SaveName} before saving", saveName);
                return false;
            }
        }
    }
}
