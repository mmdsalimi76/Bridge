using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VpsManager.Core.Models;
using VpsManager.Core.Security;
using VpsManager.Core.Services;
using VpsManager.Core.Services.Session;

namespace VpsManager.Client.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly IActiveProfileContext _profileContext;
    private readonly ConfigService _configService;
    private readonly ICredentialStore _credentialStore;
    private readonly ISshServiceFactory _sshServiceFactory;
    private readonly ISessionCoordinator _session;
    private readonly ISyncIgnoreService _syncIgnoreService;
    private bool _isUpdatingForm;

    // ── Multi-VPS Profile List & Filtering ───────────────────
    [ObservableProperty]
    private ObservableCollection<VpsProfile> _vpsProfiles = new();

    [ObservableProperty]
    private ObservableCollection<VpsProfile> _filteredProfiles = new();

    [ObservableProperty]
    private VpsProfile? _selectedProfile;

    [ObservableProperty]
    private string _searchText = string.Empty;

    // ── Form State & Navigation ──────────────────────────────
    [ObservableProperty]
    private bool _isDirty;

    [ObservableProperty]
    private string _activeSection = "General";

    [RelayCommand]
    private void SetActiveSection(string section)
    {
        if (!string.IsNullOrWhiteSpace(section))
        {
            ActiveSection = section;
        }
    }

    partial void OnActiveSectionChanged(string value)
    {
        if (value == "SyncIgnore" && SyncIgnoreTreeNodes.Count == 0 && !string.IsNullOrWhiteSpace(LocalRootPath) && Directory.Exists(LocalRootPath))
        {
            _ = ScanWorkspaceTreeAsync();
        }
    }

    [ObservableProperty]
    private string _statusMessage = "Ready to manage server settings.";

    // ── Profile Identity & Architecture ──────────────────────
    [ObservableProperty]
    private string _profileName = string.Empty;

    [ObservableProperty]
    private HostOS _targetOS = HostOS.Linux;

    [ObservableProperty]
    private bool _isDockerized = true;

    [ObservableProperty]
    private ProjectType _projectType = ProjectType.DockerCompose;

    // ── Remote Access (SSH) ──────────────────────────────────
    [ObservableProperty]
    private string _vpsIp = string.Empty;

    [ObservableProperty]
    private int _sshPort = 22;

    [ObservableProperty]
    private string _vpsUser = "root";

    [ObservableProperty]
    private SshAuthMethod _authMethod = SshAuthMethod.Password;

    public bool IsPasswordAuth
    {
        get => AuthMethod == SshAuthMethod.Password;
        set
        {
            if (value)
            {
                AuthMethod = SshAuthMethod.Password;
            }
        }
    }

    public bool IsPrivateKeyAuth
    {
        get => AuthMethod == SshAuthMethod.PrivateKey;
        set
        {
            if (value)
            {
                AuthMethod = SshAuthMethod.PrivateKey;
            }
        }
    }

    partial void OnAuthMethodChanged(SshAuthMethod value)
    {
        IsDirty = true;
        OnPropertyChanged(nameof(IsPasswordAuth));
        OnPropertyChanged(nameof(IsPrivateKeyAuth));
    }

    [ObservableProperty]
    private string _vpsPassword = string.Empty;

    [ObservableProperty]
    private string _privateKeyPath = string.Empty;

    [ObservableProperty]
    private string _keyPassphrase = string.Empty;

    [ObservableProperty]
    private bool _showPassword;

    [ObservableProperty]
    private bool _showPassphrase;

    // ── Workspaces & Paths ───────────────────────────────────
    [ObservableProperty]
    private string _localRootPath = string.Empty;

    [ObservableProperty]
    private string _remoteProjectPath = "/var/www/my_app";

    [ObservableProperty]
    private string _composeFileName = "docker-compose.yml";

    [ObservableProperty]
    private string _launchCommand = string.Empty;

    // ── Service & Daemon Lifecycle (Non-Dockerized / PM2 / Systemd)
    [ObservableProperty]
    private string _serviceName = string.Empty;

    [ObservableProperty]
    private string _startCommand = string.Empty;

    [ObservableProperty]
    private string _stopCommand = string.Empty;

    [ObservableProperty]
    private string _restartCommand = string.Empty;

    // ── Local Build & Health Checking ────────────────────────
    [ObservableProperty]
    private LocalBuildStrategy _buildStrategy = LocalBuildStrategy.None;

    [ObservableProperty]
    private string _localBuildCommand = string.Empty;

    [ObservableProperty]
    private string _buildArtifactPath = string.Empty;

    [ObservableProperty]
    private HealthCheckType _healthCheckType = HealthCheckType.DockerContainer;

    [ObservableProperty]
    private string _healthCheckUrl = "http://localhost:8080/health";

    [ObservableProperty]
    private int _healthCheckPort = 8080;

    [ObservableProperty]
    private string _healthCheckProcessName = string.Empty;

    [ObservableProperty]
    private string _healthCheckCustomCommand = string.Empty;

    // ── Database Architecture ────────────────────────────────
    [ObservableProperty]
    private DatabaseEngine _dbEngine = DatabaseEngine.PostgreSQL;

    [ObservableProperty]
    private bool _isDbContainerized = true;

    [ObservableProperty]
    private string _dbHost = "localhost";

    [ObservableProperty]
    private int _dbPort = 5432;

    [ObservableProperty]
    private string _dbContainerName = "postgres";

    [ObservableProperty]
    private string _dbName = "app_db";

    [ObservableProperty]
    private string _dbUser = "postgres";

    [ObservableProperty]
    private string _customBackupCommand = string.Empty;

    [ObservableProperty]
    private string _customMigrationCommand = string.Empty;

    // ── Global Sync Exclusions & Workspace Tree ───────────────
    [ObservableProperty]
    private ObservableCollection<string> _globalSyncIgnorePatterns = new();

    [ObservableProperty]
    private string _newIgnorePattern = string.Empty;

    [ObservableProperty]
    private ObservableCollection<FilePatchNode> _syncIgnoreTreeNodes = new();

    [ObservableProperty]
    private bool _isScanningSyncIgnoreTree;

    [ObservableProperty]
    private string _syncIgnoreFilterQuery = string.Empty;

    [ObservableProperty]
    private bool _isRawEditorMode;

    [ObservableProperty]
    private string _rawSyncIgnoreEditorText = string.Empty;

    [ObservableProperty]
    private int _ignoredFilesCount;

    [ObservableProperty]
    private int _ignoredDirsCount;

    [ObservableProperty]
    private bool _hasLocalWorkspace;

    [ObservableProperty]
    private string _syncIgnoreStatusSummary = "Configure global and project-level file sync exclusions.";

    // ── Diagnostics & Real-time Telemetry ────────────────────
    [ObservableProperty]
    private bool _isTestingConnection;

    [ObservableProperty]
    private bool _hasTestDiagnostics;

    [ObservableProperty]
    private bool _testSuccess;

    [ObservableProperty]
    private long _testLatencyMs;

    [ObservableProperty]
    private string _testDiagnosticsSummary = string.Empty;

    // ── Computed Properties ──────────────────────────────────
    public bool IsConnected => _session.IsConnected;
    public string ConnectionStatusText => !IsConnected ? "SSH Disconnected" : $"Connected ({_session.ActiveProfile?.VpsIp}:{_session.ActiveProfile?.SshPort})";
    public string ActiveProfileName => _profileContext.CurrentProfile?.Name ?? "None";
    public bool IsActiveProfile => SelectedProfile != null && _profileContext.CurrentProfile?.Id == SelectedProfile.Id;

    public ObservableCollection<HostOS> AvailableHostOS { get; } = new(Enum.GetValues<HostOS>());
    public ObservableCollection<ProjectType> AvailableProjectTypes { get; } = new(Enum.GetValues<ProjectType>());
    public ObservableCollection<SshAuthMethod> AvailableAuthMethods { get; } = new(Enum.GetValues<SshAuthMethod>());
    public ObservableCollection<DatabaseEngine> AvailableDbEngines { get; } = new(Enum.GetValues<DatabaseEngine>());
    public ObservableCollection<LocalBuildStrategy> AvailableBuildStrategies { get; } = new(Enum.GetValues<LocalBuildStrategy>());
    public ObservableCollection<HealthCheckType> AvailableHealthCheckTypes { get; } = new(Enum.GetValues<HealthCheckType>());

    // ── Events for View Interop ──────────────────────────────
    public event Func<string, Task<string?>>? BrowseFolderRequested;
    public event Func<string, string[]?, Task<string?>>? BrowseFileRequested;
    public event Action? ProfilesChanged;

    public SettingsViewModel(
        IActiveProfileContext profileContext,
        ConfigService configService,
        ICredentialStore credentialStore,
        ISshServiceFactory sshServiceFactory,
        ISessionCoordinator session,
        ISyncIgnoreService syncIgnoreService)
    {
        _profileContext = profileContext ?? throw new ArgumentNullException(nameof(profileContext));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _credentialStore = credentialStore ?? throw new ArgumentNullException(nameof(credentialStore));
        _sshServiceFactory = sshServiceFactory ?? throw new ArgumentNullException(nameof(sshServiceFactory));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _syncIgnoreService = syncIgnoreService ?? throw new ArgumentNullException(nameof(syncIgnoreService));

        _session.StateChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(ConnectionStatusText));
        };

        _profileContext.ProfilesChanged += (_, _) => SyncProfilesFromContext();
        _profileContext.ActiveProfileChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(IsActiveProfile));
            OnPropertyChanged(nameof(ActiveProfileName));
            ActivateProfileCommand.NotifyCanExecuteChanged();
        };

        SyncProfilesFromContext();
        _ = LoadGlobalSyncIgnorePatternsAsync();
    }

    public void SyncProfilesFromContext()
    {
        VpsProfiles.Clear();
        foreach (var p in _profileContext.Profiles)
        {
            VpsProfiles.Add(p);
        }

        UpdateFilteredProfiles();
        SelectedProfile = _profileContext.CurrentProfile ?? VpsProfiles.FirstOrDefault();
    }

    partial void OnSearchTextChanged(string value)
    {
        UpdateFilteredProfiles();
    }

    private void UpdateFilteredProfiles()
    {
        FilteredProfiles.Clear();
        var query = SearchText.Trim();
        foreach (var p in VpsProfiles)
        {
            if (string.IsNullOrEmpty(query) ||
                p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                p.VpsIp.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                FilteredProfiles.Add(p);
            }
        }
    }

    partial void OnSelectedProfileChanged(VpsProfile? value)
    {
        if (value != null)
        {
            LoadProfileIntoForm(value);
        }
        OnPropertyChanged(nameof(IsActiveProfile));
        ActivateProfileCommand.NotifyCanExecuteChanged();
    }

    private void LoadProfileIntoForm(VpsProfile profile)
    {
        _isUpdatingForm = true;
        try
        {
            ProfileName = profile.Name;
            TargetOS = profile.TargetOS;
            IsDockerized = profile.IsDockerized;
            ProjectType = profile.ProjectType;

            VpsIp = profile.VpsIp;
            SshPort = profile.SshPort;
            VpsUser = profile.VpsUser;
            AuthMethod = profile.AuthMethod;
            PrivateKeyPath = profile.PrivateKeyPath;

            LocalRootPath = profile.LocalRootPath;
            RemoteProjectPath = profile.RemoteProjectPath;
            ComposeFileName = profile.ComposeFileName;
            LaunchCommand = profile.LaunchCommand;

            ServiceName = profile.ServiceName;
            StartCommand = profile.StartCommand;
            StopCommand = profile.StopCommand;
            RestartCommand = profile.RestartCommand;

            BuildStrategy = profile.BuildStrategy;
            LocalBuildCommand = profile.LocalBuildCommand;
            BuildArtifactPath = profile.BuildArtifactPath;

            HealthCheckType = profile.HealthCheckType;
            HealthCheckUrl = profile.HealthCheckUrl;
            HealthCheckPort = profile.HealthCheckPort;
            HealthCheckProcessName = profile.HealthCheckProcessName;
            HealthCheckCustomCommand = profile.HealthCheckCustomCommand;

            DbEngine = profile.DbEngine;
            IsDbContainerized = profile.IsDbContainerized;
            DbHost = profile.DbHost;
            DbPort = profile.DbPort;
            DbContainerName = profile.DbContainerName;
            DbName = profile.DbName;
            DbUser = profile.DbUser;
            CustomBackupCommand = profile.CustomBackupCommand;
            CustomMigrationCommand = profile.CustomMigrationCommand;

            VpsPassword = _profileContext.GetProfilePassword(profile.Id) ?? string.Empty;
            KeyPassphrase = _profileContext.GetProfileKeyPassphrase(profile.Id) ?? string.Empty;

            HasTestDiagnostics = false;
            IsDirty = false;
            OnPropertyChanged(nameof(IsActiveProfile));

            if (!string.IsNullOrWhiteSpace(profile.LocalRootPath) && Directory.Exists(profile.LocalRootPath))
            {
                HasLocalWorkspace = true;
                _ = ScanWorkspaceTreeAsync();
            }
            else
            {
                HasLocalWorkspace = false;
                SyncIgnoreTreeNodes.Clear();
                SyncIgnoreStatusSummary = "No valid local repository folder configured. Set it in Deployment Targets & Paths.";
            }
        }
        finally
        {
            _isUpdatingForm = false;
        }
    }

    private void SaveFormIntoProfile(VpsProfile profile)
    {
        profile.Name = ProfileName.Trim();
        profile.TargetOS = TargetOS;
        profile.IsDockerized = IsDockerized;
        profile.ProjectType = ProjectType;

        profile.VpsIp = VpsIp.Trim();
        profile.SshPort = SshPort;
        profile.VpsUser = VpsUser.Trim();
        profile.AuthMethod = AuthMethod;
        profile.PrivateKeyPath = PrivateKeyPath.Trim();

        profile.LocalRootPath = LocalRootPath.Trim();
        profile.RemoteProjectPath = RemoteProjectPath.Trim();
        profile.ComposeFileName = ComposeFileName.Trim();
        profile.LaunchCommand = LaunchCommand.Trim();

        profile.ServiceName = ServiceName.Trim();
        profile.StartCommand = StartCommand.Trim();
        profile.StopCommand = StopCommand.Trim();
        profile.RestartCommand = RestartCommand.Trim();

        profile.BuildStrategy = BuildStrategy;
        profile.LocalBuildCommand = LocalBuildCommand.Trim();
        profile.BuildArtifactPath = BuildArtifactPath.Trim();

        profile.HealthCheckType = HealthCheckType;
        profile.HealthCheckUrl = HealthCheckUrl.Trim();
        profile.HealthCheckPort = HealthCheckPort;
        profile.HealthCheckProcessName = HealthCheckProcessName.Trim();
        profile.HealthCheckCustomCommand = HealthCheckCustomCommand.Trim();

        profile.DbEngine = DbEngine;
        profile.IsDbContainerized = IsDbContainerized;
        profile.DbHost = DbHost.Trim();
        profile.DbPort = DbPort;
        profile.DbContainerName = DbContainerName.Trim();
        profile.DbName = DbName.Trim();
        profile.DbUser = DbUser.Trim();
        profile.CustomBackupCommand = CustomBackupCommand.Trim();
        profile.CustomMigrationCommand = CustomMigrationCommand.Trim();
    }

    partial void OnTargetOSChanged(HostOS value)
    {
        if (value == HostOS.Windows && ProjectType == ProjectType.NativeLinuxService)
        {
            ProjectType = ProjectType.WindowsApplication;
        }
        else if (value == HostOS.Linux && ProjectType == ProjectType.WindowsApplication)
        {
            ProjectType = ProjectType.NativeLinuxService;
        }
    }

    partial void OnIsDockerizedChanged(bool value)
    {
        if (value)
        {
            ProjectType = ProjectType.DockerCompose;
        }
        else if (ProjectType == ProjectType.DockerCompose)
        {
            ProjectType = TargetOS == HostOS.Windows ? ProjectType.WindowsApplication : ProjectType.NativeLinuxService;
        }
    }

    partial void OnLocalRootPathChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value) && Directory.Exists(value))
        {
            HasLocalWorkspace = true;
            _ = ScanWorkspaceTreeAsync();
        }
        else
        {
            HasLocalWorkspace = false;
        }
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (!_isUpdatingForm &&
            e.PropertyName != nameof(IsDirty) &&
            e.PropertyName != nameof(StatusMessage) &&
            e.PropertyName != nameof(IsTestingConnection) &&
            e.PropertyName != nameof(SearchText) &&
            e.PropertyName != nameof(SelectedProfile) &&
            e.PropertyName != nameof(ActiveSection) &&
            e.PropertyName != nameof(HasTestDiagnostics) &&
            e.PropertyName != nameof(TestSuccess) &&
            e.PropertyName != nameof(TestLatencyMs) &&
            e.PropertyName != nameof(TestDiagnosticsSummary) &&
            e.PropertyName != nameof(ShowPassword) &&
            e.PropertyName != nameof(ShowPassphrase) &&
            e.PropertyName != nameof(IsActiveProfile) &&
            e.PropertyName != nameof(IsConnected) &&
            e.PropertyName != nameof(ConnectionStatusText) &&
            e.PropertyName != nameof(ActiveProfileName) &&
            e.PropertyName != nameof(SyncIgnoreFilterQuery) &&
            e.PropertyName != nameof(IsScanningSyncIgnoreTree) &&
            e.PropertyName != nameof(IsRawEditorMode) &&
            e.PropertyName != nameof(IgnoredFilesCount) &&
            e.PropertyName != nameof(IgnoredDirsCount) &&
            e.PropertyName != nameof(HasLocalWorkspace) &&
            e.PropertyName != nameof(SyncIgnoreStatusSummary))
        {
            IsDirty = true;
        }
    }

    [RelayCommand]
    private void AddProfile()
    {
        var count = VpsProfiles.Count + 1;
        var newProfile = new VpsProfile
        {
            Name = $"VPS Server {count}",
            VpsIp = "192.168.1.100",
            SshPort = 22,
            VpsUser = "root",
            AuthMethod = SshAuthMethod.Password,
            RemoteProjectPath = $"/var/www/vps_{count}",
            TargetOS = HostOS.Linux,
            IsDockerized = true,
            ProjectType = ProjectType.DockerCompose
        };

        VpsProfiles.Add(newProfile);
        UpdateFilteredProfiles();
        SelectedProfile = newProfile;
        IsDirty = true;
        StatusMessage = $"New server profile '{newProfile.Name}' created. Configure details and save.";
    }

    [RelayCommand]
    private async Task RemoveProfileAsync()
    {
        if (SelectedProfile == null || VpsProfiles.Count <= 1)
        {
            StatusMessage = "Cannot delete the last remaining VPS profile.";
            return;
        }

        var removed = SelectedProfile;
        var idx = VpsProfiles.IndexOf(removed);
        VpsProfiles.Remove(removed);
        UpdateFilteredProfiles();

        await _profileContext.DeleteProfileAsync(removed.Id);

        SelectedProfile = VpsProfiles[Math.Min(idx, VpsProfiles.Count - 1)];
        StatusMessage = $"Profile '{removed.Name}' deleted.";
        ProfilesChanged?.Invoke();
    }

    [RelayCommand]
    private async Task DuplicateProfileAsync()
    {
        if (SelectedProfile == null) return;

        SaveFormIntoProfile(SelectedProfile);
        var clone = await _profileContext.DuplicateProfileAsync(SelectedProfile);

        VpsProfiles.Add(clone);
        UpdateFilteredProfiles();
        SelectedProfile = clone;
        StatusMessage = $"Profile duplicated as '{clone.Name}'.";
        ProfilesChanged?.Invoke();
    }

    [RelayCommand]
    private async Task ActivateProfileAsync()
    {
        if (SelectedProfile == null) return;

        if (IsDirty)
        {
            await SaveSettingsAsync();
        }

        await _profileContext.SetActiveProfileAsync(SelectedProfile);
        OnPropertyChanged(nameof(IsActiveProfile));
        OnPropertyChanged(nameof(ActiveProfileName));
        ActivateProfileCommand.NotifyCanExecuteChanged();
        StatusMessage = $"'{SelectedProfile.Name}' is now the active server context across all tabs.";
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        try
        {
            if (SelectedProfile == null)
            {
                StatusMessage = "No profile selected.";
                return;
            }

            StatusMessage = "Saving settings...";
            SaveFormIntoProfile(SelectedProfile);

            var passwordToSave = AuthMethod == SshAuthMethod.Password ? VpsPassword : null;
            var passphraseToSave = AuthMethod == SshAuthMethod.PrivateKey ? KeyPassphrase : null;

            await _profileContext.SaveProfileAsync(SelectedProfile, passwordToSave, passphraseToSave);
            await _profileContext.SaveGlobalSyncIgnorePatternsAsync(GlobalSyncIgnorePatterns);

            UpdateFilteredProfiles();
            IsDirty = false;
            OnPropertyChanged(nameof(IsActiveProfile));
            ProfilesChanged?.Invoke();
            StatusMessage = $"Settings for '{SelectedProfile.Name}' saved successfully! ✅";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Save Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SaveAndTestAsync()
    {
        await SaveSettingsAsync();
        await TestConnectionAsync();
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        if (IsTestingConnection) return;
        if (string.IsNullOrWhiteSpace(VpsIp))
        {
            StatusMessage = "Specify an IP or hostname before testing connection.";
            return;
        }

        try
        {
            IsTestingConnection = true;
            StatusMessage = $"Probing SSH connection to {VpsIp}:{SshPort}...";
            var sw = Stopwatch.StartNew();

            var sshConfig = new SshConnectionConfig
            {
                Host = VpsIp.Trim(),
                Port = SshPort,
                Username = VpsUser.Trim(),
                Password = AuthMethod == SshAuthMethod.Password ? VpsPassword : null,
                PrivateKeyPath = AuthMethod == SshAuthMethod.PrivateKey ? PrivateKeyPath.Trim() : null,
                Passphrase = AuthMethod == SshAuthMethod.PrivateKey ? KeyPassphrase : null
            };

            using var sshService = _sshServiceFactory.Create();
            await sshService.ConnectAsync(sshConfig);
            sw.Stop();

            if (sshService.IsConnected)
            {
                TestLatencyMs = sw.ElapsedMilliseconds;
                var probeCmd = TargetOS == HostOS.Windows
                    ? "cmd /c ver 2>nul || echo Connected (Windows)"
                    : "uname -s -r -m 2>/dev/null || echo 'Connected'";
                var probe = await sshService.ExecuteCommandAsync(probeCmd);
                var remoteInfo = probe.IsSuccess ? probe.Output.Trim() : "Connected";

                HasTestDiagnostics = true;
                TestSuccess = true;
                TestDiagnosticsSummary = $"✔ SSH Handshake successful ({TestLatencyMs} ms) | Host: {remoteInfo}";
                StatusMessage = $"SSH Connected Successfully ({TestLatencyMs}ms)! ✅";
            }
            else
            {
                HasTestDiagnostics = true;
                TestSuccess = false;
                TestDiagnosticsSummary = "❌ SSH Connection failed. Verify host, port, credentials, and firewall.";
                StatusMessage = "SSH Connection Failed ❌";
            }
        }
        catch (Exception ex)
        {
            HasTestDiagnostics = true;
            TestSuccess = false;
            TestDiagnosticsSummary = $"❌ Connection Error: {ex.Message}";
            StatusMessage = $"Connection Test Error: {ex.Message} ❌";
        }
        finally
        {
            IsTestingConnection = false;
        }
    }

    // ── Global Sync Ignore Handlers ──────────────────────────
    private async Task LoadGlobalSyncIgnorePatternsAsync()
    {
        try
        {
            var patterns = await _profileContext.GetGlobalSyncIgnorePatternsAsync();
            GlobalSyncIgnorePatterns.Clear();
            foreach (var p in patterns)
            {
                GlobalSyncIgnorePatterns.Add(p);
            }
        }
        catch
        {
            ResetDefaultIgnorePatterns();
        }
    }

    [RelayCommand]
    private void AddIgnorePattern()
    {
        var pat = NewIgnorePattern.Trim();
        if (!string.IsNullOrWhiteSpace(pat) && !GlobalSyncIgnorePatterns.Contains(pat))
        {
            GlobalSyncIgnorePatterns.Add(pat);
            NewIgnorePattern = string.Empty;
            IsDirty = true;
        }
    }

    [RelayCommand]
    private void RemoveIgnorePattern(string pattern)
    {
        if (GlobalSyncIgnorePatterns.Remove(pattern))
        {
            IsDirty = true;
        }
    }

    [RelayCommand]
    private void ResetDefaultIgnorePatterns()
    {
        GlobalSyncIgnorePatterns.Clear();
        foreach (var p in new[] { ".git", "node_modules", ".env", "*.pyc", "__pycache__", "bin", "obj", "uploads/", "media/", "*.pem", "*.key" })
        {
            GlobalSyncIgnorePatterns.Add(p);
        }
        IsDirty = true;
        StatusMessage = "Global sync ignore patterns reset to defaults.";
    }

    // ── Selectable Workspace Sync Ignore Tree ─────────────────
    [RelayCommand]
    public async Task ScanWorkspaceTreeAsync()
    {
        if (string.IsNullOrWhiteSpace(LocalRootPath) || !Directory.Exists(LocalRootPath))
        {
            HasLocalWorkspace = false;
            SyncIgnoreStatusSummary = "No valid local repository folder configured. Set it in Deployment Targets & Paths.";
            SyncIgnoreTreeNodes.Clear();
            return;
        }

        HasLocalWorkspace = true;
        IsScanningSyncIgnoreTree = true;
        SyncIgnoreStatusSummary = "Scanning workspace directory hierarchy...";

        var syncIgnorePath = Path.Combine(LocalRootPath, ".syncignore");
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
            SyncIgnoreStatusSummary = $"Workspace tree loaded ({IgnoredFilesCount} files, {IgnoredDirsCount} folders excluded).";
        }
        catch (Exception ex)
        {
            SyncIgnoreStatusSummary = $"Failed to scan directory tree: {ex.Message}";
        }
        finally
        {
            IsScanningSyncIgnoreTree = false;
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

    [RelayCommand]
    private void ToggleRawEditorMode()
    {
        IsRawEditorMode = !IsRawEditorMode;
        if (IsRawEditorMode)
        {
            var ignoredPatterns = new List<string>();
            CollectIgnoredPatterns(SyncIgnoreTreeNodes, ignoredPatterns);
            if (ignoredPatterns.Count > 0)
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("# ===================================================================");
                sb.AppendLine("# Bridge Sync Ignore Configuration (.syncignore)");
                sb.AppendLine($"# Generated on {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
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
    public async Task SaveSyncIgnoreFileAsync()
    {
        if (string.IsNullOrWhiteSpace(LocalRootPath) || !Directory.Exists(LocalRootPath))
        {
            StatusMessage = "Cannot save .syncignore: local folder is not configured.";
            return;
        }

        try
        {
            string finalContent;
            if (IsRawEditorMode)
            {
                finalContent = RawSyncIgnoreEditorText;
            }
            else
            {
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

            var analysis = _syncIgnoreService.AnalyzeProjectWorkspace(LocalRootPath);
            foreach (var pat in analysis.ActivePatterns)
            {
                if (!GlobalSyncIgnorePatterns.Contains(pat))
                {
                    GlobalSyncIgnorePatterns.Add(pat);
                }
            }

            SyncIgnoreStatusSummary = $"Saved .syncignore with {analysis.ActivePatterns.Count} active rules.";
            StatusMessage = $"Saved .syncignore to {LocalRootPath} ✅";
        }
        catch (Exception ex)
        {
            SyncIgnoreStatusSummary = $"Failed to save .syncignore: {ex.Message}";
            StatusMessage = $"Error saving .syncignore: {ex.Message}";
        }
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
                patterns.Add(node.IsDirectory ? (node.RelativePath.EndsWith('/') ? node.RelativePath : node.RelativePath + "/") : node.RelativePath);
            }
            else if (node.Children.Count > 0)
            {
                CollectIgnoredPatterns(node.Children, patterns);
            }
        }
    }

    // ── Dialog & Picker Triggers ─────────────────────────────
    [RelayCommand]
    private async Task BrowseLocalPathAsync()
    {
        if (BrowseFolderRequested != null)
        {
            var result = await BrowseFolderRequested("Select Local Project Root Folder");
            if (!string.IsNullOrEmpty(result))
            {
                LocalRootPath = result;
            }
        }
    }

    [RelayCommand]
    private void ResetComposeFileName()
    {
        ComposeFileName = "docker-compose.yml";
        StatusMessage = "Compose file set to default 'docker-compose.yml'.";
    }

    [RelayCommand]
    private async Task BrowsePrivateKeyFileAsync()
    {
        if (BrowseFileRequested != null)
        {
            var result = await BrowseFileRequested("Select SSH Private Key", new[] { "*.pem", "*.key", "id_*", "*", "*.*" });
            if (!string.IsNullOrEmpty(result))
            {
                PrivateKeyPath = result;
            }
        }
    }

    [RelayCommand]
    private async Task BrowseArtifactPathAsync()
    {
        if (BrowseFolderRequested != null)
        {
            var result = await BrowseFolderRequested("Select Local Build Artifact Directory");
            if (!string.IsNullOrEmpty(result))
            {
                BuildArtifactPath = result;
            }
        }
    }

    [RelayCommand]
    private void TogglePasswordVisibility() => ShowPassword = !ShowPassword;

    [RelayCommand]
    private void TogglePassphraseVisibility() => ShowPassphrase = !ShowPassphrase;


    public void MaskSensitiveFields()
    {
        ShowPassword = false;
        ShowPassphrase = false;
    }
}
