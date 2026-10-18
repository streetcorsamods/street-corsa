using System.Windows.Threading;
using StreetCorsa.Logging;

namespace StreetCorsa.Helpers;

/// <summary>
/// The UI thread's queue, for what has to wait until what runs there now is over: a picker done with its pick, a property
/// told again after a binding pushed a value the code refused. The app's dispatcher, not the calling thread's: one off
/// the UI thread would never run it. The navigation's screen-bound <c>*Later</c> methods are built on this.
/// </summary>
public static class UiThread
{
    private static readonly IAppLogger Logger = AppLoggerFactory.CreateLogger(LogCategory.UI);

    /// <summary>Runs <paramref name="run"/> on the UI thread once what runs there now is over; what it throws is logged</summary>
    public static void Later(Action run, DispatcherPriority priority = DispatcherPriority.Normal)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _ = dispatcher.InvokeAsync(() =>
        {
            try
            {
                run();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "What was put off to later failed");
            }
        }, priority);
    }
}
