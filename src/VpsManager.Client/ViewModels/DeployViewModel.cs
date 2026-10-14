using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using VpsManager.Client.Messages;
using VpsManager.Core.Helpers;
using VpsManager.Core.Models;
using VpsManager.Core.Services;
using VpsManager.Core.Services.Locking;
using VpsManager.Core.Services.Session;

namespace VpsManager.Client.ViewModels;

public partial class DeployViewModel : ViewModelBase
{
    private readonly IActiveProfileContext _profileContext;
    private readonly ISessionCoordinator _session;
    private readonly ConfigService _configService;
    private readonly IDeploymentPipelineService _pipelineService;
    private readonly ISyncIgnoreService _syncIgnoreService;
    private readonly IOperationLock _operationLock;
    private CancellationTokenSource? _cancellationTokenSource;

    [ObservableProperty]
    private string _statusMessage = "Ready to deploy to VPS";

    [ObservableProperty]
    private bool _isPipelineRunning;

    [ObservableProperty]
    private bool _canCancelPipeline;

    [ObservableProperty]
    private ObservableCollection<PipelineStep> _pipelineSteps = new();

    [ObservableProperty]
    private string _terminalOutput = string.Empty;

    [ObservableProperty]
    private PipelineStatus _overallStatus = PipelineStatus.NotStarted;

    [ObservableProperty]
    private TimeSpan _totalDuration = TimeSpan.Zero;

    [ObservableProperty]
    private bool _skipDatabaseBackup;

    [ObservableProperty]
    private bool _skipHealthCheck;

    // Active Profile Properties (Read directly from VPS Profile configured in Settings)
    [ObservableProperty]
    private string _profileName = "Default VPS";

    [ObservableProperty]
    private bool _isDockerized = true;

    [ObservableProperty]
    private string _localRootPath = string.Empty;

    [ObservableProperty]
    private string _remoteProjectPath = "/var/www/my_app";

    [ObservableProperty]
    private string _composeFileName = "docker-compose.yml";

    [ObservableProperty]
    private string _launchCommand = string.Empty;

    [ObservableProperty]
    private DatabaseEngine _selectedDbEngine = DatabaseEngine.PostgreSQL;

    [ObservableProperty]
    private HealthCheckType _selectedHealthCheckType = HealthCheckType.DockerContainer;

    [ObservableProperty]
    private string _healthCheckSummary = "Docker State Probe";

    // Sync Ignore Engine State
    [ObservableProperty]
    private bool _hasSyncIgnoreFile;

    [ObservableProperty]
    private string _syncIgnoreStatusText = "Checking .syncignore...";

    [ObservableProperty]
    private int _syncIgnoreRuleCount;

    [ObservableProperty]
    private bool _hasMediaWarning;

    [ObservableProperty]
    private string _mediaWarningText = string.Empty;

    [ObservableProperty]
    private ObservableCollection<string> _detectedMediaFolders = new();

    // Sync Ignore GUI Selector Modal Properties
    [ObservableProperty]
    private bool _isSyncIgnoreSelectorOpen;

    [ObservableProperty]
    private bool _isScanningSyncIgnoreTree;

    [ObservableProperty]
    private ObservableCollection<FilePatchNode> _syncIgnoreTreeNodes = new();

    [ObservableProperty]
    private string _syncIgnoreFilterQuery = string.Empty;

    [ObservableProperty]
    private string _rawSyncIgnoreEditorText = string.Empty;

    [ObservableProperty]
    private bool _isRawEditorMode;

    [ObservableProperty]
    private int _ignoredFilesCount;

    [ObservableProperty]
    private int _ignoredDirsCount;

    public event Action<string, DateTime>? ActivityRecorded;

    public DeployViewModel(
        IActiveProfileContext profileContext,
        ISessionCoordinator session,
        ConfigService configService,
        IDeploymentPipelineService pipelineService,
        ISyncIgnoreService syncIgnoreService,
        IOperationLock operationLock)
    {
        _profileContext = profileContext ?? throw new ArgumentNullException(nameof(profileContext));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _pipelineService = pipelineService ?? throw new ArgumentNullException(nameof(pipelineService));
        _syncIgnoreService = syncIgnoreService ?? throw new ArgumentNullException(nameof(syncIgnoreService));
        _operationLock = operationLock ?? throw new ArgumentNullException(nameof(operationLock));

        _profileContext.ActiveProfileChanged += (_, profile) => ApplyProfile(profile);
        _session.StateChanged += (_, e) =>
        {
            if (e.State == SessionState.Disconnected)
            {
                StatusMessage = "SSH is disconnected. Connect in Overview before deploying.";
            }
        };

        ApplyProfile(_profileContext.CurrentProfile);
    }

    private void ApplyProfile(VpsProfile? profile)
    {
        if (profile != null)
        {
            ProfileName = profile.Name;
            IsDockerized = profile.IsDockerized;
            LocalRootPath = profile.LocalRootPath;
            RemoteProjectPath = profile.RemoteProjectPath;
            ComposeFileName = string.IsNullOrWhiteSpace(profile.ComposeFileName) ? "docker-compose.yml" : profile.ComposeFileName;
            LaunchCommand = profile.LaunchCommand;
            SelectedDbEngine = profile.DbEngine;
            SelectedHealthCheckType = profile.HealthCheckType;
            HealthCheckSummary = IsDockerized
                ? "Docker Container Health State"
                : (string.IsNullOrWhiteSpace(profile.HealthCheckUrl) ? $"{profile.HealthCheckType}" : profile.HealthCheckUrl);
        }

        RefreshPipelineSteps();
        AnalyzeSyncIgnore();
    }

    public void AnalyzeSyncIgnore()
    {
        if (string.IsNullOrWhiteSpace(LocalRootPath) || !Directory.Exists(LocalRootPath))
        {
            HasSyncIgnoreFile = false;
            SyncIgnoreStatusText = "No local project directory selected (set in Settings)";
            SyncIgnoreRuleCount = 0;
            HasMediaWarning = false;
            MediaWarningText = string.Empty;
            DetectedMediaFolders.Clear();
            return;
        }

        var analysis = _syncIgnoreService.AnalyzeProjectWorkspace(LocalRootPath);
        HasSyncIgnoreFile = analysis.HasSyncIgnoreFile;
        SyncIgnoreRuleCount = analysis.ActivePatterns.Count;

        if (analysis.HasSyncIgnoreFile)
        {
            SyncIgnoreStatusText = $".syncignore active ({SyncIgnoreRuleCount} patterns protecting server data)";
        }
        else
        {
            SyncIgnoreStatusText = "No .syncignore found. Click below to generate standard rules.";
        }

        DetectedMediaFolders.Clear();
        foreach (var m in analysis.DetectedMediaWarnings)
        {
            DetectedMediaFolders.Add(m);
        }

        if (analysis.DetectedMediaWarnings.Count > 0)
        {
            HasMediaWarning = true;
            MediaWarningText = $"Notice: Folders [{string.Join(", ", analysis.DetectedMediaWarnings)}] found in project but not listed in .syncignore. Files inside might be overwritten on server.";
        }
        else
        {
            HasMediaWarning = false;
            MediaWarningText = string.Empty;
        }
    }

    public void RefreshPipelineSteps()
    {
        var dummyConfig = new UniversalPipelineConfig
        {
            IsDockerized = IsDockerized,
            ProjectType = IsDockerized ? ProjectType.DockerCompose : ProjectType.NativeLinuxService,
            LaunchCommand = LaunchCommand,
            ComposeFilePath = ComposeFileName,
            DbEngine = SelectedDbEngine,
            HealthCheckType = SelectedHealthCheckType,
            SkipDatabaseBackup = SkipDatabaseBackup,
            SkipHealthCheck = SkipHealthCheck
        };

        var steps = _pipelineService.GetDynamicPipelineSteps(dummyConfig);
        PipelineSteps.Clear();
        foreach (var s in steps)
        {
            PipelineSteps.Add(s);
        }
    }

    partial void OnSkipDatabaseBackupChanged(bool value) => RefreshPipelineSteps();
    partial void OnSkipHealthCheckChanged(bool value) => RefreshPipelineSteps();

    partial void OnLaunchCommandChanged(string value)
    {
        if (_profileContext.CurrentProfile != null && _profileContext.CurrentProfile.LaunchCommand != value)
        {
            _profileContext.CurrentProfile.LaunchCommand = value;
            _ = _profileContext.SaveProfileAsync(_profileContext.CurrentProfile);
        }
        RefreshPipelineSteps();
    }

    [RelayCommand]
    private void SetLaunchCommandPreset(string preset)
    {
        if (!string.IsNullOrWhiteSpace(preset))
        {
            LaunchCommand = preset;
        }
    }

    [RelayCommand]
    private async Task CreateStandardSyncIgnoreAsync()
    {
        if (string.IsNullOrWhiteSpace(LocalRootPath) || !Directory.Exists(LocalRootPath))
        {
            StatusMessage = "Cannot create .syncignore: local project path is not configured.";
            return;
        }

        try
        {
            await _syncIgnoreService.CreateStandardSyncIgnoreAsync(LocalRootPath);
            AnalyzeSyncIgnore();
            StatusMessage = "Created standard .syncignore with persistent folders & security exclusions.";
            AppendTerminalOutput("📄 Generated standard .syncignore protecting media/, uploads/, storage/, data/ and secrets.");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to create .syncignore: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ProtectDetectedMediaFoldersAsync()
    {
        if (string.IsNullOrWhiteSpace(LocalRootPath) || DetectedMediaFolders.Count == 0)
            return;

        try
        {
            await _syncIgnoreService.AppendPatternsAsync(LocalRootPath, DetectedMediaFolders);
            AnalyzeSyncIgnore();
            StatusMessage = "Added detected folders to .syncignore. Persistent server data protected.";
            AppendTerminalOutput($"🛡️ Protected folders: {string.Join(", ", DetectedMediaFolders)} added to .syncignore.");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error updating .syncignore: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task OpenSyncIgnoreSelectorAsync()
    {
        if (string.IsNullOrWhiteSpace(LocalRootPath) || !Directory.Exists(LocalRootPath))
        {
            StatusMessage = "Local workspace directory is not configured or does not exist.";
            return;
        }

        IsSyncIgnoreSelectorOpen = true;
        IsScanningSyncIgnoreTree = true;
        IsRawEditorMode = false;

        string syncIgnorePath = Path.Combine(LocalRootPath, ".syncignore");
        if (File.Exists(syncIgnorePath))
        {
            try
            {
                RawSyncIgnoreEditorText = await File.ReadAllTextAsync(syncIgnorePath);
            }
            catch
            {
                RawSyncIgnoreEditorText = string.Empty;
            }
        }
        else
        {
            RawSyncIgnoreEditorText = SyncIgnoreService.StandardSyncIgnoreContent;
        }

        try
        {
            var nodes = await _syncIgnoreService.ScanWorkspaceNodesAsync(LocalRootPath);
            SyncIgnoreTreeNodes.Clear();
            foreach (var node in nodes)
            {
                AttachNodeEvents(node);
                SyncIgnoreTreeNodes.Add(node);
            }
            UpdateIgnoredCounts();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to scan directory tree: {ex.Message}";
        }
        finally
        {
            IsScanningSyncIgnoreTree = false;
        }
    }

    [RelayCommand]
    private void CloseSyncIgnoreSelector()
    {
        IsSyncIgnoreSelectorOpen = false;
    }

    [RelayCommand]
    private async Task SaveSyncIgnoreAsync()
    {
        if (string.IsNullOrWhiteSpace(LocalRootPath)) return;

        try
        {
            string finalContent;
            if (IsRawEditorMode)
            {
                finalContent = RawSyncIgnoreEditorText;
            }
            else
            {
                // Harvest all checked items from the tree
                var ignoredPatterns = new List<string>();
                CollectIgnoredPatterns(SyncIgnoreTreeNodes, ignoredPatterns);

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("# ===================================================================");
                sb.AppendLine("# Bridge Sync Ignore Configuration (.syncignore)");
                sb.AppendLine($"# Generated on {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine("# ===================================================================");
                sb.AppendLine();
                sb.AppendLine("# --- Protected / Excluded Paths ---");
                foreach (var p in ignoredPatterns.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    sb.AppendLine(p);
                }

                finalContent = sb.ToString();
            }

            await _syncIgnoreService.WriteSyncIgnoreContentAsync(LocalRootPath, finalContent);
            IsSyncIgnoreSelectorOpen = false;
            AnalyzeSyncIgnore();
            StatusMessage = $"Successfully updated .syncignore ({SyncIgnoreRuleCount} active rules).";
            AppendTerminalOutput($"💾 .syncignore saved with {SyncIgnoreRuleCount} protected rules.");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to save .syncignore: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ToggleRawEditorMode()
    {
        IsRawEditorMode = !IsRawEditorMode;
        if (IsRawEditorMode)
        {
            // Update raw text from tree if switching to raw
            var ignoredPatterns = new List<string>();
            CollectIgnoredPatterns(SyncIgnoreTreeNodes, ignoredPatterns);
            if (ignoredPatterns.Count > 0)
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("# ===================================================================");
                sb.AppendLine("# Bridge Sync Ignore Configuration (.syncignore)");
                sb.AppendLine("# ===================================================================");
                sb.AppendLine();
                foreach (var p in ignoredPatterns.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    sb.AppendLine(p);
                }
                RawSyncIgnoreEditorText = sb.ToString();
            }
        }
    }

    [RelayCommand]
    private void SelectAllInTree()
    {
        foreach (var node in SyncIgnoreTreeNodes)
        {
            node.IsSelected = true;
        }
        UpdateIgnoredCounts();
    }

    [RelayCommand]
    private void DeselectAllInTree()
    {
        foreach (var node in SyncIgnoreTreeNodes)
        {
            node.IsSelected = false;
        }
        UpdateIgnoredCounts();
    }

    partial void OnSyncIgnoreFilterQueryChanged(string value)
    {
        FilterTreeNodes(SyncIgnoreTreeNodes, value);
    }

    private static bool FilterTreeNodes(IEnumerable<FilePatchNode> nodes, string query)
    {
        bool anyChildVisible = false;
        foreach (var node in nodes)
        {
            bool matches = string.IsNullOrWhiteSpace(query) ||
                           node.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                           node.RelativePath.Contains(query, StringComparison.OrdinalIgnoreCase);

            bool childVisible = false;
            if (node.Children.Count > 0)
            {
                childVisible = FilterTreeNodes(node.Children, query);
            }

            node.IsVisibleInFilter = matches || childVisible;
            if (node.IsVisibleInFilter) anyChildVisible = true;
        }
        return anyChildVisible;
    }

    private void AttachNodeEvents(FilePatchNode node)
    {
        node.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(FilePatchNode.IsSelected))
            {
                UpdateIgnoredCounts();
            }
        };

        foreach (var child in node.Children)
        {
            AttachNodeEvents(child);
        }
    }

    private void UpdateIgnoredCounts()
    {
        int files = 0;
        int dirs = 0;
        CountIgnored(SyncIgnoreTreeNodes, ref files, ref dirs);
        IgnoredFilesCount = files;
        IgnoredDirsCount = dirs;
    }

    private static void CountIgnored(IEnumerable<FilePatchNode> nodes, ref int fileCount, ref int dirCount)
    {
        foreach (var node in nodes)
        {
            if (node.IsSelected)
            {
                if (node.IsDirectory) dirCount++;
                else fileCount++;
            }
            if (node.Children.Count > 0)
            {
                CountIgnored(node.Children, ref fileCount, ref dirCount);
            }
        }
    }

    private static void CollectIgnoredPatterns(IEnumerable<FilePatchNode> nodes, List<string> patterns)
    {
        foreach (var node in nodes)
        {
            if (node.IsSelected)
            {
                // If a whole directory is selected, we can ignore the directory with trailing slash
                patterns.Add(node.IsDirectory ? (node.RelativePath.EndsWith('/') ? node.RelativePath : node.RelativePath + "/") : node.RelativePath);
            }
            else if (node.Children.Count > 0)
            {
                // Check if any children are selected individually
                CollectIgnoredPatterns(node.Children, patterns);
            }
        }
    }

    private UniversalPipelineConfig BuildCurrentPipelineConfig(VpsProfile? profile, AppConfig config)
    {
        var targetProfile = profile ?? new VpsProfile();

        // Read patterns from active .syncignore if available, otherwise fallback to config.SyncIgnorePatterns
        var syncAnalysis = _syncIgnoreService.AnalyzeProjectWorkspace(targetProfile.LocalRootPath, config.SyncIgnorePatterns);
        var activePatterns = syncAnalysis.ActivePatterns.Count > 0 ? syncAnalysis.ActivePatterns : config.SyncIgnorePatterns;

        return new UniversalPipelineConfig
        {
            ProfileId = targetProfile.Id,
            ProfileName = targetProfile.Name,
            TargetOS = targetProfile.TargetOS,
            ProjectType = targetProfile.IsDockerized
                ? ProjectType.DockerCompose
                : (targetProfile.TargetOS == HostOS.Windows && targetProfile.ProjectType == ProjectType.NativeLinuxService
                    ? ProjectType.WindowsApplication
                    : targetProfile.ProjectType),
            LocalProjectPath = targetProfile.LocalRootPath,
            RemoteProjectPath = targetProfile.RemoteProjectPath,
            IgnorePatterns = activePatterns,
            ComposeFilePath = RemotePath.ResolveComposeFilePath(targetProfile.ComposeFileName, targetProfile.RemoteProjectPath),
            LaunchCommand = !string.IsNullOrWhiteSpace(LaunchCommand) ? LaunchCommand : targetProfile.LaunchCommand,
            ServiceName = targetProfile.ServiceName,
            StartCommand = targetProfile.StartCommand,
            StopCommand = targetProfile.StopCommand,
            RestartCommand = targetProfile.RestartCommand,
            DbEngine = targetProfile.DbEngine,
            IsDbContainerized = targetProfile.IsDbContainerized,
            DbContainerName = targetProfile.DbContainerName,
            DbUser = targetProfile.DbUser,
            DbName = targetProfile.DbName,
            DbHost = targetProfile.DbHost,
            DbPort = targetProfile.DbPort,
            CustomBackupCommand = targetProfile.CustomBackupCommand,
            CustomMigrationCommand = targetProfile.CustomMigrationCommand,
            SkipDatabaseBackup = SkipDatabaseBackup,
            HealthCheckType = targetProfile.HealthCheckType,
            HealthCheckUrl = targetProfile.HealthCheckUrl,
            HealthCheckPort = targetProfile.HealthCheckPort,
            HealthCheckProcessName = targetProfile.HealthCheckProcessName,
            HealthCheckCustomCommand = targetProfile.HealthCheckCustomCommand,
            SkipHealthCheck = SkipHealthCheck
        };
    }

    [RelayCommand]
    private async Task RunDeploymentPipelineAsync()
    {
        if (IsPipelineRunning)
            return;

        if (!_session.IsConnected)
        {
            StatusMessage = "SSH is not connected. Please connect in Overview before deploying.";
            return;
        }

        var activeProfile = _profileContext.CurrentProfile;
        if (activeProfile == null)
        {
            StatusMessage = "No active VPS profile configured. Please select a profile in Settings.";
            return;
        }

        if (!_operationLock.TryAcquireLock($"Deployment to {activeProfile.Name}", out var lockHandle))
        {
            StatusMessage = $"Cannot deploy: {_operationLock.ActiveOperationDescription} is in progress.";
            return;
        }

        try
        {

            var config = await _configService.LoadConfigAsync();
            var pipelineConfig = BuildCurrentPipelineConfig(activeProfile, config);

            // Re-initialize dynamic step collection
            var steps = _pipelineService.GetDynamicPipelineSteps(pipelineConfig);
            PipelineSteps.Clear();
            foreach (var step in steps)
            {
                PipelineSteps.Add(step);
            }

            _cancellationTokenSource = new CancellationTokenSource();
            IsPipelineRunning = true;
            CanCancelPipeline = true;
            OverallStatus = PipelineStatus.InProgress;
            TerminalOutput = string.Empty;
            TotalDuration = TimeSpan.Zero;

            string modeLabel = pipelineConfig.IsDockerized ? "Docker Compose Stack" : "Direct Host Process";
            StatusMessage = $"Starting Deployment Pipeline for {activeProfile.Name} ({modeLabel})...";
            AppendTerminalOutput($"🚀 Starting Deployment Pipeline [{modeLabel}]");
            AppendTerminalOutput($"Target: {activeProfile.VpsUser}@{activeProfile.VpsIp}:{activeProfile.SshPort} -> {activeProfile.RemoteProjectPath}");
            AppendTerminalOutput($"Sync Ignore Rules: {pipelineConfig.IgnorePatterns.Count} patterns active");

            var progress = new Progress<PipelineStep>(UpdatePipelineStep);
            var result = await _pipelineService.ExecutePipelineAsync(pipelineConfig, progress, _cancellationTokenSource.Token);

            OverallStatus = result.Status;
            TotalDuration = result.TotalDuration;
            IsPipelineRunning = false;
            CanCancelPipeline = false;

            if (result.Success)
            {
                StatusMessage = $"✅ Pipeline completed successfully in {result.TotalDuration.TotalSeconds:F1}s";
                AppendTerminalOutput("✨ Pipeline completed successfully!");
                AppendTerminalOutput($"Total duration: {result.TotalDuration.TotalSeconds:F1}s");
                WeakReferenceMessenger.Default.Send(new ActivityRecordedMessage(ActivityKind.Deployment, $"succeeded for {activeProfile.Name}", result.CompletedAt));
                ActivityRecorded?.Invoke($"succeeded for {activeProfile.Name}", result.CompletedAt);
            }
            else
            {
                StatusMessage = $"❌ Pipeline failed: {result.ErrorMessage}";
                AppendTerminalOutput($"❌ Pipeline failed: {result.ErrorMessage}");
                WeakReferenceMessenger.Default.Send(new ActivityRecordedMessage(ActivityKind.Deployment, $"failed for {activeProfile.Name}: {result.ErrorMessage}", result.CompletedAt));
                ActivityRecorded?.Invoke($"failed for {activeProfile.Name}: {result.ErrorMessage}", result.CompletedAt);
            }
        }
        catch (OperationCanceledException)
        {
            OverallStatus = PipelineStatus.Cancelled;
            IsPipelineRunning = false;
            CanCancelPipeline = false;
            StatusMessage = "Pipeline was cancelled by user.";
            AppendTerminalOutput("⚠️ Pipeline was cancelled by user.");
            WeakReferenceMessenger.Default.Send(new ActivityRecordedMessage(ActivityKind.Deployment, $"cancelled for {ProfileName}", DateTime.Now));
            ActivityRecorded?.Invoke($"cancelled for {ProfileName}", DateTime.Now);
        }
        catch (Exception ex)
        {
            OverallStatus = PipelineStatus.Failed;
            IsPipelineRunning = false;
            CanCancelPipeline = false;
            StatusMessage = $"Pipeline error: {ex.Message}";
            AppendTerminalOutput($"❌ Pipeline error: {ex.Message}");
            WeakReferenceMessenger.Default.Send(new ActivityRecordedMessage(ActivityKind.Deployment, $"failed for {ProfileName}: {ex.Message}", DateTime.Now));
            ActivityRecorded?.Invoke($"failed for {ProfileName}: {ex.Message}", DateTime.Now);
        }
        finally
        {
            lockHandle?.Dispose();
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    [RelayCommand]
    private void CancelPipeline()
    {
        if (_cancellationTokenSource != null && !_cancellationTokenSource.IsCancellationRequested)
        {
            _cancellationTokenSource.Cancel();
            StatusMessage = "Cancelling pipeline...";
        }
    }

    [RelayCommand]
    private void ClearTerminal()
    {
        TerminalOutput = string.Empty;
    }

    [RelayCommand]
    private void ResetPipeline()
    {
        if (IsPipelineRunning)
            return;

        RefreshPipelineSteps();
        OverallStatus = PipelineStatus.NotStarted;
        TotalDuration = TimeSpan.Zero;
        TerminalOutput = string.Empty;
        StatusMessage = "Pipeline reset. Ready to start.";
    }

    private void UpdatePipelineStep(PipelineStep step)
    {
        var existingStep = PipelineSteps.FirstOrDefault(s => s.Id == step.Id);
        if (existingStep != null)
        {
            var index = PipelineSteps.IndexOf(existingStep);
            existingStep.Status = step.Status;
            existingStep.StartedAt = step.StartedAt;
            existingStep.CompletedAt = step.CompletedAt;
            existingStep.ErrorMessage = step.ErrorMessage;
            
            // Re-index to ensure ObservableCollection raises item replacement if view relies on collection changes
            PipelineSteps[index] = existingStep;

            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            switch (step.Status)
            {
                case PipelineStepStatus.InProgress:
                    AppendTerminalOutput($"⚡ [{timestamp}] Starting: {step.Name}");
                    StatusMessage = $"Running: {step.Name}...";
                    break;
                case PipelineStepStatus.Completed:
                    AppendTerminalOutput($"✅ [{timestamp}] Completed: {step.Name} ({step.Duration.TotalSeconds:F1}s)");
                    break;
                case PipelineStepStatus.Failed:
                    AppendTerminalOutput($"❌ [{timestamp}] Failed: {step.Name} - {step.ErrorMessage}");
                    break;
                case PipelineStepStatus.Skipped:
                    AppendTerminalOutput($"⏭️ [{timestamp}] Skipped: {step.Name}");
                    break;
            }
        }
    }

    private void AppendTerminalOutput(string message)
    {
        TerminalOutput += message + "\n";
    }
}
