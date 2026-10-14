using VpsManager.Core.Services.Adapters;

namespace VpsManager.Core.Services.Overview;

public enum AttentionSeverity
{
    Info,
    Warning,
    Critical
}

/// <summary>
/// A single thing the operator should know about, with a severity and an optional action.
/// </summary>
public sealed record AttentionItem(AttentionSeverity Severity, string Title, string Detail, string? ActionKey = null)
{
    public static AttentionItem Critical(string title, string detail, string? actionKey = null) => new(AttentionSeverity.Critical, title, detail, actionKey);

    public static AttentionItem Warning(string title, string detail, string? actionKey = null) => new(AttentionSeverity.Warning, title, detail, actionKey);

    public static AttentionItem Info(string title, string detail, string? actionKey = null) => new(AttentionSeverity.Info, title, detail, actionKey);
}

/// <summary>Presentation-neutral level for a single reading, e.g. a metric card.</summary>
public enum MetricLevel
{
    Normal,
    Warning,
    Critical
}

public static class MetricLevels
{
    public static MetricLevel Classify(double value, double warningThreshold, double criticalThreshold) =>
        value >= criticalThreshold ? MetricLevel.Critical
        : value >= warningThreshold ? MetricLevel.Warning
        : MetricLevel.Normal;
}

public sealed class AttentionThresholds
{
    public double CpuWarningPercent { get; set; } = 85;
    public double CpuCriticalPercent { get; set; } = 95;
    public double RamWarningPercent { get; set; } = 90;
    public double RamCriticalPercent { get; set; } = 95;
    public double DiskWarningPercent { get; set; } = 80;
    public double DiskCriticalPercent { get; set; } = 90;
}

/// <summary>
/// Produces the Overview "needs attention" list. Pure and independently testable, so the
/// rules are identical wherever they are rendered.
/// </summary>
public static class AttentionItemBuilder
{
    public static IReadOnlyList<AttentionItem> Build(
        OverviewSnapshot snapshot,
        HealthCheckResult? health = null,
        AttentionThresholds? thresholds = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        thresholds ??= new AttentionThresholds();

        var items = new List<AttentionItem>();
        var metrics = snapshot.Metrics;

        if (!metrics.HasCpuMetrics && !metrics.HasRamMetrics && !metrics.HasDiskMetrics)
        {
            items.Add(AttentionItem.Info("Resource metrics unavailable", "The server did not return CPU, memory or disk metrics."));
        }
        else
        {
            if (!metrics.HasCpuMetrics)
            {
                items.Add(AttentionItem.Info("CPU metrics unavailable", "CPU usage could not be read from /proc/stat."));
            }
            else if (metrics.CpuUsagePercent >= thresholds.CpuCriticalPercent)
            {
                items.Add(AttentionItem.Critical("CPU saturated", $"Host CPU usage is {metrics.CpuUsagePercent:0.#}%."));
            }
            else if (metrics.CpuUsagePercent >= thresholds.CpuWarningPercent)
            {
                items.Add(AttentionItem.Warning("High CPU usage", $"Host CPU usage is {metrics.CpuUsagePercent:0.#}%."));
            }

            if (!metrics.HasRamMetrics)
            {
                items.Add(AttentionItem.Info("Memory metrics unavailable", "Memory usage could not be read from /proc/meminfo."));
            }
            else if (metrics.RamUsagePercent >= thresholds.RamCriticalPercent)
            {
                items.Add(AttentionItem.Critical("Memory nearly exhausted", $"Memory usage is {metrics.RamUsagePercent:0.#}% ({metrics.RamAvailableGb:0.00} GB available)."));
            }
            else if (metrics.RamUsagePercent >= thresholds.RamWarningPercent)
            {
                items.Add(AttentionItem.Warning("High memory usage", $"Memory usage is {metrics.RamUsagePercent:0.#}% ({metrics.RamAvailableGb:0.00} GB available)."));
            }

            if (!metrics.HasDiskMetrics)
            {
                items.Add(AttentionItem.Info("Disk metrics unavailable", "Root filesystem usage could not be read."));
            }
            else if (metrics.DiskUsagePercent >= thresholds.DiskCriticalPercent)
            {
                items.Add(AttentionItem.Critical("Low disk space", $"{metrics.DiskFreeGb:0.00} GB free on {metrics.DiskMountPoint}."));
            }
            else if (metrics.DiskUsagePercent >= thresholds.DiskWarningPercent)
            {
                items.Add(AttentionItem.Warning("Disk filling up", $"{metrics.DiskFreeGb:0.00} GB free on {metrics.DiskMountPoint}."));
            }
        }

        foreach (var unit in snapshot.FailedUnits)
        {
            items.Add(AttentionItem.Critical("Service failed", $"{unit} is in a failed state.", "view-workloads"));
        }

        foreach (var workload in snapshot.DockerWorkloads)
        {
            if (!IsRunning(workload.Status))
            {
                items.Add(AttentionItem.Warning("Workload not running", $"{workload.Name} is {workload.Status}.", "view-workloads"));
            }
        }

        if (health is { IsHealthy: false })
        {
            if (health.Message.Contains("No running containers were found", StringComparison.OrdinalIgnoreCase))
            {
                items.Add(AttentionItem.Info("Project standby", "No active containers for the configured Compose stack.", "open-health"));
            }
            else
            {
                items.Add(AttentionItem.Critical("Health check failed", health.Message, "open-health"));
            }
        }

        foreach (var sectionError in snapshot.SectionErrors)
        {
            items.Add(AttentionItem.Info("Partial data", sectionError));
        }

        return items;
    }

    /// <summary>Empty string when there is nothing to report; otherwise a short headline.</summary>
    public static string BuildSummary(IReadOnlyList<AttentionItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
        {
            return string.Empty;
        }

        var critical = items.Count(item => item.Severity == AttentionSeverity.Critical);
        var warning = items.Count(item => item.Severity == AttentionSeverity.Warning);
        var info = items.Count - critical - warning;

        var parts = new List<string>();
        if (critical > 0) parts.Add($"{critical} critical");
        if (warning > 0) parts.Add($"{warning} warning{(warning == 1 ? string.Empty : "s")}");
        if (info > 0) parts.Add($"{info} note{(info == 1 ? string.Empty : "s")}");
        return string.Join(" · ", parts);
    }

    public static AttentionSeverity WorstSeverity(IReadOnlyList<AttentionItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Any(item => item.Severity == AttentionSeverity.Critical)) return AttentionSeverity.Critical;
        if (items.Any(item => item.Severity == AttentionSeverity.Warning)) return AttentionSeverity.Warning;
        return AttentionSeverity.Info;
    }

    public static bool IsRunning(string status) =>
        status.Equals("running", StringComparison.OrdinalIgnoreCase)
        || status.Equals("up", StringComparison.OrdinalIgnoreCase);
}
