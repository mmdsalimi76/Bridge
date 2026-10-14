using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

public enum StepExecutionStatus
{
    Idle,
    Running,
    Success,
    Failed,
    Skipped
}

public partial class SetupViewModel : ViewModelBase
{
    private readonly IActiveProfileContext _profileContext;
    private readonly ISessionCoordinator _session;
    private readonly ISshService _sshService;
    private readonly ISetupProvisioningService _provisioningService;
    private readonly IOperationLock _operationLock;
    private CancellationTokenSource? _cancellationTokenSource;
    private VpsProfile? _currentProfile;

    public event Func<string, Task>? ClipboardRequested;

    // ── Telemetry & Connection ─────────────────────────────────
    public string ProfileName => _currentProfile?.Name ?? _session.ActiveProfile?.Name ?? "No profile selected";
    public string ServerAddress => _currentProfile != null ? $"{_currentProfile.VpsIp}:{_currentProfile.SshPort}" : _session.ActiveProfile != null ? $"{_session.ActiveProfile.VpsIp}:{_session.ActiveProfile.SshPort}" : "Not configured";
    public HostOS TargetOS => _currentProfile?.TargetOS ?? _session.ActiveProfile?.TargetOS ?? HostOS.Linux;
    public bool IsWindowsTarget => TargetOS == HostOS.Windows;
    public bool IsConnected => _session.IsConnected;
    public string ConnectionStatusText => !IsConnected ? "SSH Disconnected" : $"Connected to {ServerAddress}";
    public string ProvisioningStateBadgeText => IsRunning ? "Provisioning..." : "Ready";

    // ── Preflight Telemetry Data ───────────────────────────────
    [ObservableProperty]
    private bool _hasPreflightData;

    [ObservableProperty]
    private string _detectedOs = string.Empty;

    [ObservableProperty]
    private string _detectedKernel = string.Empty;

    [ObservableProperty]
    private string _detectedArch = string.Empty;

    [ObservableProperty]
    private string _detectedMemory = string.Empty;

    [ObservableProperty]
    private string _detectedDisk = string.Empty;

    [ObservableProperty]
    private string _detectedUptime = string.Empty;

    // ── Module Selection Properties ────────────────────────────
    [ObservableProperty]
    private bool _updateSystem = true;

    [ObservableProperty]
    private bool _installEssentials = true;

    [ObservableProperty]
    private bool _installDocker = true;

    [ObservableProperty]
    private bool _configureFirewall = false;

    [ObservableProperty]
    private bool _installFail2Ban = false;

    [ObservableProperty]
    private bool _enableUnattendedUpgrades = true;

    [ObservableProperty]
    private bool _hardenSshSecurity = false;

    [ObservableProperty]
    private bool _createSwap = false;

    [ObservableProperty]
    private bool _prepareProjectDirectory = true;

    [ObservableProperty]
    private bool _installNodeJs = false;

    [ObservableProperty]
    private bool _installNginx = false;

    // ── Module Execution Statuses ──────────────────────────────
    [ObservableProperty]
    private StepExecutionStatus _updateSystemStatus = StepExecutionStatus.Idle;

    [ObservableProperty]
    private StepExecutionStatus _installEssentialsStatus = StepExecutionStatus.Idle;

    [ObservableProperty]
    private StepExecutionStatus _installDockerStatus = StepExecutionStatus.Idle;

    [ObservableProperty]
    private StepExecutionStatus _configureFirewallStatus = StepExecutionStatus.Idle;

    [ObservableProperty]
    private StepExecutionStatus _installFail2BanStatus = StepExecutionStatus.Idle;

    [ObservableProperty]
    private StepExecutionStatus _enableUnattendedUpgradesStatus = StepExecutionStatus.Idle;

    [ObservableProperty]
    private StepExecutionStatus _hardenSshSecurityStatus = StepExecutionStatus.Idle;

    [ObservableProperty]
    private StepExecutionStatus _createSwapStatus = StepExecutionStatus.Idle;

    [ObservableProperty]
    private StepExecutionStatus _prepareProjectDirectoryStatus = StepExecutionStatus.Idle;

    [ObservableProperty]
    private StepExecutionStatus _installNodeJsStatus = StepExecutionStatus.Idle;

    [ObservableProperty]
    private StepExecutionStatus _installNginxStatus = StepExecutionStatus.Idle;

    // ── Configuration Parameters ───────────────────────────────
    [ObservableProperty]
    private string _dockerSource = "Docker official repository";

    [ObservableProperty]
    private string _dockerRepositoryUrl = "https://download.docker.com/linux/ubuntu";

    [ObservableProperty]
    private string _packageMirrorUrl = "https://archive.ubuntu.com/ubuntu";

    [ObservableProperty]
    private bool _isCustomDockerSource;

    [ObservableProperty]
    private int _swapSizeGb = 2;

    [ObservableProperty]
    private string _projectDirectory = "/var/www/my_app";

    // ── Execution Progress & Terminal View ─────────────────────
    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string _statusMessage = "Ready to configure the selected VPS.";

    [ObservableProperty]
    private string _terminalOutput = "Select a VPS tab, review the setup actions, and run the setup when ready.\n";

    [ObservableProperty]
    private int _currentStep;

    [ObservableProperty]
    private int _totalSteps;

    [ObservableProperty]
    private double _progressPercentage;

    [ObservableProperty]
    private bool _autoScroll = true;

    [ObservableProperty]
    private bool _wordWrap = true;

    [ObservableProperty]
    private double _terminalFontSize = 12.0;

    public ObservableCollection<string> DockerSources { get; } = new()
    {
        "Docker official repository",
        "Ubuntu packages (docker.io)",
        "Custom Docker repository"
    };

    public SetupViewModel(
        IActiveProfileContext profileContext,
        ISessionCoordinator session,
        ISshService sshService,
        ISetupProvisioningService provisioningService,
        IOperationLock operationLock)
    {
        _profileContext = profileContext ?? throw new ArgumentNullException(nameof(profileContext));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _sshService = sshService ?? throw new ArgumentNullException(nameof(sshService));
        _provisioningService = provisioningService ?? throw new ArgumentNullException(nameof(provisioningService));
        _operationLock = operationLock ?? throw new ArgumentNullException(nameof(operationLock));
        IsCustomDockerSource = DockerSource == "Custom Docker repository";

        _session.StateChanged += (_, e) =>
        {
            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(ConnectionStatusText));
            OnPropertyChanged(nameof(ProfileName));
            OnPropertyChanged(nameof(ServerAddress));
            OnPropertyChanged(nameof(TargetOS));
            OnPropertyChanged(nameof(IsWindowsTarget));

            if (e.State != SessionState.Connected && IsRunning)
            {
                Cancel();
                StatusMessage = $"SSH {e.StatusText}. Provisioning halted.";
            }
            else if (IsConnected)
            {
                StatusMessage = $"Ready to configure {ProfileName}";
            }
            else
            {
                StatusMessage = "SSH disconnected. Connect in Overview first.";
            }
        };

        _profileContext.ActiveProfileChanged += (_, profile) =>
        {
            if (profile != null)
            {
                _currentProfile = profile;
                OnPropertyChanged(nameof(ProfileName));
                OnPropertyChanged(nameof(ServerAddress));
                OnPropertyChanged(nameof(IsConnected));
                OnPropertyChanged(nameof(ConnectionStatusText));
                OnPropertyChanged(nameof(TargetOS));
                OnPropertyChanged(nameof(IsWindowsTarget));
                if (!string.IsNullOrWhiteSpace(profile.RemoteProjectPath))
                {
                    ProjectDirectory = profile.RemoteProjectPath;
                }
            }
        };

        if (_profileContext.CurrentProfile is { } initial)
        {
            _currentProfile = initial;
            if (!string.IsNullOrWhiteSpace(initial.RemoteProjectPath))
            {
                ProjectDirectory = initial.RemoteProjectPath;
            }
        }
    }

    partial void OnDockerSourceChanged(string value)
    {
        IsCustomDockerSource = value == "Custom Docker repository";
    }

    [RelayCommand]
    private async Task RunSelectedAsync()
    {
        if (IsRunning)
        {
            return;
        }

        var steps = BuildSelectedSteps().ToList();
        if (steps.Count == 0)
        {
            StatusMessage = "Select at least one setup module.";
            return;
        }

        if (!_session.IsConnected)
        {
            StatusMessage = "The selected VPS is not connected. Connect in Overview first.";
            return;
        }

        if (!_operationLock.TryAcquireLock($"Server Provisioning on {ProfileName}", out var lockHandle))
        {
            StatusMessage = $"Cannot provision: {_operationLock.ActiveOperationDescription} is in progress.";
            return;
        }

        ResetAllStepStatuses();

        _cancellationTokenSource = new CancellationTokenSource();
        IsRunning = true;
        OnPropertyChanged(nameof(ProvisioningStateBadgeText));
        TerminalOutput = string.Empty;
        if (IsWindowsTarget)
        {
            AppendOutput("⚠️ Warning: Active profile target OS is Windows. Automated APT provisioning modules (update, ufw, fail2ban) are designed for Debian/Ubuntu Linux.\n");
        }
        TotalSteps = steps.Count;
        CurrentStep = 0;
        ProgressPercentage = 0;
        StatusMessage = $"Executing {steps.Count} setup module(s)...";

        try
        {
            for (var index = 0; index < steps.Count; index++)
            {
                _cancellationTokenSource.Token.ThrowIfCancellationRequested();
                var step = steps[index];
                CurrentStep = index + 1;
                ProgressPercentage = (double)(CurrentStep - 1) / TotalSteps * 100;
                StatusMessage = $"Step {CurrentStep} of {TotalSteps}: {step.Name}";
                step.SetStatus(StepExecutionStatus.Running);

                AppendOutput($"\n══════════════════════════════════════════════════════════");
                AppendOutput($"▶ [{CurrentStep}/{TotalSteps}] {step.Name}");
                AppendOutput($"══════════════════════════════════════════════════════════");

                var result = await _provisioningService.ExecuteStepAsync(step.Definition, _cancellationTokenSource.Token);
                AppendOutput(result.Output);
                AppendOutput(result.Error);

                if (result.ExitCode != 0)
                {
                    step.SetStatus(StepExecutionStatus.Failed);
                    StatusMessage = $"Stopped: {step.Name} failed with exit code {result.ExitCode}.";
                    AppendOutput($"\n❌ Step failed with exit code {result.ExitCode}.");
                    return;
                }

                step.SetStatus(StepExecutionStatus.Success);
                ProgressPercentage = (double)CurrentStep / TotalSteps * 100;
            }

            StatusMessage = "Server provisioning completed successfully.";
            AppendOutput("\n✔ All selected modules configured successfully.");
            ProgressPercentage = 100;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Provisioning cancelled by user.";
            AppendOutput("\n⚠ Provisioning cancelled by user.");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Provisioning failed: {ex.Message}";
            AppendOutput($"\n❌ ERROR: {ex.Message}");
        }
        finally
        {
            lockHandle?.Dispose();
            IsRunning = false;
            OnPropertyChanged(nameof(ProvisioningStateBadgeText));
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _cancellationTokenSource?.Cancel();
        StatusMessage = "Cancelling provisioning...";
    }

    [RelayCommand]
    private void ClearLog()
    {
        TerminalOutput = string.Empty;
        StatusMessage = "Output log cleared.";
    }

    [RelayCommand]
    private async Task CopyLogAsync()
    {
        if (ClipboardRequested != null && !string.IsNullOrEmpty(TerminalOutput))
        {
            await ClipboardRequested(TerminalOutput);
            StatusMessage = "Log output copied to clipboard.";
        }
    }

    [RelayCommand]
    private void ToggleAutoScroll()
    {
        AutoScroll = !AutoScroll;
    }

    [RelayCommand]
    private void ToggleWordWrap()
    {
        WordWrap = !WordWrap;
    }

    [RelayCommand]
    private void SelectAll()
    {
        UpdateSystem = true;
        InstallEssentials = true;
        InstallDocker = true;
        ConfigureFirewall = true;
        InstallFail2Ban = true;
        EnableUnattendedUpgrades = true;
        HardenSshSecurity = true;
        CreateSwap = true;
        PrepareProjectDirectory = true;
        InstallNodeJs = true;
        InstallNginx = true;
    }

    [RelayCommand]
    private void ClearAll()
    {
        UpdateSystem = false;
        InstallEssentials = false;
        InstallDocker = false;
        ConfigureFirewall = false;
        InstallFail2Ban = false;
        EnableUnattendedUpgrades = false;
        HardenSshSecurity = false;
        CreateSwap = false;
        PrepareProjectDirectory = false;
        InstallNodeJs = false;
        InstallNginx = false;
        ResetAllStepStatuses();
    }

    [RelayCommand]
    private void ApplyFullStackPreset()
    {
        UpdateSystem = true;
        InstallEssentials = true;
        InstallDocker = true;
        DockerSource = "Docker official repository";
        ConfigureFirewall = true;
        InstallFail2Ban = true;
        EnableUnattendedUpgrades = true;
        HardenSshSecurity = true;
        CreateSwap = true;
        SwapSizeGb = 2;
        PrepareProjectDirectory = true;
        InstallNodeJs = false;
        InstallNginx = false;
        ResetAllStepStatuses();
        StatusMessage = "Applied 'Full Web Stack' preset.";
    }

    [RelayCommand]
    private void ApplyDockerPreset()
    {
        UpdateSystem = true;
        InstallEssentials = true;
        InstallDocker = true;
        DockerSource = "Docker official repository";
        ConfigureFirewall = true;
        InstallFail2Ban = false;
        EnableUnattendedUpgrades = true;
        HardenSshSecurity = false;
        CreateSwap = true;
        SwapSizeGb = 2;
        PrepareProjectDirectory = false;
        InstallNodeJs = false;
        InstallNginx = false;
        ResetAllStepStatuses();
        StatusMessage = "Applied 'Docker Host' preset.";
    }

    [RelayCommand]
    private void ApplyHardenedPreset()
    {
        UpdateSystem = true;
        InstallEssentials = true;
        InstallDocker = false;
        ConfigureFirewall = true;
        InstallFail2Ban = true;
        EnableUnattendedUpgrades = true;
        HardenSshSecurity = true;
        CreateSwap = false;
        PrepareProjectDirectory = false;
        InstallNodeJs = false;
        InstallNginx = false;
        ResetAllStepStatuses();
        StatusMessage = "Applied 'Hardened Baseline' preset.";
    }

    [RelayCommand]
    private async Task RunPreflightAsync()
    {
        if (IsRunning)
        {
            return;
        }

        if (!_session.IsConnected)
        {
            StatusMessage = "The selected VPS is not connected. Connect in Overview first.";
            return;
        }

        IsRunning = true;
        StatusMessage = "Probing remote host environment...";
        AppendOutput("\n$ Inspecting operating system, resources, and kernel architecture...");

        try
        {
            var info = await _provisioningService.RunPreflightProbeAsync();
            DetectedOs = info.Os;
            DetectedKernel = info.Kernel;
            DetectedArch = info.Arch;
            DetectedMemory = info.MemorySummary;
            DetectedDisk = info.DiskFree;
            DetectedUptime = info.Uptime;
            HasPreflightData = true;

            StatusMessage = $"Preflight complete: {DetectedOs} ({DetectedArch})";
            AppendOutput($"✔ OS: {DetectedOs} | Kernel: {DetectedKernel} | Arch: {DetectedArch}");
            AppendOutput($"✔ Memory: {DetectedMemory} | Root Disk: {DetectedDisk} | {DetectedUptime}");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Preflight probe failed: {ex.Message}";
            AppendOutput($"\n❌ Preflight probe error: {ex.Message}");
        }
        finally
        {
            IsRunning = false;
        }
    }

    private void ResetAllStepStatuses()
    {
        UpdateSystemStatus = UpdateSystem ? StepExecutionStatus.Idle : StepExecutionStatus.Skipped;
        InstallEssentialsStatus = InstallEssentials ? StepExecutionStatus.Idle : StepExecutionStatus.Skipped;
        InstallDockerStatus = InstallDocker ? StepExecutionStatus.Idle : StepExecutionStatus.Skipped;
        ConfigureFirewallStatus = ConfigureFirewall ? StepExecutionStatus.Idle : StepExecutionStatus.Skipped;
        InstallFail2BanStatus = InstallFail2Ban ? StepExecutionStatus.Idle : StepExecutionStatus.Skipped;
        EnableUnattendedUpgradesStatus = EnableUnattendedUpgrades ? StepExecutionStatus.Idle : StepExecutionStatus.Skipped;
        HardenSshSecurityStatus = HardenSshSecurity ? StepExecutionStatus.Idle : StepExecutionStatus.Skipped;
        CreateSwapStatus = CreateSwap ? StepExecutionStatus.Idle : StepExecutionStatus.Skipped;
        PrepareProjectDirectoryStatus = PrepareProjectDirectory ? StepExecutionStatus.Idle : StepExecutionStatus.Skipped;
        InstallNodeJsStatus = InstallNodeJs ? StepExecutionStatus.Idle : StepExecutionStatus.Skipped;
        InstallNginxStatus = InstallNginx ? StepExecutionStatus.Idle : StepExecutionStatus.Skipped;
    }

    private ProvisioningPlanRequest CreatePlanRequest() => new()
    {
        UpdateSystem = UpdateSystem,
        InstallEssentials = InstallEssentials,
        InstallDocker = InstallDocker,
        ConfigureFirewall = ConfigureFirewall,
        InstallFail2Ban = InstallFail2Ban,
        EnableUnattendedUpgrades = EnableUnattendedUpgrades,
        HardenSshSecurity = HardenSshSecurity,
        CreateSwap = CreateSwap,
        PrepareProjectDirectory = PrepareProjectDirectory,
        InstallNodeJs = InstallNodeJs,
        InstallNginx = InstallNginx,
        DockerSource = DockerSource,
        DockerRepositoryUrl = DockerRepositoryUrl,
        PackageMirrorUrl = PackageMirrorUrl,
        SwapSizeGb = SwapSizeGb,
        ProjectDirectory = ProjectDirectory
    };

    private IEnumerable<SetupStep> BuildSelectedSteps()
    {
        var plan = CreatePlanRequest();
        var definitions = _provisioningService.BuildSteps(plan);
        foreach (var def in definitions)
        {
            yield return new SetupStep(def, GetStatusUpdaterForStep(def.Id));
        }
    }

    private Action<StepExecutionStatus> GetStatusUpdaterForStep(string stepId) => stepId switch
    {
        "system_update" => s => UpdateSystemStatus = s,
        "essentials" => s => InstallEssentialsStatus = s,
        "docker" => s => InstallDockerStatus = s,
        "ufw" => s => ConfigureFirewallStatus = s,
        "fail2ban" => s => InstallFail2BanStatus = s,
        "unattended_upgrades" => s => EnableUnattendedUpgradesStatus = s,
        "ssh_hardening" => s => HardenSshSecurityStatus = s,
        "swap" => s => CreateSwapStatus = s,
        "project_dir" => s => PrepareProjectDirectoryStatus = s,
        "nodejs" => s => InstallNodeJsStatus = s,
        "nginx" => s => InstallNginxStatus = s,
        _ => _ => { }
    };

    private void AppendOutput(string? output)
    {
        if (!string.IsNullOrEmpty(output))
        {
            TerminalOutput += output.TrimEnd() + "\n";
        }
    }

    private sealed record SetupStep(ProvisioningStepDefinition Definition, Action<StepExecutionStatus> SetStatus)
    {
        public string Name => Definition.Name;
    }
}
