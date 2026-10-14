namespace VpsManager.Core.Services;

/// <summary>
/// Service for managing interactive SSH terminal sessions.
/// </summary>
public interface ITerminalService
{
    /// <summary>
    /// Start an interactive terminal session.
    /// </summary>
    Task StartTerminalSessionAsync(string workingDirectory = "", CancellationToken cancellationToken = default);

    /// <summary>
    /// Execute a command in the terminal session.
    /// </summary>
    Task<string> ExecuteCommandAsync(string command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Send input to the terminal (for interactive commands).
    /// </summary>
    Task SendInputAsync(string input, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the current terminal output.
    /// </summary>
    Task<string> GetOutputAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Clear the terminal output.
    /// </summary>
    Task ClearOutputAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stop the terminal session.
    /// </summary>
    Task StopTerminalSessionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a terminal session is active.
    /// </summary>
    bool IsSessionActive { get; }

    /// <summary>
    /// Current working directory of the terminal session.
    /// </summary>
    string CurrentWorkingDirectory { get; }

    /// <summary>
    /// Event raised when new output is available from the terminal.
    /// </summary>
    event EventHandler<string>? OutputReceived;
}