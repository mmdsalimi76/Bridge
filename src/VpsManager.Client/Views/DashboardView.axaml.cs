using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using VpsManager.Client.ViewModels;
using VpsManager.Client.ViewModels.Dashboard;
using VpsManager.Client.Views.Controls;

namespace VpsManager.Client.Views;

public partial class DashboardView : UserControl
{
    private DashboardViewModel? _viewModel;
    private WorkloadsViewModel? _workloads;
    private TerminalViewer? _logViewer;
    private CheckBox? _followToggle;

    public DashboardView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        Detach();

        _viewModel = DataContext as DashboardViewModel;
        if (_viewModel == null)
        {
            return;
        }

        _logViewer = this.FindControl<TerminalViewer>("WorkloadLogViewer");
        _followToggle = this.FindControl<CheckBox>("FollowLogsToggle");
        if (_followToggle != null)
        {
            _followToggle.PropertyChanged += OnFollowToggleChanged;
            if (_logViewer != null)
            {
                _logViewer.AutoScroll = _followToggle.IsChecked == true;
            }
        }

        _viewModel.ClipboardRequested += CopyToClipboardAsync;
        _viewModel.SaveReportRequested += SaveReportAsync;
        _viewModel.Workloads.CopyLogsRequested += CopyToClipboardAsync;
        _viewModel.Workloads.PropertyChanged += OnWorkloadsPropertyChanged;
        _workloads = _viewModel.Workloads;

        var passwordBox = this.FindControl<TextBox>("PasswordBox");
        if (passwordBox != null)
        {
            passwordBox.KeyDown += OnPasswordKeyDown;
        }

        RenderLogs();
    }

    private void Detach()
    {
        if (_viewModel != null)
        {
            _viewModel.ClipboardRequested -= CopyToClipboardAsync;
            _viewModel.SaveReportRequested -= SaveReportAsync;
            _viewModel.Workloads.CopyLogsRequested -= CopyToClipboardAsync;
            _viewModel.Workloads.PropertyChanged -= OnWorkloadsPropertyChanged;
        }

        if (_followToggle != null)
        {
            _followToggle.PropertyChanged -= OnFollowToggleChanged;
        }

        var passwordBox = this.FindControl<TextBox>("PasswordBox");
        if (passwordBox != null)
        {
            passwordBox.KeyDown -= OnPasswordKeyDown;
        }

        _viewModel = null;
        _workloads = null;
    }

    private void OnFollowToggleChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == CheckBox.IsCheckedProperty && _logViewer != null)
        {
            _logViewer.AutoScroll = _followToggle?.IsChecked == true;
        }
    }

    private void OnPasswordKeyDown(object? sender, KeyEventArgs e)
    {
        // Enter submits the credential prompt, matching normal desktop expectations.
        if (e.Key == Key.Enter && _viewModel != null && _viewModel.Connection.ConnectCommand.CanExecute(null))
        {
            _viewModel.Connection.ConnectCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnWorkloadsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkloadsViewModel.Logs))
        {
            RenderLogs();
        }
    }

    private void RenderLogs()
    {
        _logViewer?.SetText(_workloads?.Logs ?? string.Empty);
        if (_followToggle?.IsChecked == true)
        {
            _logViewer?.ScrollToEnd();
        }
    }

    private async Task CopyToClipboardAsync(string text)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard == null)
        {
            throw new InvalidOperationException("Clipboard is unavailable.");
        }

        await clipboard.SetTextAsync(text);
    }

    private async Task<string?> SaveReportAsync(string suggestedName, string content)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null)
        {
            return null;
        }

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save diagnostics report",
            SuggestedFileName = suggestedName,
            DefaultExtension = "md",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("Markdown") { Patterns = new[] { "*.md" } }
            }
        });

        if (file == null)
        {
            return null;
        }

        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(content);
        return file.Name;
    }
}
