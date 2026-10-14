using System.Globalization;
using System.Text;
using VpsManager.Core.Models;
using VpsManager.Core.Services.Adapters;

namespace VpsManager.Core.Services.Overview;

/// <summary>
/// Turns the last Overview snapshot into a shareable Markdown report for troubleshooting.
/// No credentials or secrets are ever included; the host address is redacted by default.
/// </summary>
public static class DiagnosticsReportBuilder
{
    public static string Build(
        OverviewSnapshot snapshot,
        VpsProfile? profile = null,
        HealthCheckResult? health = null,
        string connectionStatus = "Unknown",
        bool redactHost = true)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var builder = new StringBuilder();
        builder.AppendLine("# Bridge diagnostics report");
        builder.AppendLine();
        builder.AppendLine($"- Generated: {snapshot.CapturedAt:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine($"- Profile: {profile?.Name ?? "Unselected"}");
        builder.AppendLine($"- Server: {DescribeHost(profile, redactHost)}");
        builder.AppendLine($"- Connection: {connectionStatus}");
        builder.AppendLine($"- Remote commands per refresh: {snapshot.RemoteCommandCount}");
        builder.AppendLine();

        AppendMetrics(builder, snapshot);
        AppendAttention(builder, snapshot, health);
        AppendWorkloads(builder, snapshot);
        AppendFailedUnits(builder, snapshot);
        AppendProcesses(builder, snapshot);
        AppendStorage(builder, snapshot);

        if (snapshot.SectionErrors.Count > 0)
        {
            builder.AppendLine("## Collection notes");
            builder.AppendLine();
            foreach (var error in snapshot.SectionErrors)
            {
                builder.AppendLine($"- {error}");
            }
            builder.AppendLine();
        }

        return builder.ToString().TrimEnd() + Environment.NewLine;
    }

    private static void AppendMetrics(StringBuilder builder, OverviewSnapshot snapshot)
    {
        var metrics = snapshot.Metrics;
        builder.AppendLine("## Resources");
        builder.AppendLine();
        builder.AppendLine("| Metric | Value |");
        builder.AppendLine("| --- | --- |");
        builder.AppendLine($"| CPU | {(metrics.HasCpuMetrics ? $"{metrics.CpuUsagePercent:0.#}%" : "unavailable")} |");
        builder.AppendLine($"| Load average | {(string.IsNullOrWhiteSpace(metrics.LoadAverage) ? "unavailable" : metrics.LoadAverage)} |");
        builder.AppendLine($"| CPU cores | {(metrics.CpuCoreCount > 0 ? metrics.CpuCoreCount.ToString(CultureInfo.InvariantCulture) : "unavailable")} |");
        builder.AppendLine($"| Memory | {(metrics.HasRamMetrics ? $"{metrics.RamUsedGb:0.00} / {metrics.RamTotalGb:0.00} GB ({metrics.RamUsagePercent:0.#}%)" : "unavailable")} |");
        builder.AppendLine($"| Disk ({metrics.DiskMountPoint}) | {(metrics.HasDiskMetrics ? $"{metrics.DiskFreeGb:0.00} GB free of {metrics.DiskTotalGb:0.00} GB ({metrics.DiskUsagePercent:0.#}% used)" : "unavailable")} |");
        builder.AppendLine($"| Uptime | {(string.IsNullOrWhiteSpace(metrics.Uptime) ? "unavailable" : metrics.Uptime)} |");
        builder.AppendLine();
    }

    private static void AppendAttention(StringBuilder builder, OverviewSnapshot snapshot, HealthCheckResult? health)
    {
        var items = AttentionItemBuilder.Build(snapshot, health);
        builder.AppendLine("## Needs attention");
        builder.AppendLine();

        if (items.Count == 0)
        {
            builder.AppendLine("- No issues detected.");
            builder.AppendLine();
            return;
        }

        foreach (var item in items)
        {
            builder.AppendLine($"- **{item.Severity}** — {item.Title}: {item.Detail}");
        }

        builder.AppendLine();
    }

    private static void AppendWorkloads(StringBuilder builder, OverviewSnapshot snapshot)
    {
        builder.AppendLine("## Workloads");
        builder.AppendLine();

        if (snapshot.Workloads.Count == 0)
        {
            builder.AppendLine("- None detected.");
            builder.AppendLine();
            return;
        }

        builder.AppendLine("| Name | Source | Status | Image | Ports |");
        builder.AppendLine("| --- | --- | --- | --- | --- |");
        foreach (var workload in snapshot.Workloads)
        {
            builder.AppendLine($"| {workload.Name} | {workload.Source} | {workload.Status} | {workload.Image} | {workload.Ports} |");
        }

        builder.AppendLine();
    }

    private static void AppendFailedUnits(StringBuilder builder, OverviewSnapshot snapshot)
    {
        builder.AppendLine("## Failed systemd units");
        builder.AppendLine();
        if (snapshot.FailedUnits.Count == 0)
        {
            builder.AppendLine("- None.");
        }
        else
        {
            foreach (var unit in snapshot.FailedUnits)
            {
                builder.AppendLine($"- {unit}");
            }
        }

        builder.AppendLine();
    }

    private static void AppendProcesses(StringBuilder builder, OverviewSnapshot snapshot)
    {
        builder.AppendLine("## Top processes");
        builder.AppendLine();
        if (snapshot.TopProcesses.Count == 0)
        {
            builder.AppendLine("- Not collected.");
            builder.AppendLine();
            return;
        }

        builder.AppendLine("| Command | CPU % | Memory % |");
        builder.AppendLine("| --- | --- | --- |");
        foreach (var process in snapshot.TopProcesses)
        {
            builder.AppendLine($"| {process.Command} | {process.CpuPercent:0.#} | {process.MemoryPercent:0.#} |");
        }

        builder.AppendLine();
    }

    private static void AppendStorage(StringBuilder builder, OverviewSnapshot snapshot)
    {
        builder.AppendLine("## Storage");
        builder.AppendLine();
        if (snapshot.Mounts.Count == 0)
        {
            builder.AppendLine("- Filesystem usage not collected.");
        }
        else
        {
            builder.AppendLine("| Mount | Used (GB) | Total (GB) | Used % |");
            builder.AppendLine("| --- | --- | --- | --- |");
            foreach (var mount in snapshot.Mounts)
            {
                builder.AppendLine($"| {mount.MountPoint} | {mount.UsedGb:0.00} | {mount.TotalGb:0.00} | {mount.UsagePercent:0.#} |");
            }
        }

        builder.AppendLine();

        if (snapshot.DockerDiskUsageLines.Count > 0)
        {
            builder.AppendLine("### Docker disk usage");
            builder.AppendLine();
            builder.AppendLine("```");
            foreach (var line in snapshot.DockerDiskUsageLines)
            {
                builder.AppendLine(line);
            }
            builder.AppendLine("```");
            builder.AppendLine();
        }
    }

    private static string DescribeHost(VpsProfile? profile, bool redactHost)
    {
        if (profile is null || string.IsNullOrWhiteSpace(profile.VpsIp))
        {
            return "not configured";
        }

        return redactHost ? "<redacted>" : $"{profile.VpsIp}:{profile.SshPort}";
    }
}
