using System.Security.Cryptography;

namespace VpsManager.Core.Security;

/// <summary>
/// Identity of a remote SSH server key, as presented during the handshake.
/// </summary>
public sealed record HostKeyInfo(string Host, int Port, string KeyAlgorithm, string Fingerprint)
{
    /// <summary>Stable lookup key for a server endpoint.</summary>
    public string Endpoint => $"{Host}:{Port}";

    /// <summary>Computes the OpenSSH-style SHA-256 fingerprint for a raw host key.</summary>
    public static string ComputeFingerprint(byte[] hostKey)
    {
        ArgumentNullException.ThrowIfNull(hostKey);
        var hash = SHA256.HashData(hostKey);
        return $"SHA256:{Convert.ToBase64String(hash).TrimEnd('=')}";
    }
}

/// <summary>
/// Outcome of evaluating a presented host key.
/// </summary>
public sealed record HostKeyDecision(bool Allow, string? Reason)
{
    public static HostKeyDecision Trust() => new(true, null);
    public static HostKeyDecision Reject(string reason) => new(false, reason);
}

/// <summary>
/// Persistent record of host keys the user has explicitly trusted.
/// </summary>
public interface IKnownHostsStore
{
    bool TryGetFingerprint(string host, int port, out string fingerprint);

    void Store(HostKeyInfo hostKey);

    bool Forget(string host, int port);

    IReadOnlyList<HostKeyInfo> GetAll();
}

/// <summary>
/// Asks the user whether an unknown or changed host key should be trusted.
/// Implementations may block while presenting UI; the SSH service calls this
/// from a background thread during the handshake, never from the UI thread.
/// </summary>
public interface IHostKeyTrustPrompt
{
    /// <summary>First contact with this server. Returns true when the user trusts the key.</summary>
    bool ShouldTrustUnknownHost(HostKeyInfo hostKey);

    /// <summary>
    /// The stored key for a known host no longer matches. Returns true only when the
    /// user explicitly accepts the new key.
    /// </summary>
    bool ShouldTrustChangedHost(HostKeyInfo hostKey, string previousFingerprint);
}

/// <summary>
/// Evaluates presented host keys against the trusted store and, when required, the user.
/// </summary>
public interface IHostKeyVerifier
{
    HostKeyDecision Evaluate(HostKeyInfo hostKey);
}

/// <summary>
/// Default host key policy: keys from known hosts must match exactly; unknown hosts are
/// rejected unless the user explicitly trusts them; changed keys are rejected unless the
/// user explicitly accepts the new key.
/// </summary>
public sealed class HostKeyPolicy : IHostKeyVerifier
{
    private readonly IKnownHostsStore _store;
    private readonly IHostKeyTrustPrompt? _prompt;

    public HostKeyPolicy(IKnownHostsStore store, IHostKeyTrustPrompt? prompt = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _prompt = prompt;
    }

    public HostKeyDecision Evaluate(HostKeyInfo hostKey)
    {
        ArgumentNullException.ThrowIfNull(hostKey);

        if (_store.TryGetFingerprint(hostKey.Host, hostKey.Port, out var knownFingerprint))
        {
            if (string.Equals(knownFingerprint, hostKey.Fingerprint, StringComparison.Ordinal))
            {
                return HostKeyDecision.Trust();
            }

            if (_prompt?.ShouldTrustChangedHost(hostKey, knownFingerprint) == true)
            {
                _store.Store(hostKey);
                return HostKeyDecision.Trust();
            }

            return HostKeyDecision.Reject(
                $"The host key for {hostKey.Endpoint} has changed. " +
                $"Previously trusted {knownFingerprint}, server now presents {hostKey.Fingerprint}. " +
                "This can indicate a man-in-the-middle attack, or a legitimate server rebuild. " +
                "Review the new key before trusting it.");
        }

        if (_prompt?.ShouldTrustUnknownHost(hostKey) == true)
        {
            _store.Store(hostKey);
            return HostKeyDecision.Trust();
        }

        return HostKeyDecision.Reject(
            $"The host key for {hostKey.Endpoint} is not trusted yet ({hostKey.Fingerprint}). " +
            "Confirm the fingerprint with the server operator before trusting it.");
    }
}

/// <summary>
/// Raised when the SSH handshake is aborted because the server key could not be trusted.
/// </summary>
public sealed class HostKeyVerificationException : Exception
{
    public HostKeyVerificationException(string message) : base(message)
    {
    }
}
