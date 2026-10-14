using System.Text;

namespace VpsManager.Core.Services;

/// <summary>
/// Service for managing SSH terminal sessions using SSH.NET.
/// Note: This provides command execution rather than full PTY terminal emulation.
/// </summary>
public class TerminalService : ITerminalService, IDisposable
{
    private readonly ISshService _sshService;
    private readonly StringBuilder _outputBuffer = new();
    private bool _isSessionActive = false;
    private string _currentWorkingDirectory = string.Empty;

    public bool IsSessionActive => _isSessionActive;
    public string CurrentWorkingDirectory => _currentWorkingDirectory;

    public event EventHandler<string>? OutputReceived;

    public TerminalService(ISshService sshService)
    {
        _sshService = sshService ?? throw new ArgumentNullException(nameof(sshService));
    }

    public async Task StartTerminalSessionAsync(string workingDirectory = "", CancellationToken cancellationToken = default)
    {
        if (_isSessionActive)
            throw new InvalidOperationException("Terminal session is already active.");

        if (!_sshService.IsConnected)
            throw new InvalidOperationException("SSH connection is not established.");

        try
        {
            _currentWorkingDirectory = workingDirectory;
            _isSessionActive = true;
            _outputBuffer.Clear();

            // Validate or resolve working directory if provided
            var displayDir = string.IsNullOrEmpty(workingDirectory) ? "~" : workingDirectory;
            var welcome = $"[Connected] SSH terminal session started. Working directory: {displayDir}\n";
            _outputBuffer.Append(welcome);
            OutputReceived?.Invoke(this, welcome);

            if (!string.IsNullOrEmpty(workingDirectory))
            {
                var checkDir = await _sshService.ExecuteCommandAsync($"cd \"{workingDirectory}\" && pwd", cancellationToken);
                if (!string.IsNullOrWhiteSpace(checkDir.Output))
                {
                    _currentWorkingDirectory = checkDir.Output.Trim();
                }
            }
        }
        catch (Exception ex)
        {
            _isSessionActive = false;
            throw new InvalidOperationException($"Failed to start terminal session: {ex.Message}", ex);
        }
    }

    public async Task<string> ExecuteCommandAsync(string command, CancellationToken cancellationToken = default)
    {
        if (!_isSessionActive)
            throw new InvalidOperationException("No active terminal session.");

        if (string.IsNullOrWhiteSpace(command))
            return string.Empty;

        try
        {
            // Add command prompt line to output for visibility
            var promptPath = string.IsNullOrEmpty(_currentWorkingDirectory) ? "~" : _currentWorkingDirectory;
            var commandPrompt = $"{promptPath} $ {command}\n";
            _outputBuffer.Append(commandPrompt);
            OutputReceived?.Invoke(this, commandPrompt);

            var trimmed = command.Trim();
            if (trimmed == "cd" || trimmed == "cd ~")
            {
                var result = await _sshService.ExecuteCommandAsync("cd ~ && pwd", cancellationToken);
                if (!string.IsNullOrWhiteSpace(result.Output))
                {
                    _currentWorkingDirectory = result.Output.Trim();
                }
                return string.Empty;
            }
            else if (trimmed.StartsWith("cd ", StringComparison.OrdinalIgnoreCase))
            {
                var targetDir = trimmed[3..].Trim().Trim('"', '\'');
                string cdCmd = !string.IsNullOrEmpty(_currentWorkingDirectory) && !targetDir.StartsWith('/') && !targetDir.StartsWith('~')
                    ? $"cd \"{_currentWorkingDirectory}\" && cd {targetDir} && pwd"
                    : $"cd {targetDir} && pwd";

                var result = await _sshService.ExecuteCommandAsync(cdCmd, cancellationToken);
                if (!string.IsNullOrEmpty(result.Error))
                {
                    _outputBuffer.Append(result.Error + "\n");
                    OutputReceived?.Invoke(this, result.Error + "\n");
                    return result.Error;
                }
                else
                {
                    _currentWorkingDirectory = result.Output.Trim();
                    return string.Empty;
                }
            }

            // Execute standard command in the current working directory
            var remoteCmd = !string.IsNullOrEmpty(_currentWorkingDirectory)
                ? $"cd \"{_currentWorkingDirectory}\" && {command}"
                : command;

            var execResult = await _sshService.ExecuteCommandAsync(remoteCmd, cancellationToken);

            if (!string.IsNullOrEmpty(execResult.Output))
            {
                _outputBuffer.Append(execResult.Output);
                OutputReceived?.Invoke(this, execResult.Output);
            }

            if (!string.IsNullOrEmpty(execResult.Error))
            {
                _outputBuffer.Append(execResult.Error);
                OutputReceived?.Invoke(this, execResult.Error);
            }

            _outputBuffer.Append("\n");
            OutputReceived?.Invoke(this, "\n");

            return execResult.Output + execResult.Error;
        }
        catch (OperationCanceledException)
        {
            var canceled = "^C\n[Command interrupted by user]\n";
            _outputBuffer.Append(canceled);
            OutputReceived?.Invoke(this, canceled);
            throw;
        }
        catch (Exception ex)
        {
            var error = $"Error executing command: {ex.Message}\n";
            _outputBuffer.Append(error);
            OutputReceived?.Invoke(this, error);
            throw;
        }
    }

    public Task SendInputAsync(string input, CancellationToken cancellationToken = default)
    {
        // For command-based terminal, we treat input as a command
        return ExecuteCommandAsync(input, cancellationToken);
    }

    public Task<string> GetOutputAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_outputBuffer.ToString());
    }

    public Task ClearOutputAsync(CancellationToken cancellationToken = default)
    {
        _outputBuffer.Clear();
        var cleared = "Terminal output cleared.\n";
        _outputBuffer.Append(cleared);
        OutputReceived?.Invoke(this, cleared);
        return Task.CompletedTask;
    }

    public Task StopTerminalSessionAsync(CancellationToken cancellationToken = default)
    {
        if (!_isSessionActive)
            return Task.CompletedTask;

        _isSessionActive = false;

        var goodbye = "Terminal session ended.\n";
        _outputBuffer.Append(goodbye);
        OutputReceived?.Invoke(this, goodbye);

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        StopTerminalSessionAsync().GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }
}