using System;
using System.Collections.Generic;
using System.Linq;

namespace VpsManager.Core.Services.Overview;

public sealed record MetricSample(
    DateTime CapturedAt,
    double CpuPercent,
    double RamPercent,
    double DiskPercent,
    double NetRxMbps = 0,
    double NetTxMbps = 0);

/// <summary>
/// Keeps a bounded rolling window of metric samples for the Overview sparklines and
/// delta indicators. In-memory only: this is live situational awareness, not history.
/// </summary>
public sealed class MetricHistoryStore
{
    private readonly Queue<MetricSample> _samples;

    public MetricHistoryStore(int capacity = 60)
    {
        if (capacity < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "History needs at least two samples to be useful.");
        }

        Capacity = capacity;
        _samples = new Queue<MetricSample>(capacity);
    }

    public int Capacity { get; }

    public int Count => _samples.Count;

    public IReadOnlyList<MetricSample> Samples => _samples.ToArray();

    public MetricSample? Latest => _samples.Count > 0 ? _samples.Last() : null;

    public void Add(MetricSample sample)
    {
        _samples.Enqueue(sample);
        while (_samples.Count > Capacity)
        {
            _samples.Dequeue();
        }
    }

    public void Add(OverviewSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var metrics = snapshot.Metrics;
        Add(new MetricSample(
            snapshot.CapturedAt,
            metrics.HasCpuMetrics ? metrics.CpuUsagePercent : 0,
            metrics.HasRamMetrics ? metrics.RamUsagePercent : 0,
            metrics.HasDiskMetrics ? metrics.DiskUsagePercent : 0,
            metrics.HasNetMetrics ? metrics.NetRxMbps : 0,
            metrics.HasNetMetrics ? metrics.NetTxMbps : 0));
    }

    public void Clear() => _samples.Clear();

    public IReadOnlyList<double> CpuSeries => Series(sample => sample.CpuPercent);

    public IReadOnlyList<double> RamSeries => Series(sample => sample.RamPercent);

    public IReadOnlyList<double> DiskSeries => Series(sample => sample.DiskPercent);

    public IReadOnlyList<double> NetRxSeries => Series(sample => sample.NetRxMbps);

    public IReadOnlyList<double> NetTxSeries => Series(sample => sample.NetTxMbps);

    /// <summary>Change between the last two samples, or null when there is nothing to compare.</summary>
    public double? CpuDelta => Delta(sample => sample.CpuPercent);

    public double? RamDelta => Delta(sample => sample.RamPercent);

    public double? DiskDelta => Delta(sample => sample.DiskPercent);

    private IReadOnlyList<double> Series(Func<MetricSample, double> selector) =>
        _samples.Select(selector).ToArray();

    private double? Delta(Func<MetricSample, double> selector)
    {
        if (_samples.Count < 2)
        {
            return null;
        }

        var values = _samples.ToArray();
        return selector(values[^1]) - selector(values[^2]);
    }
}
