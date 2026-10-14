using Renci.SshNet;
using Renci.SshNet.Common;
using VpsManager.Core.Models;
using VpsManager.Core.Security;

namespace VpsManager.Core.Services;

public class SshService : ISshService
{
    private static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(30);

    private readonly IHostKeyVerifier _hostKeyVerifier;

    private SshClient? _sshClient;
    private ScpClient? _scpClient;
    private SshConnectionConfig? _currentConfig;
    private string? _hostKeyRejection;
    private bool _connectionLostRaised;

    public SshService(IHostKeyVerifier? hostKeyVerifier = null)
    {
        // Default posture is fail-closed: unknown hosts are rejected until the host key is
        // explicitly trusted through a prompt (see HostKeyPolicy).
        _hostKeyVerifier = hostKeyVerifier ?? new HostKeyPolicy(new JsonKnownHostsStore());
    }

    public bool IsConnected => !_connectionLostRaised && _sshClient?.IsConnected == true;

    /// <summary>Fingerprint of the key this session is running against, once connected.</summary>
    public string? ConnectedHostKeyFingerprint { get; private set; }

    public event EventHandler? ConnectionLost;

    public async Task ConnectAsync(SshConnectionConfig config, CancellationToken cancellationToken = default)
    {
        _currentConfig = config ?? throw new ArgumentNullException(nameof(config));
        Disconnect();

        _hostKeyRejection = null;
        _connectionLostRaised = false;
        ConnectedHostKeyFingerprint = null;

        ConnectionInfo connectionInfo = CreateConnectionInfo(config);

        _sshClient = new SshClient(connectionInfo)
        {
            KeepAliveInterval = KeepAliveInterval
        };
        _scpClient = new ScpClient(connectionInfo, RemotePathTransformation.ShellQuote)
        {
            KeepAliveInterval = KeepAliveInterval
        };

        _sshClient.HostKeyReceived += OnHostKeyReceived;
        _sshClient.ErrorOccurred += OnClientErrorOccurred;
        _scpClient.ErrorOccurred += OnClientErrorOccurred;

        try
        {
            await Task.Run(() =>
            {
                _sshClient.Connect();
                _scpClient.Connect();
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            Disconnect();
            if (_hostKeyRejection != null)
            {
                throw new HostKeyVerificationException(_hostKeyRejection);
            }

            throw ex is OperationCanceledException
                ? ex
                : new InvalidOperationException($"SSH connection to {config.Host}:{config.Port} failed: {ex.Message}", ex);
        }

        if (!_sshClient.IsConnected)
        {
            Disconnect();
            throw new InvalidOperationException($"SSH connection to {config.Host}:{config.Port} was not established.");
        }
    }

    public void Disconnect()
    {
        if (_sshClient != null)
        {
            _sshClient.HostKeyReceived -= OnHostKeyReceived;
            _sshClient.ErrorOccurred -= OnClientErrorOccurred;
        }

        if (_scpClient != null)
        {
            _scpClient.ErrorOccurred -= OnClientErrorOccurred;
        }

        try
        {
            if (_scpClient?.IsConnected == true)
            {
                _scpClient.Disconnect();
            }
        }
        catch (Exception ex) when (ex is SshException or ObjectDisposedException)
        {
        }
        finally
        {
            _scpClient?.Dispose();
            _scpClient = null;
        }

        try
        {
            if (_sshClient?.IsConnected == true)
            {
                _sshClient.Disconnect();
            }
        }
        catch (Exception ex) when (ex is SshException or ObjectDisposedException)
        {
        }
        finally
        {
            _sshClient?.Dispose();
            _sshClient = null;
        }
    }

    public async Task<CommandResult> ExecuteCommandAsync(string commandText, CancellationToken cancellationToken = default)
    {
        var client = _sshClient;
        if (client == null || !client.IsConnected)
        {
            throw new InvalidOperationException("SSH client is not connected.");
        }

        try
        {
            return await Task.Run(() =>
            {
                using var cmd = client.CreateCommand(commandText);
                cmd.Execute();
                return new CommandResult
                {
                    ExitCode = cmd.ExitStatus ?? -1,
                    Output = cmd.Result,
                    Error = cmd.Error
                };
            }, cancellationToken);
        }
        catch (Exception ex) when (IsConnectionLevelFailure(ex))
        {
            MarkConnectionLost();
            throw new InvalidOperationException(
                "The SSH connection was lost while running a remote command. Reconnect to continue.", ex);
        }
    }

    public async Task<CommandResult> ExecuteCommandWithInputAsync(
        string commandText,
        ReadOnlyMemory<byte> standardInput,
        CancellationToken cancellationToken = default)
    {
        var client = _sshClient;
        if (client == null || !client.IsConnected)
        {
            throw new InvalidOperationException("SSH client is not connected.");
        }

        try
        {
            using var command = client.CreateCommand(commandText);
            var execution = command.ExecuteAsync(cancellationToken);
            using (var input = command.CreateInputStream())
            {
                await input.WriteAsync(standardInput, cancellationToken);
            }

            await execution;
            return new CommandResult
            {
                ExitCode = command.ExitStatus ?? -1,
                Output = command.Result,
                Error = command.Error
            };
        }
        catch (Exception ex) when (IsConnectionLevelFailure(ex))
        {
            MarkConnectionLost();
            throw new InvalidOperationException(
                "The SSH connection was lost while streaming command input. Reconnect to continue.", ex);
        }
    }

    public async Task UploadFileAsync(Stream source, string remotePath, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var client = _scpClient;
        if (client == null || !client.IsConnected)
        {
            throw new InvalidOperationException("SCP client is not connected.");
        }

        try
        {
            await Task.Run(() =>
            {
                client.Upload(source, remotePath);
            }, cancellationToken);
        }
        catch (Exception ex) when (IsConnectionLevelFailure(ex))
        {
            MarkConnectionLost();
            throw new InvalidOperationException(
                "The SSH connection was lost while uploading a file. Reconnect to continue.", ex);
        }
    }

    public async Task DownloadFileAsync(string remotePath, Stream destination, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var client = _scpClient;
        if (client == null || !client.IsConnected)
        {
            throw new InvalidOperationException("SCP client is not connected.");
        }

        try
        {
            await Task.Run(() =>
            {
                client.Download(remotePath, destination);
            }, cancellationToken);
        }
        catch (Exception ex) when (IsConnectionLevelFailure(ex))
        {
            MarkConnectionLost();
            throw new InvalidOperationException(
                "The SSH connection was lost while downloading a file. Reconnect to continue.", ex);
        }
    }

    private void OnHostKeyReceived(object? sender, HostKeyEventArgs e)
    {
        var host = _currentConfig?.Host ?? "unknown";
        var port = _currentConfig?.Port ?? 0;
        var info = new HostKeyInfo(host, port, e.HostKeyName ?? string.Empty, HostKeyInfo.ComputeFingerprint(e.HostKey));

        var decision = _hostKeyVerifier.Evaluate(info);
        e.CanTrust = decision.Allow;

        if (decision.Allow)
        {
            ConnectedHostKeyFingerprint = info.Fingerprint;
        }
        else
        {
            _hostKeyRejection = decision.Reason;
        }
    }

    private void OnClientErrorOccurred(object? sender, ExceptionEventArgs e)
    {
        if (IsConnectionLevelFailure(e.Exception))
        {
            MarkConnectionLost();
        }
    }

    private void MarkConnectionLost()
    {
        if (_connectionLostRaised)
        {
            return;
        }

        _connectionLostRaised = true;
        ConnectionLost?.Invoke(this, EventArgs.Empty);
    }

    private static bool IsConnectionLevelFailure(Exception exception)
    {
        return exception switch
        {
            SshConnectionException => true,
            SshOperationTimeoutException => true,
            System.Net.Sockets.SocketException => true,
            ObjectDisposedException => true,
            IOException => true,
            _ => false
        };
    }

    private static ConnectionInfo CreateConnectionInfo(SshConnectionConfig config)
    {
        var authMethods = new List<AuthenticationMethod>();

        if (!string.IsNullOrEmpty(config.PrivateKeyPath) && File.Exists(config.PrivateKeyPath))
        {
            var keyFile = string.IsNullOrEmpty(config.Passphrase)
                ? new PrivateKeyFile(config.PrivateKeyPath)
                : new PrivateKeyFile(config.PrivateKeyPath, config.Passphrase);

            authMethods.Add(new PrivateKeyAuthenticationMethod(config.Username, keyFile));
        }

        if (!string.IsNullOrEmpty(config.Password))
        {
            authMethods.Add(new PasswordAuthenticationMethod(config.Username, config.Password));
        }

        if (authMethods.Count == 0)
        {
            throw new ArgumentException("No valid authentication credentials provided.");
        }

        return new ConnectionInfo(config.Host, config.Port, config.Username, authMethods.ToArray());
    }

    public void Dispose()
    {
        Disconnect();
        GC.SuppressFinalize(this);
    }
}
