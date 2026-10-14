using Avalonia.Data.Converters;
using Avalonia.Media;
using System.Globalization;
using VpsManager.Core.Models;

namespace VpsManager.Client.Converters;

public class StatusToColorConverter : IValueConverter
{
    public object? Convert(object? value, System.Type targetType, object? parameter, CultureInfo culture)
    {
        string? mode = parameter as string;
        bool isBackground = string.Equals(mode, "background", System.StringComparison.OrdinalIgnoreCase);

        if (value is PipelineStatus status)
        {
            if (isBackground)
            {
                return status switch
                {
                    PipelineStatus.NotStarted => ResolveBrush("PanelBrush", Brushes.DimGray),
                    PipelineStatus.InProgress => ResolveBrush("StepInProgressBackgroundBrush", Brushes.DarkSlateBlue),
                    PipelineStatus.Completed => ResolveBrush("StepCompletedBackgroundBrush", Brushes.DarkGreen),
                    PipelineStatus.Failed => ResolveBrush("StepFailedBackgroundBrush", Brushes.DarkRed),
                    PipelineStatus.Cancelled => ResolveBrush("StepSkippedBackgroundBrush", Brushes.DarkGoldenrod),
                    _ => ResolveBrush("PanelBrush", Brushes.DimGray)
                };
            }

            return status switch
            {
                PipelineStatus.NotStarted => ResolveBrush("TextMutedBrush", Brushes.Gray),
                PipelineStatus.InProgress => ResolveBrush("StepInProgressBrush", Brushes.DeepSkyBlue),
                PipelineStatus.Completed => ResolveBrush("StepCompletedBrush", Brushes.LimeGreen),
                PipelineStatus.Failed => ResolveBrush("StepFailedBrush", Brushes.IndianRed),
                PipelineStatus.Cancelled => ResolveBrush("StepSkippedBrush", Brushes.Goldenrod),
                _ => ResolveBrush("TextMutedBrush", Brushes.Gray)
            };
        }

        if (value is PipelineStepStatus stepStatus)
        {
            if (isBackground)
            {
                return stepStatus switch
                {
                    PipelineStepStatus.Pending => ResolveBrush("StepPendingBackgroundBrush", Brushes.DarkSlateGray),
                    PipelineStepStatus.InProgress => ResolveBrush("StepInProgressBackgroundBrush", Brushes.DarkSlateBlue),
                    PipelineStepStatus.Completed => ResolveBrush("StepCompletedBackgroundBrush", Brushes.DarkGreen),
                    PipelineStepStatus.Failed => ResolveBrush("StepFailedBackgroundBrush", Brushes.DarkRed),
                    PipelineStepStatus.Skipped => ResolveBrush("StepSkippedBackgroundBrush", Brushes.DarkGoldenrod),
                    _ => ResolveBrush("StepPendingBackgroundBrush", Brushes.DarkSlateGray)
                };
            }

            return stepStatus switch
            {
                PipelineStepStatus.Pending => ResolveBrush("StepPendingBrush", Brushes.SlateGray),
                PipelineStepStatus.InProgress => ResolveBrush("StepInProgressBrush", Brushes.DeepSkyBlue),
                PipelineStepStatus.Completed => ResolveBrush("StepCompletedBrush", Brushes.LimeGreen),
                PipelineStepStatus.Failed => ResolveBrush("StepFailedBrush", Brushes.IndianRed),
                PipelineStepStatus.Skipped => ResolveBrush("StepSkippedBrush", Brushes.Gold),
                _ => ResolveBrush("StepPendingBrush", Brushes.SlateGray)
            };
        }

        return ResolveBrush("TextMutedBrush", Brushes.Gray);
    }

    private static IBrush ResolveBrush(string key, IBrush fallback)
    {
        if (Avalonia.Application.Current != null
            && Avalonia.Application.Current.TryGetResource(key, null, out var res)
            && res is IBrush brush)
        {
            return brush;
        }
        return fallback;
    }

    public object? ConvertBack(object? value, System.Type targetType, object? parameter, CultureInfo culture)
    {
        throw new System.NotImplementedException();
    }
}