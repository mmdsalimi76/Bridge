using System;
using System.Collections.Generic;
using System.Linq;
using VpsManager.Core.Models;
using VpsManager.Core.Services;
using VpsManager.Core.Services.Adapters;
using VpsManager.Core.Services.Overview;

namespace VpsManager.Core.Tests;

public class OverviewLogicTests
{
    private static OverviewSnapshot Snapshot(
        double cpu = 10,
        double ramUsed = 1,
        double ramTotal = 8,
        double diskUsed = 10,
        double diskTotal = 100,
        IReadOnlyList<RuntimeInfo>? workloads = null,
        IReadOnlyList<string>? failedUnits = null,
        IReadOnlyList<string>? sectionErrors = null)
    {
        return new OverviewSnapshot
        {
            CapturedAt = new DateTime(2026, 5, 1, 12, 0, 0),
            Metrics = new SystemResourceUsage
            {
                CpuUsagePercent = cpu,
                HasCpuMetrics = true,
                RamUsedGb = ramUsed,
                RamTotalGb = ramTotal,
                HasRamMetrics = true,
                DiskUsedGb = diskUsed,
                DiskTotalGb = diskTotal,
                DiskFreeGb = diskTotal - diskUsed,
                DiskMountPoint = "/",
                HasDiskMetrics = true,
                LoadAverage = "0.10 0.20 0.30",
                CpuCoreCount = 2,
                Uptime = "up 1 hour"
            },
            Workloads = workloads ?? Array.Empty<RuntimeInfo>(),
            FailedUnits = failedUnits ?? Array.Empty<string>(),
            SectionErrors = sectionErrors ?? Array.Empty<string>()
        };
    }

    // ── Attention ──────────────────────────────────────────────

    [Fact]
    public void CleanSnapshot_ProducesNoAttentionItems()
    {
        var items = AttentionItemBuilder.Build(Snapshot());

        Assert.Empty(items);
        Assert.Equal(string.Empty, AttentionItemBuilder.BuildSummary(items));
    }

    [Theory]
    [InlineData(50, 0)]
    [InlineData(85, 1)]
    [InlineData(99, 1)]
    public void CpuThreshold_ProducesExpectedItemCount(double cpu, int expectedCount)
    {
        var items = AttentionItemBuilder.Build(Snapshot(cpu: cpu));

        Assert.Equal(expectedCount, items.Count);
    }

    [Fact]
    public void CriticalCpu_IsReportedAsCritical()
    {
        var items = AttentionItemBuilder.Build(Snapshot(cpu: 97));

        var item = Assert.Single(items);
        Assert.Equal(AttentionSeverity.Critical, item.Severity);
        Assert.Contains("97", item.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void LowDisk_IsReportedAsCriticalWithFreeSpace()
    {
        var items = AttentionItemBuilder.Build(Snapshot(diskUsed: 95, diskTotal: 100));

        var item = Assert.Single(items);
        Assert.Equal(AttentionSeverity.Critical, item.Severity);
        Assert.Contains("5.00 GB free", item.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void FailedUnit_And_StoppedWorkload_AreBothReported()
    {
        var snapshot = Snapshot(
            workloads: new[] { new RuntimeInfo { Name = "api", Source = "Docker", Status = "exited" } },
            failedUnits: new[] { "postgres.service" });

        var items = AttentionItemBuilder.Build(snapshot);

        Assert.Equal(2, items.Count);
        Assert.All(items, item => Assert.Contains(item.Severity, new[] { AttentionSeverity.Critical, AttentionSeverity.Warning }));
        Assert.Contains(items, item => item.Title.Contains("Service failed", StringComparison.Ordinal));
        Assert.Contains(items, item => item.Title.Contains("Workload not running", StringComparison.Ordinal));
    }

    [Fact]
    public void UnhealthyHealthCheck_IsCriticalAndUsesHealthMessage()
    {
        var items = AttentionItemBuilder.Build(
            Snapshot(),
            new HealthCheckResult { IsHealthy = false, Message = "HTTP probe failed with 500" });

        var item = Assert.Single(items);
        Assert.Equal(AttentionSeverity.Critical, item.Severity);
        Assert.Contains("HTTP probe failed", item.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void UnstartedComposeProject_IsReportedAsStandbyInfoNotCritical()
    {
        var items = AttentionItemBuilder.Build(
            Snapshot(),
            new HealthCheckResult { IsHealthy = false, Message = "No running containers were found for the selected Compose project." });

        var item = Assert.Single(items);
        Assert.Equal(AttentionSeverity.Info, item.Severity);
        Assert.Equal("Project standby", item.Title);
        Assert.Contains("No active containers", item.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Summary_CountsSeverities()
    {
        // CPU 97% is critical (>=95); memory 91.25% is a warning (>=90, <95).
        var snapshot = Snapshot(cpu: 97, ramUsed: 7.3, ramTotal: 8);

        var items = AttentionItemBuilder.Build(snapshot);

        Assert.Equal(AttentionSeverity.Critical, items[0].Severity);
        Assert.Equal(AttentionSeverity.Warning, items[1].Severity);

        var summary = AttentionItemBuilder.BuildSummary(items);
        Assert.Equal("1 critical · 1 warning", summary);
    }

    [Fact]
    public void MissingMetrics_AreReportedAsInformational()
    {
        var snapshot = new OverviewSnapshot { Metrics = new SystemResourceUsage() };

        var items = AttentionItemBuilder.Build(snapshot);

        var item = Assert.Single(items);
        Assert.Equal(AttentionSeverity.Info, item.Severity);
    }

    // ── Filtering ──────────────────────────────────────────────

    [Fact]
    public void WorkloadFilter_MatchesSearchAcrossNameImageAndPorts()
    {
        var workloads = new List<RuntimeInfo>
        {
            new() { Name = "web", Source = "Docker", Image = "nginx:latest", Ports = "80/tcp" },
            new() { Name = "api", Source = "systemd", Image = "", Ports = "3000/tcp" }
        };

        Assert.Single(WorkloadFilter.Apply(workloads, "nginx", WorkloadFilter.AllSources));
        Assert.Single(WorkloadFilter.Apply(workloads, "3000", WorkloadFilter.AllSources));
        Assert.Single(WorkloadFilter.Apply(workloads, null, "Docker"));
        Assert.Equal(2, WorkloadFilter.Apply(workloads, null, WorkloadFilter.AllSources).Count);
        Assert.Empty(WorkloadFilter.Apply(workloads, "missing", WorkloadFilter.AllSources));
    }

    [Fact]
    public void WorkloadFilter_SourcesAreDerivedFromData()
    {
        var workloads = new List<RuntimeInfo>
        {
            new() { Name = "web", Source = "Docker" },
            new() { Name = "lb", Source = "Web gateway" },
            new() { Name = "api", Source = "Docker" }
        };

        var sources = WorkloadFilter.AvailableSources(workloads);

        Assert.Equal(new[] { "All", "Docker", "Web gateway" }, sources);
    }

    // ── History ────────────────────────────────────────────────

    [Fact]
    public void MetricHistory_TrimsToCapacityAndReportsDeltas()
    {
        var history = new MetricHistoryStore(capacity: 3);

        history.Add(Snapshot(cpu: 10));
        history.Add(Snapshot(cpu: 20));
        history.Add(Snapshot(cpu: 30));
        history.Add(Snapshot(cpu: 40));

        Assert.Equal(3, history.Count);
        Assert.Equal(new[] { 20d, 30d, 40d }, history.CpuSeries);
        Assert.Equal(10, history.CpuDelta!.Value, 3);
    }

    [Fact]
    public void MetricHistory_SingleSampleHasNoDelta()
    {
        var history = new MetricHistoryStore();
        history.Add(Snapshot(cpu: 10));

        Assert.Null(history.CpuDelta);
        Assert.Single(history.Samples);
    }

    [Fact]
    public void MetricHistory_ClearRemovesEverything()
    {
        var history = new MetricHistoryStore();
        history.Add(Snapshot());
        history.Clear();

        Assert.Equal(0, history.Count);
        Assert.Null(history.Latest);
    }

    // ── Diagnostics report ─────────────────────────────────────

    [Fact]
    public void DiagnosticsReport_RedactsHostByDefaultAndListsSections()
    {
        var profile = new VpsProfile { Name = "Production", VpsIp = "203.0.113.10", SshPort = 22 };

        var report = DiagnosticsReportBuilder.Build(Snapshot(), profile, connectionStatus: "Connected (SSH)");

        Assert.Contains("# Bridge diagnostics report", report, StringComparison.Ordinal);
        Assert.Contains("Production", report, StringComparison.Ordinal);
        Assert.Contains("<redacted>", report, StringComparison.Ordinal);
        Assert.DoesNotContain("203.0.113.10", report, StringComparison.Ordinal);
        Assert.Contains("## Resources", report, StringComparison.Ordinal);
        Assert.Contains("## Needs attention", report, StringComparison.Ordinal);
        Assert.Contains("No issues detected", report, StringComparison.Ordinal);
    }

    [Fact]
    public void DiagnosticsReport_CanIncludeHostWhenExplicitlyRequested()
    {
        var profile = new VpsProfile { Name = "Production", VpsIp = "203.0.113.10", SshPort = 2222 };

        var report = DiagnosticsReportBuilder.Build(Snapshot(), profile, redactHost: false);

        Assert.Contains("203.0.113.10:2222", report, StringComparison.Ordinal);
    }

    [Fact]
    public void DiagnosticsReport_IncludesWorkloadsAndFailures()
    {
        var snapshot = Snapshot(
            workloads: new[] { new RuntimeInfo { Name = "api", Source = "Docker", Status = "running", Image = "api:1.2", Ports = "8080/tcp" } },
            failedUnits: new[] { "cron.service" },
            sectionErrors: new[] { "Docker workload inventory could not be collected." });

        var report = DiagnosticsReportBuilder.Build(snapshot);

        Assert.Contains("api:1.2", report, StringComparison.Ordinal);
        Assert.Contains("cron.service", report, StringComparison.Ordinal);
        Assert.Contains("## Collection notes", report, StringComparison.Ordinal);
    }
}
