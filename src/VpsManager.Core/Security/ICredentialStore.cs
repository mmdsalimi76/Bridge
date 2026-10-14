namespace VpsManager.Core.Security;

public interface ICredentialStore
{
    /// <summary>
    /// True when secrets survive process restarts. False for session-only stores, where
    /// offering to "remember" a password would be misleading.
    /// </summary>
    bool SupportsPersistence { get; }

    void SaveCredential(string targetName, string username, string secret);
    (string Username, string Secret)? GetCredential(string targetName);
    void DeleteCredential(string targetName);
}
