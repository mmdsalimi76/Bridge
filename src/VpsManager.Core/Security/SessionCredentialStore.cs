namespace VpsManager.Core.Security;

/// <summary>
/// Keeps credentials in memory for the lifetime of the process only.
/// Used on platforms without an OS-protected secret store: writing a password to disk with
/// a locally stored key would be obfuscation rather than protection, so nothing is persisted.
/// </summary>
public sealed class SessionCredentialStore : ICredentialStore
{
    private readonly Dictionary<string, (string Username, string Secret)> _credentials = new(StringComparer.Ordinal);

    public bool SupportsPersistence => false;

    public void SaveCredential(string targetName, string username, string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetName);
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        _credentials[targetName] = (username, secret);
    }

    public (string Username, string Secret)? GetCredential(string targetName)
    {
        if (string.IsNullOrWhiteSpace(targetName))
        {
            return null;
        }

        return _credentials.TryGetValue(targetName, out var credential) ? credential : null;
    }

    public void DeleteCredential(string targetName)
    {
        if (!string.IsNullOrWhiteSpace(targetName))
        {
            _credentials.Remove(targetName);
        }
    }
}

/// <summary>
/// Selects the strongest credential store the current platform offers.
/// </summary>
public static class CredentialStoreFactory
{
    public static ICredentialStore Create()
    {
        return OperatingSystem.IsWindows()
            ? new WindowsCredentialStore()
            : new SessionCredentialStore();
    }
}
