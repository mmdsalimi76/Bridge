using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using VpsManager.Core.Models;
using VpsManager.Core.Services.Adapters;
using VpsManager.Core.Services.Overview;

namespace VpsManager.Client.ViewModels.Dashboard;

/// <summary>
/// Presents what needs attention: one consolidated, severity-aware list covering resources,
/// failed services, stopped workloads and application health, plus the backup summary.
/// </summary>
public partial class AttentionViewModel : ViewModelBase
{
    private readonly AttentionThresholds _thresholds;

    [ObservableProperty]
    private IReadOnlyList<AttentionItem> _items = Array.Empty<AttentionItem>();

    [ObservableProperty]
    private string _summary = string.Empty;

    [ObservableProperty]
    private string _headline = "Connect to review server health";

    [ObservableProperty]
    private string _severity = nameof(MetricLevel.Normal);

    [ObservableProperty]
    private bool _hasItems;

    [ObservableProperty]
    private string _healthStatus = "Unknown";

    [ObservableProperty]
    private string _healthSummary = "Connect to run the configured application health check.";

    [ObservableProperty]
    private string _healthLevel = nameof(MetricLevel.Normal);

    [ObservableProperty]
    private IReadOnlyList<string> _failedUnits = Array.Empty<string>();

    [ObservableProperty]
    private IReadOnlyList<string> _sectionErrors = Array.Empty<string>();

    [ObservableProperty]
    private string _latestBackupSummary = "No backup activity loaded.";

    [ObservableProperty]
    private string _latestBackupTime = string.Empty;

    [ObservableProperty]
    private string _scheduledBackupSummary = "No scheduled backup activity loaded.";

    [ObservableProperty]
    private bool _hasRecentBackup;

    [ObservableProperty]
    private string _latestBackupHero = "No Backups";

    [ObservableProperty]
    private string _latestBackupScope = "Vault";

    [ObservableProperty]
    private string _latestBackupBadge = "Empty";

    [ObservableProperty]
    private string _latestBackupFooter = "No backups stored";

    public AttentionViewModel(AttentionThresholds thresholds)
    {
        _thresholds = thresholds ?? throw new ArgumentNullException(nameof(thresholds));
    }

    /// <summary>Rebuilds the attention list from the latest snapshot and health result.</summary>
    public void Apply(OverviewSnapshot snapshot, HealthCheckResult? health)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        Items = AttentionItemBuilder.Build(snapshot, health, _thresholds);
        Summary = AttentionItemBuilder.BuildSummary(Items);
        HasItems = Items.Count > 0;
        FailedUnits = snapshot.FailedUnits;
        SectionErrors = snapshot.SectionErrors;

        if (Items.Count == 0)
        {
            Headline = "All clear";
            Severity = nameof(MetricLevel.Normal);
        }
        else
        {
            var worst = AttentionItemBuilder.WorstSeverity(Items);
            Severity = worst.ToString();
            Headline = Items.Count == 1 ? "1 item needs attention" : $"{Items.Count} items need attention";
        }

        if (health != null)
        {
            if (health.IsHealthy)
            {
                HealthStatus = "Healthy";
                HealthSummary = health.Message;
                HealthLevel = nameof(MetricLevel.Normal);
            }
            else if (health.Message.Contains("No running containers were found", StringComparison.OrdinalIgnoreCase))
            {
                HealthStatus = "Standby";
                HealthSummary = "Project stack is not running or not yet deployed.";
                HealthLevel = nameof(MetricLevel.Normal);
            }
            else
            {
                HealthStatus = "Unhealthy";
                HealthSummary = health.Message;
                HealthLevel = nameof(MetricLevel.Critical);
            }
        }
    }

    public void ApplyBackup(DatabaseBackupInfo? latestBackup, string scheduledSummary, bool hasRecentBackup)
    {
        HasRecentBackup = hasRecentBackup;
        LatestBackupHero = latestBackup == null ? "No Backups" : latestBackup.FormattedSize;
        LatestBackupScope = latestBackup == null ? "Vault" : latestBackup.ScopeDisplayName;
        LatestBackupBadge = latestBackup == null ? "Empty" : (hasRecentBackup ? "Verified" : "Archived");
        LatestBackupSummary = latestBackup == null
            ? "No backups found in the remote vault."
            : $"{latestBackup.ScopeDisplayName}: {latestBackup.FileName}";
        LatestBackupTime = latestBackup == null
            ? string.Empty
            : latestBackup.CreatedAt.ToLocalTime().ToString("g");
        LatestBackupFooter = latestBackup == null
            ? "No backups stored"
            : $"Saved {LatestBackupTime}";
        ScheduledBackupSummary = scheduledSummary;
    }

    public void ApplyBackupError(string message)
    {
        HasRecentBackup = false;
        LatestBackupHero = "Unavailable";
        LatestBackupScope = "Vault";
        LatestBackupBadge = "Error";
        LatestBackupSummary = message;
        LatestBackupTime = string.Empty;
        LatestBackupFooter = "Backup error";
    }

    /// <summary>Replaces the list with a single failure item after a collection error.</summary>
    public void ApplyFailure(string title, string detail)
    {
        Items = new[] { AttentionItem.Critical(title, detail) };
        Summary = "1 critical";
        HasItems = true;
        Headline = title;
        Severity = nameof(MetricLevel.Critical);
    }

    public void Clear()
    {
        Items = Array.Empty<AttentionItem>();
        Summary = string.Empty;
        Headline = "Connect to review server health";
        Severity = nameof(MetricLevel.Normal);
        HasItems = false;
        HealthStatus = "Unknown";
        HealthSummary = "Connect to run the configured application health check.";
        HealthLevel = nameof(MetricLevel.Normal);
        FailedUnits = Array.Empty<string>();
        SectionErrors = Array.Empty<string>();
        LatestBackupHero = "No Backups";
        LatestBackupScope = "Vault";
        LatestBackupBadge = "Empty";
        LatestBackupSummary = "No backup activity loaded.";
        LatestBackupTime = string.Empty;
        LatestBackupFooter = "No backups stored";
        ScheduledBackupSummary = "No scheduled backup activity loaded.";
        HasRecentBackup = false;
    }

    public bool HasSectionErrors => SectionErrors.Count > 0;

    public string SectionErrorSummary => SectionErrors.Count == 0
        ? string.Empty
        : string.Join(" ", SectionErrors);
}
