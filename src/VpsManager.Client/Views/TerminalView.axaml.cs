using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using VpsManager.Client.ViewModels;
using VpsManager.Core.Models;

namespace VpsManager.Client.Views;

public partial class TerminalView : UserControl
{
    private TerminalViewModel? _viewModel;

    public TerminalView()
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

        _viewModel = DataContext as TerminalViewModel;
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

    private void OnCommandInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (_viewModel == null) return;

        if (e.Key == Key.Enter)
        {
            if (_viewModel.ExecuteCommandCommand.CanExecute(null))
            {
                _viewModel.ExecuteCommandCommand.Execute(null);
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Escape)
        {
            if (_viewModel.IsProcessingCommand && _viewModel.CancelCommandCommand.CanExecute(null))
            {
                _viewModel.CancelCommandCommand.Execute(null);
                e.Handled = true;
            }
            else
            {
                _viewModel.CurrentCommand = string.Empty;
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Up)
        {
            _viewModel.NavigateHistoryUp();
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            _viewModel.NavigateHistoryDown();
            e.Handled = true;
        }
        else if (e.Key == Key.L && (e.KeyModifiers & KeyModifiers.Control) != 0)
        {
            if (_viewModel.ClearTerminalCommand.CanExecute(null))
            {
                _viewModel.ClearTerminalCommand.Execute(null);
                e.Handled = true;
            }
        }
    }
}