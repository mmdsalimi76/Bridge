using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VpsManager.Core.Helpers;
using VpsManager.Core.Models;
using VpsManager.Core.Services;
using VpsManager.Core.Services.Session;

namespace VpsManager.Client.ViewModels;

public partial class DockerViewModel : ViewModelBase
{
    private readonly IActiveProfileContext _profileContext;
    private readonly ISessionCoordinator _session;
    private readonly IDockerService _dockerService;
    private readonly ConfigService _configService;
    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _logPollingCts;

    // -------------------------------------------------------------------------
    // Active Profile & Environment Telemetry
    // -------------------------------------------------------------------------

    [ObservableProperty]
    private string _profileName = "Default";

    [ObservableProperty]
    private string _composeFileName = "docker-compose.yml";

    [ObservableProperty]
    private string _remoteProjectPath = string.Empty;

    public string ResolvedComposeFilePath =>
        RemotePath.ResolveComposeFilePath(ComposeFileName, RemoteProjectPath);

    [ObservableProperty]
    private DockerScope _scope = DockerScope.ComposeStack;

    [ObservableProperty]
    private bool _isComposeScope = true;

    // -------------------------------------------------------------------------
    // Telemetry & Daemon Metrics
    // -------------------------------------------------------------------------

    [ObservableProperty]
    private string _daemonVersion = "Detecting...";

    [ObservableProperty]
    private string _operatingSystem = string.Empty;

    [ObservableProperty]
    private int _totalContainersCount;

    [ObservableProperty]
    private int _runningCount;

    [ObservableProperty]
    private int _pausedCount;

    [ObservableProperty]
    private int _stoppedCount;

    [ObservableProperty]
    private int _imagesCount;

    [ObservableProperty]
    private string _stackStatusSummary = "Ready";

    // -------------------------------------------------------------------------
    // Container Collections & Selection
    // -------------------------------------------------------------------------

    private readonly List<ContainerInfo> _allContainers = new();

    [ObservableProperty]
    private ObservableCollection<ContainerInfo> _filteredContainers = new();

    [ObservableProperty]
    private ContainerInfo? _selectedContainer;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private bool _hasContainers;

    // -------------------------------------------------------------------------
    // Log Viewer & Stream Controls
    // -------------------------------------------------------------------------

    [ObservableProperty]
    private string _containerLogs = string.Empty;

    [ObservableProperty]
    private int _logLineCount = 100;

    [ObservableProperty]
    private bool _includeTimestamps;

    [ObservableProperty]
    private bool _isAutoRefreshLogs;

    [ObservableProperty]
    private string _logSearchQuery = string.Empty;

    // -------------------------------------------------------------------------
    // Operational State & Status
    // -------------------------------------------------------------------------

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private bool _isBusy;

    public ObservableCollection<int> AvailableLineCounts { get; } = new() { 50, 100, 250, 500, 1000 };

    // -------------------------------------------------------------------------
    // Constructor
    // -------------------------------------------------------------------------

    public DockerViewModel(
        IActiveProfileContext profileContext,
        ISessionCoordinator session,
        IDockerService dockerService,
        ConfigService configService)
    {
        _profileContext = profileContext ?? throw new ArgumentNullException(nameof(profileContext));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _dockerService = dockerService ?? throw new ArgumentNullException(nameof(dockerService));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));

        _profileContext.ActiveProfileChanged += (_, profile) => ApplyProfile(profile);

        _session.StateChanged += (_, e) =>
        {
            UpdateConnectionState();
            if (e.State == SessionState.Connected)
            {
                _ = RefreshContainersAsync();
            }
            else if (e.State == SessionState.Disconnected || e.State == SessionState.Failed)
            {
                StopAutoLogPolling();
                StatusMessage = $"SSH {e.StatusText}. Connect in Overview to manage containers.";
            }
        };

        ApplyProfile(_profileContext.CurrentProfile);
        UpdateConnectionState();

        if (_session.IsConnected)
        {
            _ = RefreshContainersAsync();
        }
    }

    private void ApplyProfile(VpsProfile? profile)
    {
        if (profile != null)
        {
            ProfileName = profile.Name;
            ComposeFileName = string.IsNullOrWhiteSpace(profile.ComposeFileName) ? "docker-compose.yml" : profile.ComposeFileName;
            RemoteProjectPath = profile.RemoteProjectPath ?? string.Empty;
        }
        else
        {
            RemoteProjectPath = string.Empty;
        }
        OnPropertyChanged(nameof(ResolvedComposeFilePath));

        _allContainers.Clear();
        FilteredContainers.Clear();
        HasContainers = false;
        ContainerLogs = string.Empty;
        SelectedContainer = null;
        StatusMessage = $"Switched profile to {ProfileName}.";
    }

    // -------------------------------------------------------------------------
    // Partial Property Callbacks
    // -------------------------------------------------------------------------

    partial void OnComposeFileNameChanged(string value) => OnPropertyChanged(nameof(ResolvedComposeFilePath));
    partial void OnRemoteProjectPathChanged(string value) => OnPropertyChanged(nameof(ResolvedComposeFilePath));

    partial void OnSearchQueryChanged(string value) => ApplyFilter();

    partial void OnSelectedContainerChanged(ContainerInfo? value)
    {
        if (value != null)
        {
            _ = FetchLogsAsync();
        }
        else
        {
            ContainerLogs = string.Empty;
        }
    }

    partial void OnIsComposeScopeChanged(bool value)
    {
        Scope = value ? DockerScope.ComposeStack : DockerScope.AllHostContainers;
        _ = RefreshContainersAsync();
    }

    partial void OnLogLineCountChanged(int value)
    {
        if (SelectedContainer != null) _ = FetchLogsAsync();
    }

    partial void OnIncludeTimestampsChanged(bool value)
    {
        if (SelectedContainer != null) _ = FetchLogsAsync();
    }

    partial void OnIsAutoRefreshLogsChanged(bool value)
    {
        if (value)
            StartAutoLogPolling();
        else
            StopAutoLogPolling();
    }

    // -------------------------------------------------------------------------
    // Connection & Daemon Health State
    // -------------------------------------------------------------------------

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private bool _isDaemonAvailable;

    [ObservableProperty]
    private bool _isDaemonConnected;

    [ObservableProperty]
    private bool _isDaemonOffline;

    [ObservableProperty]
    private bool _isSshDisconnected = true;

    [ObservableProperty]
    private string _connectionStatusText = "Disconnected";

    [ObservableProperty]
    private string _daemonStatusHeadline = "SSH Disconnected";

    [ObservableProperty]
    private string _daemonHealthText = "Host Disconnected";

    // -------------------------------------------------------------------------
    // Commands: Container & Telemetry Refresh
    // -------------------------------------------------------------------------

    private void UpdateConnectionState()
    {
        IsConnected = _session.IsConnected;
        if (!IsConnected)
        {
            IsDaemonAvailable = false;
            IsDaemonConnected = false;
            IsDaemonOffline = false;
            IsSshDisconnected = true;
            ConnectionStatusText = "Disconnected";
            DaemonStatusHeadline = "SSH Disconnected";
            DaemonHealthText = "Host Disconnected";
            DaemonVersion = "Unavailable";
            OperatingSystem = "Host Disconnected";
        }
        else
        {
            IsSshDisconnected = false;
            if (IsDaemonAvailable)
            {
                IsDaemonConnected = true;
                IsDaemonOffline = false;
                ConnectionStatusText = "Connected";
                DaemonStatusHeadline = "Daemon Active";
                DaemonHealthText = "Active (100%)";
            }
            else
            {
                IsDaemonConnected = false;
                IsDaemonOffline = true;
                ConnectionStatusText = "Connected (No Daemon)";
                DaemonStatusHeadline = "Daemon Offline";
                DaemonHealthText = "Daemon Unreachable";
            }
        }
    }

    [RelayCommand]
    public async Task RefreshContainersAsync()
    {
        if (!_session.IsConnected)
        {
            UpdateConnectionState();
            StatusMessage = "SSH is disconnected. Connect in Overview to view containers.";
            return;
        }

        if (IsBusy) return;
        IsBusy = true;

        try
        {
            StatusMessage = "Querying Docker daemon telemetry...";

            var daemonTask = _dockerService.GetDaemonInfoAsync();
            Task<List<ContainerInfo>> containersTask = IsComposeScope
                ? _dockerService.GetContainersAsync(ResolvedComposeFilePath)
                : _dockerService.GetAllHostContainersAsync();

            await Task.WhenAll(daemonTask, containersTask);

            var daemon = await daemonTask;
            var list = await containersTask;

            // Update Daemon metrics
            if (daemon.IsAvailable)
            {
                IsDaemonAvailable = true;
                DaemonVersion = daemon.ServerVersion;
                OperatingSystem = daemon.OperatingSystem;
                ImagesCount = daemon.ImagesCount;
            }
            else
            {
                IsDaemonAvailable = false;
                DaemonVersion = "Offline / Unavailable";
                OperatingSystem = string.IsNullOrWhiteSpace(daemon.ErrorMessage) ? "Daemon unreachable" : daemon.ErrorMessage;
            }

            UpdateConnectionState();

            _allContainers.Clear();
            _allContainers.AddRange(list);

            TotalContainersCount = list.Count;
            RunningCount = list.Count(c => c.IsRunning);
            PausedCount = list.Count(c => c.IsPaused);
            StoppedCount = list.Count(c => c.IsExited);

            ApplyFilter();
            HasContainers = FilteredContainers.Count > 0;

            StackStatusSummary = $"{RunningCount} running, {StoppedCount} stopped";
            StatusMessage = $"Updated at {DateTime.Now:HH:mm:ss} — {TotalContainersCount} container(s) discovered.";

            // If there are running containers, fetch live CPU/Mem stats in the background without blocking initial rendering
            if (RunningCount > 0)
            {
                try
                {
                    var stats = await _dockerService.GetContainerStatsAsync();
                    if (stats.Count > 0)
                    {
                        foreach (var c in _allContainers)
                        {
                            if (!string.IsNullOrWhiteSpace(c.Id) && stats.TryGetValue(c.Id, out var sById))
                            {
                                c.CpuPercentage = sById.CpuPercentage;
                                c.MemoryUsage = sById.MemoryUsage;
                                c.MemoryPercentage = sById.MemoryPercentage;
                                c.NetIO = sById.NetIO;
                            }
                            else if (!string.IsNullOrWhiteSpace(c.Names) && stats.TryGetValue(c.Names, out var sByName))
                            {
                                c.CpuPercentage = sByName.CpuPercentage;
                                c.MemoryUsage = sByName.MemoryUsage;
                                c.MemoryPercentage = sByName.MemoryPercentage;
                                c.NetIO = sByName.NetIO;
                            }
                        }
                    }
                }
                catch
                {
                    // Stats are optional live telemetry; ignore transient failure
                }
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Refresh error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public void ToggleScope()
    {
        IsComposeScope = !IsComposeScope;
    }

    private void ApplyFilter()
    {
        string query = SearchQuery?.Trim() ?? string.Empty;
        FilteredContainers.Clear();

        foreach (var c in _allContainers)
        {
            bool matches = string.IsNullOrWhiteSpace(query) ||
                           c.Names.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                           c.Image.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                           c.Ports.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                           c.State.Contains(query, StringComparison.OrdinalIgnoreCase);

            if (matches)
            {
                FilteredContainers.Add(c);
            }
        }

        HasContainers = FilteredContainers.Count > 0;
    }

    // -------------------------------------------------------------------------
    // Commands: Stack Orchestration
    // -------------------------------------------------------------------------

    [RelayCommand]
    public async Task ExecuteStackActionAsync(string actionStr)
    {
        if (!Enum.TryParse<DockerComposeAction>(actionStr, true, out var action)) return;

        if (!_session.IsConnected)
        {
            StatusMessage = "SSH is disconnected. Connect in Overview first.";
            return;
        }

        if (IsBusy) return;
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        IsBusy = true;

        try
        {
            StatusMessage = $"Executing {action} on Compose stack ({ComposeFileName})...";
            var result = await _dockerService.ExecuteComposeActionAsync(ResolvedComposeFilePath, action, null, _cts.Token);

            if (result.IsSuccess)
            {
                StatusMessage = $"Stack {action} completed successfully.";
            }
            else
            {
                StatusMessage = $"Stack {action} failed: {result.Error}";
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Stack action cancelled.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error executing stack {action}: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }

        // Auto-refresh containers window after stack actions (delayed so Docker updates process table)
        await Task.Delay(500);
        await RefreshContainersAsync();

        if (SelectedContainer != null)
        {
            _ = FetchLogsAsync();
        }
    }

    // -------------------------------------------------------------------------
    // Commands: Individual Container Lifecycle (Start, Stop, Restart, Pause)
    // -------------------------------------------------------------------------

    [RelayCommand]
    public Task StartContainerAsync(ContainerInfo? container) =>
        ExecuteContainerActionAsync(container ?? SelectedContainer, "start");

    [RelayCommand]
    public Task StopContainerAsync(ContainerInfo? container) =>
        ExecuteContainerActionAsync(container ?? SelectedContainer, "stop");

    [RelayCommand]
    public Task RestartContainerAsync(ContainerInfo? container) =>
        ExecuteContainerActionAsync(container ?? SelectedContainer, "restart");

    [RelayCommand]
    public Task PauseContainerAsync(ContainerInfo? container) =>
        ExecuteContainerActionAsync(container ?? SelectedContainer, "pause");

    [RelayCommand]
    public Task UnpauseContainerAsync(ContainerInfo? container) =>
        ExecuteContainerActionAsync(container ?? SelectedContainer, "unpause");

    [RelayCommand]
    public void SelectContainer(ContainerInfo container)
    {
        SelectedContainer = container;
    }

    private async Task ExecuteContainerActionAsync(ContainerInfo? container, string action)
    {
        if (container == null) return;
        if (!_session.IsConnected)
        {
            StatusMessage = "SSH is disconnected. Connect in Overview first.";
            return;
        }

        if (IsBusy) return;
        IsBusy = true;

        try
        {
            StatusMessage = $"Executing {action} on {container.Names}...";

            CommandResult result;
            if (IsComposeScope && Enum.TryParse<DockerComposeAction>(action, true, out var composeAction))
            {
                result = await _dockerService.ExecuteComposeActionAsync(ResolvedComposeFilePath, composeAction, container.Names);
            }
            else
            {
                string targetId = string.IsNullOrWhiteSpace(container.Id) ? container.Names : container.Id;
                result = await _dockerService.ExecuteContainerActionAsync(targetId, action);
            }

            if (result.IsSuccess)
            {
                StatusMessage = $"{container.Names} {action} completed successfully.";
            }
            else
            {
                StatusMessage = $"{container.Names} {action} failed: {result.Error}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }

        // Auto-refresh containers window and telemetry after any action:
        // Brief pause allows Docker daemon to transition process table state
        await Task.Delay(400);
        await RefreshContainersAsync();

        // If the acted container is currently selected, refresh its logs as well
        if (SelectedContainer != null && (SelectedContainer.Names == container.Names || SelectedContainer.Id == container.Id))
        {
            _ = FetchLogsAsync();
        }
    }

    // -------------------------------------------------------------------------
    // Commands: Log Console & Streaming
    // -------------------------------------------------------------------------

    [RelayCommand]
    public async Task FetchLogsAsync()
    {
        if (SelectedContainer == null) return;
        if (!_session.IsConnected)
        {
            StatusMessage = "SSH is disconnected.";
            return;
        }

        try
        {
            StatusMessage = $"Fetching logs for {SelectedContainer.Names} ({LogLineCount} lines)...";
            ContainerLogs = await _dockerService.GetContainerLogsAsync(
                SelectedContainer.Names,
                LogLineCount,
                IncludeTimestamps);

            StatusMessage = $"Logs updated for {SelectedContainer.Names} at {DateTime.Now:HH:mm:ss}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading logs: {ex.Message}";
        }
    }

    [RelayCommand]
    public void ClearLogs()
    {
        ContainerLogs = string.Empty;
    }

    [RelayCommand]
    public void CancelOperation()
    {
        _cts?.Cancel();
        StatusMessage = "Operation cancelled.";
    }

    private void StartAutoLogPolling()
    {
        StopAutoLogPolling();
        _logPollingCts = new CancellationTokenSource();
        _ = PollLogsLoopAsync(_logPollingCts.Token);
    }

    private void StopAutoLogPolling()
    {
        _logPollingCts?.Cancel();
        _logPollingCts = null;
    }

    private async Task PollLogsLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && IsAutoRefreshLogs)
        {
            try
            {
                await Task.Delay(3000, token);
                if (_session.IsConnected && SelectedContainer != null && IsAutoRefreshLogs)
                {
                    ContainerLogs = await _dockerService.GetContainerLogsAsync(
                        SelectedContainer.Names,
                        LogLineCount,
                        IncludeTimestamps,
                        token);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Soft ignore polling glitches
            }
        }
    }
}
