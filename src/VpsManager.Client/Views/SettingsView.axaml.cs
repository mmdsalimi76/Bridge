using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using VpsManager.Client.ViewModels;

namespace VpsManager.Client.Views;

public partial class SettingsView : UserControl
{
    private SettingsViewModel? _subscribedVm;

    public SettingsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_subscribedVm != null)
        {
            _subscribedVm.BrowseFolderRequested -= OnBrowseFolderRequested;
            _subscribedVm.BrowseFileRequested -= OnBrowseFileRequested;
            _subscribedVm = null;
        }

        if (DataContext is SettingsViewModel vm)
        {
            _subscribedVm = vm;
            vm.BrowseFolderRequested += OnBrowseFolderRequested;
            vm.BrowseFileRequested += OnBrowseFileRequested;
        }
    }

    private async Task<string?> OnBrowseFolderRequested(string title)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return null;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });

        return folders.FirstOrDefault()?.Path.LocalPath;
    }

    private async Task<string?> OnBrowseFileRequested(string title, string[]? patterns)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return null;

        var fileFilters = new List<FilePickerFileType>();
        if (patterns != null && patterns.Length > 0)
        {
            fileFilters.Add(new FilePickerFileType(title) { Patterns = patterns });
        }
        fileFilters.Add(new FilePickerFileType("All Files") { Patterns = new[] { "*" } });

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = fileFilters
        });

        return files.FirstOrDefault()?.Path.LocalPath;
    }
}
