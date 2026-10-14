using System.Threading;
using System.Threading.Tasks;
using Moq;
using VpsManager.Core.Models;
using VpsManager.Core.Services;

namespace VpsManager.Core.Tests;

public class RuntimeInventoryServiceTests
{
    [Fact]
    public async Task GetInventoryAsync_ResolvesComposeFileAgainstProjectPathAndIncludesNativeProcesses()
    {
        var mockSsh = new Mock<ISshService>();
        mockSsh.Setup(service => service.ExecuteCommandAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string command, CancellationToken _) =>
            {
                if (command.StartsWith(
                    "docker compose -f '/var/www/with-docker/docker-compose.yml'",
                    StringComparison.Ordinal))
                {
                    return Task.FromResult(new CommandResult
                    {
                        ExitCode = 0,
                        Output = "with-docker-hello-1\trunning\tpython:3.12-slim\tUp 1 minute\t0.0.0.0:8081->8080/tcp\n"
                    });
                }

                if (command.StartsWith("ps -eo", StringComparison.Ordinal))
                {
                    return Task.FromResult(new CommandResult
                    {
                        ExitCode = 0,
                        Output = "30901 python3 00:01 0.0 0.1 python3 /var/www/no-docker/server.py\n"
                    });
                }

                return Task.FromResult(new CommandResult { ExitCode = 0 });
            });

        var service = new RuntimeInventoryService(mockSsh.Object);

        var inventory = await service.GetInventoryAsync("docker-compose.yml", "/var/www/with-docker");

        Assert.Contains(inventory, runtime =>
            runtime.Name == "with-docker-hello-1"
            && runtime.Source == "Docker"
            && runtime.Status == "running"
            && runtime.Image == "python:3.12-slim"
            && runtime.Ports.Contains("8081", StringComparison.Ordinal));
        Assert.Contains(inventory, runtime =>
            runtime.Name == "python3 (30901)"
            && runtime.Source == "Process"
            && runtime.Status == "running");
    }

    [Fact]
    public async Task GetInventoryAsync_PreservesAbsoluteComposeFilePath()
    {
        var mockSsh = new Mock<ISshService>();
        string? dockerCommand = null;
        mockSsh.Setup(service => service.ExecuteCommandAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((command, _) =>
            {
                if (command.StartsWith("docker compose", StringComparison.Ordinal))
                {
                    dockerCommand = command;
                }
            })
            .ReturnsAsync(new CommandResult { ExitCode = 0 });

        var service = new RuntimeInventoryService(mockSsh.Object);

        await service.GetInventoryAsync("/srv/app/compose.yml", "/srv/app");

        Assert.StartsWith("docker compose -f '/srv/app/compose.yml'", dockerCommand);
    }

    [Fact]
    public async Task GetLogsAsync_UsesBoundedRequestedLineCount()
    {
        var mockSsh = new Mock<ISshService>();
        string? executedCommand = null;
        mockSsh.Setup(service => service.ExecuteCommandAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((command, _) => executedCommand = command)
            .ReturnsAsync(new CommandResult { ExitCode = 0, Output = "recent logs" });

        var service = new RuntimeInventoryService(mockSsh.Object);

        var logs = await service.GetLogsAsync(new RuntimeInfo
        {
            Name = "web",
            LogsHint = "docker logs --tail {lineCount} 'web'"
        }, 2500);

        Assert.Equal("recent logs", logs);
        Assert.Equal("docker logs --tail 1000 'web'", executedCommand);
    }
}
