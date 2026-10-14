using System;
using Avalonia;
using Avalonia.Controls;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using VpsManager.Core.Helpers;

namespace VpsManager.Client.Views.Controls;

public partial class TerminalViewer : UserControl
{
    public static readonly StyledProperty<bool> AutoScrollProperty =
        AvaloniaProperty.Register<TerminalViewer, bool>(nameof(AutoScroll), true);

    public static readonly StyledProperty<bool> StripAnsiCodesProperty =
        AvaloniaProperty.Register<TerminalViewer, bool>(nameof(StripAnsiCodes), false);

    public static readonly StyledProperty<bool> ColorizeAnsiProperty =
        AvaloniaProperty.Register<TerminalViewer, bool>(nameof(ColorizeAnsi), true);

    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<TerminalViewer, string>(nameof(Text), string.Empty);

    public static readonly StyledProperty<double> TerminalFontSizeProperty =
        AvaloniaProperty.Register<TerminalViewer, double>(nameof(TerminalFontSize), 12.0);

    public static readonly StyledProperty<bool> WordWrapProperty =
        AvaloniaProperty.Register<TerminalViewer, bool>(nameof(WordWrap), true);

    private readonly TextEditor _editor;
    private readonly AnsiColorizingTransformer _ansiColorizer = new();
    private bool _colorizerAttached;

    public TerminalViewer()
    {
        InitializeComponent();
        _editor = this.FindControl<TextEditor>("TerminalEditor") ?? throw new InvalidOperationException("TerminalEditor not found");

        // Configure the editor for terminal-like appearance
        _editor.Options = new TextEditorOptions
        {
            EnableHyperlinks = false,
            EnableEmailHyperlinks = false
        };

        ApplyColorizerState();
    }

    /// <summary>Follow new output by scrolling to the end. Bind a "Follow" toggle here.</summary>
    public bool AutoScroll
    {
        get => GetValue(AutoScrollProperty);
        set => SetValue(AutoScrollProperty, value);
    }

    /// <summary>Remove ANSI escape codes instead of rendering them.</summary>
    public bool StripAnsiCodes
    {
        get => GetValue(StripAnsiCodesProperty);
        set => SetValue(StripAnsiCodesProperty, value);
    }

    /// <summary>Render ANSI colors (overridden by <see cref="StripAnsiCodes"/>).</summary>
    public bool ColorizeAnsi
    {
        get => GetValue(ColorizeAnsiProperty);
        set => SetValue(ColorizeAnsiProperty, value);
    }

    /// <summary>Terminal text content for two-way or one-way binding.</summary>
    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>Editor font size.</summary>
    public double TerminalFontSize
    {
        get => GetValue(TerminalFontSizeProperty);
        set => SetValue(TerminalFontSizeProperty, value);
    }

    /// <summary>Word wrap mode.</summary>
    public bool WordWrap
    {
        get => GetValue(WordWrapProperty);
        set => SetValue(WordWrapProperty, value);
    }

    /// <summary>Appends text to the terminal.</summary>
    public void AppendText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var document = _editor.Document;
        document.Insert(document.TextLength, Prepare(text));

        if (AutoScroll)
        {
            _editor.ScrollToEnd();
        }
    }

    /// <summary>Replaces the terminal contents.</summary>
    public void SetText(string text)
    {
        _editor.Document.Text = Prepare(text);

        if (AutoScroll)
        {
            _editor.ScrollToEnd();
        }
    }

    /// <summary>Scrolls to the newest output.</summary>
    public void ScrollToEnd() => _editor.ScrollToEnd();

    /// <summary>Clears all text from the terminal.</summary>
    public void Clear()
    {
        _editor.Document.Text = string.Empty;
    }

    /// <summary>Gets the current text content of the terminal.</summary>
    public string GetText()
    {
        return _editor.Document.Text;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ColorizeAnsiProperty || change.Property == StripAnsiCodesProperty)
        {
            ApplyColorizerState();
            RedrawExistingText();
        }
        else if (change.Property == TextProperty)
        {
            var newText = change.GetNewValue<string>() ?? string.Empty;
            if (_editor.Document.Text != newText)
            {
                SetText(newText);
            }
        }
        else if (change.Property == TerminalFontSizeProperty)
        {
            _editor.FontSize = change.GetNewValue<double>();
        }
        else if (change.Property == WordWrapProperty)
        {
            _editor.WordWrap = change.GetNewValue<bool>();
        }
    }

    private string Prepare(string text)
    {
        var keepCodes = ColorizeAnsi && !StripAnsiCodes;
        return keepCodes ? text : AnsiColorParser.StripAnsiCodes(text);
    }

    private void ApplyColorizerState()
    {
        var shouldColorize = ColorizeAnsi && !StripAnsiCodes;
        if (shouldColorize == _colorizerAttached)
        {
            return;
        }

        var transformers = _editor.TextArea.TextView.LineTransformers;
        if (shouldColorize)
        {
            transformers.Add(_ansiColorizer);
        }
        else
        {
            transformers.Remove(_ansiColorizer);
        }

        _colorizerAttached = shouldColorize;
    }

    private void RedrawExistingText()
    {
        var current = _editor.Document.Text;
        if (string.IsNullOrEmpty(current))
        {
            return;
        }

        var rendered = Prepare(current);
        _editor.Document.Text = rendered;
    }
}
