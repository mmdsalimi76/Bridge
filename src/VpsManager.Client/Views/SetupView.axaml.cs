using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using VpsManager.Client.ViewModels;

namespace VpsManager.Client.Views;

public partial class SetupView : UserControl
{
    private SetupViewModel? _viewModel;

    public SetupView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel != null)
        {
            _viewModel.ClipboardRequested -= CopyToClipboardAsync;
        }

        _viewModel = DataContext as SetupViewModel;
        if (_viewModel != null)
        {
            _viewModel.ClipboardRequested += CopyToClipboardAsync;
        }
    }

    private async Task CopyToClipboardAsync(string text)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null)
        {
            await clipboard.SetTextAsync(text);
        }
    }
}
