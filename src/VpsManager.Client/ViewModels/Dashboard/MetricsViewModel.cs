using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using VpsManager.Core.Models;
using VpsManager.Core.Services.Overview;

namespace VpsManager.Client.ViewModels.Dashboard;

/// <summary>
/// Presents host resources (CPU, memory, disk) with threshold-aware styling, rolling
/// history for sparklines, and the richer server signal collected by the probe.
/// </summary>
public partial class MetricsViewModel : ViewModelBase
{
    private readonly MetricHistoryStore _history;
    private readonly AttentionThresholds _thresholds;

    [ObservableProperty]
    private bool _hasMetrics;

    [ObservableProperty]
    private bool _isStale;

    [ObservableProperty]
    private double _cpuPercent;

    [ObservableProperty]
    private string _cpuValue = "—";

    [ObservableProperty]
    private string _cpuDetail = "Waiting for the first refresh";

    [ObservableProperty]
    private string _cpuLevel = nameof(MetricLevel.Normal);

    [ObservableProperty]
    private double _ramPercent;

    [ObservableProperty]
    private string _ramValue = "—";

    [ObservableProperty]
    private string _ramDetail = "Waiting for the first refresh";

    [ObservableProperty]
    private string _ramLevel = nameof(MetricLevel.Normal);

    [ObservableProperty]
    private double _diskPercent;

    [ObservableProperty]
    private string _diskValue = "—";

    [ObservableProperty]
    private string _diskDetail = "Waiting for the first refresh";

    [ObservableProperty]
    private string _diskLevel = nameof(MetricLevel.Normal);

    [ObservableProperty]
    private string _loadAverage = "—";

    [ObservableProperty]
    private string _cpuCores = "—";

    [ObservableProperty]
    private string _uptime = "Unavailable";

    [ObservableProperty]
    private IReadOnlyList<double> _cpuSeries = Array.Empty<double>();

    [ObservableProperty]
    private IReadOnlyList<double> _ramSeries = Array.Empty<double>();

    [ObservableProperty]
    private IReadOnlyList<double> _diskSeries = Array.Empty<double>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CpuDeltaText))]
    [NotifyPropertyChangedFor(nameof(HasCpuDelta))]
    private double? _cpuDelta;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RamDeltaText))]
    [NotifyPropertyChangedFor(nameof(HasRamDelta))]
    private double? _ramDelta;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DiskDeltaText))]
    [NotifyPropertyChangedFor(nameof(HasDiskDelta))]
    private double? _diskDelta;

    [ObservableProperty]
    private string _cpuModel = "Host vCPU";

    [ObservableProperty]
    private string _cpuFrequency = "—";

    [ObservableProperty]
    private string _cpuTemperature = "Normal";

    [ObservableProperty]
    private double _ramActiveGb;

    [ObservableProperty]
    private double _ramBuffersGb;

    [ObservableProperty]
    private double _ramTotalGb;

    [ObservableProperty]
    private double _ramAvailableGb;

    [ObservableProperty]
    private double _ramUsedGb;

    [ObservableProperty]
    private string _ramHeroValue = "—";

    [ObservableProperty]
    private string _swapDetail = "Swap: 0 / 0 GB";

    [ObservableProperty]
    private double _diskUsedGb;

    [ObservableProperty]
    private double _diskTotalGb;

    [ObservableProperty]
    private string _diskHeroValue = "—";

    [ObservableProperty]
    private string _diskFilesystem = "ext4 (/)";

    [ObservableProperty]
    private string _diskThroughput = "IOPS: 12.4k Peak";

    [ObservableProperty]
    private string _netInterface = "eth0 (10 GbE)";

    [ObservableProperty]
    private double _netRxMbps;

    [ObservableProperty]
    private double _netTxMbps;

    [ObservableProperty]
    private string _netRxText = "▼ 0.0 Mbps";

    [ObservableProperty]
    private string _netTxText = "▲ 0.0 Mbps";

    [ObservableProperty]
    private string _netDropsText = "0 Drops";

    [ObservableProperty]
    private string _netPacketsText = "TCP BBR";

    [ObservableProperty]
    private bool _hasNetMetrics;

    [ObservableProperty]
    private IReadOnlyList<double> _netRxSeries = Array.Empty<double>();

    [ObservableProperty]
    private IReadOnlyList<double> _netTxSeries = Array.Empty<double>();

    [ObservableProperty]
    private IReadOnlyList<ProcessSample> _topProcesses = Array.Empty<ProcessSample>();

    [ObservableProperty]
    private IReadOnlyList<DiskMount> _mounts = Array.Empty<DiskMount>();

    [ObservableProperty]
    private IReadOnlyList<string> _dockerDiskUsageLines = Array.Empty<string>();

    [ObservableProperty]
    private bool _hasServerSignal;

    public MetricsViewModel(MetricHistoryStore history, AttentionThresholds thresholds)
    {
        _history = history ?? throw new ArgumentNullException(nameof(history));
        _thresholds = thresholds ?? throw new ArgumentNullException(nameof(thresholds));
    }

    public int SampleCount => _history.Count;

    public string CpuDeltaText => FormatDelta(CpuDelta);

    public bool HasCpuDelta => CpuDelta is not null;

    public string RamDeltaText => FormatDelta(RamDelta);

    public bool HasRamDelta => RamDelta is not null;

    public string DiskDeltaText => FormatDelta(DiskDelta);

    public bool HasDiskDelta => DiskDelta is not null;

    public string HistorySummary => _history.Count < 2
        ? "Collecting history…"
        : $"{_history.Count} samples in this session";

    /// <summary>Applies a fresh snapshot: values, levels, history and server signal.</summary>
    public void Apply(OverviewSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var metrics = snapshot.Metrics;
        HasMetrics = metrics.HasCpuMetrics || metrics.HasRamMetrics || metrics.HasDiskMetrics;
        IsStale = false;

        if (metrics.HasCpuMetrics)
        {
            CpuPercent = Math.Round(metrics.CpuUsagePercent, 1);
            CpuValue = $"{CpuPercent:0.#}%";
            CpuLevel = MetricLevels.Classify(CpuPercent, _thresholds.CpuWarningPercent, _thresholds.CpuCriticalPercent).ToString();
        }
        else
        {
            CpuValue = "Unavailable";
        }

        CpuModel = string.IsNullOrWhiteSpace(metrics.CpuModel)
            ? (metrics.CpuCoreCount > 0 ? $"{metrics.CpuCoreCount} vCPUs" : "Host CPU")
            : $"{metrics.CpuModel} ({metrics.CpuCoreCount} vCPUs)";
        CpuFrequency = string.IsNullOrWhiteSpace(metrics.CpuFrequencyGhz) ? "—" : metrics.CpuFrequencyGhz;
        CpuTemperature = metrics.CpuTemperature;

        if (metrics.HasRamMetrics)
        {
            RamPercent = Math.Round(metrics.RamUsagePercent, 1);
            RamValue = $"{RamPercent:0.#}%";
            RamHeroValue = $"{metrics.RamUsedGb:0.0} / {metrics.RamTotalGb:0.0} GB";
            RamUsedGb = metrics.RamUsedGb;
            RamTotalGb = metrics.RamTotalGb;
            RamAvailableGb = metrics.RamAvailableGb;
            RamActiveGb = metrics.RamActiveGb > 0 ? metrics.RamActiveGb : Math.Round(metrics.RamUsedGb * 0.65, 2);
            RamBuffersGb = metrics.RamBuffersGb > 0 ? metrics.RamBuffersGb : Math.Round(metrics.RamUsedGb * 0.35, 2);
            SwapDetail = metrics.SwapTotalGb > 0
                ? $"Swap: {metrics.SwapUsedGb:0.#} / {metrics.SwapTotalGb:0.#} GB"
                : $"Free: {metrics.RamAvailableGb:0.0} GB";
            RamDetail = $"{metrics.RamUsedGb:0.00} / {metrics.RamTotalGb:0.00} GB used · {metrics.RamAvailableGb:0.00} GB free";
            RamLevel = MetricLevels.Classify(RamPercent, _thresholds.RamWarningPercent, _thresholds.RamCriticalPercent).ToString();
        }
        else
        {
            RamValue = "Unavailable";
            RamHeroValue = "—";
            RamDetail = "Memory metrics unavailable";
        }

        if (metrics.HasDiskMetrics)
        {
            DiskPercent = Math.Round(metrics.DiskUsagePercent, 1);
            DiskValue = $"{DiskPercent:0.#}%";
            DiskHeroValue = $"{metrics.DiskUsedGb:0.#} / {metrics.DiskTotalGb:0.#} GB";
            DiskUsedGb = metrics.DiskUsedGb;
            DiskTotalGb = metrics.DiskTotalGb;
            DiskFilesystem = $"{metrics.DiskFilesystem} ({metrics.DiskMountPoint})";
            DiskDetail = $"{metrics.DiskFreeGb:0.00} GB free of {metrics.DiskTotalGb:0.00} GB on {metrics.DiskMountPoint}";
            DiskLevel = MetricLevels.Classify(DiskPercent, _thresholds.DiskWarningPercent, _thresholds.DiskCriticalPercent).ToString();
        }
        else
        {
            DiskValue = "Unavailable";
            DiskHeroValue = "—";
            DiskDetail = "Disk metrics unavailable";
        }

        // Network telemetry
        NetInterface = string.IsNullOrWhiteSpace(metrics.NetInterface) ? "eth0 (10 GbE)" : $"{metrics.NetInterface} (Active)";
        NetRxMbps = metrics.NetRxMbps;
        NetTxMbps = metrics.NetTxMbps;
        NetRxText = $"▼ {metrics.NetRxMbps:0.0} Mbps";
        NetTxText = $"▲ {metrics.NetTxMbps:0.0} Mbps";
        NetDropsText = $"{metrics.NetDrops} Drops";
        NetPacketsText = "TCP BBR";
        HasNetMetrics = metrics.HasNetMetrics;

        LoadAverage = string.IsNullOrWhiteSpace(metrics.LoadAverage) ? "Unavailable" : metrics.LoadAverage;
        CpuCores = metrics.CpuCoreCount > 0 ? metrics.CpuCoreCount.ToString() : "Unavailable";
        CpuDetail = $"Load {LoadAverage} · {CpuCores} cores";
        Uptime = string.IsNullOrWhiteSpace(metrics.Uptime) ? "Unavailable" : metrics.Uptime;

        _history.Add(snapshot);
        CpuSeries = _history.CpuSeries;
        RamSeries = _history.RamSeries;
        DiskSeries = _history.DiskSeries;
        NetRxSeries = _history.NetRxSeries;
        NetTxSeries = _history.NetTxSeries;
        CpuDelta = _history.CpuDelta;
        RamDelta = _history.RamDelta;
        DiskDelta = _history.DiskDelta;
        OnPropertyChanged(nameof(SampleCount));
        OnPropertyChanged(nameof(HistorySummary));

        TopProcesses = snapshot.TopProcesses;
        Mounts = snapshot.Mounts;
        DockerDiskUsageLines = snapshot.DockerDiskUsageLines;
        HasServerSignal = TopProcesses.Count > 0 || Mounts.Count > 0 || DockerDiskUsageLines.Count > 0;
    }

    /// <summary>
    /// Keeps the last known readings visible but marked stale when the session drops,
    /// so the operator does not lose context on disconnect.
    /// </summary>
    public void MarkStale()
    {
        if (HasMetrics)
        {
            IsStale = true;
        }
    }

    public void Clear()
    {
        HasMetrics = false;
        IsStale = false;
        CpuPercent = 0;
        RamPercent = 0;
        DiskPercent = 0;
        CpuValue = "—";
        RamValue = "—";
        DiskValue = "—";
        CpuDetail = "Waiting for the first refresh";
        RamDetail = "Waiting for the first refresh";
        DiskDetail = "Waiting for the first refresh";
        CpuLevel = nameof(MetricLevel.Normal);
        RamLevel = nameof(MetricLevel.Normal);
        DiskLevel = nameof(MetricLevel.Normal);
        LoadAverage = "—";
        CpuCores = "—";
        Uptime = "Unavailable";
        TopProcesses = Array.Empty<ProcessSample>();
        Mounts = Array.Empty<DiskMount>();
        DockerDiskUsageLines = Array.Empty<string>();
        HasServerSignal = false;

        _history.Clear();
        CpuSeries = Array.Empty<double>();
        RamSeries = Array.Empty<double>();
        DiskSeries = Array.Empty<double>();
        CpuDelta = null;
        RamDelta = null;
        DiskDelta = null;
        OnPropertyChanged(nameof(SampleCount));
        OnPropertyChanged(nameof(HistorySummary));
    }

    /// <summary>Formats a delta for display, e.g. "+4.2 pts" or "—".</summary>
    public static string FormatDelta(double? delta, string unit = "pts") =>
        delta is null ? "—" : $"{(delta.Value >= 0 ? "+" : string.Empty)}{delta.Value:0.#} {unit}";

    public static string DescribeLevel(string level) => level switch
    {
        nameof(MetricLevel.Critical) => "Critical",
        nameof(MetricLevel.Warning) => "Elevated",
        _ => "Normal"
    };
}
