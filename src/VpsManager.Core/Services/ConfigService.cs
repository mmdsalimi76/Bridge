using System.Text.Json;
using VpsManager.Core.Models;

namespace VpsManager.Core.Services;

public class ConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _configFilePath;

    public ConfigService(string? customPath = null)
    {
        if (!string.IsNullOrEmpty(customPath))
        {
            _configFilePath = customPath;
        }
        else
        {
            string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VpsManager");
            Directory.CreateDirectory(appData);
            _configFilePath = Path.Combine(appData, "config.json");
        }
    }

    public async Task<AppConfig> LoadConfigAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_configFilePath))
        {
            var defaultConfig = new AppConfig();
            await SaveConfigAsync(defaultConfig, cancellationToken);
            return defaultConfig;
        }

        using var stream = File.OpenRead(_configFilePath);
        var config = await JsonSerializer.DeserializeAsync<AppConfig>(stream, JsonOptions, cancellationToken);
        config ??= new AppConfig();
        config.MigrateFromLegacy();
        return config;
    }

    public async Task SaveConfigAsync(AppConfig config, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        using var stream = File.Create(_configFilePath);
        await JsonSerializer.SerializeAsync(stream, config, JsonOptions, cancellationToken);
    }
}
