using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VpsManager.Client.ViewModels.Dashboard;
using VpsManager.Core.Helpers;
using VpsManager.Core.Models;
using VpsManager.Core.Services;
using VpsManager.Core.Services.Adapters;
using VpsManager.Core.Services.Overview;
using VpsManager.Core.Services.Session;

namespace VpsManager.Client.ViewModels;

/// <summary>
/// The Overview tab. Thin orchestration: it owns profile selection and the refresh cycle,
/// and delegates presentation to focused sub-view-models.
/// </summary>
public partial class DashboardViewModel : ViewModelBase
{
    private readonly IActiveProfileContext _profileContext;
    private readonly ConfigService _configService;
    private readonly ISessionCoordinator _session;
    private readonly IOverviewProbe _probe;
    private readonly IHealthCheckService _healthCheckService;
    private readonly IDatabaseService? _databaseService;
    private readonly AttentionThresholds _thresholds;

    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private CancellationTokenSource? _liveRefreshCancellation;
    private CancellationTokenSource? _refreshCancellation;
    private bool _isReloadingProfiles;

    [ObservableProperty]
    private ObservableCollection<VpsProfile> _vpsProfiles = new();

    [ObservableProperty]
    private VpsProfile? _selectedVps;

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private bool _isLiveRefreshEnabled;

    [ObservableProperty]
    private string _lastUpdated = "Not refreshed yet";

    [ObservableProperty]
    private bool _redactHostInReports = true;

    [ObservableProperty]
    private string _localRootPath = "Not set";

    [ObservableProperty]
    private string _remoteProjectPath = "Not set";

    [ObservableProperty]
    private bool _canExportDiagnostics;

    /// <summary>Latest successfully collected snapshot; used for export and diagnostics.</summary>
    public OverviewSnapshot? LatestSnapshot { get; private set; }

    public ConnectionPanelViewModel Connection { get; }

    public MetricsViewModel Metrics { get; }

    public WorkloadsViewModel Workloads { get; }

    public AttentionViewModel Attention { get; }

    public ActivityFeedViewModel Activity { get; }

    /// <summary>Raised when the user selects a different server, so the shell can propagate it.</summary>
    public event Action<VpsProfile>? ActiveVpsChanged;

    /// <summary>Hook for writing text to the clipboard, supplied by the view.</summary>
    public Func<string, Task>? ClipboardRequested;

    /// <summary>Hook for saving a generated report; receives (suggested name, content).</summary>
    public Func<string, string, Task<string?>>? SaveReportRequested;

    public DashboardViewModel(
        IActiveProfileContext profileContext,
        ConfigService configService,
        ISessionCoordinator session,
        IOverviewProbe probe,
        IHealthCheckService healthCheckService,
        IRuntimeInventoryService runtimeInventoryService,
        MetricHistoryStore history,
        AttentionThresholds thresholds,
        IDatabaseService? databaseService = null)
    {
        _profileContext = profileContext ?? throw new ArgumentNullException(nameof(profileContext));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _healthCheckService = healthCheckService ?? throw new ArgumentNullException(nameof(healthCheckService));
        _databaseService = databaseService;
        _thresholds = thresholds ?? throw new ArgumentNullException(nameof(thresholds));

        Connection = new ConnectionPanelViewModel(_session)
        {
            ActiveProfileProvider = () => SelectedVps
        };
        Connection.Connected += OnConnectedAsync;
        Connection.Disconnected += OnDisconnected;

        Metrics = new MetricsViewModel(history, _thresholds);
        Workloads = new WorkloadsViewModel(runtimeInventoryService);
        Workloads.StatusReported += message => StatusMessage = message;
        Attention = new AttentionViewModel(_thresholds);
        Activity = new ActivityFeedViewModel();

        _profileContext.ProfilesChanged += (_, _) => SyncProfilesFromContext();
        _profileContext.ActiveProfileChanged += (_, _) => SyncProfilesFromContext();
    }

    partial void OnSelectedVpsChanged(VpsProfile? value)
    {
        Connection.UpdateProfileSummary(value);
        LocalRootPath = string.IsNullOrWhiteSpace(value?.LocalRootPath) ? "Not set" : value.LocalRootPath;
        RemoteProjectPath = string.IsNullOrWhiteSpace(value?.RemoteProjectPath) ? "Not set" : value.RemoteProjectPath;

        if (value != null && !_isReloadingProfiles)
        {
            CancelInFlightRefresh();
            ActiveVpsChanged?.Invoke(value);
            if (_session.State == SessionState.Connected || _session.IsConnected)
            {
                _ = RefreshDashboardAsync();
            }
        }
    }

    /// <summary>
    /// Synchronizes the ViewModel's local profile collection and selection from the central context.
    /// Does not trigger a reload in the context itself.
    /// </summary>
    public void SyncProfilesFromContext()
    {
        _isReloadingProfiles = true;
        try
        {
            VpsProfiles.Clear();
            foreach (var profile in _profileContext.Profiles)
            {
                VpsProfiles.Add(profile);
            }

            var active = _profileContext.CurrentProfile;
            SelectedVps = active != null
                ? VpsProfiles.FirstOrDefault(profile => profile.Id == active.Id) ?? VpsProfiles.FirstOrDefault()
                : VpsProfiles.FirstOrDefault();
        }
        finally
        {
            _isReloadingProfiles = false;
        }

        Connection.UpdateProfileSummary(SelectedVps);
    }

    /// <summary>Reloads the profile list from configuration; called when settings change.</summary>
    public async Task ReloadProfilesAsync()
    {
        await _profileContext.ReloadProfilesAsync();
        SyncProfilesFromContext();
    }

    [RelayCommand]
    public async Task RefreshDashboardAsync()
    {
        var cancellation = BeginRefreshScope();
        await RefreshCoreAsync(includeActivity: true, cancellation.Token);
    }

    public void StartLiveRefresh()
    {
        StopLiveRefresh();
        _liveRefreshCancellation = new CancellationTokenSource();
        _ = LiveRefreshLoopAsync(_liveRefreshCancellation.Token);
    }

    public void StopLiveRefresh()
    {
        var cancellation = Interlocked.Exchange(ref _liveRefreshCancellation, null);
        cancellation?.Cancel();
    }

    private async Task LiveRefreshLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await RefreshCoreAsync(includeActivity: false, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task OnConnectedAsync()
    {
        await RefreshDashboardAsync();
    }

    private void OnDisconnected()
    {
        CancelInFlightRefresh();
        Metrics.MarkStale();
        Workloads.Clear();
        Attention.Clear();
        StatusMessage = "Disconnected from the selected server.";
        Connection.UpdateProfileSummary(SelectedVps);
    }

    private async Task RefreshCoreAsync(bool includeActivity, CancellationToken cancellationToken)
    {
        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            IsRefreshing = true;

            var profile = SelectedVps;
            if (profile == null)
            {
                Metrics.Clear();
                Workloads.Clear();
                Attention.Clear();
                StatusMessage = "No server profile selected. Add one in Settings.";
                return;
            }

            Connection.UpdateProfileSummary(profile);
            LocalRootPath = string.IsNullOrWhiteSpace(profile.LocalRootPath) ? "Not set" : profile.LocalRootPath;
            RemoteProjectPath = string.IsNullOrWhiteSpace(profile.RemoteProjectPath) ? "Not set" : profile.RemoteProjectPath;

            if (!_session.IsConnected)
            {
                if (_session.HasStoredCredential(profile))
                {
                    StatusMessage = "Connecting to server…";
                    var connected = await _session.ConnectAsync(profile, cancellationToken: cancellationToken);
                    if (!connected)
                    {
                        Metrics.MarkStale();
                        Workloads.Clear();
                        Attention.Clear();
                        StatusMessage = _session.Detail ?? "Failed to connect to this server.";
                        return;
                    }
                }
                else
                {
                    // Keep the last readings visible but clearly stale instead of blanking the tab.
                    Metrics.MarkStale();
                    Workloads.Clear();
                    Attention.Clear();
                    StatusMessage = "Connect to this server to fetch live telemetry.";
                    return;
                }
            }

            StatusMessage = "Collecting server telemetry…";

            var healthConfig = BuildHealthConfig(profile);
            var request = new OverviewProbeRequest
            {
                ComposeFileName = profile.ComposeFileName,
                RemoteProjectPath = profile.RemoteProjectPath,
                TargetOS = profile.TargetOS,
                HealthProbeCommand = _healthCheckService.BuildProbeCommand(healthConfig)
            };

            var snapshot = await _probe.ProbeAsync(request, cancellationToken);
            LatestSnapshot = snapshot;
            CanExportDiagnostics = true;

            Metrics.Apply(snapshot);
            Workloads.Apply(snapshot.Workloads);

            var health = _healthCheckService.InterpretProbe(
                healthConfig,
                snapshot.HealthOutput,
                snapshot.HealthExitCode,
                ToContainers(snapshot));
            Attention.Apply(snapshot, health);

            if (includeActivity)
            {
                await RefreshBackupSummaryAsync(cancellationToken);
            }

            LastUpdated = snapshot.CapturedAt.ToString("HH:mm:ss");
            StatusMessage = snapshot.SectionErrors.Count == 0
                ? $"Updated {LastUpdated} · {snapshot.Workloads.Count} workloads · 1 remote command"
                : $"Updated {LastUpdated} with {snapshot.SectionErrors.Count} collection note(s)";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusMessage = "Refresh superseded.";
        }
        catch (Exception ex)
        {
            // Never leave the tab looking healthy after a failed collection.
            Metrics.MarkStale();
            Attention.ApplyFailure("Overview refresh failed", ex.Message);
            StatusMessage = $"Refresh failed: {ex.Message}";
        }
        finally
        {
            IsRefreshing = false;
            _refreshGate.Release();
        }
    }

    private async Task RefreshBackupSummaryAsync(CancellationToken cancellationToken)
    {
        if (_databaseService == null)
        {
            Attention.ApplyBackupError("Backup activity is unavailable.");
            return;
        }

        try
        {
            var backups = await _databaseService.GetBackupsAsync(cancellationToken: cancellationToken);
            var latest = backups.OrderByDescending(backup => backup.CreatedAt).FirstOrDefault();

            var runs = await _databaseService.GetCronHistoryAsync(cancellationToken);
            var latestRun = runs.OrderByDescending(run => run.Timestamp).FirstOrDefault();
            var scheduledSummary = latestRun == null
                ? "No scheduled runs recorded."
                : $"{latestRun.StatusText} · {latestRun.Scope} · {latestRun.Timestamp.ToLocalTime():g}";

            Attention.ApplyBackup(latest, scheduledSummary, latest != null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Attention.ApplyBackupError($"Unable to load remote backup activity: {ex.Message}");
        }
    }

    private UniversalPipelineConfig BuildHealthConfig(VpsProfile profile) => new()
    {
        ProfileId = profile.Id,
        ProfileName = profile.Name,
        TargetOS = profile.TargetOS,
        ProjectType = profile.ProjectType,
        RemoteProjectPath = profile.RemoteProjectPath,
        ComposeFilePath = RemotePath.ResolveComposeFilePath(profile.ComposeFileName, profile.RemoteProjectPath),
        ServiceName = profile.ServiceName,
        HealthCheckType = profile.HealthCheckType,
        HealthCheckUrl = profile.HealthCheckUrl,
        HealthCheckPort = profile.HealthCheckPort,
        HealthCheckProcessName = profile.HealthCheckProcessName,
        HealthCheckCustomCommand = profile.HealthCheckCustomCommand,
        SkipHealthCheck = false
    };

    private static IReadOnlyList<ContainerInfo> ToContainers(OverviewSnapshot snapshot) =>
        snapshot.DockerWorkloads
            .Select(workload => new ContainerInfo
            {
                Names = workload.Name,
                State = workload.Status,
                Image = workload.Image,
                Status = workload.Details,
                Ports = workload.Ports
            })
            .ToList();

    [RelayCommand]
    private async Task CopyDiagnosticsAsync()
    {
        if (LatestSnapshot == null)
        {
            StatusMessage = "Refresh the Overview before exporting diagnostics.";
            return;
        }

        if (ClipboardRequested == null)
        {
            StatusMessage = "Clipboard support is unavailable.";
            return;
        }

        var report = DiagnosticsReportBuilder.Build(
            LatestSnapshot,
            SelectedVps,
            BuildHealthResult(),
            _session.StatusText,
            RedactHostInReports);

        try
        {
            await ClipboardRequested.Invoke(report);
            StatusMessage = RedactHostInReports
                ? "Diagnostics report copied (host redacted)."
                : "Diagnostics report copied (host included).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Unable to copy diagnostics: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ExportDiagnosticsAsync()
    {
        if (LatestSnapshot == null)
        {
            StatusMessage = "Refresh the Overview before exporting diagnostics.";
            return;
        }

        if (SaveReportRequested == null)
        {
            StatusMessage = "Saving reports is unavailable on this platform.";
            return;
        }

        var report = DiagnosticsReportBuilder.Build(
            LatestSnapshot,
            SelectedVps,
            BuildHealthResult(),
            _session.StatusText,
            RedactHostInReports);

        var suggestedName = $"bridge-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.md";
        try
        {
            var path = await SaveReportRequested.Invoke(suggestedName, report);
            StatusMessage = string.IsNullOrWhiteSpace(path)
                ? "Diagnostics export cancelled."
                : $"Diagnostics report saved to {path}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Unable to save diagnostics: {ex.Message}";
        }
    }

    /// <summary>Reconstructs the health verdict from the last snapshot for reporting.</summary>
    private HealthCheckResult? BuildHealthResult()
    {
        if (LatestSnapshot == null || SelectedVps == null)
        {
            return null;
        }

        return _healthCheckService.InterpretProbe(
            BuildHealthConfig(SelectedVps),
            LatestSnapshot.HealthOutput,
            LatestSnapshot.HealthExitCode,
            ToContainers(LatestSnapshot));
    }

    public void RecordDeploymentActivity(string summary, DateTime completedAt) =>
        Activity.RecordDeployment(summary, completedAt);

    public void RecordBackupActivity(string summary, DateTime completedAt) =>
        Activity.RecordBackup(summary, completedAt);

    private CancellationTokenSource BeginRefreshScope()
    {
        var previous = Interlocked.Exchange(ref _refreshCancellation, new CancellationTokenSource());
        // Cancelled, not disposed: the running refresh may still be observing the token.
        previous?.Cancel();
        return _refreshCancellation!;
    }

    private void CancelInFlightRefresh()
    {
        Interlocked.Exchange(ref _refreshCancellation, null)?.Cancel();
    }
}
