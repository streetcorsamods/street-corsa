using System.IO;
using System.Text;
using StreetCorsa.Parts.Export;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Services
{
    /// <summary>
    /// The sound half of the overlay: the car's own bank and GUIDs into the keep folder, the race's in under the car's
    /// name, and the car's own back after (see the class summary for the rule)
    /// </summary>
    public partial class CarDataOverlay
    {
        /// <summary>Where a car's own sound waits inside its sfx folder while a race runs on another</summary>
        public const string SfxKeepFolder = AcCarSound.KeepFolder;

        /// <summary>
        /// The car's own bank and GUIDs into the keep folder, each marked in the manifest once it is there, then the race's
        /// sound in their place
        /// </summary>
        private void ApplySound(string carId, string keep, Manifest manifest, string sfxDirectory, CarSound sound)
        {
            var keepFolder = Path.Combine(sfxDirectory, SfxKeepFolder);
            Directory.CreateDirectory(keepFolder);

            var sfx = manifest.Sfx!;
            var bank = Path.Combine(sfxDirectory, AcCarSound.BankFileName(carId));
            var guids = Path.Combine(sfxDirectory, AcCarSound.GuidsFileName);
            if (Keep(carId, bank, Path.Combine(keepFolder, AcCarSound.BankFileName(carId))))
            {
                sfx.BankKept = true;
                WriteManifest(keep, manifest);
            }

            if (Keep(carId, guids, Path.Combine(keepFolder, AcCarSound.GuidsFileName)))
            {
                sfx.GuidsKept = true;
                WriteManifest(keep, manifest);
            }

            var linked = LinkOrCopy(sound.BankPath, bank);

            // A plain write is enough: the car's own GUIDs were moved into the keep folder just now, so this is a new
            // file of ours, linked to nothing, and the restore moves the car's own back over it
            File.WriteAllText(guids, sound.GuidsFor(carId), Encoding.ASCII);

            _logger.Information("{Car}: races on the sound of {Donor} ({Bank}, {How})", carId, sound.DonorId, sound.BankPath, linked ? "linked" : "copied");
        }

        /// <summary>
        /// Moves a file of the car's sound into the keep folder. A file already there is the car's own, left by a race
        /// whose manifest was lost: the one in place is what that race put in, and it is the one that goes, never the
        /// only copy of the car's own. True when the car's own is in the keep folder after it.
        /// </summary>
        private bool Keep(string carId, string file, string kept)
        {
            if (File.Exists(kept))
            {
                if (!File.Exists(file)) return true;
                _logger.Warning("{Car}: {File} was already kept from an earlier race; the one in its place is not the car's and goes", carId, Path.GetFileName(file));
                File.Delete(file);
                return true;
            }

            if (!File.Exists(file)) return false;
            File.Move(file, kept);
            return true;
        }

        /// <summary>
        /// The sound as it can go in right now, or null for none. A donor car that races on another sound itself has its
        /// own bank in its keep folder, and the file under its name is the other sound's (the GUID text is always the
        /// donor's own), so the bank is taken from where its bytes are. A bank that is gone (the car uninstalled, the
        /// library folder removed while the game ran) leaves the car on its own sound.
        /// </summary>
        private CarSound? Usable(string carId, CarSound? sound)
        {
            if (sound == null) return null;

            var bank = AcCarSound.OwnBank(sound.BankPath);
            if (bank == null)
            {
                _logger.Warning("{Car}: the bank of {Donor} is gone ({Bank}); it keeps its own sound", carId, sound.DonorId, sound.BankPath);
                return null;
            }

            return bank == sound.BankPath ? sound : sound with { BankPath = bank };
        }

        /// <summary>
        /// The link goes, the originals come back out of the keep folder. Every step checks what is there, so a
        /// restore that was cut short finishes the next time. A kept original that is gone (a cleaner emptied the keep
        /// folder during the race) can never go back: the race's file stays under the car's name, and that is logged
        /// and told to the player like a data file's, not retried forever. One the manifest never marked as kept did not
        /// leave its place (the race failed before it got that far): it is the car's own, and stays.
        /// </summary>
        /// <param name="problems">Gets a line for the player about each file whose original is lost for good</param>
        private void RestoreSound(string carId, string sfxDirectory, Manifest.SfxEntry sfx, ICollection<string>? problems)
        {
            var bank = Path.Combine(sfxDirectory, AcCarSound.BankFileName(carId));
            var guids = Path.Combine(sfxDirectory, AcCarSound.GuidsFileName);
            var keepFolder = Path.Combine(sfxDirectory, SfxKeepFolder);
            var keptBank = Path.Combine(keepFolder, AcCarSound.BankFileName(carId));
            var keptGuids = Path.Combine(keepFolder, AcCarSound.GuidsFileName);

            if (File.Exists(keptBank)) File.Move(keptBank, bank, true);
            else if (sfx.BankKept) Lost(carId, bank, problems);
            else if (!sfx.BankExisted && File.Exists(bank)) File.Delete(bank);

            if (File.Exists(keptGuids)) File.Move(keptGuids, guids, true);
            else if (sfx.GuidsKept) Lost(carId, guids, problems);
            else if (!sfx.GuidsExisted && File.Exists(guids)) File.Delete(guids);

            if (Directory.Exists(keepFolder) && !Directory.EnumerateFileSystemEntries(keepFolder).Any()) Directory.Delete(keepFolder);
            if (sfx.CreatedFolder && Directory.Exists(sfxDirectory) && !Directory.EnumerateFileSystemEntries(sfxDirectory).Any()) Directory.Delete(sfxDirectory);

            _logger.Information("{Car}: its own sound is back", carId);
        }

        /// <summary>A sound file the car had whose kept copy is gone: told, with the race's file left where it is</summary>
        private void Lost(string carId, string target, ICollection<string>? problems)
        {
            var name = Path.GetFileName(target);
            _logger.Error("{Car}: the kept original of {File} is gone (deleted outside the game?); the race version stays in {Target}", carId, name, target);
            problems?.Add(GameText.Say("launch.not_put_back.original_gone", ("car", carId), ("file", name), ("folder", target)));
        }
    }
}
