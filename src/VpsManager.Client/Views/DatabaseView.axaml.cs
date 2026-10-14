using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using VpsManager.Client.ViewModels;

namespace VpsManager.Client.Views;

public partial class DatabaseView : UserControl
{
    public DatabaseView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (DataContext is DatabaseViewModel vm)
        {
            vm.SaveFileRequested = OnSaveFileRequested;
        }
    }

    private async Task<string?> OnSaveFileRequested(string defaultFileName)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return null;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Backup File Locally",
            SuggestedFileName = defaultFileName,
            DefaultExtension = Path.GetExtension(defaultFileName)
        });

        return file?.Path.LocalPath;
    }
}
