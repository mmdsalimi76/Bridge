using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VpsManager.Core.Models;
using VpsManager.Core.Services;
using VpsManager.Core.Services.Session;

using VpsManager.Core.Services.Locking;
using VpsManager.Core.Services.Logging;

namespace VpsManager.Client.ViewModels;

public enum NavigationTarget
{
    Dashboard,
    Deploy,
    Patch,
    Database,
    Docker,
    Terminal,
    Setup,
    Settings
}

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IActiveProfileContext _profileContext;
    private readonly ConfigService _configService;
    private readonly ISessionCoordinator _session;
    private readonly IOperationLock _operationLock;
    private readonly IAppLogger _logger;
    private int _selectionVersion;

    [ObservableProperty]
    private bool _isOperationLocked;

    [ObservableProperty]
    private string? _activeOperationDescription;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDashboardSelected))]
    [NotifyPropertyChangedFor(nameof(IsDeploySelected))]
    [NotifyPropertyChangedFor(nameof(IsPatchSelected))]
    [NotifyPropertyChangedFor(nameof(IsDatabaseSelected))]
    [NotifyPropertyChangedFor(nameof(IsDockerSelected))]
    [NotifyPropertyChangedFor(nameof(IsTerminalSelected))]
    [NotifyPropertyChangedFor(nameof(IsSetupSelected))]
    [NotifyPropertyChangedFor(nameof(IsSettingsSelected))]
    private ViewModelBase _currentView;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NavigationPaneWidth))]
    private bool _isNavigationPaneOpen;

    public double NavigationPaneWidth => IsNavigationPaneOpen ? 264 : 52;

    // ViewModels for Tab Navigation
    public DashboardViewModel DashboardVM { get; }
    public DeployViewModel DeployVM { get; }
    public PatchViewModel PatchVM { get; }
    public DatabaseViewModel DatabaseVM { get; }
    public DockerViewModel DockerVM { get; }

    public TerminalViewModel TerminalVM { get; }
    public SetupViewModel SetupVM { get; }
    public SettingsViewModel SettingsVM { get; }

    public bool IsDashboardSelected => CurrentView == DashboardVM;
    public bool IsDeploySelected => CurrentView == DeployVM;
    public bool IsPatchSelected => CurrentView == PatchVM;
    public bool IsDatabaseSelected => CurrentView == DatabaseVM;
    public bool IsDockerSelected => CurrentView == DockerVM;
    public bool IsTerminalSelected => CurrentView == TerminalVM;
    public bool IsSetupSelected => CurrentView == SetupVM;
    public bool IsSettingsSelected => CurrentView == SettingsVM;

    public MainWindowViewModel(
        IActiveProfileContext profileContext,
        ConfigService configService,
        ISessionCoordinator session,
        IOperationLock operationLock,
        IAppLogger logger,
        DashboardViewModel dashboardViewModel,
        DeployViewModel deployViewModel,
        PatchViewModel patchViewModel,
        DatabaseViewModel databaseViewModel,
        DockerViewModel dockerViewModel,
        TerminalViewModel terminalViewModel,
        SetupViewModel setupViewModel,
        SettingsViewModel settingsViewModel)
    {
        _profileContext = profileContext ?? throw new ArgumentNullException(nameof(profileContext));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _operationLock = operationLock ?? throw new ArgumentNullException(nameof(operationLock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        DashboardVM = dashboardViewModel ?? throw new ArgumentNullException(nameof(dashboardViewModel));
        DeployVM = deployViewModel ?? throw new ArgumentNullException(nameof(deployViewModel));
        PatchVM = patchViewModel ?? throw new ArgumentNullException(nameof(patchViewModel));
        DatabaseVM = databaseViewModel ?? throw new ArgumentNullException(nameof(databaseViewModel));
        DockerVM = dockerViewModel ?? throw new ArgumentNullException(nameof(dockerViewModel));
        TerminalVM = terminalViewModel ?? throw new ArgumentNullException(nameof(terminalViewModel));
        SetupVM = setupViewModel ?? throw new ArgumentNullException(nameof(setupViewModel));
        SettingsVM = settingsViewModel ?? throw new ArgumentNullException(nameof(settingsViewModel));

        // Operation lock changes update UI and prevent switching
        _operationLock.LockChanged += (_, args) =>
        {
            IsOperationLocked = args.IsLocked;
            ActiveOperationDescription = args.OperationDescription;
            if (args.IsLocked)
            {
                StatusMessage = $"Locked: {args.OperationDescription}";
            }
            else
            {
                StatusMessage = "Ready";
            }
        };

        // Server selection wiring
        DashboardVM.ActiveVpsChanged += OnActiveVpsChanged;
        _profileContext.ActiveProfileChanged += (_, profile) =>
        {
            if (profile != null && _session.ActiveProfile?.Id != profile.Id)
            {
                _ = OnActiveVpsChangedAsync(profile);
            }
        };

        _currentView = DashboardVM;
        _ = RunSafelyAsync(InitializeAsync, "Application initialization failed");
    }

    private async Task InitializeAsync()
    {
        var active = await _profileContext.LoadInitialProfileAsync();
        DashboardVM.SyncProfilesFromContext();
        if (active != null)
        {
            SyncSelectedProfile(active);
            if (_session.HasStoredCredential(active))
            {
                await _session.ConnectAsync(active);
                await DashboardVM.RefreshDashboardAsync();
            }
        }
    }

    private void OnActiveVpsChanged(VpsProfile profile)
    {
        _ = OnActiveVpsChangedAsync(profile);
    }

    /// <summary>
    /// Switches the active server: persists the selection, resets the session and, when a
    /// credential is already stored, connects and refreshes. The session coordinator owns
    /// all connection state, so this method never touches SSH directly.
    /// </summary>
    private async Task OnActiveVpsChangedAsync(VpsProfile profile)
    {
        if (_operationLock.IsLocked)
        {
            StatusMessage = $"Cannot switch VPS: {_operationLock.ActiveOperationDescription} is in progress.";
            _logger.Warn($"Blocked VPS switch to {profile.Name} due to active lock: {_operationLock.ActiveOperationDescription}");
            return;
        }

        var selectionVersion = Interlocked.Increment(ref _selectionVersion);

        try
        {
            DashboardVM.StopLiveRefresh();
            await _session.DisconnectAsync("Switching server…");
            SyncSelectedProfile(profile);

            if (_profileContext.CurrentProfile?.Id != profile.Id)
            {
                await _profileContext.SetActiveProfileAsync(profile);
            }

            if (selectionVersion != _selectionVersion)
            {
                return;
            }

            if (_session.HasStoredCredential(profile))
            {
                await _session.ConnectAsync(profile);
            }
            else
            {
                _session.RequireCredentials("Enter SSH credentials in Overview to connect.");
            }

            if (selectionVersion != _selectionVersion)
            {
                return;
            }

            await DashboardVM.RefreshDashboardAsync();
            if (DashboardVM.IsLiveRefreshEnabled && CurrentView == DashboardVM)
            {
                DashboardVM.StartLiveRefresh();
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error switching server: {ex.Message}";
        }
    }

    private async Task RunSafelyAsync(Func<Task> operation, string errorMessage)
    {
        try
        {
            await operation();
        }
        catch (Exception ex)
        {
            StatusMessage = $"{errorMessage}: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Navigate(string target)
    {
        NavigateTo(ParseNavigationTarget(target));
    }

    private void NavigateTo(NavigationTarget target)
    {
        CurrentView = target switch
        {
            NavigationTarget.Dashboard => DashboardVM,
            NavigationTarget.Deploy => DeployVM,
            NavigationTarget.Patch => PatchVM,
            NavigationTarget.Database => DatabaseVM,
            NavigationTarget.Docker => DockerVM,
            NavigationTarget.Terminal => TerminalVM,
            NavigationTarget.Setup => SetupVM,
            NavigationTarget.Settings => SettingsVM,
            _ => DashboardVM
        };

        if (target != NavigationTarget.Settings)
        {
            SettingsVM.MaskSensitiveFields();
        }

        if (CurrentView == DashboardVM)
        {
            _ = DashboardVM.RefreshDashboardAsync();
            if (DashboardVM.IsLiveRefreshEnabled)
            {
                DashboardVM.StartLiveRefresh();
            }
        }
        else
        {
            DashboardVM.StopLiveRefresh();
        }
    }

    private static NavigationTarget ParseNavigationTarget(string target)
    {
        return target switch
        {
            "Dashboard" => NavigationTarget.Dashboard,
            "Deploy" => NavigationTarget.Deploy,
            "Patch" => NavigationTarget.Patch,
            "Database" => NavigationTarget.Database,
            "Docker" => NavigationTarget.Docker,
            "Terminal" => NavigationTarget.Terminal,
            "Setup" => NavigationTarget.Setup,
            "Settings" => NavigationTarget.Settings,
            _ => NavigationTarget.Dashboard
        };
    }

    [RelayCommand]
    private void ToggleNavigationPane()
    {
        IsNavigationPaneOpen = !IsNavigationPaneOpen;
    }

    [RelayCommand]
    private void SelectVps(VpsProfile? profile)
    {
        if (profile == null)
        {
            return;
        }

        if (_operationLock.IsLocked)
        {
            StatusMessage = $"Cannot switch VPS: {_operationLock.ActiveOperationDescription} is in progress.";
            _logger.Warn($"Blocked VPS switch to {profile.Name} due to active lock: {_operationLock.ActiveOperationDescription}");
            return;
        }

        SyncSelectedProfile(profile);
    }

    private void SyncSelectedProfile(VpsProfile profile)
    {
        var dashProfile = DashboardVM.VpsProfiles.FirstOrDefault(p => p.Id == profile.Id) ?? profile;
        if (DashboardVM.SelectedVps != dashProfile)
        {
            DashboardVM.SelectedVps = dashProfile;
        }

        var settProfile = SettingsVM.VpsProfiles.FirstOrDefault(p => p.Id == profile.Id) ?? profile;
        if (SettingsVM.SelectedProfile != settProfile)
        {
            SettingsVM.SelectedProfile = settProfile;
        }
    }

    [RelayCommand]
    private void AddVps()
    {
        SettingsVM.AddProfileCommand.Execute(null);
        NavigateTo(NavigationTarget.Settings);
    }
}
