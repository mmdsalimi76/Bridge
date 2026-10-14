using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VpsManager.Core.Models;
using VpsManager.Core.Services;
using VpsManager.Core.Services.Locking;
using VpsManager.Core.Services.Session;

namespace VpsManager.Client.ViewModels;

public partial class PatchViewModel : ViewModelBase
{
    private readonly IActiveProfileContext _profileContext;
    private readonly ISessionCoordinator _session;
    private readonly IPatchService _patchService;
    private readonly ConfigService _configService;
    private readonly ISyncIgnoreService _syncIgnoreService;
    private readonly IOperationLock _operationLock;
    private CancellationTokenSource? _cts;

    // -------------------------------------------------------------------------
    // Active Profile & Environment Context
    // -------------------------------------------------------------------------

    [ObservableProperty]
    private string _profileName = "Default";

    [ObservableProperty]
    private string _localRootPath = string.Empty;

    [ObservableProperty]
    private string _remoteProjectPath = string.Empty;

    [ObservableProperty]
    private bool _isDockerized = true;

    [ObservableProperty]
    private string _composeFileName = "docker-compose.yml";

    [ObservableProperty]
    private string _serviceName = string.Empty;

    [ObservableProperty]
    private string _restartCommand = string.Empty;

    [ObservableProperty]
    private string _launchCommand = string.Empty;

    [ObservableProperty]
    private string _syncIgnoreStatusText = "Checking .syncignore...";

    [ObservableProperty]
    private int _syncIgnoreRuleCount;

    [ObservableProperty]
    private bool _hasSyncIgnoreFile;

    // -------------------------------------------------------------------------
    // Git Working Tree & Scan State
    // -------------------------------------------------------------------------

    [ObservableProperty]
    private string _gitBranchName = "Detecting...";

    [ObservableProperty]
    private string _gitStatusSummary = "Ready to scan.";

    [ObservableProperty]
    private bool _isGitRepository;

    [ObservableProperty]
    private bool _hasGitChanges;

    [ObservableProperty]
    private bool _isGitScanMode = true;

    // -------------------------------------------------------------------------
    // File Tree & Search Filter
    // -------------------------------------------------------------------------

    [ObservableProperty]
    private ObservableCollection<FilePatchNode> _files = new();

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    // -------------------------------------------------------------------------
    // Counter & Payload Metrics
    // -------------------------------------------------------------------------

    [ObservableProperty]
    private int _totalFileCount;

    [ObservableProperty]
    private int _selectedFileCount;

    [ObservableProperty]
    private int _modifiedCount;

    [ObservableProperty]
    private int _newCount;

    [ObservableProperty]
    private int _deletedCount;

    [ObservableProperty]
    private long _payloadSizeBytes;

    [ObservableProperty]
    private string _formattedPayloadSize = "0 B";

    // -------------------------------------------------------------------------
    // Safety Gates & Deployment Options
    // -------------------------------------------------------------------------

    [ObservableProperty]
    private bool _createBackupSnapshot = true;

    [ObservableProperty]
    private bool _autoReloadService = true;

    [ObservableProperty]
    private string _lastBackupPath = string.Empty;

    [ObservableProperty]
    private bool _hasBackupToRollback;

    // -------------------------------------------------------------------------
    // Real-Time Console & Status
    // -------------------------------------------------------------------------

    [ObservableProperty]
    private string _terminalOutput = string.Empty;

    [ObservableProperty]
    private string _statusMessage = "Ready. Select a scan mode to begin.";

    [ObservableProperty]
    private bool _isBusy;

    // -------------------------------------------------------------------------
    // Constructor
    // -------------------------------------------------------------------------

    public PatchViewModel(
        IActiveProfileContext profileContext,
        ISessionCoordinator session,
        IPatchService patchService,
        ConfigService configService,
        ISyncIgnoreService syncIgnoreService,
        IOperationLock operationLock)
    {
        _profileContext = profileContext ?? throw new ArgumentNullException(nameof(profileContext));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _patchService = patchService ?? throw new ArgumentNullException(nameof(patchService));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _syncIgnoreService = syncIgnoreService ?? throw new ArgumentNullException(nameof(syncIgnoreService));
        _operationLock = operationLock ?? throw new ArgumentNullException(nameof(operationLock));

        _profileContext.ActiveProfileChanged += (_, profile) => ApplyProfile(profile);

        _session.StateChanged += (_, e) =>
        {
            DeployPatchCommand.NotifyCanExecuteChanged();
            RollbackLastPatchCommand.NotifyCanExecuteChanged();

            if (e.State == SessionState.Disconnected)
            {
                StatusMessage = "SSH is disconnected. Connect in Overview before deploying patches.";
            }
        };

        ApplyProfile(_profileContext.CurrentProfile);
    }

    private void ApplyProfile(VpsProfile? profile)
    {
        if (profile != null)
        {
            ProfileName = profile.Name;
            LocalRootPath = profile.LocalRootPath;
            RemoteProjectPath = profile.RemoteProjectPath;
            IsDockerized = profile.IsDockerized;
            ComposeFileName = string.IsNullOrWhiteSpace(profile.ComposeFileName) ? "docker-compose.yml" : profile.ComposeFileName;
            ServiceName = profile.ServiceName;
            RestartCommand = profile.RestartCommand;
            LaunchCommand = profile.LaunchCommand;
        }

        foreach (var old in Files)
            DetachNodeEvents(old);
        Files.Clear();
        UpdateCounters();
        AnalyzeSyncIgnore();
        _ = RefreshGitStatusAsync();

        StatusMessage = $"Switched profile to {ProfileName}. Ready to scan.";
    }

    public void AnalyzeSyncIgnore()
    {
        if (string.IsNullOrWhiteSpace(LocalRootPath) || !Directory.Exists(LocalRootPath))
        {
            HasSyncIgnoreFile = false;
            SyncIgnoreStatusText = "No local project directory configured";
            SyncIgnoreRuleCount = 0;
            return;
        }

        var analysis = _syncIgnoreService.AnalyzeProjectWorkspace(LocalRootPath);
        HasSyncIgnoreFile = analysis.HasSyncIgnoreFile;
        SyncIgnoreRuleCount = analysis.ActivePatterns.Count;

        SyncIgnoreStatusText = analysis.HasSyncIgnoreFile
            ? $".syncignore active ({SyncIgnoreRuleCount} patterns protected)"
            : "No .syncignore found in workspace";
    }

    public async Task RefreshGitStatusAsync()
    {
        if (string.IsNullOrWhiteSpace(LocalRootPath) || !Directory.Exists(LocalRootPath))
        {
            GitBranchName = "No Path";
            GitStatusSummary = "Configure workspace in Settings";
            IsGitRepository = false;
            return;
        }

        try
        {
            var gitStatus = await _patchService.GetGitStatusAsync(LocalRootPath);
            IsGitRepository = gitStatus.IsGitRepository;
            GitBranchName = gitStatus.BranchName;
            GitStatusSummary = gitStatus.Summary;
            HasGitChanges = gitStatus.HasUncommittedChanges;
        }
        catch (Exception ex)
        {
            GitBranchName = "Error";
            GitStatusSummary = ex.Message;
            IsGitRepository = false;
        }
    }

    // -------------------------------------------------------------------------
    // Partial Property Callbacks
    // -------------------------------------------------------------------------

    partial void OnSearchQueryChanged(string value) => ApplyFilter();

    partial void OnIsGitScanModeChanged(bool value)
    {
        // Re-scan when switching scan modes
        _ = ScanChangesAsync();
    }

    // -------------------------------------------------------------------------
    // Commands
    // -------------------------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanScan))]
    public async Task ScanGitChangesAsync()
    {
        IsGitScanMode = true;
        await ScanChangesAsync();
    }

    [RelayCommand(CanExecute = nameof(CanScan))]
    public async Task ScanAllFilesAsync()
    {
        IsGitScanMode = false;
        await ScanChangesAsync();
    }

    [RelayCommand(CanExecute = nameof(CanScan))]
    public async Task ScanChangesAsync()
    {
        if (IsBusy) return;

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        IsBusy = true;
        HasGitChanges = false;

        try
        {
            var profile = _profileContext.CurrentProfile;
            var config = await _configService.LoadConfigAsync();

            string localRoot = profile?.LocalRootPath ?? config.LocalRootPath;
            if (string.IsNullOrWhiteSpace(localRoot) || !Directory.Exists(localRoot))
            {
                StatusMessage = "⚠ No local root path configured or directory not found. Set it in Settings.";
                AppendTerminalOutput("⚠️ Scan aborted: Local workspace path is invalid or missing.");
                return;
            }

            // Combine active .syncignore rules with global fallback patterns
            var syncAnalysis = _syncIgnoreService.AnalyzeProjectWorkspace(localRoot, config.SyncIgnorePatterns);
            var activePatterns = syncAnalysis.ActivePatterns.Count > 0 ? syncAnalysis.ActivePatterns : config.SyncIgnorePatterns;

            List<FilePatchNode> scanned;

            if (IsGitScanMode)
            {
                StatusMessage = "🔍 Scanning Git working tree changes...";
                AppendTerminalOutput($"🔍 Running Git status scan on {localRoot}...");
                scanned = await _patchService.ScanGitChangesAsync(localRoot, activePatterns, _cts.Token);
            }
            else
            {
                StatusMessage = "🔍 Scanning all workspace files...";
                AppendTerminalOutput($"🔍 Scanning full project hierarchy on {localRoot} ({activePatterns.Count} ignore rules active)...");
                scanned = await _patchService.ScanLocalChangesAsync(localRoot, activePatterns, _cts.Token);
            }

            foreach (var old in Files)
                DetachNodeEvents(old);
            Files.Clear();
            foreach (var node in scanned)
            {
                AttachNodeEvents(node);
                Files.Add(node);
            }

            ApplyFilter();
            UpdateCounters();
            await RefreshGitStatusAsync();

            if (IsGitScanMode)
            {
                HasGitChanges = scanned.Count > 0;
                if (scanned.Count == 0)
                {
                    StatusMessage = "✅ No uncommitted changes detected by Git. Working tree is clean.";
                    AppendTerminalOutput("✅ Git scan complete: Working tree is clean (0 changes).");
                }
                else
                {
                    StatusMessage = $"✅ Git scan complete — {ModifiedCount} modified, {NewCount} new, {DeletedCount} deleted.";
                    AppendTerminalOutput($"✅ Git scan detected {scanned.Count} uncommitted file changes ({FormattedPayloadSize}).");
                }
            }
            else
            {
                StatusMessage = $"✅ Workspace scan complete — {TotalFileCount} file(s) indexed.";
                AppendTerminalOutput($"✅ Workspace scan complete: {TotalFileCount} file(s) discovered.");
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "⚠ Scan cancelled.";
            AppendTerminalOutput("⚠️ Scan was cancelled by user.");
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Scan error: {ex.Message}";
            AppendTerminalOutput($"❌ Scan error: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanScan() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanDeploy))]
    public async Task DeployPatchAsync()
    {
        if (IsBusy) return;

        if (!_session.IsConnected)
        {
            StatusMessage = "SSH is disconnected. Connect in Overview before deploying patches.";
            AppendTerminalOutput("❌ Deployment aborted: SSH connection is not active.");
            return;
        }

        if (!_operationLock.TryAcquireLock($"Patching {ProfileName}", out var lockHandle))
        {
            StatusMessage = $"Cannot patch: {_operationLock.ActiveOperationDescription} is in progress.";
            AppendTerminalOutput($"⚠️ Cannot patch: {_operationLock.ActiveOperationDescription} is in progress.");
            return;
        }

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        IsBusy = true;

        try
        {
            var profile = _profileContext.CurrentProfile;
            var config = await _configService.LoadConfigAsync();

            string localRoot = profile?.LocalRootPath ?? config.LocalRootPath;
            string remoteTarget = profile?.RemoteProjectPath ?? config.RemoteProjectPath;

            if (string.IsNullOrWhiteSpace(remoteTarget))
            {
                StatusMessage = "⚠ Remote project path is not configured. Set it in Settings.";
                AppendTerminalOutput("❌ Deployment aborted: Target remote path is missing.");
                return;
            }

            var selected = GetAllSelectedFiles(Files).ToList();
            if (selected.Count == 0)
            {
                StatusMessage = "⚠ No files selected. Check the files you want to hot-patch.";
                AppendTerminalOutput("⚠️ No files selected in the patch tree.");
                return;
            }

            string modeLabel = IsDockerized ? "Docker Compose" : "Direct Host Process";
            AppendTerminalOutput("===================================================================");
            AppendTerminalOutput($"🚀 Starting Hot-Patch Deployment [{modeLabel}]");
            AppendTerminalOutput($"Target: {profile?.VpsUser}@{profile?.VpsIp}:{profile?.SshPort} -> {remoteTarget}");
            AppendTerminalOutput($"Payload: {selected.Count} file(s) selected ({FormattedPayloadSize})");
            AppendTerminalOutput("===================================================================");

            var options = new PatchDeploymentOptions
            {
                CreateBackupSnapshot = CreateBackupSnapshot,
                AutoReloadService = AutoReloadService,
                IsDockerized = IsDockerized,
                ComposeFileName = ComposeFileName,
                ServiceName = ServiceName,
                RestartCommand = RestartCommand,
                LaunchCommand = LaunchCommand
            };

            var progress = new Progress<string>(AppendTerminalOutput);
            var result = await _patchService.DeployPatchAsync(
                selected,
                localRoot,
                remoteTarget,
                options,
                progress,
                _cts.Token);

            if (result.IsSuccess)
            {
                StatusMessage = $"✅ Patch deployed successfully in {result.Duration.TotalSeconds:F1}s";
                if (!string.IsNullOrWhiteSpace(result.BackupPath))
                {
                    LastBackupPath = result.BackupPath;
                    HasBackupToRollback = true;
                }
            }
            else
            {
                StatusMessage = $"❌ Patch deployment failed: {result.Error}";
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "⚠ Deployment cancelled.";
            AppendTerminalOutput("⚠️ Deployment cancelled by user.");
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Deployment error: {ex.Message}";
            AppendTerminalOutput($"❌ Fatal error: {ex.Message}");
        }
        finally
        {
            lockHandle?.Dispose();
            IsBusy = false;
            DeployPatchCommand.NotifyCanExecuteChanged();
            RollbackLastPatchCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanDeploy() => !IsBusy && SelectedFileCount > 0 && _session.IsConnected;

    [RelayCommand(CanExecute = nameof(CanRollback))]
    public async Task RollbackLastPatchAsync()
    {
        if (IsBusy || string.IsNullOrWhiteSpace(LastBackupPath) || !_session.IsConnected)
            return;

        if (!_operationLock.TryAcquireLock($"Rollback on {ProfileName}", out var lockHandle))
        {
            StatusMessage = $"Cannot rollback: {_operationLock.ActiveOperationDescription} is in progress.";
            AppendTerminalOutput($"⚠️ Cannot rollback: {_operationLock.ActiveOperationDescription} is in progress.");
            return;
        }

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        IsBusy = true;

        try
        {
            var profile = _profileContext.CurrentProfile;
            var config = await _configService.LoadConfigAsync();
            string remoteTarget = profile?.RemoteProjectPath ?? config.RemoteProjectPath;

            AppendTerminalOutput("===================================================================");
            AppendTerminalOutput($"⏪ Initiating Rollback from Snapshot: {LastBackupPath}");
            AppendTerminalOutput("===================================================================");

            var progress = new Progress<string>(AppendTerminalOutput);
            var result = await _patchService.RollbackPatchAsync(remoteTarget, LastBackupPath, progress, _cts.Token);

            if (result.IsSuccess)
            {
                StatusMessage = $"✅ Rollback complete. Target restored from {Path.GetFileName(LastBackupPath)}.";
                HasBackupToRollback = false;
            }
            else
            {
                StatusMessage = $"❌ Rollback failed: {result.Error}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Rollback error: {ex.Message}";
            AppendTerminalOutput($"❌ Rollback error: {ex.Message}");
        }
        finally
        {
            lockHandle?.Dispose();
            IsBusy = false;
            RollbackLastPatchCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanRollback() => !IsBusy && HasBackupToRollback && !string.IsNullOrWhiteSpace(LastBackupPath) && _session.IsConnected;

    [RelayCommand]
    private void SelectModifiedOnly()
    {
        foreach (var node in Files)
            SetModifiedSelection(node, selectModified: true);
        UpdateCounters();
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var node in Files)
            node.IsSelected = true;
        UpdateCounters();
    }

    [RelayCommand]
    private void DeselectAll()
    {
        foreach (var node in Files)
            node.IsSelected = false;
        UpdateCounters();
    }

    [RelayCommand]
    private void CancelOperation()
    {
        _cts?.Cancel();
        StatusMessage = "⚠ Operation cancelled by user.";
        AppendTerminalOutput("⚠️ Cancellation requested.");
    }

    [RelayCommand]
    private void ClearTerminal()
    {
        TerminalOutput = string.Empty;
    }

    // -------------------------------------------------------------------------
    // Reactive Selection & Tree Filtering
    // -------------------------------------------------------------------------

    private void AttachNodeEvents(FilePatchNode node)
    {
        node.PropertyChanged += OnNodePropertyChanged;
        foreach (var child in node.Children)
        {
            AttachNodeEvents(child);
        }
    }

    private void DetachNodeEvents(FilePatchNode node)
    {
        node.PropertyChanged -= OnNodePropertyChanged;
        foreach (var child in node.Children)
        {
            DetachNodeEvents(child);
        }
    }

    private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FilePatchNode.IsSelected))
        {
            UpdateCounters();
        }
    }

    private void ApplyFilter()
    {
        string query = SearchQuery?.Trim() ?? string.Empty;
        FilterTreeNodes(Files, query);
    }

    private static bool FilterTreeNodes(IEnumerable<FilePatchNode> nodes, string query)
    {
        bool anyVisible = false;
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
            if (node.IsVisibleInFilter) anyVisible = true;
        }
        return anyVisible;
    }

    public void UpdateCounters()
    {
        var allFiles = GetAllFiles(Files).ToList();
        var selectedFiles = allFiles.Where(f => f.IsSelected).ToList();

        TotalFileCount = allFiles.Count;
        SelectedFileCount = selectedFiles.Count;

        ModifiedCount = allFiles.Count(f => f.ChangeStatus == FileChangeStatus.Modified);
        NewCount = allFiles.Count(f => f.ChangeStatus == FileChangeStatus.New);
        DeletedCount = allFiles.Count(f => f.ChangeStatus == FileChangeStatus.Deleted);

        // Sum payload size of selected files that are NOT deleted
        PayloadSizeBytes = selectedFiles
            .Where(f => f.ChangeStatus != FileChangeStatus.Deleted)
            .Sum(f => f.FileSizeBytes);

        FormattedPayloadSize = FilePatchNode.FormatSize(PayloadSizeBytes);

        ScanChangesCommand.NotifyCanExecuteChanged();
        DeployPatchCommand.NotifyCanExecuteChanged();
        RollbackLastPatchCommand.NotifyCanExecuteChanged();
    }

    private void AppendTerminalOutput(string message)
    {
        TerminalOutput += message + "\n";
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static IEnumerable<FilePatchNode> GetAllFiles(IEnumerable<FilePatchNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (!node.IsDirectory)
                yield return node;
            else
                foreach (var child in GetAllFiles(node.Children))
                    yield return child;
        }
    }

    private static IEnumerable<FilePatchNode> GetAllSelectedFiles(IEnumerable<FilePatchNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (!node.IsDirectory)
            {
                if (node.IsSelected) yield return node;
            }
            else
            {
                foreach (var child in GetAllSelectedFiles(node.Children))
                    yield return child;
            }
        }
    }

    private static void SetModifiedSelection(FilePatchNode node, bool selectModified)
    {
        if (!node.IsDirectory)
        {
            node.IsSelected = selectModified &&
                              node.ChangeStatus != FileChangeStatus.Unchanged;
        }
        else
        {
            foreach (var child in node.Children)
                SetModifiedSelection(child, selectModified);
        }
    }
}
