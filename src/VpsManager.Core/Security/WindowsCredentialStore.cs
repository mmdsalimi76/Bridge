using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace VpsManager.Core.Security;

public class WindowsCredentialStore : ICredentialStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("VpsManager.Entropy.v1");

    public bool SupportsPersistence => true;

    [SupportedOSPlatform("windows")]
    public void SaveCredential(string targetName, string username, string secret)
    {
        if (string.IsNullOrEmpty(targetName)) throw new ArgumentNullException(nameof(targetName));
        if (string.IsNullOrEmpty(secret)) throw new ArgumentNullException(nameof(secret));

        byte[] plainBytes = Encoding.UTF8.GetBytes($"{username}:{secret}");
        byte[] encryptedBytes = ProtectedData.Protect(plainBytes, Entropy, DataProtectionScope.CurrentUser);

        string appDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VpsManager", "Secrets");
        Directory.CreateDirectory(appDataFolder);

        string filePath = Path.Combine(appDataFolder, $"{SanitizeFileName(targetName)}.bin");
        File.WriteAllBytes(filePath, encryptedBytes);
    }

    [SupportedOSPlatform("windows")]
    public (string Username, string Secret)? GetCredential(string targetName)
    {
        string appDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VpsManager", "Secrets");
        string filePath = Path.Combine(appDataFolder, $"{SanitizeFileName(targetName)}.bin");

        if (!File.Exists(filePath)) return null;

        try
        {
            byte[] encryptedBytes = File.ReadAllBytes(filePath);
            byte[] plainBytes = ProtectedData.Unprotect(encryptedBytes, Entropy, DataProtectionScope.CurrentUser);
            string raw = Encoding.UTF8.GetString(plainBytes);

            int colonIndex = raw.IndexOf(':');
            if (colonIndex < 0) return null;

            string username = raw[..colonIndex];
            string secret = raw[(colonIndex + 1)..];
            return (username, secret);
        }
        catch
        {
            return null;
        }
    }

    public void DeleteCredential(string targetName)
    {
        string appDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VpsManager", "Secrets");
        string filePath = Path.Combine(appDataFolder, $"{SanitizeFileName(targetName)}.bin");

        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }

    private static string SanitizeFileName(string name) =>
        string.Concat(name.Split(Path.GetInvalidFileNameChars()));
}
