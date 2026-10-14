using System.Text.Json;
using System.Text.Json.Serialization;

namespace VpsManager.Core.Security;

/// <summary>
/// Stores trusted host keys as JSON beside the application configuration.
/// Writes are atomic and the file never contains credentials.
/// </summary>
public sealed class JsonKnownHostsStore : IKnownHostsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _filePath;
    private readonly object _gate = new();
    private readonly List<HostKeyInfo> _entries = new();
    private bool _loaded;

    public JsonKnownHostsStore(string? customPath = null)
    {
        if (!string.IsNullOrWhiteSpace(customPath))
        {
            _filePath = customPath;
            return;
        }

        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VpsManager");
        Directory.CreateDirectory(appData);
        _filePath = Path.Combine(appData, "known_hosts.json");
    }

    public bool TryGetFingerprint(string host, int port, out string fingerprint)
    {
        lock (_gate)
        {
            EnsureLoaded();
            var match = _entries.FirstOrDefault(entry =>
                string.Equals(entry.Host, host, StringComparison.OrdinalIgnoreCase)
                && entry.Port == port);

            fingerprint = match?.Fingerprint ?? string.Empty;
            return match != null;
        }
    }

    public void Store(HostKeyInfo hostKey)
    {
        ArgumentNullException.ThrowIfNull(hostKey);

        lock (_gate)
        {
            EnsureLoaded();
            var index = _entries.FindIndex(entry =>
                string.Equals(entry.Host, hostKey.Host, StringComparison.OrdinalIgnoreCase)
                && entry.Port == hostKey.Port);

            if (index >= 0)
            {
                _entries[index] = hostKey;
            }
            else
            {
                _entries.Add(hostKey);
            }

            Persist();
        }
    }

    public bool Forget(string host, int port)
    {
        lock (_gate)
        {
            EnsureLoaded();
            var removed = _entries.RemoveAll(entry =>
                string.Equals(entry.Host, host, StringComparison.OrdinalIgnoreCase)
                && entry.Port == port) > 0;

            if (removed)
            {
                Persist();
            }

            return removed;
        }
    }

    public IReadOnlyList<HostKeyInfo> GetAll()
    {
        lock (_gate)
        {
            EnsureLoaded();
            return _entries.ToList();
        }
    }

    private void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        if (!File.Exists(_filePath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            var loaded = JsonSerializer.Deserialize<List<HostKeyInfo>>(json, JsonOptions);
            if (loaded != null)
            {
                _entries.AddRange(loaded.Where(entry => !string.IsNullOrWhiteSpace(entry.Fingerprint)));
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A corrupt or unreadable trust store must fail closed: treat it as empty so
            // every server key is re-verified instead of silently trusting anything.
            _entries.Clear();
        }
    }

    private void Persist()
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = $"{_filePath}.tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(_entries, JsonOptions));
        File.Move(tempPath, _filePath, overwrite: true);
    }
}
