using Avalonia;
using Avalonia.Controls;

namespace VpsManager.Client.Controls;

/// <summary>
/// A compact status tile: header, coloured headline, detail and an optional footer.
/// </summary>
public partial class StatusCard : UserControl
{
    public static readonly StyledProperty<string> HeaderProperty =
        AvaloniaProperty.Register<StatusCard, string>(nameof(Header), string.Empty);

    public static readonly StyledProperty<string> HeadlineProperty =
        AvaloniaProperty.Register<StatusCard, string>(nameof(Headline), string.Empty);

    public static readonly StyledProperty<string> DetailProperty =
        AvaloniaProperty.Register<StatusCard, string>(nameof(Detail), string.Empty);

    public static readonly StyledProperty<string> FooterProperty =
        AvaloniaProperty.Register<StatusCard, string>(nameof(Footer), string.Empty);

    public static readonly StyledProperty<string> LevelProperty =
        AvaloniaProperty.Register<StatusCard, string>(nameof(Level), "Normal");

    private static readonly DirectProperty<StatusCard, bool> HasFooterProperty =
        AvaloniaProperty.RegisterDirect<StatusCard, bool>(nameof(HasFooter), card => card.HasFooter);

    public StatusCard()
    {
        InitializeComponent();
    }

    public string Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public string Headline
    {
        get => GetValue(HeadlineProperty);
        set => SetValue(HeadlineProperty, value);
    }

    public string Detail
    {
        get => GetValue(DetailProperty);
        set => SetValue(DetailProperty, value);
    }

    public string Footer
    {
        get => GetValue(FooterProperty);
        set => SetValue(FooterProperty, value);
    }

    public string Level
    {
        get => GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }

    public bool HasFooter => !string.IsNullOrWhiteSpace(Footer);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == FooterProperty)
        {
            var hasFooter = HasFooter;
            RaisePropertyChanged(HasFooterProperty, !hasFooter, hasFooter);
        }
    }
}
