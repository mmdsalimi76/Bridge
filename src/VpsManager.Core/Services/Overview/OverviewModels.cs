using VpsManager.Core.Models;

namespace VpsManager.Core.Services.Overview;

/// <summary>
/// Everything the Overview tab needs to collect from a server in one round trip.
/// </summary>
public sealed class OverviewProbeRequest
{
    public string ComposeFileName { get; set; } = "docker-compose.yml";

    public string RemoteProjectPath { get; set; } = string.Empty;

    public HostOS TargetOS { get; set; } = HostOS.Linux;

    /// <summary>
    /// Health check command to embed in the probe, or null when health is derived from the
    /// container inventory (Docker compose health checks).
    /// </summary>
    public string? HealthProbeCommand { get; set; }
}

/// <summary>
/// Result of a single batched probe against a server.
/// </summary>
public sealed class OverviewSnapshot
{
    public DateTime CapturedAt { get; init; } = DateTime.Now;

    public SystemResourceUsage Metrics { get; init; } = new();

    public IReadOnlyList<RuntimeInfo> Workloads { get; init; } = Array.Empty<RuntimeInfo>();

    public IReadOnlyList<string> FailedUnits { get; init; } = Array.Empty<string>();

    public IReadOnlyList<ProcessSample> TopProcesses { get; init; } = Array.Empty<ProcessSample>();

    public IReadOnlyList<DiskMount> Mounts { get; init; } = Array.Empty<DiskMount>();

    public IReadOnlyList<string> DockerDiskUsageLines { get; init; } = Array.Empty<string>();

    public string? HealthOutput { get; init; }

    public int? HealthExitCode { get; init; }

    /// <summary>Per-section problems, so partial failures degrade instead of blanking the tab.</summary>
    public IReadOnlyList<string> SectionErrors { get; init; } = Array.Empty<string>();

    /// <summary>Number of remote commands this snapshot cost. Always one; asserted in tests.</summary>
    public int RemoteCommandCount => 1;

    public IEnumerable<RuntimeInfo> DockerWorkloads =>
        Workloads.Where(workload => workload.Source.Equals("Docker", StringComparison.OrdinalIgnoreCase));
}

public sealed record ProcessSample(string Command, double CpuPercent, double MemoryPercent);

public sealed record DiskMount(string Device, string MountPoint, double UsedGb, double TotalGb)
{
    public double FreeGb => Math.Max(0, TotalGb - UsedGb);

    public double UsagePercent => TotalGb > 0 ? UsedGb / TotalGb * 100 : 0;
}
