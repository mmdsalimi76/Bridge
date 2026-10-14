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
using VpsManager.Core.Models;
using VpsManager.Core.Services;
using VpsManager.Core.Services.Locking;
using VpsManager.Core.Services.Session;

namespace VpsManager.Client.ViewModels;

public partial class DatabaseViewModel : ViewModelBase
{
    private readonly IActiveProfileContext _profileContext;
    private readonly ISessionCoordinator _session;
    private readonly IDatabaseService _databaseService;
    private readonly ConfigService _configService;
    private readonly IOperationLock _operationLock;
    private CancellationTokenSource? _cts;

    public List<string> AvailableScopes { get; } = new() { "Database", "Folder / Media", "Full Project" };
    public List<string> AvailableEngines { get; } = new() { "PostgreSQL", "MySQL", "MongoDB", "SQLite" };

    // -------------------------------------------------------------------------
    // Active Profile & Environment Telemetry
    // -------------------------------------------------------------------------

    [ObservableProperty]
    private string _profileName = "Default";

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private string _connectionStatusText = "Disconnected";

    // -------------------------------------------------------------------------
    // Backup Scope & Target Form
    // -------------------------------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFolderOrFullScope))]
    [NotifyPropertyChangedFor(nameof(IsDatabaseOrFullScope))]
    private string _selectedScope = "Database";

    public bool IsFolderOrFullScope => SelectedScope is "Folder / Media" or "Full Project";
    public bool IsDatabaseOrFullScope => SelectedScope is "Database" or "Full Project";

    [ObservableProperty]
    private bool _isDatabaseScopeSelected = true;

    [ObservableProperty]
    private bool _isFolderScopeSelected;

    [ObservableProperty]
    private bool _isFullScopeSelected;

    [ObservableProperty]
    private string _selectedEngine = "PostgreSQL";

    [ObservableProperty]
    private string _dbContainerName = "postgres";

    [ObservableProperty]
    private string _dbUser = "postgres";

    [ObservableProperty]
    private string _dbPassword = string.Empty;

    [ObservableProperty]
    private string _dbName = "app_db";

    [ObservableProperty]
    private string _remoteFolderPath = "/var/www/uploads";

    [ObservableProperty]
    private string _backupName = "assets";

    [ObservableProperty]
    private bool _downloadToLocalAfterBackup;

    [ObservableProperty]
    private string _cronSchedule = "0 2 * * *"; // Daily at 2 AM

    [ObservableProperty]
    private string _lastMd5Hash = "None";

    [ObservableProperty]
    private string _statusMessage = "Ready for Backup Operations";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    private double _downloadProgressPercent;

    [ObservableProperty]
    private bool _hasBackups;

    // -------------------------------------------------------------------------
    // Confirmation States for Destructive Actions
    // -------------------------------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingAction))]
    private DatabaseBackupInfo? _pendingRestoreBackup;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingAction))]
    private DatabaseBackupInfo? _pendingDeleteBackup;

    public bool HasPendingAction => PendingRestoreBackup != null || PendingDeleteBackup != null;

    // -------------------------------------------------------------------------
    // Vault Telemetry Summary
    // -------------------------------------------------------------------------

    [ObservableProperty]
    private int _totalBackupsCount;

    [ObservableProperty]
    private string _formattedTotalVaultSize = "0 B";

    [ObservableProperty]
    private string _latestBackupDateText = "None";

    [ObservableProperty]
    private string _cronCadenceText = "Daily (02:00)";

    [ObservableProperty]
    private string _lastCronRunStatus = "No runs";

    [ObservableProperty]
    private bool _hasVerifiedMd5;

    public ObservableCollection<DatabaseBackupInfo> Backups { get; } = new();
    public ObservableCollection<CronExecutionRecord> CronHistory { get; } = new();
    public event Action<string, DateTime>? ActivityRecorded;

    // UI Delegate for save file picker dialog
    public Func<string, Task<string?>>? SaveFileRequested { get; set; }

    public DatabaseViewModel(
        IActiveProfileContext profileContext,
        ISessionCoordinator session,
        IDatabaseService databaseService,
        ConfigService configService,
        IOperationLock operationLock)
    {
        _profileContext = profileContext ?? throw new ArgumentNullException(nameof(profileContext));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _operationLock = operationLock ?? throw new ArgumentNullException(nameof(operationLock));

        _profileContext.ActiveProfileChanged += (_, profile) =>
        {
            ApplyProfile(profile);
            if (_session.IsConnected)
            {
                _ = LoadAllDataAsync();
            }
            else
            {
                Backups.Clear();
                CronHistory.Clear();
                UpdateVaultMetrics();
            }
        };

        _session.StateChanged += (_, e) =>
        {
            UpdateConnectionState();
            if (e.State == SessionState.Connected)
            {
                _ = LoadAllDataAsync();
            }
            else if (e.State == SessionState.Disconnected || e.State == SessionState.Failed)
            {
                StatusMessage = $"SSH {e.StatusText}. Connect in Overview to manage backups.";
            }
        };

        ApplyProfile(_profileContext.CurrentProfile);
        UpdateConnectionState();

        if (_session.IsConnected)
        {
            _ = LoadAllDataAsync();
        }
    }

    private void UpdateConnectionState()
    {
        IsConnected = _session.IsConnected;
        ConnectionStatusText = IsConnected ? "Connected" : "Disconnected";
    }

    private void ApplyProfile(VpsProfile? profile)
    {
        ProfileName = profile?.Name ?? "Default";
        DbPassword = string.Empty; // Zero credential on profile swap
        PendingRestoreBackup = null;
        PendingDeleteBackup = null;

        if (profile != null)
        {
            if (profile.DbEngine != DatabaseEngine.None)
            {
                SelectedEngine = profile.DbEngine.ToString();
            }

            if (!string.IsNullOrWhiteSpace(profile.DbContainerName))
            {
                DbContainerName = profile.DbContainerName;
            }

            if (!string.IsNullOrWhiteSpace(profile.DbUser))
            {
                DbUser = profile.DbUser;
            }

            if (!string.IsNullOrWhiteSpace(profile.DbName))
            {
                DbName = profile.DbName;
            }

            if (!string.IsNullOrWhiteSpace(profile.RemoteProjectPath))
            {
                RemoteFolderPath = $"{profile.RemoteProjectPath.TrimEnd('/')}/uploads";
            }
        }

        UpdateConnectionState();
    }

    partial void OnSelectedScopeChanged(string value)
    {
        IsDatabaseScopeSelected = value == "Database";
        IsFolderScopeSelected = value == "Folder / Media";
        IsFullScopeSelected = value == "Full Project";
    }

    partial void OnIsDatabaseScopeSelectedChanged(bool value)
    {
        if (value) SelectedScope = "Database";
    }

    partial void OnIsFolderScopeSelectedChanged(bool value)
    {
        if (value) SelectedScope = "Folder / Media";
    }

    partial void OnIsFullScopeSelectedChanged(bool value)
    {
        if (value) SelectedScope = "Full Project";
    }

    partial void OnLastMd5HashChanged(string value)
    {
        HasVerifiedMd5 = !string.IsNullOrWhiteSpace(value) && !value.Equals("None", StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateVaultMetrics()
    {
        TotalBackupsCount = Backups.Count;
        HasBackups = Backups.Count > 0;
        long totalBytes = Backups.Sum(b => b.SizeBytes);
        FormattedTotalVaultSize = FormatSize(totalBytes);

        if (Backups.Count > 0)
        {
            var latest = Backups.OrderByDescending(b => b.CreatedAt).FirstOrDefault();
            LatestBackupDateText = latest != null ? latest.CreatedAt.ToLocalTime().ToString("MMM dd, HH:mm") : "None";
        }
        else
        {
            LatestBackupDateText = "None";
        }

        if (CronHistory.Count > 0)
        {
            var latestRun = CronHistory.First();
            LastCronRunStatus = latestRun.IsSuccess ? "Success" : "Failed";
        }
        else
        {
            LastCronRunStatus = "No runs";
        }

        HasVerifiedMd5 = !string.IsNullOrWhiteSpace(LastMd5Hash) && !LastMd5Hash.Equals("None", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1024 * 1024 * 1024 => $"{(bytes / (1024.0 * 1024 * 1024)):F2} GB",
        >= 1024 * 1024 => $"{(bytes / (1024.0 * 1024)):F2} MB",
        >= 1024 => $"{(bytes / 1024.0):F1} KB",
        _ => $"{bytes} B"
    };

    [RelayCommand]
    public void CancelOperation()
    {
        if (_cts != null && !_cts.IsCancellationRequested)
        {
            _cts.Cancel();
            StatusMessage = "Cancelling operation...";
        }
    }

    // -------------------------------------------------------------------------
    // Commands: Destructive Action Confirmation
    // -------------------------------------------------------------------------

    [RelayCommand]
    public void RequestRestore(DatabaseBackupInfo? backup)
    {
        if (backup == null) return;
        PendingDeleteBackup = null;
        PendingRestoreBackup = backup;
        StatusMessage = $"Confirm restore of '{backup.FileName}' — this will replace active database data.";
    }

    [RelayCommand]
    public void CancelRestore()
    {
        PendingRestoreBackup = null;
        StatusMessage = "Restore cancelled.";
    }

    [RelayCommand]
    public Task ConfirmRestoreAsync()
    {
        var backup = PendingRestoreBackup;
        PendingRestoreBackup = null;
        return RestoreBackupAsync(backup);
    }

    [RelayCommand]
    public void RequestDelete(DatabaseBackupInfo? backup)
    {
        if (backup == null) return;
        PendingRestoreBackup = null;
        PendingDeleteBackup = backup;
        StatusMessage = $"Confirm deletion of '{backup.FileName}' from remote VPS storage.";
    }

    [RelayCommand]
    public void CancelDelete()
    {
        PendingDeleteBackup = null;
        StatusMessage = "Delete cancelled.";
    }

    [RelayCommand]
    public Task ConfirmDeleteAsync()
    {
        var backup = PendingDeleteBackup;
        PendingDeleteBackup = null;
        return DeleteBackupAsync(backup);
    }

    // -------------------------------------------------------------------------
    // Commands: Backup Creation & Download
    // -------------------------------------------------------------------------

    [RelayCommand]
    private async Task CreateBackupAsync()
    {
        if (IsBusy) return;

        if (!_session.IsConnected)
        {
            StatusMessage = "SSH is disconnected. Connect in Overview before creating backups.";
            return;
        }

        if (!_operationLock.TryAcquireLock($"Database Backup on {ProfileName}", out var lockHandle))
        {
            StatusMessage = $"Cannot backup: {_operationLock.ActiveOperationDescription} is in progress.";
            return;
        }

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        IsBusy = true;

        try
        {
            DatabaseBackupInfo? backup = null;

            if (SelectedScope == "Folder / Media")
            {
                StatusMessage = $"Creating folder archive of '{RemoteFolderPath}'...";
                backup = await _databaseService.CreateFolderBackupAsync(RemoteFolderPath, BackupName, _cts.Token);
            }
            else if (SelectedScope == "Full Project")
            {
                StatusMessage = $"Creating full project backup (DB + '{RemoteFolderPath}')...";
                backup = await _databaseService.CreateFullBackupAsync(DbContainerName, DbUser, DbPassword, DbName, SelectedEngine, RemoteFolderPath, BackupName, _cts.Token);
            }
            else
            {
                StatusMessage = $"Creating remote {SelectedEngine} database dump...";
                backup = await _databaseService.CreateRemoteBackupAsync(DbContainerName, DbUser, DbPassword, DbName, SelectedEngine, _cts.Token);
            }

            LastMd5Hash = backup.Md5Hash;
            StatusMessage = $"Backup '{backup.FileName}' ({backup.FormattedSize}) created on VPS!";
            WeakReferenceMessenger.Default.Send(new ActivityRecordedMessage(ActivityKind.Backup, $"'{backup.FileName}' created ({backup.FormattedSize})", backup.CreatedAt));
            ActivityRecorded?.Invoke($"'{backup.FileName}' created ({backup.FormattedSize})", backup.CreatedAt);

            // Refresh vault list
            await LoadBackupsAsync();

            // Automatically download to local machine if requested
            if (DownloadToLocalAfterBackup)
            {
                await DownloadBackupAsync(backup);
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Backup creation cancelled.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Backup Error: {ex.Message}";
        }
        finally
        {
            lockHandle?.Dispose();
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DownloadBackupAsync(DatabaseBackupInfo? backup)
    {
        if (backup == null) return;
        if (!_session.IsConnected)
        {
            StatusMessage = "SSH is disconnected. Connect in Overview first.";
            return;
        }

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        IsBusy = true;
        IsDownloading = true;
        DownloadProgressPercent = 0;

        try
        {
            string? targetPath = null;
            if (SaveFileRequested != null)
            {
                targetPath = await SaveFileRequested(backup.FileName);
            }

            if (string.IsNullOrEmpty(targetPath))
            {
                // Default fallback: user's Downloads folder
                string downloadsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                targetPath = Path.Combine(downloadsDir, backup.FileName);
            }

            StatusMessage = $"Downloading {backup.FileName} to local machine...";
            var progress = new Progress<double>(p => DownloadProgressPercent = Math.Clamp(p * 100, 0, 100));
            await _databaseService.DownloadBackupAsync(backup.RemotePath, targetPath, progress, _cts.Token);

            // Verify checksum if we have it
            bool verified = await _databaseService.VerifyMd5ChecksumAsync(backup.RemotePath, targetPath, _cts.Token);
            string verifiedStr = verified ? " (MD5 checksum verified ✓)" : "";

            StatusMessage = $"Downloaded successfully to {targetPath}{verifiedStr}";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Download cancelled.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Download Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            IsDownloading = false;
            DownloadProgressPercent = 0;
        }
    }

    [RelayCommand]
    private async Task LoadAllDataAsync()
    {
        await LoadBackupsAsync();
        await LoadCronHistoryAsync();
    }

    [RelayCommand]
    private async Task LoadBackupsAsync()
    {
        if (!_session.IsConnected)
        {
            StatusMessage = "SSH is disconnected. Connect in Overview to view backups.";
            return;
        }

        try
        {
            var backups = await _databaseService.GetBackupsAsync();
            Backups.Clear();
            foreach (var b in backups)
            {
                Backups.Add(b);
            }
            UpdateVaultMetrics();
        }
        catch (Exception ex)
        {
            UpdateVaultMetrics();
            StatusMessage = $"Failed to load backups: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task LoadCronHistoryAsync()
    {
        if (!_session.IsConnected) return;

        try
        {
            var history = await _databaseService.GetCronHistoryAsync();
            CronHistory.Clear();
            foreach (var item in history)
            {
                CronHistory.Add(item);
            }
            UpdateVaultMetrics();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load cron history: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task RestoreBackupAsync(DatabaseBackupInfo? backup)
    {
        if (backup == null) return;
        if (!_session.IsConnected)
        {
            StatusMessage = "SSH is disconnected. Connect in Overview first.";
            return;
        }

        if (!_operationLock.TryAcquireLock($"Restoring {backup.FileName}", out var lockHandle))
        {
            StatusMessage = $"Cannot restore: {_operationLock.ActiveOperationDescription} is in progress.";
            return;
        }

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        IsBusy = true;

        try
        {
            StatusMessage = $"Restoring from {backup.FileName} (this may take a while)...";
            var result = await _databaseService.RestoreBackupAsync(DbContainerName, DbUser, DbPassword, DbName, SelectedEngine, backup.RemotePath, _cts.Token);
            if (result.IsSuccess)
            {
                StatusMessage = $"Restored {backup.FileName} successfully!";
                WeakReferenceMessenger.Default.Send(new ActivityRecordedMessage(ActivityKind.Backup, $"Restored {backup.FileName}", DateTime.Now));
            }
            else
            {
                StatusMessage = $"Restore failed: {result.Error}";
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Restore operation cancelled.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Restore Error: {ex.Message}";
        }
        finally
        {
            lockHandle?.Dispose();
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DeleteBackupAsync(DatabaseBackupInfo? backup)
    {
        if (backup == null) return;
        if (!_session.IsConnected)
        {
            StatusMessage = "SSH is disconnected. Connect in Overview first.";
            return;
        }

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        IsBusy = true;

        try
        {
            StatusMessage = $"Deleting {backup.FileName}...";
            var result = await _databaseService.DeleteBackupAsync(backup.RemotePath, _cts.Token);
            if (result.IsSuccess)
            {
                Backups.Remove(backup);
                UpdateVaultMetrics();
                StatusMessage = $"Backup '{backup.FileName}' deleted successfully from VPS.";
            }
            else
            {
                StatusMessage = $"Failed to delete backup: {result.Error}";
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Deletion cancelled.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Delete Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public void SetCronPreset(string preset)
    {
        if (string.IsNullOrWhiteSpace(preset)) return;
        CronSchedule = preset;
        CronCadenceText = preset switch
        {
            "0 2 * * *" => "Daily at 02:00",
            "0 */12 * * *" => "Every 12h",
            "0 */6 * * *" => "Every 6h",
            "0 3 * * 0" => "Weekly (Sun)",
            _ => $"Custom ({preset})"
        };
        StatusMessage = $"Schedule preset selected: {CronCadenceText}";
    }

    [RelayCommand]
    private async Task SetupScheduleAsync()
    {
        if (!_session.IsConnected)
        {
            StatusMessage = "SSH is disconnected. Connect in Overview first.";
            return;
        }

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        IsBusy = true;

        try
        {
            StatusMessage = "Configuring cron schedule with execution history logging...";

            BackupScope scope = SelectedScope switch
            {
                "Folder / Media" => BackupScope.Folder,
                "Full Project" => BackupScope.Full,
                _ => BackupScope.Database
            };

            var result = await _databaseService.SetupScheduledBackupAsync(
                CronSchedule,
                scope,
                DbContainerName,
                DbUser,
                DbPassword,
                DbName,
                SelectedEngine,
                RemoteFolderPath,
                _cts.Token);

            if (result.IsSuccess)
            {
                StatusMessage = $"Cron job installed: '{CronSchedule}' ({SelectedScope}) with run history logging.";
                ActivityRecorded?.Invoke($"Scheduled {SelectedScope} backup ({CronSchedule})", DateTime.Now);
                await LoadCronHistoryAsync();
            }
            else
            {
                StatusMessage = $"Failed to setup schedule: {result.Error}";
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Schedule configuration cancelled.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Schedule Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
