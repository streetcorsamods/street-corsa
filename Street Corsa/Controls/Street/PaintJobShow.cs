using StreetCorsa.Parts.Cars;

namespace StreetCorsa.Controls.Street;

/// <summary>
/// What the paint booth's screen has its viewport show of the job (<see cref="PaintBoothViewport3D"/>): what the car
/// wears (a skin of its mod, a colour of the player's own over it) while the player picks, the painter going round it
/// while he sprays, and each coat as it goes on. Told on the UI thread; the viewport listens while it is loaded.
/// </summary>
public sealed class PaintJobShow
{
    /// <summary>The car's skin (null: its first) and what it shows (the paint's colour in it)</summary>
    public string? Skin { get; private set; }

    public ShownSet? Shown { get; private set; }

    /// <summary>The painter is at work, for <see cref="SprayTime"/> from when he started</summary>
    public bool IsSpraying { get; private set; }

    public TimeSpan SprayTime { get; private set; }

    /// <summary>The last coat put on: the paint's textures as they are this far into the job</summary>
    public IReadOnlyList<(string Texture, byte[] Bytes)>? Coat { get; private set; }

    /// <summary>The car is to wear something else</summary>
    public event Action? LookChanged;

    /// <summary>The painter starts or stops</summary>
    public event Action? SprayChanged;

    /// <summary>A coat went on</summary>
    public event Action? Coated;

    /// <summary>The car in its skin <paramref name="skin"/>, showing <paramref name="shown"/></summary>
    public void Wear(string? skin, ShownSet? shown)
    {
        Skin = skin;
        Shown = shown;
        Coat = null;
        LookChanged?.Invoke();
    }

    /// <summary>The painter goes round the car with his gun, over <paramref name="time"/></summary>
    public void Spray(TimeSpan time)
    {
        IsSpraying = true;
        SprayTime = time;
        SprayChanged?.Invoke();
    }

    /// <summary>The painter puts his gun down and goes back to where he waits</summary>
    public void StopSpraying()
    {
        if (!IsSpraying) return;
        IsSpraying = false;
        SprayChanged?.Invoke();
    }

    /// <summary>A coat of the job on the car: its paint's textures as <paramref name="textures"/> have them</summary>
    public void PutCoat(IReadOnlyList<(string Texture, byte[] Bytes)> textures)
    {
        Coat = textures;
        Coated?.Invoke();
    }
}
