using VpsManager.Core.Models;

namespace VpsManager.Core.Services;

public interface ISshService : IDisposable
{
    bool IsConnected { get; }

    /// <summary>Fingerprint of the verified server key for the current session, when connected.</summary>
    string? ConnectedHostKeyFingerprint { get; }

    /// <summary>
    /// Raised when an established session drops (socket failure, timeout, or keep-alive loss)
    /// so callers can stop reporting the server as connected.
    /// </summary>
    event EventHandler? ConnectionLost;

    Task ConnectAsync(SshConnectionConfig config, CancellationToken cancellationToken = default);
    void Disconnect();
    Task<CommandResult> ExecuteCommandAsync(string commandText, CancellationToken cancellationToken = default);
    Task<CommandResult> ExecuteCommandWithInputAsync(string commandText, ReadOnlyMemory<byte> standardInput, CancellationToken cancellationToken = default);
    Task UploadFileAsync(Stream source, string remotePath, IProgress<double>? progress = null, CancellationToken cancellationToken = default);
    Task DownloadFileAsync(string remotePath, Stream destination, IProgress<double>? progress = null, CancellationToken cancellationToken = default);
}
