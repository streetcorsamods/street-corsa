using System.IO;
using System.Text;
using Newtonsoft.Json;
using StreetCorsa.Configuration;
using StreetCorsa.Helpers;
using StreetCorsa.Parts.Export;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Services
{
    /// <summary>
    /// The race copies: a second car of one model races in a marked copy of the car folder, made before the race
    /// and deleted after it, at start-up, and wherever an earlier install left one (see the class summary for the rule)
    /// </summary>
    public partial class CarDataOverlay
    {
        /// <summary>
        /// Every race copy made, by absolute folder, until it is removed: a copy left in an install the settings no
        /// longer name (the AC folder changed after a crash) is still found and taken away
        /// </summary>
        private const string ClonesFile = "clones.json";

        /// <summary>
        /// A copy of a car folder under <paramref name="cloneId"/>, for a second car of that model in the race: it races
        /// on <paramref name="files"/> over the car's own data and on <paramref name="sound"/> (the car's own when null),
        /// and the original is not touched. Models, textures and skins are hard links (nothing is copied but the data,
        /// which is written to); the marker goes in first, so a copy cut short is still known to be ours. The copy is
        /// made from the car as it ships: what an earlier race left changed in it goes back first, and a car already
        /// changed for this race cannot be copied.
        /// </summary>
        public void CreateClone(string carId, string cloneId, IReadOnlyDictionary<string, string> files, CarSound? sound, string? masterGuidsPath = null)
        {
            using var gate = AcInstallGate.Hold();
            var source = CarDirectory(carId);
            if (!IsCloneId(cloneId)) throw new ArgumentException($"'{cloneId}' is not a name for a copy (it must end with {AcCarFolder.CloneSuffix})", nameof(cloneId));
            var target = CarDirectory(cloneId);
            if (!Directory.Exists(source)) throw new DirectoryNotFoundException($"{carId} is not installed");
            if (_appliedNow.Contains(carId)) throw new InvalidOperationException($"{carId} is already changed for this race: its copy is made before that");
            var targetData = Path.Combine(target, AcCarData.DataFolder);
            foreach (var name in files.Keys) DataFile(targetData, name);
            RestoreLeftover(carId);

            if (Directory.Exists(target))
            {
                if (!IsOurClone(target)) throw new IOException($"{cloneId} is a car of the install, not a copy: it is left alone");
                RemoveClone(cloneId);
            }

            // Recorded before the folder exists: whatever becomes of the settings, the copy is found again
            // The copy's own files are plain writes: the folder is ours (made just now, deleted after the race), and
            // what is written in it is new, never a link to the car's own. The marker alone is written whole or not at
            // all: it is what a later clean-up trusts to delete the folder (AcCarFolder.IsClone), so a cut-short one
            // must never be read as half a marker
            RecordClone(target);
            Directory.CreateDirectory(target);
            SafeFile.WriteAllText(Path.Combine(target, AcCarFolder.CloneMarker),
                JsonConvert.SerializeObject(new { Source = carId, Created = DateTime.Now }, Formatting.Indented));

            var dataFolder = Path.Combine(source, AcCarData.DataFolder);
            var sfxFolder = Path.Combine(source, AcCarSound.SfxFolder);
            var linked = 0;
            var copied = 0;
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                // The data and the sound are made below; data.acd would not open under another name
                if (IsUnder(file, dataFolder) || IsUnder(file, sfxFolder)) continue;
                var relative = Path.GetRelativePath(source, file);
                if (relative.Equals(AcdFile.FileName, StringComparison.OrdinalIgnoreCase)) continue;

                var destination = Path.Combine(target, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                if (LinkOrCopy(file, destination)) linked++;
                else copied++;
            }

            // The data is written to, so it is a copy: the car's own folder, or what its data.acd holds
            if (Directory.Exists(dataFolder))
            {
                Directory.CreateDirectory(targetData);
                foreach (var file in Directory.GetFiles(dataFolder)) File.Copy(file, Path.Combine(targetData, Path.GetFileName(file)), true);
            }
            else
            {
                UnpackAcd(carId, source, targetData);
            }

            foreach (var file in Directory.GetFiles(targetData)) File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly);
            foreach (var (name, content) in files) File.WriteAllText(DataFile(targetData, name), content, Encoding.Latin1);

            // The bank goes in under the copy's name, with the GUID lines written for it; a chosen sound whose bank is
            // gone leaves the copy on the car's own
            sound = Usable(cloneId, sound) ?? Usable(cloneId, AcCarSound.FromCar(source, masterGuidsPath ?? AppSettings.Instance.SfxGuidsPath));
            if (sound != null)
            {
                var targetSfx = Path.Combine(target, AcCarSound.SfxFolder);
                Directory.CreateDirectory(targetSfx);
                LinkOrCopy(sound.BankPath, Path.Combine(targetSfx, AcCarSound.BankFileName(cloneId)));
                File.WriteAllText(Path.Combine(targetSfx, AcCarSound.GuidsFileName), sound.GuidsFor(cloneId), Encoding.ASCII);
            }
            else
            {
                _logger.Warning("{Car} has no sound of its own to give its copy: {Clone} races without one", carId, cloneId);
            }

            _logger.Information("{Clone}: a copy of {Car} for the race ({Linked} file(s) linked, {Copied} copied, {Files} data file(s) changed, sound of {Sound})",
                cloneId, carId, linked, copied, files.Count, sound?.DonorId ?? "none");
        }

        /// <summary>
        /// Deletes a copy made for a race. Only a folder that is ours on every count is touched: its name ends with
        /// <see cref="AcCarFolder.CloneSuffix"/>, it sits right in the install's content\cars, and it has the marker.
        /// A user's own car that happens to contain a marker file (a copy kept as a car, a mod shipping the file) is
        /// left alone.
        /// </summary>
        public bool RemoveClone(string cloneId)
        {
            using var gate = AcInstallGate.Hold();
            if (!IsCloneId(cloneId)) return false;
            var folder = CarDirectory(cloneId);
            if (!Directory.Exists(folder) || !IsOurClone(folder)) return false;

            DeleteClone(folder);
            _logger.Information("{Clone}: the copy is gone", cloneId);
            return true;
        }

        /// <summary>
        /// Deletes a copy's folder, checked to be ours by the caller. Deleting a link leaves the car's own file where it
        /// is. The marker goes last, after every file and folder, so a delete cut short leaves a folder still known to
        /// be ours, and the next start finishes it.
        /// </summary>
        private static void DeleteClone(string folder)
        {
            var marker = Path.Combine(folder, AcCarFolder.CloneMarker);
            foreach (var file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
            {
                if (!file.Equals(marker, StringComparison.OrdinalIgnoreCase)) File.Delete(file);
            }

            foreach (var directory in Directory.GetDirectories(folder)) Directory.Delete(directory, true);
            File.Delete(marker);
            Directory.Delete(folder);
        }

        /// <summary>
        /// Every copy in the install, whichever race made it, and every recorded copy in an install the game ran on
        /// before (the AC folder in the settings changed since). Each folder is judged and deleted inside its own try:
        /// one that cannot be listed (a right denied, a scanner's lock) is told and left to the next start, and the
        /// others still go.
        /// </summary>
        /// <param name="problems">Gets a line for the player about each copy that could not be deleted</param>
        public int RemoveClones(ICollection<string>? problems = null)
        {
            using var gate = AcInstallGate.Hold();
            var removed = 0;
            if (Directory.Exists(_carsPath))
            {
                foreach (var folder in Directory.GetDirectories(_carsPath))
                {
                    try
                    {
                        if (IsOurClone(folder) && RemoveClone(Path.GetFileName(folder))) removed++;
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Could not delete the copy {Folder}; the next start tries again", folder);
                        problems?.Add(GameText.Say("launch.not_put_back.copy", ("folder", folder)));
                    }
                }
            }

            return removed + RemoveRecordedClones(problems);
        }

        /// <summary>
        /// The copies on record that are still there, wherever they are: each one only when it still looks like ours
        /// (named as a copy, in a "cars" folder, with the marker or empty). What is gone or not ours drops off the
        /// record; what could not be deleted (or looked into) stays on it for the next try.
        /// </summary>
        private int RemoveRecordedClones(ICollection<string>? problems)
        {
            var recorded = ReadClones();
            if (recorded.Count == 0) return 0;

            var removed = 0;
            var left = new List<string>();
            foreach (var folder in recorded)
            {
                try
                {
                    if (!Directory.Exists(folder)) continue;
                    if (!LooksLikeOurClone(folder))
                    {
                        _logger.Warning("{Folder} is on record as a race copy but is not one any more: it is left alone", folder);
                        continue;
                    }

                    DeleteClone(folder);
                    removed++;
                    _logger.Information("{Folder}: a copy left from an earlier race is gone", folder);
                }
                catch (Exception ex)
                {
                    left.Add(folder);
                    _logger.Error(ex, "Could not delete the copy {Folder}; the next start tries again", folder);
                    problems?.Add(GameText.Say("launch.not_put_back.copy", ("folder", folder)));
                }
            }

            try
            {
                WriteClones(left);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.Warning(ex, "The record of race copies {Path} could not be updated; what is gone drops off it next time", ClonesPath);
            }

            return removed;
        }

        private string ClonesPath => Path.Combine(_keepPath, ClonesFile);

        /// <summary>The copies on record; an unreadable record is logged and read as none (the install's own scan still runs)</summary>
        private List<string> ReadClones()
        {
            if (!File.Exists(ClonesPath)) return [];
            try
            {
                return JsonConvert.DeserializeObject<List<string>>(File.ReadAllText(ClonesPath))?
                    .Where(f => !string.IsNullOrWhiteSpace(f) && Path.IsPathFullyQualified(f))
                    .ToList() ?? [];
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                _logger.Error(ex, "The record of race copies {Path} is not readable; only the install's own copies are looked for", ClonesPath);
                return [];
            }
        }

        private void WriteClones(List<string> folders)
        {
            if (folders.Count == 0)
            {
                if (File.Exists(ClonesPath)) File.Delete(ClonesPath);
                return;
            }

            SafeFile.WriteAllText(ClonesPath, JsonConvert.SerializeObject(folders, Formatting.Indented));
        }

        private void RecordClone(string folder)
        {
            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
            var recorded = ReadClones();
            if (recorded.Contains(full, StringComparer.OrdinalIgnoreCase)) return;
            recorded.Add(full);
            WriteClones(recorded);
        }

        private static bool IsCloneId(string? id) =>
            PathNames.IsSafeSegment(id) && id!.EndsWith(AcCarFolder.CloneSuffix, StringComparison.OrdinalIgnoreCase)
            && id.Length > AcCarFolder.CloneSuffix.Length;

        /// <summary>
        /// A copy of ours: named as one, directly in content\cars, with the marker. Or with nothing in it at all: a copy
        /// cut short between its folder and its marker (nobody else makes an empty folder with that name, and deleting
        /// it loses nothing).
        /// </summary>
        private bool IsOurClone(string folder)
        {
            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
            var parent = Path.GetDirectoryName(full);
            return parent != null
                && string.Equals(parent, Path.TrimEndingDirectorySeparator(Path.GetFullPath(_carsPath)), StringComparison.OrdinalIgnoreCase)
                && LooksLikeOurClone(full);
        }

        /// <summary>
        /// A copy of ours in any install: named as one, in a "cars" folder, with the marker or empty. Listing a folder
        /// named as a copy can throw (a right denied): the caller's try per folder tells it, the other folders go on.
        /// </summary>
        private static bool LooksLikeOurClone(string folder)
        {
            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
            var parent = Path.GetDirectoryName(full);
            return IsCloneId(Path.GetFileName(full))
                && parent != null
                && string.Equals(Path.GetFileName(parent), "cars", StringComparison.OrdinalIgnoreCase)
                && (AcCarFolder.IsClone(full) || !Directory.EnumerateFileSystemEntries(full).Any());
        }

        private static bool IsUnder(string file, string folder) =>
            file.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
