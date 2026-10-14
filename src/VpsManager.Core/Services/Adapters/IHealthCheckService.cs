using VpsManager.Core.Helpers;
using VpsManager.Core.Models;

namespace VpsManager.Core.Services.Adapters;

public interface IHealthCheckService
{
    Task<HealthCheckResult> VerifyHealthAsync(ISshService sshService, IDockerService dockerService, UniversalPipelineConfig config, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the shell command that produces this health check's evidence, or null when the
    /// result is derived from another source (container states come from the Docker inventory).
    /// </summary>
    string? BuildProbeCommand(UniversalPipelineConfig config);

    /// <summary>
    /// Pure interpretation of probe evidence. Used by both the single-check path and the
    /// batched Overview refresh.
    /// </summary>
    HealthCheckResult InterpretProbe(UniversalPipelineConfig config, string? output, int? exitCode, IReadOnlyList<ContainerInfo>? containers);
}

public class UniversalHealthCheckService : IHealthCheckService
{
    public async Task<HealthCheckResult> VerifyHealthAsync(ISshService sshService, IDockerService dockerService, UniversalPipelineConfig config, CancellationToken cancellationToken = default)
    {
        if (config.SkipHealthCheck)
        {
            return new HealthCheckResult { IsHealthy = true, Message = "Health check skipped by user request." };
        }

        int maxAttempts = 5;
        TimeSpan delayBetweenAttempts = TimeSpan.FromSeconds(2);
        HealthCheckResult lastResult = new() { IsHealthy = false, Message = "Health check did not produce a result." };

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (config.HealthCheckType == HealthCheckType.DockerContainer)
            {
                var containers = await dockerService.GetContainersAsync(config.ComposeFilePath, cancellationToken);
                lastResult = InterpretProbe(config, null, null, containers);
            }
            else
            {
                var command = BuildProbeCommand(config);
                if (string.IsNullOrWhiteSpace(command))
                {
                    return InterpretProbe(config, null, null, null);
                }

                var result = await sshService.ExecuteCommandAsync(command, cancellationToken);
                lastResult = InterpretProbe(config, result.Output, result.ExitCode, null);
            }

            if (lastResult.IsHealthy)
            {
                return lastResult;
            }

            if (attempt < maxAttempts)
            {
                try
                {
                    await Task.Delay(delayBetweenAttempts, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        return lastResult;
    }

    public string? BuildProbeCommand(UniversalPipelineConfig config)
    {
        return config.HealthCheckType switch
        {
            HealthCheckType.HttpEndpoint => BuildHttpProbeCommand(config),
            HealthCheckType.TcpPortProbe => BuildTcpProbeCommand(config),
            HealthCheckType.ProcessState => BuildProcessProbeCommand(config),
            HealthCheckType.CustomCommand => string.IsNullOrWhiteSpace(config.HealthCheckCustomCommand) ? null : config.HealthCheckCustomCommand,
            _ => null
        };
    }

    public HealthCheckResult InterpretProbe(UniversalPipelineConfig config, string? output, int? exitCode, IReadOnlyList<ContainerInfo>? containers)
    {
        var trimmed = output?.Trim() ?? string.Empty;

        switch (config.HealthCheckType)
        {
            case HealthCheckType.DockerContainer:
                var running = containers ?? Array.Empty<ContainerInfo>();
                if (running.Count == 0)
                {
                    return new HealthCheckResult { IsHealthy = false, Message = "No running containers were found for the selected Compose project." };
                }

                var unhealthy = running.Where(container =>
                    !container.State.Equals("running", StringComparison.OrdinalIgnoreCase)
                    && !container.State.Equals("up", StringComparison.OrdinalIgnoreCase)).ToList();
                if (unhealthy.Count > 0)
                {
                    return new HealthCheckResult
                    {
                        IsHealthy = false,
                        Message = $"Containers not in running state: {string.Join(", ", unhealthy.Select(entry => entry.Names))}"
                    };
                }

                return new HealthCheckResult { IsHealthy = true, Message = $"All {running.Count} containers are healthy and running." };

            case HealthCheckType.HttpEndpoint:
                var targetUrl = ResolveHealthUrl(config);
                if (exitCode == 0 && StatusCodesAreHealthy(trimmed))
                {
                    return new HealthCheckResult { IsHealthy = true, Message = $"HTTP probe to {targetUrl} succeeded with status code {trimmed}." };
                }

                return new HealthCheckResult { IsHealthy = false, Message = $"HTTP probe to {targetUrl} failed. Returned code: {trimmed}" };

            case HealthCheckType.TcpPortProbe:
                var port = config.HealthCheckPort > 0 ? config.HealthCheckPort : 8080;
                if (exitCode == 0 && !string.IsNullOrWhiteSpace(trimmed))
                {
                    return new HealthCheckResult { IsHealthy = true, Message = $"TCP port {port} is listening and accepting connections." };
                }

                return new HealthCheckResult { IsHealthy = false, Message = $"TCP port {port} is not listening." };

            case HealthCheckType.ProcessState:
                var processName = ResolveProcessName(config);
                if (exitCode == 0 && !string.IsNullOrWhiteSpace(trimmed))
                {
                    return new HealthCheckResult { IsHealthy = true, Message = $"Target process/service '{processName}' is running (PID / Status: {trimmed.Replace('\n', ' ')})" };
                }

                return new HealthCheckResult { IsHealthy = false, Message = $"Target process/service '{processName}' is not running." };

            case HealthCheckType.CustomCommand:
                if (string.IsNullOrWhiteSpace(config.HealthCheckCustomCommand))
                {
                    return new HealthCheckResult { IsHealthy = true, Message = "Custom health check command is empty." };
                }

                if (exitCode == 0)
                {
                    return new HealthCheckResult { IsHealthy = true, Message = $"Custom health check passed: {trimmed}" };
                }

                return new HealthCheckResult { IsHealthy = false, Message = $"Custom health check failed with exit code {exitCode ?? -1}: {trimmed}" };

            default:
                return new HealthCheckResult { IsHealthy = true, Message = "Health check verified." };
        }
    }

    public static string ResolveHealthUrl(UniversalPipelineConfig config) =>
        string.IsNullOrWhiteSpace(config.HealthCheckUrl) ? "http://localhost:8080/health" : config.HealthCheckUrl;

    public static string ResolveProcessName(UniversalPipelineConfig config)
    {
        var processName = !string.IsNullOrWhiteSpace(config.HealthCheckProcessName)
            ? config.HealthCheckProcessName
            : config.ServiceName;
        return string.IsNullOrWhiteSpace(processName) ? "my_app" : processName;
    }

    private static bool StatusCodesAreHealthy(string code) =>
        code is "200" or "204" or "301" or "302";

    private static string BuildHttpProbeCommand(UniversalPipelineConfig config)
    {
        var targetUrl = ResolveHealthUrl(config);
        return $"curl -s -o /dev/null -w '%{{http_code}}' --max-time 5 {ShellQuote.Quote(targetUrl)}";
    }

    private static string BuildTcpProbeCommand(UniversalPipelineConfig config)
    {
        var port = config.HealthCheckPort > 0 ? config.HealthCheckPort : 8080;
        return $"nc -z -v -w 5 127.0.0.1 {port} 2>&1 || (exec 6<>/dev/tcp/127.0.0.1/{port} && echo 'open') 2>/dev/null || ss -tuln | grep :{port}";
    }

    private static string BuildProcessProbeCommand(UniversalPipelineConfig config)
    {
        var quotedProcess = ShellQuote.Quote(ResolveProcessName(config));
        return $"pgrep -f {quotedProcess} || ps aux | grep {quotedProcess} | grep -v grep || systemctl is-active {quotedProcess}";
    }
}

public class HealthCheckResult
{
    public bool IsHealthy { get; set; }
    public string Message { get; set; } = string.Empty;
}
