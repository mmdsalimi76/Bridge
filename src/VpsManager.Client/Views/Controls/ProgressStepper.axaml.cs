using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using System.Collections.Generic;
using VpsManager.Core.Models;

namespace VpsManager.Client.Views.Controls;

public partial class ProgressStepper : UserControl
{
    public static readonly StyledProperty<IList<PipelineStep>?> StepsProperty =
        AvaloniaProperty.Register<ProgressStepper, IList<PipelineStep>?>(nameof(Steps));

    public IList<PipelineStep>? Steps
    {
        get => GetValue(StepsProperty);
        set => SetValue(StepsProperty, value);
    }

    public ProgressStepper()
    {
        InitializeComponent();
    }
}