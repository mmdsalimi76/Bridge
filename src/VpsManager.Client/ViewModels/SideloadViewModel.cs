using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VpsManager.Core.Services;

namespace VpsManager.Client.ViewModels;

public partial class SideloadViewModel : ViewModelBase
{
    private readonly ISideloadService _sideloadService;

    [ObservableProperty]
    private string _localTarPath = string.Empty;

    [ObservableProperty]
    private double _transferProgress = 0;

    [ObservableProperty]
    private string _statusMessage = "Select a Docker .tar archive to sideload.";

    public SideloadViewModel(ISideloadService sideloadService)
    {
        _sideloadService = sideloadService;
    }

    [RelayCommand]
    private async Task StartSideloadAsync()
    {
        if (string.IsNullOrWhiteSpace(LocalTarPath))
        {
            StatusMessage = "Please specify a valid local tar path.";
            return;
        }

        try
        {
            StatusMessage = "Uploading image tar archive over SCP...";
            var progressHandler = new Progress<double>(p => TransferProgress = p);
            var result = await _sideloadService.SideloadImageAsync(LocalTarPath, "/tmp", progressHandler);
            
            StatusMessage = result.IsSuccess ? "Docker image loaded into VPS successfully!" : $"Error: {result.Error}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Sideload Error: {ex.Message}";
        }
    }
}
