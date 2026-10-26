using System.Diagnostics;

namespace StreetCorsa.Services
{
    /// <summary>
    /// The game's processes as the launcher sees them: whether any runs, the one it starts, the ones it waits on and the
    /// ones it kills. The real ones by default (<see cref="AcProcesses"/>, <see cref="Process"/>); a test puts its own in
    /// to run the launch from start to finish without Assetto Corsa.
    /// </summary>
    public class AcProcessHost
    {
        /// <summary>The machine's processes</summary>
        public static AcProcessHost System { get; } = new();

        /// <inheritdoc cref="AcProcesses.QueryAnyRunning"/>
        public virtual bool? QueryAnyRunning() => AcProcesses.QueryAnyRunning();

        /// <summary>Starts the game; null when no process came of it</summary>
        public virtual IGameProcess? Start(ProcessStartInfo startInfo) =>
            Process.Start(startInfo) is { } process ? new SystemProcess(process) : null;

        /// <summary>The processes of that name running now; throws when they cannot be listed</summary>
        public virtual IReadOnlyList<IGameProcess> ByName(string processName) =>
            [.. Process.GetProcessesByName(processName).Select(p => new SystemProcess(p))];

        /// <inheritdoc cref="AcProcesses.KillAll"/>
        public virtual int KillAll(string processName) => AcProcesses.KillAll(processName);

        /// <summary>A <see cref="Process"/> of the machine's</summary>
        private sealed class SystemProcess(Process process) : IGameProcess
        {
            public int Id => process.Id;

            public bool HasExited => process.HasExited;

            public int ExitCode => process.ExitCode;

            public Task WaitForExitAsync(CancellationToken cancellationToken) => process.WaitForExitAsync(cancellationToken);

            public void Kill() => process.Kill(entireProcessTree: true);

            public void Dispose() => process.Dispose();
        }
    }

    /// <summary>A game process the launcher started or waits on, as much of <see cref="Process"/> as it uses</summary>
    public interface IGameProcess : IDisposable
    {
        int Id { get; }

        /// <summary>Throws <see cref="InvalidOperationException"/> once disposed, as <see cref="Process.HasExited"/> does</summary>
        bool HasExited { get; }

        int ExitCode { get; }

        Task WaitForExitAsync(CancellationToken cancellationToken);

        /// <summary>Kills it with its children</summary>
        void Kill();
    }
}
