using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;

namespace VpsManager.Client.Controls;

/// <summary>
/// A single host metric: value, threshold-aware colour, trend bar and rolling sparkline.
/// Reused for CPU, memory and disk so the three cards cannot drift apart.
/// </summary>
public partial class MetricCard : UserControl
{
    public static readonly StyledProperty<string> HeaderProperty =
        AvaloniaProperty.Register<MetricCard, string>(nameof(Header), string.Empty);

    public static readonly StyledProperty<string> ValueProperty =
        AvaloniaProperty.Register<MetricCard, string>(nameof(Value), "—");

    public static readonly StyledProperty<string> DetailProperty =
        AvaloniaProperty.Register<MetricCard, string>(nameof(Detail), string.Empty);

    public static readonly StyledProperty<string> LevelProperty =
        AvaloniaProperty.Register<MetricCard, string>(nameof(Level), "Normal");

    public static readonly StyledProperty<double> PercentProperty =
        AvaloniaProperty.Register<MetricCard, double>(nameof(Percent));

    public static readonly StyledProperty<bool> HasBarProperty =
        AvaloniaProperty.Register<MetricCard, bool>(nameof(HasBar), true);

    public static readonly StyledProperty<IReadOnlyList<double>?> SeriesProperty =
        AvaloniaProperty.Register<MetricCard, IReadOnlyList<double>?>(nameof(Series));

    public static readonly StyledProperty<string> DeltaTextProperty =
        AvaloniaProperty.Register<MetricCard, string>(nameof(DeltaText), string.Empty);

    public static readonly StyledProperty<bool> HasDeltaProperty =
        AvaloniaProperty.Register<MetricCard, bool>(nameof(HasDelta));

    private static readonly DirectProperty<MetricCard, bool> HasSeriesProperty =
        AvaloniaProperty.RegisterDirect<MetricCard, bool>(nameof(HasSeries), card => card.HasSeries);

    public MetricCard()
    {
        InitializeComponent();
    }

    public string Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public string Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public string Detail
    {
        get => GetValue(DetailProperty);
        set => SetValue(DetailProperty, value);
    }

    /// <summary>Normal, Warning or Critical — drives the accent colour.</summary>
    public string Level
    {
        get => GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }

    public double Percent
    {
        get => GetValue(PercentProperty);
        set => SetValue(PercentProperty, value);
    }

    public bool HasBar
    {
        get => GetValue(HasBarProperty);
        set => SetValue(HasBarProperty, value);
    }

    public IReadOnlyList<double>? Series
    {
        get => GetValue(SeriesProperty);
        set => SetValue(SeriesProperty, value);
    }

    public string DeltaText
    {
        get => GetValue(DeltaTextProperty);
        set => SetValue(DeltaTextProperty, value);
    }

    public bool HasDelta
    {
        get => GetValue(HasDeltaProperty);
        set => SetValue(HasDeltaProperty, value);
    }

    /// <summary>A trend line needs at least two readings to be meaningful.</summary>
    public bool HasSeries => Series is { Count: > 1 };

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SeriesProperty)
        {
            var hasSeries = HasSeries;
            RaisePropertyChanged(HasSeriesProperty, !hasSeries, hasSeries);
        }
    }
}
