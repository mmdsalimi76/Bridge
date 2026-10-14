using VpsManager.Core.Models;

namespace VpsManager.Core.Services;

public interface IDockerService
{
    Task<List<ContainerInfo>> GetContainersAsync(string composeFilePath, CancellationToken cancellationToken = default);
    Task<List<ContainerInfo>> GetAllHostContainersAsync(CancellationToken cancellationToken = default);
    Task<DockerDaemonInfo> GetDaemonInfoAsync(CancellationToken cancellationToken = default);
    Task<Dictionary<string, ContainerStatsInfo>> GetContainerStatsAsync(CancellationToken cancellationToken = default);
    Task<CommandResult> ExecuteComposeActionAsync(string composeFilePath, DockerComposeAction action, string? serviceName = null, CancellationToken cancellationToken = default);
    Task<CommandResult> ExecuteContainerActionAsync(string containerIdOrName, string action, CancellationToken cancellationToken = default);
    Task<string> GetContainerLogsAsync(string containerName, int lineCount = 100, bool includeTimestamps = false, CancellationToken cancellationToken = default);
}
