using StreetCorsa.Audio;
using StreetCorsa.Parts.Sounds;
using StreetCorsa.ViewModels;

namespace StreetCorsa.Screens.SoundBrowser
{
    /// <summary>A folder of sounds, or a family of engine sounds: one line of the browser's left-hand list</summary>
    public sealed class SoundCategory
    {
        public SoundCategory(string title, IReadOnlyList<SoundItem> files)
        {
            Title = title;
            Files = files;
            Engines = [];
        }

        public SoundCategory(string title, IReadOnlyList<EngineItem> engines)
        {
            Title = title;
            Files = [];
            Engines = engines;
        }

        public string Title { get; }

        public IReadOnlyList<SoundItem> Files { get; }

        public IReadOnlyList<EngineItem> Engines { get; }

        public bool IsEngines => Engines.Count > 0;

        public int Count => IsEngines ? Engines.Count : Files.Count;
    }

    /// <summary>A sound file in the list: what it is, who made it, and whether it is playing or kept</summary>
    public sealed class SoundItem(SoundFile file, bool kept) : ObservableObject
    {
        private bool _isKept = kept;
        private bool _isPlaying;
        private string _length = "";

        public SoundFile File { get; } = file;

        public string Title => File.Title;

        public string Detail => string.Join(" · ", new[] { File.Author, File.License, System.IO.Path.GetFileName(File.FullPath) }
            .Where(s => !string.IsNullOrEmpty(s)));

        public bool IsKept
        {
            get => _isKept;
            set => SetProperty(ref _isKept, value);
        }

        public bool IsPlaying
        {
            get => _isPlaying;
            set => SetProperty(ref _isPlaying, value);
        }

        /// <summary>How long it runs, once it has been played and FMOD has said</summary>
        public string Length
        {
            get => _length;
            set => SetProperty(ref _length, value);
        }

        public bool Matches(string filter) =>
            File.Title.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || File.RelativePath.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || (File.Author?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    /// <summary>An engine sound out of the library: a bank to start and rev</summary>
    public sealed class EngineItem(SoundEntry entry)
    {
        public SoundEntry Entry { get; } = entry;

        public string Title => Entry.Name;

        public string Detail => string.Join(" · ", new[]
        {
            Entry.Curated ? "curated" : "from an AC car",
            Entry.Cylinders is { } cylinders ? $"{cylinders} cylinders" : null,
            Entry.RpmMax is { } rpm ? $"to {rpm:0} rpm" : null,
            Entry.Carriers.Count > 0 ? $"{Entry.Carriers.Count} car{(Entry.Carriers.Count == 1 ? "" : "s")}" : null,
            Entry.Id
        }.Where(s => s != null));

        public bool Matches(string filter) =>
            Entry.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || Entry.Id.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || Entry.Carriers.Any(c => c.Contains(filter, StringComparison.OrdinalIgnoreCase));
    }
}
