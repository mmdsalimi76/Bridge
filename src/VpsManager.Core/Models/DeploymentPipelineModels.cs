using CommunityToolkit.Mvvm.ComponentModel;

namespace VpsManager.Core.Models;

public enum ProjectType
{
    DockerCompose,
    SingleDockerfile,
    NativeLinuxService,  // Systemd, PM2, Supervisor
    TelegramBot,         // Systemd daemon / PM2 / Python venv / Docker
    WindowsApplication,  // Windows Service (sc.exe / NSSM) or Scheduled Task
    StaticWebApp         // Nginx / Caddy docroot
}

public enum DatabaseEngine
{
    None,
    PostgreSQL,
    MySQL,
    SQLite,
    MongoDB,
    Redis,
    MSSQL,
    CustomCommand
}

public enum LocalBuildStrategy
{
    None,
    NodeNpm,
    DotnetPublish,
    PythonVenv,
    GoBuild,
    RustCargo,
    CustomScript
}

public enum HealthCheckType
{
    DockerContainer,
    HttpEndpoint,
    TcpPortProbe,
    ProcessState,
    CustomCommand
}

public enum HostOS
{
    Linux,
    Windows
}

public class UniversalPipelineConfig
{
    public string ProfileId { get; set; } = string.Empty;
    public string ProfileName { get; set; } = string.Empty;
    public HostOS TargetOS { get; set; } = HostOS.Linux;
    private bool? _isDockerized;
    public bool IsDockerized
    {
        get => _isDockerized ?? (ProjectType == ProjectType.DockerCompose || ProjectType == ProjectType.SingleDockerfile);
        set => _isDockerized = value;
    }
    public ProjectType ProjectType { get; set; } = ProjectType.DockerCompose;
    
    // Project Paths
    public string LocalProjectPath { get; set; } = string.Empty;
    public string RemoteProjectPath { get; set; } = string.Empty;
    public List<string> IgnorePatterns { get; set; } = new();

    // Launch Specs (Non-Dockerized)
    public string LaunchCommand { get; set; } = string.Empty;

    // Docker Specs
    public string ComposeFilePath { get; set; } = "docker-compose.yml";
    public string DockerfileName { get; set; } = "Dockerfile";
    public string ContainerName { get; set; } = string.Empty;

    // Service / Daemon Specs (Systemd, PM2, Windows Service)
    public string ServiceName { get; set; } = string.Empty;
    public string StartCommand { get; set; } = string.Empty;
    public string StopCommand { get; set; } = string.Empty;
    public string RestartCommand { get; set; } = string.Empty;

    // Local Build Configuration
    public LocalBuildStrategy BuildStrategy { get; set; } = LocalBuildStrategy.None;
    public string LocalBuildCommand { get; set; } = string.Empty;
    public string BuildArtifactPath { get; set; } = string.Empty;

    // Database Configuration
    public DatabaseEngine DbEngine { get; set; } = DatabaseEngine.None;
    public bool IsDbContainerized { get; set; } = true;
    public string DbContainerName { get; set; } = string.Empty;
    public string DbUser { get; set; } = string.Empty;
    public string DbName { get; set; } = string.Empty;
    public string DbHost { get; set; } = "localhost";
    public int DbPort { get; set; } = 5432;
    public string CustomBackupCommand { get; set; } = string.Empty;
    public string CustomMigrationCommand { get; set; } = string.Empty;
    public bool SkipDatabaseBackup { get; set; } = false;

    // Health Check Configuration
    public HealthCheckType HealthCheckType { get; set; } = HealthCheckType.DockerContainer;
    public string HealthCheckUrl { get; set; } = "http://localhost:8080/health";
    public int HealthCheckPort { get; set; } = 8080;
    public string HealthCheckProcessName { get; set; } = string.Empty;
    public string HealthCheckCustomCommand { get; set; } = string.Empty;
    public bool SkipHealthCheck { get; set; } = false;
}

public enum PipelineStepStatus
{
    Pending,
    InProgress,
    Completed,
    Failed,
    Skipped
}

public enum PipelineStatus
{
    NotStarted,
    InProgress,
    Completed,
    Failed,
    Cancelled
}

public class PipelineStep : ObservableObject
{
    private string _id = string.Empty;
    private string _name = string.Empty;
    private string _description = string.Empty;
    private PipelineStepStatus _status = PipelineStepStatus.Pending;
    private DateTime? _startedAt;
    private DateTime? _completedAt;
    private string _errorMessage = string.Empty;

    public string Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
    }

    public PipelineStepStatus Status
    {
        get => _status;
        set
        {
            if (SetProperty(ref _status, value))
            {
                OnPropertyChanged(nameof(Duration));
                OnPropertyChanged(nameof(DurationDisplay));
                OnPropertyChanged(nameof(StatusIcon));
                OnPropertyChanged(nameof(IsCompleted));
                OnPropertyChanged(nameof(IsInProgress));
                OnPropertyChanged(nameof(IsFailed));
                OnPropertyChanged(nameof(IsSkipped));
                OnPropertyChanged(nameof(IsPending));
            }
        }
    }

    public DateTime? StartedAt
    {
        get => _startedAt;
        set
        {
            if (SetProperty(ref _startedAt, value))
            {
                OnPropertyChanged(nameof(Duration));
                OnPropertyChanged(nameof(DurationDisplay));
            }
        }
    }

    public DateTime? CompletedAt
    {
        get => _completedAt;
        set
        {
            if (SetProperty(ref _completedAt, value))
            {
                OnPropertyChanged(nameof(Duration));
                OnPropertyChanged(nameof(DurationDisplay));
                OnPropertyChanged(nameof(IsCompleted));
            }
        }
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    public TimeSpan Duration => (CompletedAt ?? DateTime.UtcNow) - (StartedAt ?? DateTime.UtcNow);

    public bool IsCompleted => Status == PipelineStepStatus.Completed;
    public bool IsInProgress => Status == PipelineStepStatus.InProgress;
    public bool IsFailed => Status == PipelineStepStatus.Failed;
    public bool IsSkipped => Status == PipelineStepStatus.Skipped;
    public bool IsPending => Status == PipelineStepStatus.Pending;

    public string StatusIcon => Status switch
    {
        PipelineStepStatus.Pending => "⏳",
        PipelineStepStatus.InProgress => "⚡",
        PipelineStepStatus.Completed => "✅",
        PipelineStepStatus.Failed => "❌",
        PipelineStepStatus.Skipped => "⏭️",
        _ => "⏳"
    };

    public string DurationDisplay
    {
        get
        {
            if (Status == PipelineStepStatus.Pending) return "Pending";
            if (Status == PipelineStepStatus.InProgress) return "Running...";
            if (Status == PipelineStepStatus.Skipped) return "Skipped";

            if (Duration.TotalSeconds > 0)
            {
                if (Duration.TotalSeconds < 1) return $"{Duration.TotalMilliseconds:F0}ms";
                return $"{Duration.TotalSeconds:F1}s";
            }

            return Status == PipelineStepStatus.Completed ? "Done" : "Pending";
        }
    }
}

public class PipelineConfig : UniversalPipelineConfig
{
}

public class PipelineResult
{
    public PipelineStatus Status { get; set; } = PipelineStatus.NotStarted;
    public bool Success { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime CompletedAt { get; set; }
    public List<PipelineStep> Steps { get; set; } = new();

    public TimeSpan TotalDuration => CompletedAt - StartedAt;
}