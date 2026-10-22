using System.Runtime.InteropServices;
using StreetCorsa.Logging;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Controls;

/// <summary>
/// The 3D views' troubles, told to the player instead of leaving a black screen: the runtimes SlimDX needs and Windows
/// does not ship (checked at start-up, as the installer's checks page does), and a 3D view that could not start (once a
/// session, the first one; the rest go to the log). A clean Windows in a sandbox showed only black backgrounds, with
/// the reason in the log alone (2026-10-22).
/// </summary>
public static class GraphicsTrouble
{
    private static readonly IAppLogger Logger = AppLoggerFactory.CreateLogger(LogCategory.Startup);
    private static int _told;

    /// <summary>The Visual C++ 2015-2022 runtime: SlimDX does not load without it</summary>
    public static readonly IReadOnlyList<string> VcRuntime = ["vcruntime140.dll", "msvcp140.dll"];

    /// <summary>DirectX's June 2010 runtime, which Windows 10 and 11 never had: SlimDX loads it when the 3D starts</summary>
    public static readonly IReadOnlyList<string> DirectXJune2010 = ["d3dx11_43.dll", "D3DCompiler_43.dll"];

    /// <summary>The runtimes' files that do not load here, by <paramref name="loads"/> (the system's loader when not given)</summary>
    public static List<string> MissingRuntimes(Func<string, bool>? loads = null)
    {
        loads ??= Loads;
        return VcRuntime.Concat(DirectXJune2010).Where(file => !loads(file)).ToList();
    }

    private static bool Loads(string file)
    {
        if (!NativeLibrary.TryLoad(file, out var handle)) return false;
        NativeLibrary.Free(handle);
        return true;
    }

    /// <summary>The player's message for <paramref name="missing"/>: which runtime to install and where from; null when none is</summary>
    public static (string Text, string Title)? RuntimesMessage(IReadOnlyCollection<string> missing)
    {
        if (missing.Count == 0) return null;

        var runtimes = new List<string>();
        if (missing.Any(f => VcRuntime.Contains(f, StringComparer.OrdinalIgnoreCase))) runtimes.Add(GameText.Say("graphics.runtime.vc"));
        if (missing.Any(f => DirectXJune2010.Contains(f, StringComparer.OrdinalIgnoreCase))) runtimes.Add(GameText.Say("graphics.runtime.directx"));

        var text = GameText.Say("graphics.runtimes_missing",
            ("runtimes", string.Join("\n", runtimes.Select(r => "  " + r))),
            ("files", string.Join(", ", missing)));
        return (text, GameText.Say("graphics.runtimes_missing.title"));
    }

    /// <summary>Checks the runtimes once at start-up and tells the player what is missing; logged either way</summary>
    public static void CheckRuntimes(Action<string, string> show)
    {
        var missing = MissingRuntimes();
        if (missing.Count == 0) return;

        Logger.Warning("Missing the runtimes the 3D needs: {Files}", string.Join(", ", missing));
        if (RuntimesMessage(missing) is { } message) show(message.Text, message.Title);
    }

    /// <summary>
    /// A 3D view could not start or went on failing (<paramref name="why"/>): the player is told once a session, with
    /// the runtimes missing if any are, and where the log is
    /// </summary>
    public static void ViewFailed(string view, string why)
    {
        if (Interlocked.Exchange(ref _told, 1) == 1) return;
        if (System.Windows.Application.Current is not App app || app.DialogService == null) return;

        var missing = MissingRuntimes();
        var text = GameText.Say("graphics.view_failed",
            ("error", why),
            ("missing", missing.Count == 0 ? string.Empty : "\n\n" + RuntimesMessage(missing)!.Value.Text),
            ("log", AppLoggerFactory.LogDirectory));
        Logger.Warning("Told the player the 3D view {View} could not start: {Why}", view, why);
        _ = app.Dispatcher.BeginInvoke(() => app.DialogService.ShowDialog(
            new Dialogs.Information.InformationDialogViewModel(app.DialogService, text, GameText.Say("graphics.view_failed.title"))));
    }
}
