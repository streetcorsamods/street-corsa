using System.Windows.Input;
using StreetCorsa.Logging;

namespace StreetCorsa.ViewModels;

/// <summary>
/// A simple implementation of ICommand for async operations in ViewModels.
///
/// WPF runs a command as a fire-and-forget call, so an exception out of the task would reach the dispatcher
/// and end the game. Execute catches it: it is logged, and handed to <c>onError</c> when the owner wants to
/// tell the player in its own words, else to <see cref="ReportFailure"/>, so that a failed sale or repair is
/// never silent.
/// </summary>
public class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;
    private readonly Action<Exception>? _onError;
    private readonly string _name;
    private bool _isExecuting;

    /// <summary>
    /// How a command without an <c>onError</c> of its own tells the player it failed. Set once by the composition
    /// root (App) to the game's "Something went wrong" dialog: the commands are built all over the view models,
    /// which should not each need the dialog service for a failure they cannot do anything about. Unset (the
    /// tests), a failure is only logged.
    /// </summary>
    public static Action<Exception>? ReportFailure { get; set; }

    /// <param name="onError">Called on the UI thread with whatever the command threw, after it is logged; the
    /// default is <see cref="ReportFailure"/></param>
    /// <param name="name">What the log calls the command; the method's name when left out (a lambda's is not readable)</param>
    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null, Action<Exception>? onError = null, string? name = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
        _onError = onError;
        _name = name ?? execute.Method.Name;
    }

    public event EventHandler? CanExecuteChanged
    {
        add { CommandManager.RequerySuggested += value; }
        remove { CommandManager.RequerySuggested -= value; }
    }

    public bool CanExecute(object? parameter)
    {
        return !_isExecuting && (_canExecute == null || _canExecute());
    }

    public async void Execute(object? parameter)
    {
        if (_isExecuting)
            return;

        _isExecuting = true;
        RaiseCanExecuteChanged();

        try
        {
            await _execute();
        }
        catch (Exception ex)
        {
            var logger = AppLoggerFactory.CreateLogger(LogCategory.UI);
            logger.Error(ex, "A command failed: {Command}", _name);

            try
            {
                (_onError ?? ReportFailure)?.Invoke(ex);
            }
            catch (Exception inner)
            {
                logger.Error(inner, "The error handler of {Command} failed as well", _name);
            }
        }
        finally
        {
            _isExecuting = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged()
    {
        CommandManager.InvalidateRequerySuggested();
    }
}
