using VpsManager.Core.Models;
using VpsManager.Core.Security;

namespace VpsManager.Core.Services.Session;

public enum SessionState
{
    Disconnected,
    Connecting,
    Connected,
    CredentialsRequired,
    Failed
}

public sealed class SessionStateChangedEventArgs : EventArgs
{
    public required SessionState State { get; init; }

    public required string StatusText { get; init; }

    public string? Detail { get; init; }

    public string? HostKeyFingerprint { get; init; }
}

/// <summary>
/// The single owner of the SSH session. View models observe this instead of connecting
/// themselves, so connection state has exactly one source of truth.
/// </summary>
public interface ISessionCoordinator
{
    SessionState State { get; }

    string StatusText { get; }

    string? Detail { get; }

    string? HostKeyFingerprint { get; }

    VpsProfile? ActiveProfile { get; }

    bool IsConnected { get; }

    bool CanRememberCredentials { get; }

    bool HasStoredCredential(VpsProfile profile);

    event EventHandler<SessionStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// Connects to a profile using the supplied password, falling back to a stored credential.
    /// Returns false when credentials are missing or the connection failed; the state and
    /// detail explain why.
    /// </summary>
    Task<bool> ConnectAsync(VpsProfile profile, string? password = null, bool rememberPassword = false, CancellationToken cancellationToken = default);

    Task DisconnectAsync(string? reason = null, CancellationToken cancellationToken = default);

    /// <summary>Reports that credentials are needed without attempting a connection.</summary>
    void RequireCredentials(string detail);
}

public sealed class SessionCoordinator : ISessionCoordinator
{
    private readonly ISshService _sshService;
    private readonly ICredentialStore _credentialStore;
    private readonly SemaphoreSlim _connectionGate = new(1, 1);
    private readonly object _connectionAttemptLock = new();
    private CancellationTokenSource? _connectionAttemptCancellation;
    private long _connectionRevision;

    private SessionState _state = SessionState.Disconnected;
    private string _statusText = "Disconnected";
    private string? _detail;

    public SessionCoordinator(ISshService sshService, ICredentialStore credentialStore)
    {
        _sshService = sshService ?? throw new ArgumentNullException(nameof(sshService));
        _credentialStore = credentialStore ?? throw new ArgumentNullException(nameof(credentialStore));
        _sshService.ConnectionLost += OnConnectionLost;
    }

    public SessionState State => _state;

    public string StatusText => _statusText;

    public string? Detail => _detail;

    public string? HostKeyFingerprint { get; private set; }

    public VpsProfile? ActiveProfile { get; private set; }

    public bool IsConnected => _state == SessionState.Connected && _sshService.IsConnected;

    public bool CanRememberCredentials => _credentialStore.SupportsPersistence;

    public event EventHandler<SessionStateChangedEventArgs>? StateChanged;

    public static string CredentialTargetName(string profileId) => $"VpsManager_{profileId}";
    public static string PassphraseCredentialTargetName(string profileId) => $"VpsManager_{profileId}_KeyPassphrase";

    public bool HasStoredCredential(VpsProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.AuthMethod == SshAuthMethod.PrivateKey)
        {
            return !string.IsNullOrWhiteSpace(profile.PrivateKeyPath) && File.Exists(profile.PrivateKeyPath);
        }
        return _credentialStore.GetCredential(CredentialTargetName(profile.Id)).HasValue;
    }

    public async Task<bool> ConnectAsync(VpsProfile profile, string? password = null, bool rememberPassword = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        cancellationToken.ThrowIfCancellationRequested();

        var revision = Interlocked.Increment(ref _connectionRevision);
        CancelConnectionAttempt();
        await _connectionGate.WaitAsync(cancellationToken);
        CancellationTokenSource? attemptCancellation = null;

        try
        {
            if (revision != Volatile.Read(ref _connectionRevision))
            {
                return false;
            }

            ActiveProfile = profile;
            HostKeyFingerprint = null;

            SshConnectionConfig sshConfig;
            var suppliedCredential = !string.IsNullOrWhiteSpace(password);
            string? resolvedSecret;

            if (profile.AuthMethod == SshAuthMethod.PrivateKey)
            {
                var keyPath = profile.PrivateKeyPath;
                if (string.IsNullOrWhiteSpace(keyPath) || !File.Exists(keyPath))
                {
                    SetState(SessionState.CredentialsRequired, "SSH Key Missing", $"Private key file not found: '{keyPath}'");
                    return false;
                }

                resolvedSecret = suppliedCredential
                    ? password
                    : _credentialStore.GetCredential(PassphraseCredentialTargetName(profile.Id))?.Secret;

                sshConfig = new SshConnectionConfig
                {
                    Host = profile.VpsIp,
                    Port = profile.SshPort,
                    Username = profile.VpsUser,
                    PrivateKeyPath = keyPath,
                    Passphrase = resolvedSecret
                };
            }
            else
            {
                resolvedSecret = suppliedCredential
                    ? password!
                    : _credentialStore.GetCredential(CredentialTargetName(profile.Id))?.Secret;

                if (string.IsNullOrWhiteSpace(resolvedSecret))
                {
                    SetState(SessionState.CredentialsRequired, "Credentials required", "Enter the SSH password for this server to connect.");
                    return false;
                }

                sshConfig = new SshConnectionConfig
                {
                    Host = profile.VpsIp,
                    Port = profile.SshPort,
                    Username = profile.VpsUser,
                    Password = resolvedSecret
                };
            }

            attemptCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            lock (_connectionAttemptLock)
            {
                _connectionAttemptCancellation = attemptCancellation;
            }

            SetState(SessionState.Connecting, "Connecting...", $"Connecting to {profile.Name}...");

            await _sshService.ConnectAsync(sshConfig, attemptCancellation.Token);

            if (revision != Volatile.Read(ref _connectionRevision))
            {
                return false;
            }

            if (!_sshService.IsConnected)
            {
                throw new InvalidOperationException("The SSH service did not establish a connection.");
            }

            if (suppliedCredential && rememberPassword && _credentialStore.SupportsPersistence && !string.IsNullOrEmpty(resolvedSecret))
            {
                var target = profile.AuthMethod == SshAuthMethod.PrivateKey
                    ? PassphraseCredentialTargetName(profile.Id)
                    : CredentialTargetName(profile.Id);
                _credentialStore.SaveCredential(target, profile.VpsUser, resolvedSecret);
            }

            HostKeyFingerprint = _sshService.ConnectedHostKeyFingerprint;
            SetState(SessionState.Connected, "Connected (SSH)", $"Signed in to {profile.Name} as {profile.VpsUser}.");
            return true;
        }
        catch (HostKeyVerificationException ex)
        {
            if (revision == Volatile.Read(ref _connectionRevision))
            {
                SetState(SessionState.Failed, "Connection failed", ex.Message);
            }

            return false;
        }
        catch (OperationCanceledException)
        {
            if (revision == Volatile.Read(ref _connectionRevision))
            {
                SetState(SessionState.Disconnected, "Disconnected", "The connection attempt was cancelled.");
            }

            return false;
        }
        catch (Exception ex)
        {
            if (revision == Volatile.Read(ref _connectionRevision))
            {
                SetState(SessionState.Failed, "Connection failed", ex.Message);
            }

            return false;
        }
        finally
        {
            lock (_connectionAttemptLock)
            {
                if (ReferenceEquals(_connectionAttemptCancellation, attemptCancellation))
                {
                    _connectionAttemptCancellation = null;
                }
            }

            attemptCancellation?.Dispose();
            _connectionGate.Release();
        }
    }

    public async Task DisconnectAsync(string? reason = null, CancellationToken cancellationToken = default)
    {
        var revision = Interlocked.Increment(ref _connectionRevision);
        CancelConnectionAttempt();
        await _connectionGate.WaitAsync(cancellationToken);

        try
        {
            if (revision != Volatile.Read(ref _connectionRevision))
            {
                return;
            }

            _sshService.Disconnect();
            HostKeyFingerprint = null;
            SetState(SessionState.Disconnected, "Disconnected", reason ?? "Disconnected from the selected VPS.");
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    public void RequireCredentials(string detail)
    {
        SetState(SessionState.CredentialsRequired, "Credentials required", detail);
    }

    private void OnConnectionLost(object? sender, EventArgs e)
    {
        SetState(SessionState.Failed, "Connection lost", "The SSH connection dropped. Reconnect to continue.");
    }

    private void CancelConnectionAttempt()
    {
        lock (_connectionAttemptLock)
        {
            _connectionAttemptCancellation?.Cancel();
        }
    }

    private void SetState(SessionState state, string statusText, string? detail = null)
    {
        if (_state == state && _statusText == statusText && _detail == detail)
        {
            return;
        }

        _state = state;
        _statusText = statusText;
        _detail = detail;

        StateChanged?.Invoke(this, new SessionStateChangedEventArgs
        {
            State = state,
            StatusText = statusText,
            Detail = detail,
            HostKeyFingerprint = HostKeyFingerprint
        });
    }
}
