using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VpsManager.Core.Helpers;
using VpsManager.Core.Models;

namespace VpsManager.Core.Services;

public class DockerService : IDockerService
{
    private readonly ISshService _sshService;

    public DockerService(ISshService sshService)
    {
        _sshService = sshService ?? throw new ArgumentNullException(nameof(sshService));
    }

    public async Task<List<ContainerInfo>> GetContainersAsync(string composeFilePath, CancellationToken cancellationToken = default)
    {
        string cmd = $"docker compose -f {ShellQuote.Quote(composeFilePath)} ps -a --format json";
        var result = await _sshService.ExecuteCommandAsync(cmd, cancellationToken);

        if (!result.IsSuccess || string.IsNullOrWhiteSpace(result.Output))
        {
            // If docker compose ps -a is not supported or failed, try standard ps
            string fallbackCmd = $"docker compose -f {ShellQuote.Quote(composeFilePath)} ps --format json";
            result = await _sshService.ExecuteCommandAsync(fallbackCmd, cancellationToken);
            if (!result.IsSuccess || string.IsNullOrWhiteSpace(result.Output))
            {
                return [];
            }
        }

        return ParseContainerJsonLines(result.Output);
    }

    public async Task<List<ContainerInfo>> GetAllHostContainersAsync(CancellationToken cancellationToken = default)
    {
        string cmd = "docker ps -a --format json";
        var result = await _sshService.ExecuteCommandAsync(cmd, cancellationToken);

        if (!result.IsSuccess || string.IsNullOrWhiteSpace(result.Output))
        {
            return [];
        }

        return ParseContainerJsonLines(result.Output);
    }

    public async Task<DockerDaemonInfo> GetDaemonInfoAsync(CancellationToken cancellationToken = default)
    {
        var info = new DockerDaemonInfo();
        string cmd = "docker info --format json";
        var result = await _sshService.ExecuteCommandAsync(cmd, cancellationToken);

        if (!result.IsSuccess || string.IsNullOrWhiteSpace(result.Output))
        {
            // Try minimal fallback with docker version
            var verResult = await _sshService.ExecuteCommandAsync("docker version --format '{{.Server.Version}}'", cancellationToken);
            if (verResult.IsSuccess && !string.IsNullOrWhiteSpace(verResult.Output))
            {
                info.ServerVersion = verResult.Output.Trim();
                info.IsAvailable = true;
            }
            else
            {
                info.IsAvailable = false;
                info.ErrorMessage = result.Error;
            }
            return info;
        }

        try
        {
            using var doc = JsonDocument.Parse(result.Output);
            var root = doc.RootElement;

            info.IsAvailable = true;
            info.ServerVersion = root.TryGetProperty("ServerVersion", out var sv) ? sv.GetString() ?? "Unknown" : "Unknown";
            info.StorageDriver = root.TryGetProperty("Driver", out var dr) ? dr.GetString() ?? "" : "";
            info.OperatingSystem = root.TryGetProperty("OperatingSystem", out var os) ? os.GetString() ?? "" : "";
            info.Architecture = root.TryGetProperty("Architecture", out var arch) ? arch.GetString() ?? "" : "";

            if (root.TryGetProperty("Containers", out var cTotal) && cTotal.TryGetInt32(out int total))
                info.TotalContainers = total;

            if (root.TryGetProperty("ContainersRunning", out var cRun) && cRun.TryGetInt32(out int running))
                info.RunningContainers = running;

            if (root.TryGetProperty("ContainersPaused", out var cPause) && cPause.TryGetInt32(out int paused))
                info.PausedContainers = paused;

            if (root.TryGetProperty("ContainersStopped", out var cStop) && cStop.TryGetInt32(out int stopped))
                info.StoppedContainers = stopped;

            if (root.TryGetProperty("Images", out var imgs) && imgs.TryGetInt32(out int imgCount))
                info.ImagesCount = imgCount;
        }
        catch (Exception ex)
        {
            info.IsAvailable = true;
            info.ErrorMessage = ex.Message;
        }

        return info;
    }

    public async Task<Dictionary<string, ContainerStatsInfo>> GetContainerStatsAsync(CancellationToken cancellationToken = default)
    {
        var dict = new Dictionary<string, ContainerStatsInfo>(StringComparer.OrdinalIgnoreCase);
        string cmd = "docker stats --no-stream --format json";
        var result = await _sshService.ExecuteCommandAsync(cmd, cancellationToken);

        if (!result.IsSuccess || string.IsNullOrWhiteSpace(result.Output))
        {
            return dict;
        }

        string[] lines = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var line in lines)
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;

                string id = root.TryGetProperty("ID", out var idProp) ? idProp.GetString() ?? "" : (root.TryGetProperty("Container", out var cProp) ? cProp.GetString() ?? "" : "");
                string name = root.TryGetProperty("Name", out var nProp) ? nProp.GetString() ?? "" : "";

                var stats = new ContainerStatsInfo
                {
                    ContainerId = id,
                    Name = name,
                    CpuPercentage = root.TryGetProperty("CPUPerc", out var cpu) ? cpu.GetString() ?? "" : "",
                    MemoryUsage = root.TryGetProperty("MemUsage", out var memU) ? memU.GetString() ?? "" : "",
                    MemoryPercentage = root.TryGetProperty("MemPerc", out var memP) ? memP.GetString() ?? "" : "",
                    NetIO = root.TryGetProperty("NetIO", out var net) ? net.GetString() ?? "" : "",
                    BlockIO = root.TryGetProperty("BlockIO", out var block) ? block.GetString() ?? "" : "",
                    Pids = root.TryGetProperty("PIDs", out var pids) ? pids.GetString() ?? "" : ""
                };

                if (!string.IsNullOrWhiteSpace(id)) dict[id] = stats;
                if (!string.IsNullOrWhiteSpace(name)) dict[name] = stats;
            }
            catch
            {
                // Continue parsing other lines
            }
        }

        return dict;
    }

    public async Task<CommandResult> ExecuteComposeActionAsync(
        string composeFilePath,
        DockerComposeAction action,
        string? serviceName = null,
        CancellationToken cancellationToken = default)
    {
        bool hasService = !string.IsNullOrWhiteSpace(serviceName);
        string target = hasService ? $" {ShellQuote.Quote(serviceName!)}" : "";

        string actionCmd = action switch
        {
            DockerComposeAction.Up => $"up -d{target}",
            DockerComposeAction.Down => hasService ? $"stop{target}" : "down", // If single service, stop it cleanly
            DockerComposeAction.Stop => $"stop{target}",
            DockerComposeAction.Start => $"start{target}",
            DockerComposeAction.Restart => $"restart{target}",
            DockerComposeAction.Build => $"build{target}",
            DockerComposeAction.Pause => $"pause{target}",
            DockerComposeAction.Unpause => $"unpause{target}",
            DockerComposeAction.Status => $"ps{target}",
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };

        string fullCmd = $"docker compose -f {ShellQuote.Quote(composeFilePath)} {actionCmd}";
        return await _sshService.ExecuteCommandAsync(fullCmd, cancellationToken);
    }

    public async Task<CommandResult> ExecuteContainerActionAsync(
        string containerIdOrName,
        string action,
        CancellationToken cancellationToken = default)
    {
        string normalizedAction = action.Trim().ToLowerInvariant() switch
        {
            "start" => "start",
            "stop" => "stop",
            "restart" => "restart",
            "pause" => "pause",
            "unpause" => "unpause",
            "kill" => "kill",
            "rm" or "remove" => "rm -f",
            _ => throw new ArgumentException($"Unsupported container action: '{action}'", nameof(action))
        };

        string cmd = $"docker {normalizedAction} {ShellQuote.Quote(containerIdOrName)}";
        return await _sshService.ExecuteCommandAsync(cmd, cancellationToken);
    }

    public async Task<string> GetContainerLogsAsync(
        string containerName,
        int lineCount = 100,
        bool includeTimestamps = false,
        CancellationToken cancellationToken = default)
    {
        var boundedLineCount = Math.Clamp(lineCount, 1, 10000);
        string tsFlag = includeTimestamps ? "-t " : "";
        string cmd = $"docker logs {tsFlag}--tail {boundedLineCount} {ShellQuote.Quote(containerName)}";
        var result = await _sshService.ExecuteCommandAsync(cmd, cancellationToken);
        return string.IsNullOrWhiteSpace(result.Output) ? result.Error : result.Output;
    }

    // -------------------------------------------------------------------------
    // JSON Stream Parsing Helper
    // -------------------------------------------------------------------------

    private static List<ContainerInfo> ParseContainerJsonLines(string jsonOutput)
    {
        var containers = new List<ContainerInfo>();
        string[] lines = jsonOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var line in lines)
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;

                string id = root.TryGetProperty("ID", out var idProp) ? idProp.GetString() ?? "" : "";
                string name = root.TryGetProperty("Names", out var namesProp)
                    ? namesProp.GetString() ?? ""
                    : (root.TryGetProperty("Name", out var nameProp) ? nameProp.GetString() ?? "" : "");

                string state = root.TryGetProperty("State", out var stateProp) ? stateProp.GetString() ?? "" : "";
                string status = root.TryGetProperty("Status", out var statusProp) ? statusProp.GetString() ?? "" : "";
                string image = root.TryGetProperty("Image", out var imgProp) ? imgProp.GetString() ?? "" : "";
                string ports = root.TryGetProperty("Ports", out var portsProp) ? portsProp.GetString() ?? "" : "";
                string created = root.TryGetProperty("CreatedAt", out var createdProp)
                    ? createdProp.GetString() ?? ""
                    : (root.TryGetProperty("Created", out var cProp) ? cProp.GetString() ?? "" : "");

                containers.Add(new ContainerInfo
                {
                    Id = id,
                    Names = name,
                    Image = image,
                    State = state,
                    Status = status,
                    Ports = ports,
                    Created = created
                });
            }
            catch
            {
                // Fallback for non-JSON lines or commentary
            }
        }

        return containers;
    }
}
