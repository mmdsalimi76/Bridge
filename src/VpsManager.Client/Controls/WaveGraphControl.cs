using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace VpsManager.Client.Controls;

/// <summary>
/// High-performance smooth cubic-spline wave graph control.
/// Renders rolling telemetry with cubic Bézier curve interpolation,
/// an optional vertical gradient glow under the curve, and dual-series
/// support for comparisons (e.g. Network RX solid vs. TX dashed).
/// Always produces a smooth, aesthetically rich waveform even when data
/// is initial, sparse, or awaiting server probes.
/// </summary>
public class WaveGraphControl : Control
{
    public static readonly StyledProperty<IReadOnlyList<double>?> ValuesProperty =
        AvaloniaProperty.Register<WaveGraphControl, IReadOnlyList<double>?>(nameof(Values));

    public static readonly StyledProperty<IReadOnlyList<double>?> SecondaryValuesProperty =
        AvaloniaProperty.Register<WaveGraphControl, IReadOnlyList<double>?>(nameof(SecondaryValues));

    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<WaveGraphControl, IBrush?>(nameof(Stroke));

    public static readonly StyledProperty<IBrush?> SecondaryStrokeProperty =
        AvaloniaProperty.Register<WaveGraphControl, IBrush?>(nameof(SecondaryStroke));

    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<WaveGraphControl, IBrush?>(nameof(Fill));

    public static readonly StyledProperty<IBrush?> BaselineBrushProperty =
        AvaloniaProperty.Register<WaveGraphControl, IBrush?>(nameof(BaselineBrush));

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<WaveGraphControl, double>(nameof(Maximum), 100d);

    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<WaveGraphControl, double>(nameof(StrokeThickness), 2.0d);

    public static readonly StyledProperty<bool> IsSecondaryDashedProperty =
        AvaloniaProperty.Register<WaveGraphControl, bool>(nameof(IsSecondaryDashed), true);

    public static readonly StyledProperty<bool> ShowBaselineProperty =
        AvaloniaProperty.Register<WaveGraphControl, bool>(nameof(ShowBaseline), true);

    public static readonly StyledProperty<double> BaselinePercentProperty =
        AvaloniaProperty.Register<WaveGraphControl, double>(nameof(BaselinePercent), 24d);

    static WaveGraphControl()
    {
        AffectsRender<WaveGraphControl>(
            ValuesProperty,
            SecondaryValuesProperty,
            StrokeProperty,
            SecondaryStrokeProperty,
            FillProperty,
            BaselineBrushProperty,
            MaximumProperty,
            StrokeThicknessProperty,
            IsSecondaryDashedProperty,
            ShowBaselineProperty,
            BaselinePercentProperty);
    }

    public WaveGraphControl()
    {
        ClipToBounds = true;
    }

    public IReadOnlyList<double>? Values
    {
        get => GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public IReadOnlyList<double>? SecondaryValues
    {
        get => GetValue(SecondaryValuesProperty);
        set => SetValue(SecondaryValuesProperty, value);
    }

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public IBrush? SecondaryStroke
    {
        get => GetValue(SecondaryStrokeProperty);
        set => SetValue(SecondaryStrokeProperty, value);
    }

    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public IBrush? BaselineBrush
    {
        get => GetValue(BaselineBrushProperty);
        set => SetValue(BaselineBrushProperty, value);
    }

    public double Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public bool IsSecondaryDashed
    {
        get => GetValue(IsSecondaryDashedProperty);
        set => SetValue(IsSecondaryDashedProperty, value);
    }

    public bool ShowBaseline
    {
        get => GetValue(ShowBaselineProperty);
        set => SetValue(ShowBaselineProperty, value);
    }

    public double BaselinePercent
    {
        get => GetValue(BaselinePercentProperty);
        set => SetValue(BaselinePercentProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var width = Bounds.Width;
        var height = Bounds.Height;

        if (width < 4 || height < 4)
        {
            return;
        }

        // Draw faint baseline if enabled
        if (ShowBaseline)
        {
            var baseBrush = BaselineBrush ?? ResolveFallbackBaselineBrush();
            if (baseBrush != null)
            {
                var baselinePen = new Pen(baseBrush, 1);
                context.DrawLine(baselinePen, new Point(0, height - 1), new Point(width, height - 1));
            }
        }

        var max = Maximum > 0 ? Maximum : 100d;
        var stroke = ResolveStrokeBrush();

        // 1. Primary Series: guaranteed to display a rich smooth wave curve and gradient fill
        var rawValues = Values;
        var displayValues = PrepareDisplaySeries(rawValues, max, defaultBaselinePercent: BaselinePercent, isSecondary: false);
        if (displayValues.Count >= 2)
        {
            var points = ComputePoints(displayValues, width, height, max);
            var (strokeGeo, fillGeo) = BuildSplineGeometries(points, height);

            // Draw vertical gradient fill area under the wave curve
            var fillBrush = ResolveFillBrush(stroke);
            if (fillBrush != null && fillGeo != null)
            {
                context.DrawGeometry(fillBrush, null, fillGeo);
            }

            // Draw main wave stroke
            var strokePen = new Pen(stroke, StrokeThickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            context.DrawGeometry(null, strokePen, strokeGeo);
        }

        // 2. Secondary Series (e.g. Network TX dashed series)
        var secRaw = SecondaryValues;
        var secStroke = SecondaryStroke;
        if (secStroke != null || secRaw != null || IsSecondaryDashed)
        {
            var resolvedSecStroke = secStroke ?? stroke;
            var secBaseline = Math.Max(10d, BaselinePercent * 0.65);
            var secValues = PrepareDisplaySeries(secRaw, max, defaultBaselinePercent: secBaseline, isSecondary: true);
            if (secValues.Count >= 2)
            {
                var secPoints = ComputePoints(secValues, width, height, max);
                var (secStrokeGeo, _) = BuildSplineGeometries(secPoints, height);

                var dashStyle = IsSecondaryDashed ? DashStyle.Dash : null;
                var secPen = new Pen(resolvedSecStroke, 1.4, dashStyle: dashStyle, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
                context.DrawGeometry(null, secPen, secStrokeGeo);
            }
        }
    }

    private static IReadOnlyList<double> PrepareDisplaySeries(
        IReadOnlyList<double>? rawValues,
        double max,
        double defaultBaselinePercent,
        bool isSecondary)
    {
        // If null or empty: provide an ambient resting wave so the control is immediately visible and alive
        if (rawValues == null || rawValues.Count == 0)
        {
            return GenerateAmbientWave(max, defaultBaselinePercent, isSecondary);
        }

        // If single value: synthesize an undulating wave centered around that reading and ending exactly at that reading
        if (rawValues.Count == 1)
        {
            return GenerateSingleValueWave(rawValues[0], max, isSecondary);
        }

        // If sparse series (2 to 11 points): interpolate smooth points so Catmull-Rom forms natural undulating crests
        if (rawValues.Count < 12)
        {
            return InterpolateSmoothSeries(rawValues, targetPoints: 18);
        }

        return rawValues;
    }

    private static IReadOnlyList<double> GenerateAmbientWave(double max, double baselinePercent, bool isSecondary)
    {
        const int count = 18;
        var result = new double[count];
        var phase = isSecondary ? 1.5 : 0.0;
        var amplitude = isSecondary ? 0.30 : 0.45;
        var center = Math.Clamp(baselinePercent, 10.0, max * 0.65);

        for (var i = 0; i < count; i++)
        {
            var t = (double)i / (count - 1);
            var wave = (Math.Sin((t * 3.6 * Math.PI) + phase) * 0.75)
                     + (Math.Cos((t * 2.1 * Math.PI) + (phase * 1.4)) * 0.25);
            var val = center * (1.0 + (wave * amplitude));
            result[i] = Math.Clamp(val, 2.0, max * 0.95);
        }

        return result;
    }

    private static IReadOnlyList<double> GenerateSingleValueWave(double value, double max, bool isSecondary)
    {
        const int count = 18;
        var result = new double[count];
        var current = Math.Max(value, 3.0);
        var phase = isSecondary ? 1.8 : 0.0;
        var amplitude = isSecondary ? 0.22 : 0.32;

        for (var i = 0; i < count; i++)
        {
            var t = (double)i / (count - 1);
            // Damping ensures the waveform smoothly converges exactly to the live reading at the rightmost edge
            var damping = 1.0 - (0.85 * t * t);
            var wave = ((Math.Sin((t * 3.8 * Math.PI) + phase) * 0.75)
                     + (Math.Cos((t * 2.2 * Math.PI) + phase) * 0.25)) * amplitude * damping;
            var val = current * (1.0 + wave);
            result[i] = Math.Clamp(val, 1.0, max * 0.95);
        }

        // Ensure the latest point exactly equals the live reading
        result[count - 1] = Math.Clamp(value, 0.0, max);
        return result;
    }

    private static IReadOnlyList<double> InterpolateSmoothSeries(IReadOnlyList<double> raw, int targetPoints)
    {
        var inCount = raw.Count;
        if (inCount >= targetPoints)
        {
            return raw;
        }

        var result = new double[targetPoints];
        for (var i = 0; i < targetPoints; i++)
        {
            var t = (double)i / (targetPoints - 1) * (inCount - 1);
            var index0 = (int)Math.Floor(t);
            var index1 = Math.Min(index0 + 1, inCount - 1);
            var fraction = t - index0;

            // Cosine easing creates gentle organic transitions between points
            var ft = fraction * Math.PI;
            var f = (1.0 - Math.Cos(ft)) * 0.5;
            result[i] = (raw[index0] * (1.0 - f)) + (raw[index1] * f);
        }

        return result;
    }

    private static Point[] ComputePoints(IReadOnlyList<double> values, double width, double height, double max)
    {
        var count = values.Count;
        var stepX = width / (count - 1);
        var points = new Point[count];

        // Vertical margin so peaks don't clip against card edges
        const double topMargin = 3.0;
        const double bottomMargin = 2.0;
        var usableHeight = Math.Max(1.0, height - topMargin - bottomMargin);

        for (var i = 0; i < count; i++)
        {
            var normalized = Math.Clamp(values[i] / max, 0d, 1d);
            var x = i * stepX;
            var y = topMargin + (usableHeight * (1d - normalized));
            points[i] = new Point(x, y);
        }

        return points;
    }

    private static (StreamGeometry strokeGeo, StreamGeometry? fillGeo) BuildSplineGeometries(Point[] points, double height)
    {
        var count = points.Length;
        var strokeGeo = new StreamGeometry();
        var fillGeo = new StreamGeometry();

        using (var strokeCtx = strokeGeo.Open())
        using (var fillCtx = fillGeo.Open())
        {
            strokeCtx.BeginFigure(points[0], false);
            fillCtx.BeginFigure(points[0], true);

            for (var i = 0; i < count - 1; i++)
            {
                var p0 = i > 0 ? points[i - 1] : points[i];
                var p1 = points[i];
                var p2 = points[i + 1];
                var p3 = i + 2 < count ? points[i + 2] : p2;

                // Catmull-Rom to Cubic Bézier control points
                var c1 = new Point(
                    p1.X + (p2.X - p0.X) / 6.0,
                    Math.Clamp(p1.Y + (p2.Y - p0.Y) / 6.0, 0, height)
                );
                var c2 = new Point(
                    p2.X - (p3.X - p1.X) / 6.0,
                    Math.Clamp(p2.Y - (p3.Y - p1.Y) / 6.0, 0, height)
                );

                strokeCtx.CubicBezierTo(c1, c2, p2);
                fillCtx.CubicBezierTo(c1, c2, p2);
            }

            strokeCtx.EndFigure(false);

            // Close fill polygon down to the baseline
            fillCtx.LineTo(new Point(points[^1].X, height));
            fillCtx.LineTo(new Point(points[0].X, height));
            fillCtx.EndFigure(true);
        }

        return (strokeGeo, fillGeo);
    }

    private IBrush ResolveStrokeBrush()
    {
        if (Stroke != null)
        {
            return Stroke;
        }

        if (Application.Current != null && Application.Current.TryFindResource("MetricCpuWaveBrush", out var res) && res is IBrush brush)
        {
            return brush;
        }

        return Brushes.SkyBlue;
    }

    private static IBrush? ResolveFallbackBaselineBrush()
    {
        if (Application.Current != null && Application.Current.TryFindResource("DividerBrush", out var res) && res is IBrush b)
        {
            return b;
        }

        return null;
    }

    private IBrush? ResolveFillBrush(IBrush stroke)
    {
        if (Fill != null)
        {
            return Fill;
        }

        // Auto-generate linear gradient from stroke color if it's an ISolidColorBrush
        if (stroke is ISolidColorBrush scb)
        {
            var baseColor = scb.Color;
            return new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(110, baseColor.R, baseColor.G, baseColor.B), 0.0),
                    new GradientStop(Color.FromArgb(35, baseColor.R, baseColor.G, baseColor.B), 0.55),
                    new GradientStop(Color.FromArgb(0, baseColor.R, baseColor.G, baseColor.B), 1.0)
                }
            };
        }

        return null;
    }
}
