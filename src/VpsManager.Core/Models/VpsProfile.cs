namespace VpsManager.Core.Models;
 
public enum SshAuthMethod
{
    Password,
    PrivateKey
}

/// <summary>
/// Represents a single VPS configuration profile.
/// Users can define multiple profiles and switch between them.
/// </summary>
public class VpsProfile
{
    /// <summary>
    /// Unique identifier for this VPS profile.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>
    /// Human-readable display name (e.g. "Production Server", "Staging", "Local Docker").
    /// </summary>
    public string Name { get; set; } = "My VPS";

    public string VpsIp { get; set; } = string.Empty;
    public int SshPort { get; set; } = 22;
    public string VpsUser { get; set; } = "root";
    public SshAuthMethod AuthMethod { get; set; } = SshAuthMethod.Password;
    public string PrivateKeyPath { get; set; } = string.Empty;
    public string LocalRootPath { get; set; } = string.Empty;
    public string RemoteProjectPath { get; set; } = "/var/www/my_app";
    public string ComposeFileName { get; set; } = "docker-compose.yml";
    public string DbContainerName { get; set; } = "postgres";
    public string DbName { get; set; } = "app_db";
    public string DbUser { get; set; } = "postgres";

    public HostOS TargetOS { get; set; } = HostOS.Linux;
    public bool IsDockerized { get; set; } = true;
    public ProjectType ProjectType { get; set; } = ProjectType.DockerCompose;

    // Database Fields
    public DatabaseEngine DbEngine { get; set; } = DatabaseEngine.PostgreSQL;
    public bool IsDbContainerized { get; set; } = true;
    public string DbHost { get; set; } = "localhost";
    public int DbPort { get; set; } = 5432;
    public string CustomBackupCommand { get; set; } = string.Empty;
    public string CustomMigrationCommand { get; set; } = string.Empty;

    // Service / Daemon / Non-Dockerized Execution Fields
    public string LaunchCommand { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public string StartCommand { get; set; } = string.Empty;
    public string StopCommand { get; set; } = string.Empty;
    public string RestartCommand { get; set; } = string.Empty;

    // Build Strategy Fields
    public LocalBuildStrategy BuildStrategy { get; set; } = LocalBuildStrategy.None;
    public string LocalBuildCommand { get; set; } = string.Empty;
    public string BuildArtifactPath { get; set; } = string.Empty;

    // Health Check Fields
    public HealthCheckType HealthCheckType { get; set; } = HealthCheckType.DockerContainer;
    public string HealthCheckUrl { get; set; } = "http://localhost:8080/health";
    public int HealthCheckPort { get; set; } = 8080;
    public string HealthCheckProcessName { get; set; } = string.Empty;
    public string HealthCheckCustomCommand { get; set; } = string.Empty;

    public override string ToString() => $"{Name} ({VpsIp}:{SshPort} - {ProjectType})";
}
