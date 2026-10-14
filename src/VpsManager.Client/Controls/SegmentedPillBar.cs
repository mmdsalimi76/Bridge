using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace VpsManager.Client.Controls;

/// <summary>
/// A modern segmented pill-shaped progress bar for multi-resource allocations
/// (e.g., Active RAM vs. Buffers/Cache vs. Free memory).
/// </summary>
public class SegmentedPillBar : Control
{
    public static readonly StyledProperty<double> Value1Property =
        AvaloniaProperty.Register<SegmentedPillBar, double>(nameof(Value1));

    public static readonly StyledProperty<double> Value2Property =
        AvaloniaProperty.Register<SegmentedPillBar, double>(nameof(Value2));

    public static readonly StyledProperty<double> TotalProperty =
        AvaloniaProperty.Register<SegmentedPillBar, double>(nameof(Total), 100d);

    public static readonly StyledProperty<IBrush?> Segment1BrushProperty =
        AvaloniaProperty.Register<SegmentedPillBar, IBrush?>(nameof(Segment1Brush));

    public static readonly StyledProperty<IBrush?> Segment2BrushProperty =
        AvaloniaProperty.Register<SegmentedPillBar, IBrush?>(nameof(Segment2Brush));

    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<SegmentedPillBar, IBrush?>(nameof(TrackBrush));

    public static readonly StyledProperty<CornerRadius> CornerRadiusProperty =
        AvaloniaProperty.Register<SegmentedPillBar, CornerRadius>(nameof(CornerRadius), new CornerRadius(4));

    static SegmentedPillBar()
    {
        AffectsRender<SegmentedPillBar>(
            Value1Property,
            Value2Property,
            TotalProperty,
            Segment1BrushProperty,
            Segment2BrushProperty,
            TrackBrushProperty,
            CornerRadiusProperty);
    }

    public double Value1
    {
        get => GetValue(Value1Property);
        set => SetValue(Value1Property, value);
    }

    public double Value2
    {
        get => GetValue(Value2Property);
        set => SetValue(Value2Property, value);
    }

    public double Total
    {
        get => GetValue(TotalProperty);
        set => SetValue(TotalProperty, value);
    }

    public IBrush? Segment1Brush
    {
        get => GetValue(Segment1BrushProperty);
        set => SetValue(Segment1BrushProperty, value);
    }

    public IBrush? Segment2Brush
    {
        get => GetValue(Segment2BrushProperty);
        set => SetValue(Segment2BrushProperty, value);
    }

    public IBrush? TrackBrush
    {
        get => GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public CornerRadius CornerRadius
    {
        get => GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var width = Bounds.Width;
        var height = Bounds.Height;

        if (width <= 0 || height <= 0)
        {
            return;
        }

        var radius = CornerRadius.TopLeft;

        // 1. Draw track background
        var track = TrackBrush ?? new SolidColorBrush(Color.FromArgb(50, 255, 255, 255));
        var trackRect = new RoundedRect(new Rect(0, 0, width, height), radius);
        context.DrawRectangle(track, null, trackRect);

        var total = Total > 0 ? Total : 100d;
        var w1 = Math.Clamp((Value1 / total) * width, 0d, width);
        var w2 = Math.Clamp((Value2 / total) * width, 0d, Math.Max(0d, width - w1));

        // Clip subsequent segments to the pill track bounds
        using (context.PushClip(trackRect))
        {
            // 2. Draw segment 1 (e.g. Active memory)
            if (w1 > 0 && Segment1Brush != null)
            {
                context.FillRectangle(Segment1Brush, new Rect(0, 0, w1, height));
            }

            // 3. Draw segment 2 (e.g. Buffers / Cache)
            if (w2 > 0 && Segment2Brush != null)
            {
                context.FillRectangle(Segment2Brush, new Rect(w1, 0, w2, height));
            }
        }
    }
}
