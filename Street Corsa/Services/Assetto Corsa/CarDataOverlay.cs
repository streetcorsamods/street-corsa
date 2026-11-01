using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Newtonsoft.Json;
using StreetCorsa.Configuration;
using StreetCorsa.Helpers;
using StreetCorsa.Logging;
using StreetCorsa.Parts.Export;
using StreetCorsa.Services.Configuration;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Services
{
    /// <summary>
    /// Puts a car's physics data and its sound, as its parts make them, into the Assetto Corsa install for the
    /// length of a race, and takes them out again. The rule for every change to the install: the originals are
    /// kept aside first (with a manifest that says what to put back), the change is the smallest that does the
    /// job, and whatever happens the originals go back: after the race, or the next time the game starts.
    /// A car that ships packed (data.acd, no data folder) gets its data unpacked for the race and the folder
    /// removed after. Assetto Corsa reads data.acd over a data folder (seen with the setup probe on the Kunos AE86,
    /// 2026-10-28: the folder's changes did nothing until data.acd was gone), so a car's data.acd is moved aside in its
    /// own folder for the race (<see cref="AcdAsideName"/>, a rename) and back after. The race's folder is always what
    /// data.acd holds, AC's own data for the car: a data folder beside it (a leftover of an unpack, perhaps out of date)
    /// is renamed aside too (<see cref="DataAsideName"/>) and back after.
    /// The sound is the bank and GUIDs under the car's <c>sfx</c> folder: the car's own two files move into a keep
    /// folder next to them (a rename, whatever their size), the new bank is hard-linked in under the car's name
    /// (no copy either; a copy only when the link cannot be made), and the GUIDs are written for the car's id.
    /// A sound is never worth a race: one whose bank is gone leaves the car on its own.
    /// The sound's steps are in <c>CarDataOverlay.Sound.cs</c>, the race copies (a second car of one model) in
    /// <c>CarDataOverlay.Clones.cs</c>.
    /// </summary>
    public partial class CarDataOverlay
    {
        private const string ManifestFile = "manifest.json";
        private const string FilesFolder = "files";

        /// <summary>A car's data.acd while it races on its data folder: renamed beside it, never copied</summary>
        public const string AcdAsideName = AcdFile.FileName + ".streetcorsa";

        /// <summary>A car's own data folder beside its data.acd while it races on what the acd holds: renamed beside it</summary>
        public const string DataAsideName = AcCarData.DataFolder + ".streetcorsa";

        private readonly Func<string> _carsPathOf;
        private readonly string _keepPath;
        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger("CarData");

        // Cars changed since the last restore: a manifest on disk without an entry here is what an earlier
        // race left behind, not this one's
        private readonly HashSet<string> _appliedNow = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// On the AC folder the settings name at each call: start-up may ask the player for it after the overlay is
        /// made, and the races go into the folder they picked
        /// </summary>
        public CarDataOverlay() : this(() => AppSettings.Instance.CarsPath, AppSettings.AcRestorePath)
        {
        }

        /// <param name="carsPath">The install's content\cars</param>
        /// <param name="keepPath">Where the originals wait while a race runs</param>
        public CarDataOverlay(string carsPath, string keepPath) : this(() => carsPath, keepPath)
        {
        }

        /// <param name="carsPathOf">The install's content\cars, asked at each call</param>
        /// <param name="keepPath">Where the originals wait while a race runs</param>
        public CarDataOverlay(Func<string> carsPathOf, string keepPath)
        {
            _carsPathOf = carsPathOf;
            _keepPath = keepPath;
        }

        /// <summary>The install's content\cars as the settings name it now</summary>
        private string CarsPath => _carsPathOf();

        /// <summary>What was changed for one car, and how to put it back</summary>
        private sealed class Manifest
        {
            public string CarId { get; set; } = string.Empty;
            public DateTime Applied { get; set; }

            /// <summary>
            /// The absolute car folder that was changed: the one that goes back, even when the AC folder in the
            /// settings has changed since. Empty in manifests written before it was recorded.
            /// </summary>
            public string CarFolder { get; set; } = string.Empty;

            /// <summary>The data folder was made from data.acd for the race and goes away with the restore</summary>
            public bool CreatedDataFolder { get; set; }

            /// <summary>The car's data.acd is moved aside (<see cref="AcdAsideName"/>) for the race and goes back with the restore</summary>
            public bool AcdAside { get; set; }

            /// <summary>
            /// The car's own data folder, beside the data.acd AC reads over it, is moved aside (<see cref="DataAsideName"/>)
            /// for the race and goes back with the restore; the race's folder is unpacked from the acd
            /// </summary>
            public bool FolderAside { get; set; }

            public List<Entry> Files { get; set; } = new();

            /// <summary>The sound was changed; null when the car raced on its own</summary>
            public SfxEntry? Sfx { get; set; }

            public sealed class Entry
            {
                public string Name { get; set; } = string.Empty;

                /// <summary>Whether the car had the file: put back from the kept copy, or deleted</summary>
                public bool Existed { get; set; }

                /// <summary>The car's file was read-only: the flag is cleared for the race and set again after it</summary>
                public bool ReadOnly { get; set; }
            }

            public sealed class SfxEntry
            {
                /// <summary>The car had no sfx folder at all; it goes away with the restore</summary>
                public bool CreatedFolder { get; set; }

                /// <summary>The car's own bank and GUIDs were moved into the keep folder; without them there is nothing to move back</summary>
                public bool BankExisted { get; set; }

                public bool GuidsExisted { get; set; }

                /// <summary>
                /// The car's own bank is in the keep folder now: written once it is, so a race that failed before it got
                /// that far is not told as a sound lost (the car's own never left its place)
                /// </summary>
                public bool BankKept { get; set; }

                /// <summary>The car's own GUIDs are in the keep folder now (see <see cref="BankKept"/>)</summary>
                public bool GuidsKept { get; set; }

                /// <summary>Where the new bank came from, for the log and for anyone reading the manifest</summary>
                public string Source { get; set; } = string.Empty;
            }
        }

        /// <summary>Cars whose data is changed right now</summary>
        public IReadOnlyList<string> Applied =>
            Directory.Exists(_keepPath)
                ? Directory.GetDirectories(_keepPath)
                    .Where(d => File.Exists(Path.Combine(d, ManifestFile)))
                    .Select(d => Path.GetFileName(d)!)
                    .Where(PathNames.IsSafeSegment)
                    .ToList()
                : Array.Empty<string>();

        public bool IsApplied(string carId) =>
            PathNames.IsSafeSegment(carId) && File.Exists(Path.Combine(_keepPath, carId, ManifestFile));

        /// <summary>
        /// A new race's preparation starts: whatever is still listed as changed "for this race" is from an earlier one
        /// whose restore was skipped or failed, and is treated as a leftover from here on (put back before a car is
        /// read, applied or copied) instead of blocking this race's changes.
        /// </summary>
        public void BeginRace()
        {
            using var gate = AcInstallGate.Hold();
            if (_appliedNow.Count > 0)
                _logger.Warning("{Count} car(s) still listed as changed from an earlier race: they are put back before they are used again", _appliedNow.Count);
            _appliedNow.Clear();
        }

        /// <summary>
        /// A car whose data an earlier race left changed (its restore failed, or the app died during the race) is put
        /// back now, so what is read from its folder is the car's own. Nothing happens while AC runs (the game may be
        /// on that data) or when the car is changed for the race being prepared right now. True when it was put
        /// back; throws when it could not be.
        /// </summary>
        public bool RestoreIfLeftover(string carId)
        {
            using var gate = AcInstallGate.Hold();
            if (!IsApplied(carId) || _appliedNow.Contains(carId)) return false;
            if (AcProcesses.AnyRunning())
            {
                _logger.Warning("{Car} still has data from an earlier race, but Assetto Corsa is running: it goes back once the game has closed", carId);
                return false;
            }

            _logger.Warning("{Car} still had changed data from an earlier race: putting it back before it is read", carId);
            return Restore(carId);
        }

        /// <summary>
        /// The car's folder under content\cars, for an id that is one folder name; anything else (empty, ".", "..",
        /// a path) is refused before it can reach a recursive delete
        /// </summary>
        private string CarDirectory(string carId)
        {
            if (!PathNames.IsSafeSegment(carId)) throw new ArgumentException($"'{carId}' is not a car folder name", nameof(carId));
            return PathNames.CombineUnder(CarsPath, carId);
        }

        /// <summary>Where a car's originals wait, checked the same way: never the AcRestore root itself</summary>
        private string KeepDirectory(string carId)
        {
            if (!PathNames.IsSafeSegment(carId)) throw new ArgumentException($"'{carId}' is not a car folder name", nameof(carId));
            return PathNames.CombineUnder(_keepPath, carId);
        }

        /// <summary>
        /// The folder a manifest says it changed, when it is plausibly that car's folder in an AC install (named after
        /// the car, inside a "cars" folder); else the car's folder in the install the game runs on now
        /// </summary>
        private string ChangedCarDirectory(string carId, Manifest manifest)
        {
            var recorded = manifest.CarFolder;
            if (!string.IsNullOrWhiteSpace(recorded) && Path.IsPathFullyQualified(recorded))
            {
                var folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(recorded));
                var parent = Path.GetDirectoryName(folder);
                if (string.Equals(Path.GetFileName(folder), carId, StringComparison.OrdinalIgnoreCase)
                    && parent != null && string.Equals(Path.GetFileName(parent), "cars", StringComparison.OrdinalIgnoreCase))
                    return folder;

                _logger.Warning("{Car}: the manifest names {Folder}, which is not that car's folder; putting back {Current} instead", carId, recorded, CarDirectory(carId));
            }

            return CarDirectory(carId);
        }

        /// <summary>
        /// Writes the files into the car's data and the sound into its sfx folder, keeping the originals. False when
        /// the car is already changed for this race (two cars of the same model: the first set stays).
        /// </summary>
        /// <param name="files">Data file name to content; empty leaves the data alone</param>
        /// <param name="sound">The sound to race with; null leaves the car's own</param>
        public bool Apply(string carId, IReadOnlyDictionary<string, string> files, CarSound? sound = null)
        {
            using var gate = AcInstallGate.Hold();
            sound = Usable(carId, sound);
            if (files.Count == 0 && sound == null) return true;
            if (_appliedNow.Contains(carId))
            {
                _logger.Warning("{Car} already has changed data: leaving it as it is", carId);
                return false;
            }

            var carDirectory = CarDirectory(carId);
            var keep = KeepDirectory(carId);
            RestoreLeftover(carId);

            var dataDirectory = Path.Combine(carDirectory, AcCarData.DataFolder);
            var sfxDirectory = Path.Combine(carDirectory, AcCarSound.SfxFolder);
            var keepFiles = Path.Combine(keep, FilesFolder);
            if (Directory.Exists(keep)) Directory.Delete(keep, true);
            Directory.CreateDirectory(keepFiles);

            // The car's own data folder gets the race's files through temp files of SafeFile's next to them: what a
            // write cut short left there (a kill, the power) goes now, the start-up's sweep does not look into the cars
            SafeFile.SweepTemps(dataDirectory);

            var manifest = new Manifest { CarId = carId, Applied = DateTime.Now, CarFolder = Path.GetFullPath(carDirectory) };

            // The originals first, then the manifest that names them, then the change: a crash at any
            // point leaves either nothing changed or everything needed to change it back
            var acd = Path.Combine(carDirectory, AcdFile.FileName);
            var dataAside = Path.Combine(carDirectory, DataAsideName);
            if (files.Count > 0)
            {
                if (File.Exists(acd) || !Directory.Exists(dataDirectory))
                {
                    // A packed car runs on its data folder once there is one: the whole of what data.acd holds, with our
                    // files over it. A folder of its own beside the acd is not what AC reads: it goes aside for the race
                    AcdOf(carId, carDirectory);
                    manifest.CreatedDataFolder = true;
                    manifest.FolderAside = Directory.Exists(dataDirectory);
                    if (manifest.FolderAside && Directory.Exists(dataAside))
                        throw new IOException($"{carId} has a {DataAsideName} folder left by an earlier race beside its data folder: put back the right one by hand");
                }
                else
                {
                    foreach (var name in files.Keys)
                    {
                        var original = DataFile(dataDirectory, name);
                        var existed = File.Exists(original);
                        var readOnly = existed && File.GetAttributes(original).HasFlag(FileAttributes.ReadOnly);
                        // The kept copy is ours to delete after the restore: it does not carry the flag along
                        if (existed) KeptFile.Keep(original, Path.Combine(keepFiles, name));

                        manifest.Files.Add(new Manifest.Entry { Name = name, Existed = existed, ReadOnly = readOnly });
                    }
                }
            }

            if (sound != null)
            {
                // A file already in the keep folder is the car's own too (see Keep): it goes back with the restore
                var keepFolder = Path.Combine(sfxDirectory, SfxKeepFolder);
                var bankName = AcCarSound.BankFileName(carId);
                manifest.Sfx = new Manifest.SfxEntry
                {
                    CreatedFolder = !Directory.Exists(sfxDirectory),
                    BankExisted = File.Exists(Path.Combine(sfxDirectory, bankName)) || File.Exists(Path.Combine(keepFolder, bankName)),
                    GuidsExisted = File.Exists(Path.Combine(sfxDirectory, AcCarSound.GuidsFileName)) || File.Exists(Path.Combine(keepFolder, AcCarSound.GuidsFileName)),
                    Source = sound.BankPath
                };
            }

            // AC would read the car's data.acd over the folder the race's files go in: it goes aside, said before it does
            manifest.AcdAside = files.Count > 0 && File.Exists(acd);

            // Flushed to the disk and renamed into place: a power cut right after leaves a whole manifest, never an
            // empty one next to changed data
            WriteManifest(keep, manifest);
            _appliedNow.Add(carId);

            if (manifest.FolderAside)
            {
                Directory.Move(dataDirectory, dataAside);
                _logger.Information("{Car}: its data folder moved aside for the race, which runs on {Acd}", carId, AcdFile.FileName);
            }

            if (manifest.CreatedDataFolder)
            {
                UnpackAcd(carId, carDirectory, dataDirectory);
                _logger.Information("{Car}: data unpacked from {Acd} for the race", carId, AcdFile.FileName);
            }

            if (manifest.AcdAside)
            {
                // One left aside by a race whose manifest is gone is the same car's own: the one in place is the newer
                File.Move(acd, Path.Combine(carDirectory, AcdAsideName), overwrite: true);
                _logger.Information("{Car}: {Acd} moved aside for the race", carId, AcdFile.FileName);
            }

            // Each file whole, in one rename, never changed in place: a data file hard-linked to another name (a mod
            // manager, a second install) must not take the race's values to that name too
            foreach (var (name, content) in files)
                KeptFile.Replace(DataFile(dataDirectory, name), Encoding.Latin1.GetBytes(content));
            if (files.Count > 0)
                _logger.Information("{Car}: {Count} data file(s) changed for the race: {Files}", carId, files.Count, string.Join(", ", files.Keys));

            if (sound != null) ApplySound(carId, keep, manifest, sfxDirectory, sound);
            return true;
        }

        /// <summary>
        /// What an earlier race could not put back goes back before anything else goes in or is copied. Never while AC
        /// runs (the game may be on that data): then the car cannot be changed or copied either, since that would
        /// start from the earlier race's data and throw away the originals kept from it, and this throws.
        /// </summary>
        private void RestoreLeftover(string carId)
        {
            if (!IsApplied(carId) || _appliedNow.Contains(carId)) return;
            if (AcProcesses.AnyRunning())
                throw new InvalidOperationException($"{carId} still has data from an earlier race, and Assetto Corsa is running: it cannot go back, or be changed again, until the game has closed");

            _logger.Warning("{Car} still had changed data from an earlier race: putting it back first", carId);
            Restore(carId);
        }

        /// <summary>
        /// Puts a car's data back as it was. True when there was something to put back. Every step is tried, whatever
        /// the others do: the data files one by one, then the sound. The kept originals are deleted only when all of
        /// them went back; what failed stays in the manifest for the next try, and this throws to say so. A manifest
        /// that reads as nothing (0 bytes after a power cut, "null") throws too and deletes nothing.
        /// </summary>
        /// <param name="problems">Gets a line for the player about each file whose original is lost for good</param>
        public bool Restore(string carId, ICollection<string>? problems = null)
        {
            using var gate = AcInstallGate.Hold();
            var keep = KeepDirectory(carId);
            var manifestPath = Path.Combine(keep, ManifestFile);
            if (!File.Exists(manifestPath)) return false;

            Manifest? manifest;
            try
            {
                manifest = JsonConvert.DeserializeObject<Manifest>(File.ReadAllText(manifestPath));
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"The manifest of {carId} cannot be read; its originals are left under {keep}", ex);
            }

            if (manifest == null)
                throw new InvalidDataException($"The manifest of {carId} is empty; its originals are left under {keep}");

            // "Files": null deserializes over the initializer: without the list the manifest cannot say what goes where
            if (manifest.Files == null)
                throw new InvalidDataException($"The manifest of {carId} has no file list; its originals are left under {keep}");

            var carDirectory = ChangedCarDirectory(carId, manifest);
            var dataDirectory = Path.Combine(carDirectory, AcCarData.DataFolder);
            var failures = new List<Exception>();

            if (manifest.AcdAside)
            {
                try
                {
                    PutAcdBack(carId, carDirectory);
                    manifest.AcdAside = false;
                }
                catch (Exception ex)
                {
                    failures.Add(ex);
                    _logger.Error(ex, "{Car}: its {Acd} could not be put back from {Aside}", carId, AcdFile.FileName, AcdAsideName);
                }
            }

            // The unpacked folder goes only once data.acd is back: without either the car could not load. The car's own
            // folder, when it had one, is the one in place until it has been moved aside
            var dataAside = Path.Combine(carDirectory, DataAsideName);
            if (manifest.CreatedDataFolder && manifest.FolderAside && !Directory.Exists(dataAside))
            {
                manifest.CreatedDataFolder = manifest.FolderAside = false;
            }
            else if (manifest.CreatedDataFolder && !manifest.AcdAside)
            {
                try
                {
                    if (Directory.Exists(dataDirectory)) Directory.Delete(dataDirectory, true);
                    _logger.Information("{Car}: the unpacked data folder is gone again", carId);
                    manifest.CreatedDataFolder = false;
                }
                catch (Exception ex)
                {
                    failures.Add(ex);
                    _logger.Error(ex, "{Car}: the unpacked data folder {Folder} could not be deleted", carId, dataDirectory);
                }
            }

            if (manifest.FolderAside && !manifest.CreatedDataFolder)
            {
                try
                {
                    Directory.Move(dataAside, dataDirectory);
                    manifest.FolderAside = false;
                    _logger.Information("{Car}: its own data folder is back", carId);
                }
                catch (Exception ex)
                {
                    failures.Add(ex);
                    _logger.Error(ex, "{Car}: its own data folder could not be put back from {Aside}", carId, DataAsideName);
                }
            }

            // The kept originals of a car that raced on its own folder
            if (manifest.Files.Count > 0)
            {
                var left = new List<Manifest.Entry>();
                foreach (var entry in manifest.Files)
                {
                    try
                    {
                        RestoreFile(carId, keep, dataDirectory, entry, problems);
                    }
                    catch (Exception ex)
                    {
                        failures.Add(ex);
                        left.Add(entry);
                        _logger.Error(ex, "{Car}: {File} could not be put back; its original stays under {Keep}", carId, entry.Name, keep);
                    }
                }

                _logger.Information("{Car}: {Count} of {Total} data file(s) put back", carId, manifest.Files.Count - left.Count, manifest.Files.Count);
                manifest.Files = left;
                SafeFile.SweepTemps(dataDirectory);
            }

            if (manifest.Sfx != null)
            {
                try
                {
                    RestoreSound(carId, Path.Combine(carDirectory, AcCarSound.SfxFolder), manifest.Sfx, problems);
                    manifest.Sfx = null;
                }
                catch (Exception ex)
                {
                    failures.Add(ex);
                    _logger.Error(ex, "{Car}: its own sound could not be put back", carId);
                }
            }

            if (failures.Count > 0)
            {
                // What went back is off the list; what did not is tried again next time, from the same originals
                WriteManifest(keep, manifest);
                throw new AggregateException($"{carId}: {failures.Count} step(s) of the restore failed; the next start tries again", failures);
            }

            // Everything is back. The manifest goes first and on its own, as AcConfigBackup's does: a kept file a scanner
            // holds must not leave a manifest behind that would put back again what is back already
            DropManifest(keep, manifest);
            _appliedNow.Remove(carId);
            try
            {
                Directory.Delete(keep, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Without its manifest the folder is nobody's change: the car's next Apply starts it again
                _logger.Warning(ex, "{Car} is back, but its keep folder {Keep} could not be deleted; it is left for the next race", carId, keep);
            }

            return true;
        }

        /// <summary>
        /// A car's data.acd back from beside it. One already in place (put back by hand) stays, and the one aside is left
        /// where it is, never deleted; a car with none aside has nothing to put back (a restore cut short after the rename).
        /// </summary>
        private void PutAcdBack(string carId, string carDirectory)
        {
            var aside = Path.Combine(carDirectory, AcdAsideName);
            var acd = Path.Combine(carDirectory, AcdFile.FileName);
            if (!File.Exists(aside)) return;

            if (File.Exists(acd))
            {
                _logger.Warning("{Car}: {Acd} is in place already; {Aside} is left beside it", carId, AcdFile.FileName, AcdAsideName);
                return;
            }

            File.Move(aside, acd);
            _logger.Information("{Car}: {Acd} is back", carId, AcdFile.FileName);
        }

        /// <summary>
        /// Deletes the manifest of a car that is back whole. One that cannot be deleted (held) is written again with
        /// nothing left in it, so a later restore puts nothing back; throws only when neither works.
        /// </summary>
        private void DropManifest(string keep, Manifest done)
        {
            var manifestPath = Path.Combine(keep, ManifestFile);
            try
            {
                File.Delete(manifestPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.Warning(ex, "{Car} is back, but its manifest could not be deleted; it is emptied instead", done.CarId);
                WriteManifest(keep, new Manifest { CarId = done.CarId, Applied = done.Applied, CarFolder = done.CarFolder });
            }
        }

        /// <summary>
        /// One data file back from its kept copy (or deleted, when the car did not have it), with its read-only flag as
        /// it was. A kept copy that is gone cannot ever go back: that is logged as lost and not retried forever.
        /// </summary>
        private void RestoreFile(string carId, string keep, string dataDirectory, Manifest.Entry entry, ICollection<string>? problems)
        {
            var target = DataFile(dataDirectory, entry.Name);
            if (!entry.Existed)
            {
                SafeFile.Delete(target);

                return;
            }

            // Written whole or not at all: a restore cut short never leaves the car a truncated file
            if (!KeptFile.PutBack(Path.Combine(keep, FilesFolder, entry.Name), target, entry.ReadOnly))
            {
                _logger.Error("{Car}: the kept original of {File} is gone (deleted outside the game?); the race version stays in {Target}", carId, entry.Name, target);
                problems?.Add(GameText.Say("launch.not_put_back.original_gone", ("car", carId), ("file", entry.Name), ("folder", target)));
            }
        }

        /// <summary>A data file's path, for a name that is one file name inside the data folder</summary>
        private static string DataFile(string dataDirectory, string name)
        {
            if (!PathNames.IsSafeSegment(name)) throw new InvalidDataException($"'{name}' is not a data file name");
            return Path.Combine(dataDirectory, name);
        }

        private static void WriteManifest(string keep, Manifest manifest) =>
            SafeFile.WriteAllText(Path.Combine(keep, ManifestFile), JsonConvert.SerializeObject(manifest, Formatting.Indented));

        /// <summary>
        /// Puts every car back and takes away every copy made for a race; at start-up this undoes what a crash left
        /// behind. A car that cannot be put back keeps its manifest, and the next Apply to it tries again first.
        /// </summary>
        /// <param name="problems">Gets a line for the player about each car or copy that stayed changed</param>
        public int RestoreAll(ICollection<string>? problems = null)
        {
            using var gate = AcInstallGate.Hold();
            var restored = 0;
            foreach (var carId in Applied)
            {
                try
                {
                    if (Restore(carId, problems)) restored++;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Could not put the data of {Car} back; the originals are kept under {Path}", carId, Path.Combine(_keepPath, carId));
                    problems?.Add(GameText.Say("launch.not_put_back.car", ("car", carId), ("folder", Path.Combine(_keepPath, carId))));
                }
            }

            _appliedNow.Clear();
            return restored + RemoveClones(problems);
        }

        /// <summary>A packed car's data.acd; a car with neither it nor a data folder cannot race on changed data</summary>
        private static string AcdOf(string carId, string carDirectory)
        {
            var acd = Path.Combine(carDirectory, AcdFile.FileName);
            if (!File.Exists(acd)) throw new FileNotFoundException($"{carId} has neither a data folder nor {AcdFile.FileName}", acd);
            return acd;
        }

        /// <summary>
        /// What a packed car's data.acd holds, written out as a data folder. The archive comes with a mod and is not
        /// trusted: every entry must be one file name, and one that is not (a path, "..") refuses the whole archive
        /// before anything is written. Plain writes: the folder is one the game makes (for the race, or in a copy of
        /// its own) and deletes after it, so every file is new and linked to nothing.
        /// </summary>
        private static void UnpackAcd(string carId, string carDirectory, string dataDirectory)
        {
            var entries = AcdFile.Read(AcdOf(carId, carDirectory));
            var files = new List<(string Path, byte[] Content)>();
            foreach (var (name, content) in entries) files.Add((DataFile(dataDirectory, name), content));

            Directory.CreateDirectory(dataDirectory);
            foreach (var (path, content) in files) File.WriteAllBytes(path, content);
        }

        /// <summary>
        /// A hard link to <paramref name="source"/> when the two paths are on one volume and the source is not read-only
        /// (a link shares the flag: clearing it would clear it on the car's own file, and a read-only link could not be
        /// deleted or replaced after the race); otherwise a writable copy. True when linked.
        /// </summary>
        private static bool LinkOrCopy(string source, string destination)
        {
            if (File.Exists(destination)) File.Delete(destination);
            if (!File.GetAttributes(source).HasFlag(FileAttributes.ReadOnly) && CreateHardLink(destination, source, IntPtr.Zero)) return true;

            CopyWritable(source, destination);
            return false;
        }

        /// <summary>A copy of <paramref name="source"/> of its own, without the read-only flag: ours to write and delete</summary>
        private static void CopyWritable(string source, string destination)
        {
            File.Copy(source, destination, true);
            File.SetAttributes(destination, File.GetAttributes(destination) & ~FileAttributes.ReadOnly);
        }

        // The export's exact name: the generated call does not probe for the W suffix as DllImport did
        [LibraryImport("kernel32.dll", EntryPoint = "CreateHardLinkW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);
    }
}
