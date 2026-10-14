namespace VpsManager.Core.Models;

public class AppConfig
{
    /// <summary>
    /// List of all configured VPS profiles.
    /// </summary>
    public List<VpsProfile> VpsProfiles { get; set; } = new();

    /// <summary>
    /// The Id of the currently active/selected VPS profile.
    /// </summary>
    public string ActiveProfileId { get; set; } = string.Empty;

    // Legacy single-VPS fields kept for backward compatibility during migration.
    // New code should use VpsProfiles instead.
    public string VpsIp { get; set; } = string.Empty;
    public int SshPort { get; set; } = 22;
    public string VpsUser { get; set; } = "root";
    public string LocalRootPath { get; set; } = string.Empty;
    public string RemoteProjectPath { get; set; } = "/var/www/my_app";
    public string ComposeFileName { get; set; } = "docker-compose.yml";
    public string DbContainerName { get; set; } = "postgres";
    public string DbName { get; set; } = "app_db";
    public string DbUser { get; set; } = "postgres";
    public bool FirstDeploy { get; set; } = false;
    public List<string> SyncIgnorePatterns { get; set; } = new()
    {
        ".git",
        "node_modules",
        ".env",
        "*.pyc",
        "__pycache__"
    };

    /// <summary>
    /// Returns the currently active VPS profile, or null if none is set.
    /// </summary>
    public VpsProfile? GetActiveProfile()
    {
        if (VpsProfiles.Count == 0)
            return null;

        var profile = VpsProfiles.Find(p => p.Id == ActiveProfileId);
        return profile ?? VpsProfiles[0];
    }

    /// <summary>
    /// Migrates legacy single-VPS config fields into the VpsProfiles list.
    /// Called once after loading if VpsProfiles is empty but legacy fields are set.
    /// </summary>
    public void MigrateFromLegacy()
    {
        if (VpsProfiles.Count > 0)
            return;

        if (string.IsNullOrEmpty(VpsIp))
            return;

        var legacy = new VpsProfile
        {
            Name = "Default VPS",
            VpsIp = VpsIp,
            SshPort = SshPort,
            VpsUser = VpsUser,
            LocalRootPath = LocalRootPath,
            RemoteProjectPath = RemoteProjectPath,
            ComposeFileName = ComposeFileName,
            DbContainerName = DbContainerName,
            DbName = DbName,
            DbUser = DbUser
        };

        VpsProfiles.Add(legacy);
        ActiveProfileId = legacy.Id;
    }
}
