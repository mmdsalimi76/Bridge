using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;

namespace VpsManager.Client.Controls;

/// <summary>
/// A reusable empty/disconnected state banner with an icon, title, description, and optional action button.
/// </summary>
public partial class EmptyStateView : UserControl
{
    public static readonly StyledProperty<string> IconProperty =
        AvaloniaProperty.Register<EmptyStateView, string>(nameof(Icon), "☁");

    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<EmptyStateView, string>(nameof(Title), "No Items Available");

    public static readonly StyledProperty<string> DescriptionProperty =
        AvaloniaProperty.Register<EmptyStateView, string>(nameof(Description), string.Empty);

    public static readonly StyledProperty<string> ActionTextProperty =
        AvaloniaProperty.Register<EmptyStateView, string>(nameof(ActionText), string.Empty);

    public static readonly StyledProperty<ICommand?> ActionCommandProperty =
        AvaloniaProperty.Register<EmptyStateView, ICommand?>(nameof(ActionCommand));

    public static readonly StyledProperty<object?> ActionCommandParameterProperty =
        AvaloniaProperty.Register<EmptyStateView, object?>(nameof(ActionCommandParameter));

    public static readonly DirectProperty<EmptyStateView, bool> HasActionProperty =
        AvaloniaProperty.RegisterDirect<EmptyStateView, bool>(nameof(HasAction), o => o.HasAction);

    public EmptyStateView()
    {
        InitializeComponent();
    }

    public string Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public string ActionText
    {
        get => GetValue(ActionTextProperty);
        set => SetValue(ActionTextProperty, value);
    }

    public ICommand? ActionCommand
    {
        get => GetValue(ActionCommandProperty);
        set => SetValue(ActionCommandProperty, value);
    }

    public object? ActionCommandParameter
    {
        get => GetValue(ActionCommandParameterProperty);
        set => SetValue(ActionCommandParameterProperty, value);
    }

    public bool HasAction => !string.IsNullOrWhiteSpace(ActionText);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ActionTextProperty)
        {
            RaisePropertyChanged(HasActionProperty, !string.IsNullOrWhiteSpace((string?)change.OldValue), HasAction);
        }
    }
}
