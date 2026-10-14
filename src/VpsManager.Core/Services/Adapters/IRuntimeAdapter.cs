using VpsManager.Core.Helpers;
using VpsManager.Core.Models;

namespace VpsManager.Core.Services.Adapters;

public interface IRuntimeAdapter
{
    ProjectType Type { get; }
    Task<CommandResult> BuildAsync(ISshService sshService, IDockerService dockerService, UniversalPipelineConfig config, CancellationToken cancellationToken = default);
    Task<CommandResult> StartOrRestartAsync(ISshService sshService, IDockerService dockerService, UniversalPipelineConfig config, CancellationToken cancellationToken = default);
    Task<CommandResult> StopAsync(ISshService sshService, IDockerService dockerService, UniversalPipelineConfig config, CancellationToken cancellationToken = default);
}

public class UniversalRuntimeAdapter : IRuntimeAdapter
{
    private readonly ProjectType _type;

    public UniversalRuntimeAdapter(ProjectType type)
    {
        _type = type;
    }

    public ProjectType Type => _type;

    public async Task<CommandResult> BuildAsync(ISshService sshService, IDockerService dockerService, UniversalPipelineConfig config, CancellationToken cancellationToken = default)
    {
        switch (_type)
        {
            case ProjectType.DockerCompose:
                var buildResult = await dockerService.ExecuteComposeActionAsync(config.ComposeFilePath, DockerComposeAction.Build, null, cancellationToken);
                return new CommandResult { ExitCode = buildResult.IsSuccess ? 0 : 1, Output = buildResult.Output, ErrorOutput = buildResult.Error };

            case ProjectType.SingleDockerfile:
                var dockerfile = string.IsNullOrEmpty(config.DockerfileName) ? "Dockerfile" : config.DockerfileName;
                var containerTag = string.IsNullOrEmpty(config.ContainerName) ? "my_app:latest" : $"{config.ContainerName}:latest";
                var cmd = $"cd {ShellQuote.Quote(config.RemoteProjectPath)} && docker build -f {ShellQuote.Quote(dockerfile)} -t {ShellQuote.Quote(containerTag)} .";
                return await sshService.ExecuteCommandAsync(cmd, cancellationToken);

            case ProjectType.NativeLinuxService:
            case ProjectType.TelegramBot:
            case ProjectType.WindowsApplication:
            case ProjectType.StaticWebApp:
            default:
                // Non-docker builds are performed via LocalBuildStrategy or remote build scripts
                return new CommandResult { ExitCode = 0, Output = "No remote container build step required for this runtime." };
        }
    }

    public async Task<CommandResult> StartOrRestartAsync(ISshService sshService, IDockerService dockerService, UniversalPipelineConfig config, CancellationToken cancellationToken = default)
    {
        switch (_type)
        {
            case ProjectType.DockerCompose:
                var upResult = await dockerService.ExecuteComposeActionAsync(config.ComposeFilePath, DockerComposeAction.Up, null, cancellationToken);
                return new CommandResult { ExitCode = upResult.IsSuccess ? 0 : 1, Output = upResult.Output, ErrorOutput = upResult.Error };

            case ProjectType.SingleDockerfile:
                var containerName = string.IsNullOrEmpty(config.ContainerName) ? "my_app_container" : config.ContainerName;
                var containerTag = $"{containerName}:latest";
                var runCmd = $"docker stop {ShellQuote.Quote(containerName)} 2>/dev/null || true && docker rm {ShellQuote.Quote(containerName)} 2>/dev/null || true && docker run -d --name {ShellQuote.Quote(containerName)} --restart always {ShellQuote.Quote(containerTag)}";
                return await sshService.ExecuteCommandAsync(runCmd, cancellationToken);

            case ProjectType.NativeLinuxService:
            case ProjectType.TelegramBot:
            case ProjectType.StaticWebApp:
                // If user specified a LaunchCommand or script file in profile settings, execute it directly in project directory
                if (!string.IsNullOrWhiteSpace(config.LaunchCommand))
                {
                    var cmd = config.LaunchCommand.Trim();
                    string execCmd;
                    if (cmd.EndsWith(".sh", StringComparison.OrdinalIgnoreCase) || cmd.StartsWith("./", StringComparison.Ordinal))
                    {
                        execCmd = $"cd {ShellQuote.Quote(config.RemoteProjectPath)} && chmod +x {cmd} && {cmd}";
                    }
                    else
                    {
                        execCmd = $"cd {ShellQuote.Quote(config.RemoteProjectPath)} && {cmd}";
                    }
                    return await sshService.ExecuteCommandAsync(execCmd, cancellationToken);
                }

                if (!string.IsNullOrWhiteSpace(config.RestartCommand))
                {
                    return await sshService.ExecuteCommandAsync(config.RestartCommand, cancellationToken);
                }
                var serviceName = string.IsNullOrEmpty(config.ServiceName) ? "my_app" : config.ServiceName;
                var systemdCmd = $"sudo systemctl daemon-reload && sudo systemctl restart {ShellQuote.Quote(serviceName)} 2>/dev/null || pm2 restart {ShellQuote.Quote(serviceName)} 2>/dev/null || true";
                return await sshService.ExecuteCommandAsync(systemdCmd, cancellationToken);

            case ProjectType.WindowsApplication:
                if (config.TargetOS == HostOS.Windows)
                {
                    if (!string.IsNullOrWhiteSpace(config.RestartCommand))
                    {
                        return await sshService.ExecuteCommandAsync(config.RestartCommand, cancellationToken);
                    }
                    var winService = string.IsNullOrEmpty(config.ServiceName) ? "MyWindowsService" : config.ServiceName;
                    var winCmd = $"powershell -Command \"Restart-Service -Name '{winService}' -ErrorAction SilentlyContinue; if ($?) {{ Write-Host 'Service restarted' }} else {{ sc.exe start '{winService}' }}\"";
                    return await sshService.ExecuteCommandAsync(winCmd, cancellationToken);
                }
                return await sshService.ExecuteCommandAsync(config.StartCommand, cancellationToken);

            default:
                return new CommandResult { ExitCode = 0, Output = "Done" };
        }
    }

    public async Task<CommandResult> StopAsync(ISshService sshService, IDockerService dockerService, UniversalPipelineConfig config, CancellationToken cancellationToken = default)
    {
        switch (_type)
        {
            case ProjectType.DockerCompose:
                var downResult = await dockerService.ExecuteComposeActionAsync(config.ComposeFilePath, DockerComposeAction.Down, null, cancellationToken);
                return new CommandResult { ExitCode = downResult.IsSuccess ? 0 : 1, Output = downResult.Output, ErrorOutput = downResult.Error };

            case ProjectType.SingleDockerfile:
                var containerName = string.IsNullOrEmpty(config.ContainerName) ? "my_app_container" : config.ContainerName;
                return await sshService.ExecuteCommandAsync($"docker stop {ShellQuote.Quote(containerName)}", cancellationToken);

            case ProjectType.NativeLinuxService:
            case ProjectType.TelegramBot:
                var svc = string.IsNullOrEmpty(config.ServiceName) ? "my_app" : config.ServiceName;
                return await sshService.ExecuteCommandAsync($"sudo systemctl stop {ShellQuote.Quote(svc)} || pm2 stop {ShellQuote.Quote(svc)} || true", cancellationToken);

            case ProjectType.WindowsApplication:
                var winSvc = string.IsNullOrEmpty(config.ServiceName) ? "MyWindowsService" : config.ServiceName;
                return await sshService.ExecuteCommandAsync($"sc.exe stop {ShellQuote.Quote(winSvc)}", cancellationToken);

            default:
                return new CommandResult { ExitCode = 0, Output = "Stopped" };
        }
    }
}
