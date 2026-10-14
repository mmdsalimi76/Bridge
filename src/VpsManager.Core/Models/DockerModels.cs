using CommunityToolkit.Mvvm.ComponentModel;

namespace VpsManager.Core.Models;

public enum ContainerStateKind
{
    Unknown,
    Running,
    Exited,
    Paused,
    Restarting,
    Dead
}

public class ContainerInfo : ObservableObject
{
    private string _id = string.Empty;
    private string _names = string.Empty;
    private string _image = string.Empty;
    private string _state = string.Empty;
    private string _status = string.Empty;
    private string _ports = string.Empty;
    private string _created = string.Empty;
    private string _cpuPercentage = string.Empty;
    private string _memoryUsage = string.Empty;
    private string _memoryPercentage = string.Empty;
    private string _netIO = string.Empty;

    public string Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    public string Names
    {
        get => _names;
        set => SetProperty(ref _names, value);
    }

    public string Image
    {
        get => _image;
        set => SetProperty(ref _image, value);
    }

    public string State
    {
        get => _state;
        set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(StateKind));
                OnPropertyChanged(nameof(IsRunning));
                OnPropertyChanged(nameof(IsExited));
                OnPropertyChanged(nameof(IsPaused));
                OnPropertyChanged(nameof(IsRestarting));
                OnPropertyChanged(nameof(CanStart));
                OnPropertyChanged(nameof(CanStop));
                OnPropertyChanged(nameof(CanPause));
                OnPropertyChanged(nameof(CanUnpause));
                OnPropertyChanged(nameof(StatusBadgeText));
            }
        }
    }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public string Ports
    {
        get => _ports;
        set
        {
            if (SetProperty(ref _ports, value))
            {
                OnPropertyChanged(nameof(HasPorts));
            }
        }
    }

    public string Created
    {
        get => _created;
        set => SetProperty(ref _created, value);
    }

    public string CpuPercentage
    {
        get => _cpuPercentage;
        set
        {
            if (SetProperty(ref _cpuPercentage, value))
            {
                OnPropertyChanged(nameof(HasMetrics));
            }
        }
    }

    public string MemoryUsage
    {
        get => _memoryUsage;
        set
        {
            if (SetProperty(ref _memoryUsage, value))
            {
                OnPropertyChanged(nameof(HasMetrics));
            }
        }
    }

    public string MemoryPercentage
    {
        get => _memoryPercentage;
        set => SetProperty(ref _memoryPercentage, value);
    }

    public string NetIO
    {
        get => _netIO;
        set => SetProperty(ref _netIO, value);
    }

    public string ShortId => string.IsNullOrWhiteSpace(Id) ? string.Empty : (Id.Length > 12 ? Id[..12] : Id);

    public ContainerStateKind StateKind
    {
        get
        {
            var s = State?.Trim().ToLowerInvariant() ?? "";
            if (s.Contains("running") || s == "up") return ContainerStateKind.Running;
            if (s.Contains("exit") || s.Contains("stop")) return ContainerStateKind.Exited;
            if (s.Contains("pause")) return ContainerStateKind.Paused;
            if (s.Contains("restart")) return ContainerStateKind.Restarting;
            if (s.Contains("dead")) return ContainerStateKind.Dead;
            return ContainerStateKind.Unknown;
        }
    }

    public bool IsRunning => StateKind == ContainerStateKind.Running;
    public bool IsExited => StateKind == ContainerStateKind.Exited || StateKind == ContainerStateKind.Dead;
    public bool IsPaused => StateKind == ContainerStateKind.Paused;
    public bool IsRestarting => StateKind == ContainerStateKind.Restarting;

    public bool CanStart => !IsRunning;
    public bool CanStop => IsRunning || IsPaused;
    public bool CanPause => IsRunning;
    public bool CanUnpause => IsPaused;

    public string StatusBadgeText => StateKind switch
    {
        ContainerStateKind.Running => "RUNNING",
        ContainerStateKind.Exited => "EXITED",
        ContainerStateKind.Paused => "PAUSED",
        ContainerStateKind.Restarting => "RESTARTING",
        ContainerStateKind.Dead => "DEAD",
        _ => string.IsNullOrWhiteSpace(State) ? "UNKNOWN" : State.ToUpperInvariant()
    };

    public bool HasPorts => !string.IsNullOrWhiteSpace(Ports);
    public bool HasMetrics => !string.IsNullOrWhiteSpace(CpuPercentage) || !string.IsNullOrWhiteSpace(MemoryUsage);
}

public class DockerDaemonInfo
{
    public string ServerVersion { get; set; } = "Unknown";
    public string ApiVersion { get; set; } = string.Empty;
    public int TotalContainers { get; set; }
    public int RunningContainers { get; set; }
    public int PausedContainers { get; set; }
    public int StoppedContainers { get; set; }
    public int ImagesCount { get; set; }
    public string StorageDriver { get; set; } = string.Empty;
    public string OperatingSystem { get; set; } = string.Empty;
    public string Architecture { get; set; } = string.Empty;
    public bool IsAvailable { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
}

public class ContainerStatsInfo
{
    public string ContainerId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string CpuPercentage { get; set; } = string.Empty;
    public string MemoryUsage { get; set; } = string.Empty;
    public string MemoryLimit { get; set; } = string.Empty;
    public string MemoryPercentage { get; set; } = string.Empty;
    public string NetIO { get; set; } = string.Empty;
    public string BlockIO { get; set; } = string.Empty;
    public string Pids { get; set; } = string.Empty;
}

public enum DockerComposeAction
{
    Up,
    Down,
    Build,
    Restart,
    Start,
    Stop,
    Pause,
    Unpause,
    Status
}

public enum DockerScope
{
    ComposeStack,
    AllHostContainers
}
