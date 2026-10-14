using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VpsManager.Core.Models;
using VpsManager.Core.Services;
using VpsManager.Core.Services.Overview;

namespace VpsManager.Client.ViewModels.Dashboard;

/// <summary>
/// Presents the project workload inventory: filtering, selection, and log inspection.
/// </summary>
public partial class WorkloadsViewModel : ViewModelBase
{
    private readonly IRuntimeInventoryService _inventoryService;

    private IReadOnlyList<RuntimeInfo> _allWorkloads = Array.Empty<RuntimeInfo>();

    [ObservableProperty]
    private ObservableCollection<RuntimeInfo> _filtered = new();

    [ObservableProperty]
    private RuntimeInfo? _selected;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _sourceFilter = WorkloadFilter.AllSources;

    [ObservableProperty]
    private ObservableCollection<string> _sources = new() { WorkloadFilter.AllSources };

    [ObservableProperty]
    private string _logs = "Select a workload to inspect its recent logs.";

    [ObservableProperty]
    private bool _hasLogSource;

    [ObservableProperty]
    private bool _isLoadingLogs;

    [ObservableProperty]
    private int _logLineCount = 100;

    [ObservableProperty]
    private string _logsFetchedAt = "Not fetched";

    [ObservableProperty]
    private string _emptyMessage = "Connect to a server to load workloads.";

    [ObservableProperty]
    private bool _hasWorkloads;

    [ObservableProperty]
    private bool _isTruncated;

    public WorkloadsViewModel(IRuntimeInventoryService inventoryService)
    {
        _inventoryService = inventoryService ?? throw new ArgumentNullException(nameof(inventoryService));
    }

    /// <summary>Raised when the user asks to copy the current log text; the view supplies the clipboard.</summary>
    public event Func<string, Task>? CopyLogsRequested;

    /// <summary>Raised when log copying fails or succeeds, so the shell can report it.</summary>
    public event Action<string>? StatusReported;

    public ObservableCollection<int> LogLineCountOptions { get; } = new() { 50, 100, 250, 500 };

    public int WorkloadCount => _allWorkloads.Count;

    public int FilteredCount => Filtered.Count;

    public bool HasFilteredWorkloads => Filtered.Count > 0;

    /// <summary>Replaces the inventory contents, preserving filters and clearing selection.</summary>
    public void Apply(IReadOnlyList<RuntimeInfo> workloads)
    {
        _allWorkloads = workloads ?? Array.Empty<RuntimeInfo>();
        EmptyMessage = "No workloads were detected in the selected project or matching host services.";
        IsTruncated = _allWorkloads.Count > Filtered.Count;
        ApplyFilter();
        ClearLogsToPlaceholder();
    }

    public void Clear()
    {
        _allWorkloads = Array.Empty<RuntimeInfo>();
        Filtered.Clear();
        Selected = null;
        Sources = new ObservableCollection<string> { WorkloadFilter.AllSources };
        SourceFilter = WorkloadFilter.AllSources;
        EmptyMessage = "Connect to a server to load workloads.";
        HasWorkloads = false;
        ClearLogsToPlaceholder();
        Logs = "Connect to a server to inspect workload logs.";
        RaiseCounts();
    }

    [RelayCommand]
    private async Task RefreshLogsAsync()
    {
        if (Selected == null)
        {
            Logs = "Select a workload first.";
            return;
        }

        if (string.IsNullOrWhiteSpace(Selected.LogsHint))
        {
            HasLogSource = false;
            Logs = "No automatic log source is configured for this workload. Run it as a systemd service or Docker container to see logs here.";
            return;
        }

        HasLogSource = true;
        IsLoadingLogs = true;
        Logs = $"Loading recent logs for {Selected.Name}…";
        try
        {
            Logs = await _inventoryService.GetLogsAsync(Selected, LogLineCount);
            LogsFetchedAt = $"Fetched at {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logs = $"Unable to load logs: {ex.Message}";
        }
        finally
        {
            IsLoadingLogs = false;
        }
    }

    [RelayCommand]
    private void ClearLogs()
    {
        Logs = string.Empty;
        LogsFetchedAt = "Cleared";
    }

    [RelayCommand]
    private async Task CopyLogsAsync()
    {
        if (string.IsNullOrWhiteSpace(Logs))
        {
            StatusReported?.Invoke("There are no logs to copy.");
            return;
        }

        if (CopyLogsRequested == null)
        {
            StatusReported?.Invoke("Clipboard support is unavailable.");
            return;
        }

        try
        {
            await CopyLogsRequested.Invoke(Logs);
            StatusReported?.Invoke("Recent logs copied to the clipboard.");
        }
        catch (Exception ex)
        {
            StatusReported?.Invoke($"Unable to copy logs: {ex.Message}");
        }
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnSourceFilterChanged(string value) => ApplyFilter();

    partial void OnSelectedChanged(RuntimeInfo? value)
    {
        ClearLogsToPlaceholder();
        if (value != null)
        {
            _ = RefreshLogsAsync();
        }
    }

    partial void OnLogLineCountChanged(int value)
    {
        if (Selected != null && HasLogSource)
        {
            _ = RefreshLogsAsync();
        }
    }

    private void ApplyFilter()
    {
        var filtered = WorkloadFilter.Apply(_allWorkloads, SearchText, SourceFilter);

        Filtered.Clear();
        foreach (var workload in filtered)
        {
            Filtered.Add(workload);
        }

        var sources = WorkloadFilter.AvailableSources(_allWorkloads);
        var currentSources = Sources.ToArray();
        if (!currentSources.SequenceEqual(sources, StringComparer.OrdinalIgnoreCase))
        {
            Sources = new ObservableCollection<string>(sources);
        }

        HasWorkloads = _allWorkloads.Count > 0;
        IsTruncated = filtered.Count < _allWorkloads.Count;
        RaiseCounts();
    }

    private void RaiseCounts()
    {
        OnPropertyChanged(nameof(WorkloadCount));
        OnPropertyChanged(nameof(FilteredCount));
        OnPropertyChanged(nameof(HasFilteredWorkloads));
    }

    private void ClearLogsToPlaceholder()
    {
        HasLogSource = false;
        LogsFetchedAt = "Not fetched";
        Logs = _allWorkloads.Count > 0
            ? "Select a workload to inspect its recent logs."
            : "No workload selected.";
    }
}
