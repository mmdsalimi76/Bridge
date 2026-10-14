using VpsManager.Core.Models;
using VpsManager.Core.Services.Adapters;

namespace VpsManager.Core.Services;

public interface IDeploymentPipelineService
{
    Task<PipelineResult> ExecutePipelineAsync(UniversalPipelineConfig config, IProgress<PipelineStep>? progress = null, CancellationToken cancellationToken = default);
    List<PipelineStep> GetDynamicPipelineSteps(UniversalPipelineConfig config);
}

public class DeploymentPipelineService : IDeploymentPipelineService
{
    private readonly ISshService _sshService;
    private readonly IDatabaseService _databaseService;
    private readonly IDockerService _dockerService;
    private readonly IPatchService _patchService;
    private readonly ILocalBuildService _localBuildService;
    private readonly IHealthCheckService _healthCheckService;

    public DeploymentPipelineService(
        ISshService sshService,
        IDatabaseService databaseService,
        IDockerService dockerService,
        IPatchService patchService,
        ILocalBuildService localBuildService,
        IHealthCheckService healthCheckService)
    {
        _sshService = sshService ?? throw new ArgumentNullException(nameof(sshService));
        _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
        _dockerService = dockerService ?? throw new ArgumentNullException(nameof(dockerService));
        _patchService = patchService ?? throw new ArgumentNullException(nameof(patchService));
        _localBuildService = localBuildService ?? throw new ArgumentNullException(nameof(localBuildService));
        _healthCheckService = healthCheckService ?? throw new ArgumentNullException(nameof(healthCheckService));
    }

    public List<PipelineStep> GetDynamicPipelineSteps(UniversalPipelineConfig config)
    {
        var steps = new List<PipelineStep>();
        int id = 1;

        // 1. Pre-flight
        string preflightName = config.ProjectType switch
        {
            ProjectType.DockerCompose => "Pre-flight & Docker Host Check",
            ProjectType.TelegramBot => "Pre-flight & Bot Environment Check",
            ProjectType.WindowsApplication => "Pre-flight & Windows SSH Check",
            ProjectType.StaticWebApp => "Pre-flight & Web Server Check",
            ProjectType.NativeLinuxService => "Pre-flight & Linux System Check",
            _ => "Pre-flight Validation"
        };
        steps.Add(new PipelineStep
        {
            Id = (id++).ToString(),
            Name = preflightName,
            Description = $"Validate SSH connection and target path ({config.TargetOS})"
        });

        // 2. Local Build
        if (config.BuildStrategy != LocalBuildStrategy.None)
        {
            string buildName = config.BuildStrategy switch
            {
                LocalBuildStrategy.NodeNpm => "Run Local npm build",
                LocalBuildStrategy.DotnetPublish => "Run Local dotnet publish",
                LocalBuildStrategy.GoBuild => "Run Local go build",
                LocalBuildStrategy.RustCargo => "Run Local cargo build",
                LocalBuildStrategy.PythonVenv => "Freeze Requirements & Venv Prep",
                _ => "Local Project Compilation"
            };
            steps.Add(new PipelineStep
            {
                Id = (id++).ToString(),
                Name = buildName,
                Description = $"Build binaries locally using strategy: {config.BuildStrategy}"
            });
        }

        // 3. Database Safety Backup
        if (config.DbEngine != DatabaseEngine.None && !config.SkipDatabaseBackup)
        {
            string dbBackupName = config.DbEngine switch
            {
                DatabaseEngine.PostgreSQL => "PostgreSQL Dump Backup Gate",
                DatabaseEngine.MySQL => "MySQL Dump Backup Gate",
                DatabaseEngine.SQLite => "SQLite Atomic File Snapshot",
                DatabaseEngine.MongoDB => "MongoDB Archive Backup Gate",
                DatabaseEngine.Redis => "Redis BGSAVE Snapshot Gate",
                DatabaseEngine.MSSQL => "MSSQL Database Backup Gate",
                _ => "Database Safety Backup"
            };
            steps.Add(new PipelineStep
            {
                Id = (id++).ToString(),
                Name = dbBackupName,
                Description = $"Create safety dump for {config.DbEngine} database ({config.DbName})"
            });
        }

        // 4. File Sync
        string syncName = config.ProjectType switch
        {
            ProjectType.StaticWebApp => "Sync Dist Assets to Web Root",
            ProjectType.TelegramBot => "Sync Bot Source Code & Config",
            ProjectType.DockerCompose => "Sync Source & Docker Context",
            ProjectType.WindowsApplication => "Sync Windows Executable Package",
            _ => "File Synchronization"
        };
        steps.Add(new PipelineStep
        {
            Id = (id++).ToString(),
            Name = syncName,
            Description = $"Deploy project files to VPS target ({config.RemoteProjectPath})"
        });

        // 5. Container Build (only if Dockerized)
        if (config.IsDockerized)
        {
            steps.Add(new PipelineStep
            {
                Id = (id++).ToString(),
                Name = "Docker Compose Build Images",
                Description = $"Build Docker container images using {config.ComposeFilePath}"
            });
        }

        // 6. Service Start / Restart
        string serviceName;
        string serviceDesc;
        if (config.IsDockerized)
        {
            serviceName = "Docker Compose Up Services";
            serviceDesc = "Start container services in detached mode (--build --remove-orphans)";
        }
        else if (!string.IsNullOrWhiteSpace(config.LaunchCommand))
        {
            serviceName = "Execute App Launch Command";
            serviceDesc = $"Run host command/script: {config.LaunchCommand}";
        }
        else
        {
            serviceName = config.ProjectType switch
            {
                ProjectType.StaticWebApp => "Reload Nginx / Caddy Web Server",
                ProjectType.TelegramBot => "Restart Bot Daemon & Kill Previous PID",
                ProjectType.WindowsApplication => "Start Windows Service / Process",
                _ => "Reload Host Service (Systemd / PM2)"
            };
            serviceDesc = $"Reload service {config.ServiceName}";
        }

        steps.Add(new PipelineStep
        {
            Id = (id++).ToString(),
            Name = serviceName,
            Description = serviceDesc
        });

        // 7. Health Check
        if (!config.SkipHealthCheck)
        {
            string healthName = config.HealthCheckType switch
            {
                HealthCheckType.DockerContainer => "Docker Container State Probe",
                HealthCheckType.HttpEndpoint => "HTTP Status Code 200 Probe",
                HealthCheckType.TcpPortProbe => "TCP Socket Port Listener Check",
                HealthCheckType.ProcessState => "Process PID & Active Daemon Check",
                HealthCheckType.CustomCommand => "Custom Shell Health Verification",
                _ => "Health Verification"
            };
            steps.Add(new PipelineStep
            {
                Id = (id++).ToString(),
                Name = healthName,
                Description = $"Verify application stability ({config.HealthCheckType})"
            });
        }

        // 8. Database Migrations
        if (config.DbEngine != DatabaseEngine.None)
        {
            string migName = config.ProjectType switch
            {
                ProjectType.TelegramBot => "Run Bot Database Migrations",
                ProjectType.NativeLinuxService => "Run Framework Schema Migrations",
                ProjectType.DockerCompose => "Run Container Database Migrations",
                _ => "Database Migrations"
            };
            steps.Add(new PipelineStep
            {
                Id = (id++).ToString(),
                Name = migName,
                Description = $"Execute database schema migrations ({config.DbEngine})"
            });
        }

        return steps;
    }

    public async Task<PipelineResult> ExecutePipelineAsync(UniversalPipelineConfig config, IProgress<PipelineStep>? progress = null, CancellationToken cancellationToken = default)
    {
        var steps = GetDynamicPipelineSteps(config);
        var result = new PipelineResult
        {
            Status = PipelineStatus.NotStarted,
            StartedAt = DateTime.UtcNow,
            Steps = steps
        };

        var runtimeAdapter = new UniversalRuntimeAdapter(config.ProjectType);
        var dbAdapter = new UniversalDatabaseAdapter(config.DbEngine);
        try
        {
            result.Status = PipelineStatus.InProgress;
            int stepIdx = 0;

            // Step 1: Pre-flight Validation
            await ExecuteStepAsync(result.Steps[stepIdx++], async () =>
            {
                if (!_sshService.IsConnected)
                    throw new InvalidOperationException("SSH connection is not established. Please connect to VPS first.");

                if (string.IsNullOrEmpty(config.LocalProjectPath) || !Directory.Exists(config.LocalProjectPath))
                    throw new InvalidOperationException($"Local project path not found: {config.LocalProjectPath}");
            }, progress, cancellationToken);

            // Step 2: Local Compilation (if configured)
            if (config.BuildStrategy != LocalBuildStrategy.None)
            {
                await ExecuteStepAsync(result.Steps[stepIdx++], async () =>
                {
                    var buildRes = await _localBuildService.ExecuteLocalBuildAsync(config, cancellationToken);
                    if (!buildRes.IsSuccess)
                        throw new InvalidOperationException($"Local compilation failed: {buildRes.ErrorMessage}");
                }, progress, cancellationToken);
            }

            // Step 3: Database Backup (if configured and not skipped)
            if (config.DbEngine != DatabaseEngine.None && !config.SkipDatabaseBackup)
            {
                await ExecuteStepAsync(result.Steps[stepIdx++], async () =>
                {
                    var backupRes = await dbAdapter.BackupAsync(_sshService, config, cancellationToken);
                    if (!backupRes.IsSuccess)
                        throw new InvalidOperationException($"Database backup failed: {backupRes.ErrorMessage}");
                }, progress, cancellationToken);
            }

            // Step 4: File Synchronization
            await ExecuteStepAsync(result.Steps[stepIdx++], async () =>
            {
                var files = await _patchService.ScanLocalChangesAsync(
                    config.LocalProjectPath,
                    config.IgnorePatterns,
                    cancellationToken);

                if (files.Count == 0)
                    throw new InvalidOperationException("No files found to synchronize.");

                var deployResult = await _patchService.DeploySelectivePatchAsync(
                    files,
                    config.LocalProjectPath,
                    config.RemoteProjectPath,
                    cancellationToken);

                if (!deployResult.IsSuccess)
                    throw new InvalidOperationException($"File sync failed: {deployResult.Error}");
            }, progress, cancellationToken);

            // Step 5: Container Build (if Dockerized)
            if (config.IsDockerized)
            {
                await ExecuteStepAsync(result.Steps[stepIdx++], async () =>
                {
                    var buildResult = await runtimeAdapter.BuildAsync(_sshService, _dockerService, config, cancellationToken);
                    if (buildResult.ExitCode != 0)
                        throw new InvalidOperationException($"Container build failed: {buildResult.ErrorOutput}");
                }, progress, cancellationToken);
            }

            // Step 6: Service Start / Restart
            await ExecuteStepAsync(result.Steps[stepIdx++], async () =>
            {
                var startResult = await runtimeAdapter.StartOrRestartAsync(_sshService, _dockerService, config, cancellationToken);
                if (startResult.ExitCode != 0)
                    throw new InvalidOperationException($"Service restart failed: {startResult.ErrorOutput}");
            }, progress, cancellationToken);

            // Step 7: Health Verification
            if (!config.SkipHealthCheck)
            {
                await ExecuteStepAsync(result.Steps[stepIdx++], async () =>
                {
                    var healthRes = await _healthCheckService.VerifyHealthAsync(_sshService, _dockerService, config, cancellationToken);
                    if (!healthRes.IsHealthy)
                        throw new InvalidOperationException($"Health verification failed: {healthRes.Message}");
                }, progress, cancellationToken);
            }

            // Step 8: Database Migrations
            if (config.DbEngine != DatabaseEngine.None)
            {
                await ExecuteStepAsync(result.Steps[stepIdx++], async () =>
                {
                    var migResult = await dbAdapter.RunMigrationAsync(_sshService, config, cancellationToken);
                    if (migResult.ExitCode != 0)
                        throw new InvalidOperationException($"Database migration failed: {migResult.ErrorOutput}");
                }, progress, cancellationToken);
            }

            result.Status = PipelineStatus.Completed;
            result.Success = true;
        }
        catch (OperationCanceledException)
        {
            result.Status = PipelineStatus.Cancelled;
            result.Success = false;
            result.ErrorMessage = "Pipeline execution was cancelled by user.";
        }
        catch (Exception ex)
        {
            result.Status = PipelineStatus.Failed;
            result.Success = false;
            result.ErrorMessage = ex.Message;

            var failedStep = result.Steps.FirstOrDefault(s => s.Status == PipelineStepStatus.InProgress);
            if (failedStep != null)
            {
                failedStep.Status = PipelineStepStatus.Failed;
                failedStep.ErrorMessage = ex.Message;
                failedStep.CompletedAt = DateTime.UtcNow;
                progress?.Report(failedStep);
            }
        }
        finally
        {
            result.CompletedAt = DateTime.UtcNow;
        }

        return result;
    }

    private async Task ExecuteStepAsync(PipelineStep step, Func<Task> action, IProgress<PipelineStep>? progress, CancellationToken cancellationToken)
    {
        step.Status = PipelineStepStatus.InProgress;
        step.StartedAt = DateTime.UtcNow;
        progress?.Report(step);

        try
        {
            await action();
            step.Status = PipelineStepStatus.Completed;
            step.CompletedAt = DateTime.UtcNow;
            progress?.Report(step);
        }
        catch (Exception ex)
        {
            step.Status = PipelineStepStatus.Failed;
            step.ErrorMessage = ex.Message;
            step.CompletedAt = DateTime.UtcNow;
            progress?.Report(step);
            throw;
        }
    }
}