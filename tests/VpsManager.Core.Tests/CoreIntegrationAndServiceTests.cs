using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using VpsManager.Core.Models;
using VpsManager.Core.Services;
using Xunit;

namespace VpsManager.Core.Tests;

public class CoreIntegrationAndServiceTests
{
    [Fact]
    public async Task SshService_Connect_ThrowsOnMissingAuthCredentials()
    {
        using var sshService = new SshService();
        var emptyConfig = new SshConnectionConfig
        {
            Host = "127.0.0.1",
            Port = 22,
            Username = "root"
        };

        await Assert.ThrowsAsync<ArgumentException>(() => sshService.ConnectAsync(emptyConfig));
    }

    [Fact]
    public async Task DockerService_GetContainerLogs_HandlesEmptyLogsGracefully()
    {
        var mockSsh = new Mock<ISshService>();
        mockSsh.Setup(s => s.ExecuteCommandAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(new CommandResult { ExitCode = 0, Output = "", Error = "No log output available." });

        var dockerService = new DockerService(mockSsh.Object);
        string logs = await dockerService.GetContainerLogsAsync("my_container");

        Assert.Equal("No log output available.", logs);
    }

    [Fact]
    public async Task DatabaseService_RestoreBackup_GeneratesCorrectPsqlCommand()
    {
        var mockSsh = new Mock<ISshService>();
        string executedCmd = string.Empty;

        mockSsh.Setup(s => s.ExecuteCommandAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(new CommandResult { ExitCode = 0, Output = "Restored" });
        mockSsh.Setup(s => s.ExecuteCommandWithInputAsync(
                   It.IsAny<string>(),
                   It.IsAny<ReadOnlyMemory<byte>>(),
                   It.IsAny<CancellationToken>()))
               .Callback<string, ReadOnlyMemory<byte>, CancellationToken>((cmd, _, _) => executedCmd = cmd)
               .ReturnsAsync(new CommandResult { ExitCode = 0, Output = "Restored" });

        var dbService = new DatabaseService(mockSsh.Object);
        var result = await dbService.RestoreBackupAsync("postgres", "postgres", "app_db", "/tmp/backup.sql.gz");

        Assert.True(result.IsSuccess);
        // Values are shell-quoted with single quotes so profile data cannot alter the command.
        Assert.Contains("docker exec -i 'postgres' sh -c", executedCmd);
        Assert.Contains("cat > \"$credential_file\"", executedCmd);

        var restoreCommand = mockSsh.Invocations
            .Where(invocation => invocation.Method.Name == nameof(ISshService.ExecuteCommandAsync))
            .Select(invocation => invocation.Arguments[0] as string ?? string.Empty)
            .First(command => command.Contains("gunzip -c", StringComparison.Ordinal));
        Assert.Contains("gunzip", restoreCommand);
        Assert.Contains("/tmp/backup.sql.gz", restoreCommand);
        Assert.Contains("psql", restoreCommand);
        Assert.Contains("postgres", restoreCommand);
        Assert.Contains("app_db", restoreCommand);
    }
}
