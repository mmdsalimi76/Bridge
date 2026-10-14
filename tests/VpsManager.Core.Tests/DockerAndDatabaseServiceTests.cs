using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using VpsManager.Core.Models;
using VpsManager.Core.Services;
using Xunit;

namespace VpsManager.Core.Tests;

public class DockerAndDatabaseServiceTests
{
    [Fact]
    public async Task DatabaseService_GetBackupsAsync_ParsesFindOutputAndBackupScope()
    {
        var mockSsh = new Mock<ISshService>();
        mockSsh.Setup(s => s.ExecuteCommandAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult
            {
                ExitCode = 0,
                Output = "2048|2026-10-08 20:30:00|/var/backups/vpsmanager/backup_folder_media_20261008.tar.gz\n" +
                        "4096|2026-10-08 21:30:00|/var/backups/vpsmanager/backup_full_app_20261008.tar.gz\n"
            });

        var service = new DatabaseService(mockSsh.Object);

        var backups = (await service.GetBackupsAsync()).ToList();

        Assert.Equal(2, backups.Count);
        Assert.Equal(BackupScope.Full, backups[0].Scope);
        Assert.Equal("backup_full_app_20261008.tar.gz", backups[0].FileName);
        Assert.Equal(BackupScope.Folder, backups[1].Scope);
        Assert.Contains("find", mockSsh.Invocations[0].Arguments[0] as string, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DatabaseService_GetBackupsAsync_ThrowsWhenRemoteListingFails()
    {
        var mockSsh = new Mock<ISshService>();
        mockSsh.Setup(s => s.ExecuteCommandAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult { ExitCode = 1, Error = "permission denied" });
        var service = new DatabaseService(mockSsh.Object);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetBackupsAsync());

        Assert.Contains("permission denied", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DockerService_GetContainersAsync_ParsesJsonOutput()
    {
        var mockSsh = new Mock<ISshService>();
        string mockJson = "{\"ID\":\"abc123456789\",\"Name\":\"web_app\",\"Image\":\"django:latest\",\"State\":\"running\",\"Status\":\"Up 2 hours\",\"Ports\":\"0.0.0.0:8000->8000/tcp\"}";

        mockSsh.Setup(s => s.ExecuteCommandAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(new CommandResult { ExitCode = 0, Output = mockJson });

        var dockerService = new DockerService(mockSsh.Object);
        var containers = await dockerService.GetContainersAsync("/app/docker-compose.yml");

        Assert.Single(containers);
        Assert.Equal("web_app", containers[0].Names);
        Assert.Equal("running", containers[0].State);
        Assert.True(containers[0].IsRunning);
        Assert.Equal("RUNNING", containers[0].StatusBadgeText);
    }

    [Fact]
    public async Task DockerService_GetAllHostContainersAsync_ParsesHostJsonOutput()
    {
        var mockSsh = new Mock<ISshService>();
        string mockJson = "{\"ID\":\"1234567890ab\",\"Names\":\"standalone_redis\",\"Image\":\"redis:alpine\",\"State\":\"running\",\"Status\":\"Up 5 hours\",\"Ports\":\"6379/tcp\"}\n" +
                          "{\"ID\":\"cdef12345678\",\"Names\":\"old_postgres\",\"Image\":\"postgres:15\",\"State\":\"exited\",\"Status\":\"Exited (0) 10 minutes ago\",\"Ports\":\"\"}";

        mockSsh.Setup(s => s.ExecuteCommandAsync(It.Is<string>(cmd => cmd.Contains("docker ps -a")), It.IsAny<CancellationToken>()))
               .ReturnsAsync(new CommandResult { ExitCode = 0, Output = mockJson });

        var dockerService = new DockerService(mockSsh.Object);
        var containers = await dockerService.GetAllHostContainersAsync();

        Assert.Equal(2, containers.Count);
        Assert.Equal("standalone_redis", containers[0].Names);
        Assert.True(containers[0].IsRunning);

        Assert.Equal("old_postgres", containers[1].Names);
        Assert.True(containers[1].IsExited);
        Assert.Equal("EXITED", containers[1].StatusBadgeText);
    }

    [Fact]
    public async Task DockerService_GetDaemonInfoAsync_ParsesDaemonJson()
    {
        var mockSsh = new Mock<ISshService>();
        string mockJson = "{\"ServerVersion\":\"27.1.1\",\"Containers\":5,\"ContainersRunning\":3,\"ContainersPaused\":0,\"ContainersStopped\":2,\"Images\":12,\"Driver\":\"overlay2\",\"OperatingSystem\":\"Ubuntu 24.04 LTS\",\"Architecture\":\"x86_64\"}";

        mockSsh.Setup(s => s.ExecuteCommandAsync(It.Is<string>(cmd => cmd.Contains("docker info")), It.IsAny<CancellationToken>()))
               .ReturnsAsync(new CommandResult { ExitCode = 0, Output = mockJson });

        var dockerService = new DockerService(mockSsh.Object);
        var info = await dockerService.GetDaemonInfoAsync();

        Assert.True(info.IsAvailable);
        Assert.Equal("27.1.1", info.ServerVersion);
        Assert.Equal(5, info.TotalContainers);
        Assert.Equal(3, info.RunningContainers);
        Assert.Equal(2, info.StoppedContainers);
        Assert.Equal(12, info.ImagesCount);
        Assert.Equal("overlay2", info.StorageDriver);
        Assert.Equal("Ubuntu 24.04 LTS", info.OperatingSystem);
    }

    [Fact]
    public async Task DockerService_GetContainerStatsAsync_ParsesStatsJson()
    {
        var mockSsh = new Mock<ISshService>();
        string mockJson = "{\"ID\":\"abc123456789\",\"Name\":\"web_app\",\"CPUPerc\":\"1.25%\",\"MemUsage\":\"45.2MiB / 1.95GiB\",\"MemPerc\":\"2.26%\",\"NetIO\":\"1.2MB / 540KB\",\"BlockIO\":\"0B / 12KB\",\"PIDs\":\"4\"}";

        mockSsh.Setup(s => s.ExecuteCommandAsync(It.Is<string>(cmd => cmd.Contains("docker stats")), It.IsAny<CancellationToken>()))
               .ReturnsAsync(new CommandResult { ExitCode = 0, Output = mockJson });

        var dockerService = new DockerService(mockSsh.Object);
        var stats = await dockerService.GetContainerStatsAsync();

        Assert.True(stats.ContainsKey("web_app"));
        Assert.Equal("1.25%", stats["web_app"].CpuPercentage);
        Assert.Equal("45.2MiB / 1.95GiB", stats["web_app"].MemoryUsage);
        Assert.Equal("2.26%", stats["web_app"].MemoryPercentage);
    }

    [Fact]
    public async Task DockerService_ExecuteComposeAction_StopSingleService_DoesNotBringDownStack()
    {
        var mockSsh = new Mock<ISshService>();
        string? executedCommand = null;

        mockSsh.Setup(s => s.ExecuteCommandAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .Callback<string, CancellationToken>((cmd, token) => executedCommand = cmd)
               .ReturnsAsync(new CommandResult { ExitCode = 0, Output = "OK" });

        var dockerService = new DockerService(mockSsh.Object);
        
        // Stopping single service with DockerComposeAction.Stop
        await dockerService.ExecuteComposeActionAsync("docker-compose.yml", DockerComposeAction.Stop, "web");
        Assert.Equal("docker compose -f 'docker-compose.yml' stop 'web'", executedCommand);

        // Stopping single service with DockerComposeAction.Down should also safely stop, NOT run 'down'
        await dockerService.ExecuteComposeActionAsync("docker-compose.yml", DockerComposeAction.Down, "web");
        Assert.Equal("docker compose -f 'docker-compose.yml' stop 'web'", executedCommand);

        // Entire stack Down should execute 'down'
        await dockerService.ExecuteComposeActionAsync("docker-compose.yml", DockerComposeAction.Down, null);
        Assert.Equal("docker compose -f 'docker-compose.yml' down", executedCommand);
    }

    [Fact]
    public async Task DockerService_ExecuteContainerActionAsync_ValidatesAndFormatsCommand()
    {
        var mockSsh = new Mock<ISshService>();
        string? executedCommand = null;

        mockSsh.Setup(s => s.ExecuteCommandAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .Callback<string, CancellationToken>((cmd, token) => executedCommand = cmd)
               .ReturnsAsync(new CommandResult { ExitCode = 0, Output = "OK" });

        var dockerService = new DockerService(mockSsh.Object);

        await dockerService.ExecuteContainerActionAsync("redis-cache", "restart");
        Assert.Equal("docker restart 'redis-cache'", executedCommand);

        await dockerService.ExecuteContainerActionAsync("redis-cache", "stop");
        Assert.Equal("docker stop 'redis-cache'", executedCommand);

        await dockerService.ExecuteContainerActionAsync("redis-cache", "start");
        Assert.Equal("docker start 'redis-cache'", executedCommand);

        await Assert.ThrowsAsync<ArgumentException>(() => dockerService.ExecuteContainerActionAsync("redis-cache", "invalid_action"));
    }

    [Fact]
    public void ContainerInfo_StateKind_AndReactivity_CorrectlyEvaluated()
    {
        var c = new ContainerInfo
        {
            Id = "1234567890abcdef1234",
            Names = "api_server",
            State = "running"
        };

        Assert.Equal(ContainerStateKind.Running, c.StateKind);
        Assert.True(c.IsRunning);
        Assert.False(c.IsExited);
        Assert.True(c.CanStop);
        Assert.False(c.CanStart);
        Assert.Equal("1234567890ab", c.ShortId);
        Assert.Equal("RUNNING", c.StatusBadgeText);

        c.State = "exited (137)";
        Assert.Equal(ContainerStateKind.Exited, c.StateKind);
        Assert.False(c.IsRunning);
        Assert.True(c.IsExited);
        Assert.True(c.CanStart);
        Assert.False(c.CanStop);
        Assert.Equal("EXITED", c.StatusBadgeText);

        c.State = "paused";
        Assert.Equal(ContainerStateKind.Paused, c.StateKind);
        Assert.True(c.IsPaused);
        Assert.True(c.CanUnpause);
        Assert.Equal("PAUSED", c.StatusBadgeText);
    }

    [Fact]
    public async Task DatabaseService_CreateRemoteBackup_GeneratesBackupInfo()
    {
        var mockSsh = new Mock<ISshService>();
        mockSsh.Setup(s => s.ExecuteCommandAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(new CommandResult { ExitCode = 0, Output = "123\n" });
        mockSsh.Setup(s => s.ExecuteCommandWithInputAsync(It.IsAny<string>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(new CommandResult { ExitCode = 0, Output = "" });

        mockSsh.Setup(s => s.ExecuteCommandAsync(It.Is<string>(cmd => cmd.Contains("md5sum")), It.IsAny<CancellationToken>()))
               .ReturnsAsync(new CommandResult { ExitCode = 0, Output = "e10adc3949ba59abbe56e057f20f883e\n" });

        var dbService = new DatabaseService(mockSsh.Object);
        var backup = await dbService.CreateRemoteBackupAsync("postgres", "postgres", "mydb");

        Assert.NotNull(backup);
        Assert.Equal("e10adc3949ba59abbe56e057f20f883e", backup.Md5Hash);
        Assert.Contains("mydb", backup.FileName);
        Assert.Equal(BackupScope.Database, backup.Scope);
    }

    [Fact]
    public async Task DatabaseService_CreateRemoteBackup_DoesNotPutPasswordInRemoteCommand()
    {
        const string password = "sensitive'$(touch /tmp/injected)";
        string commandWithInput = string.Empty;
        string credentialFileContent = string.Empty;

        var mockSsh = new Mock<ISshService>();
        mockSsh.Setup(service => service.ExecuteCommandWithInputAsync(
                It.IsAny<string>(),
                It.IsAny<ReadOnlyMemory<byte>>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, ReadOnlyMemory<byte>, CancellationToken>((command, input, _) =>
            {
                commandWithInput = command;
                credentialFileContent = System.Text.Encoding.UTF8.GetString(input.Span);
            })
            .ReturnsAsync(new CommandResult { ExitCode = 0 });
        mockSsh.Setup(service => service.ExecuteCommandAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult { ExitCode = 0, Output = "10" });

        var service = new DatabaseService(mockSsh.Object);
        await service.CreateRemoteBackupAsync("mysql", "app", password, "app_db", "MySQL");

        Assert.DoesNotContain(password, commandWithInput, StringComparison.Ordinal);
        Assert.Contains("--defaults-extra-file", commandWithInput, StringComparison.Ordinal);
        Assert.Contains(password, credentialFileContent, StringComparison.Ordinal);
        Assert.All(mockSsh.Invocations
            .Where(invocation => invocation.Method.Name == nameof(ISshService.ExecuteCommandAsync))
            .Select(invocation => invocation.Arguments[0] as string ?? string.Empty),
            command => Assert.DoesNotContain(password, command, StringComparison.Ordinal));
    }

    [Fact]
    public async Task DatabaseService_MongoDumpStreamsCredentialConfigInsteadOfCommandLinePassword()
    {
        const string password = "mongo-sensitive-password";
        string commandWithInput = string.Empty;
        string credentialConfig = string.Empty;

        var mockSsh = new Mock<ISshService>();
        mockSsh.Setup(service => service.ExecuteCommandWithInputAsync(
                It.IsAny<string>(),
                It.IsAny<ReadOnlyMemory<byte>>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, ReadOnlyMemory<byte>, CancellationToken>((command, input, _) =>
            {
                commandWithInput = command;
                credentialConfig = System.Text.Encoding.UTF8.GetString(input.Span);
            })
            .ReturnsAsync(new CommandResult { ExitCode = 0 });
        mockSsh.Setup(service => service.ExecuteCommandAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult { ExitCode = 0, Output = "10" });

        var service = new DatabaseService(mockSsh.Object);
        await service.CreateRemoteBackupAsync("mongo", "app", password, "app_db", "MongoDB");

        Assert.DoesNotContain(password, commandWithInput, StringComparison.Ordinal);
        Assert.Contains("mongodump --config", commandWithInput, StringComparison.Ordinal);
        Assert.Contains(password, credentialConfig, StringComparison.Ordinal);
        Assert.Contains("\"authenticationDatabase\":\"app_db\"", credentialConfig, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DatabaseService_SetupScheduledBackup_EncryptsCredentialAndKeepsItOutOfScript()
    {
        const string password = "scheduled-secret";
        var remoteCommands = new List<string>();
        var streamedInputs = new List<string>();

        var mockSsh = new Mock<ISshService>();
        mockSsh.Setup(service => service.ExecuteCommandAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((command, _) => remoteCommands.Add(command))
            .ReturnsAsync(new CommandResult { ExitCode = 0 });
        mockSsh.Setup(service => service.ExecuteCommandWithInputAsync(
                It.IsAny<string>(),
                It.IsAny<ReadOnlyMemory<byte>>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, ReadOnlyMemory<byte>, CancellationToken>((command, input, _) =>
            {
                remoteCommands.Add(command);
                streamedInputs.Add(System.Text.Encoding.UTF8.GetString(input.Span));
            })
            .ReturnsAsync(new CommandResult { ExitCode = 0 });

        var service = new DatabaseService(mockSsh.Object);
        var result = await service.SetupScheduledBackupAsync(
            "0 2 * * *",
            BackupScope.Database,
            "postgres",
            "app",
            password,
            "app_db",
            "PostgreSQL",
            "/var/www/app");

        Assert.True(result.IsSuccess);
        Assert.Contains(remoteCommands, command => command.Contains("systemd-creds encrypt", StringComparison.Ordinal));
        Assert.Contains(remoteCommands, command => command.Contains("crontab -", StringComparison.Ordinal));
        Assert.Contains(streamedInputs, input => input.Contains(password, StringComparison.Ordinal));
        Assert.Contains(streamedInputs, input => input.Contains("systemd-creds decrypt", StringComparison.Ordinal));
        Assert.All(remoteCommands, command => Assert.DoesNotContain(password, command, StringComparison.Ordinal));
        Assert.All(streamedInputs.Where(input => input.StartsWith("#!/bin/bash", StringComparison.Ordinal)),
            script => Assert.DoesNotContain(password, script, StringComparison.Ordinal));
    }

    [Fact]
    public async Task DatabaseService_SetupCronJob_RejectsShellSyntaxInSchedule()
    {
        var mockSsh = new Mock<ISshService>();
        var service = new DatabaseService(mockSsh.Object);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.SetupCronJobAsync("0 2 * * *; touch /tmp/injected", "echo safe"));
        Assert.Empty(mockSsh.Invocations);
    }

    [Fact]
    public async Task DatabaseService_CreateFolderBackup_ExecutesTarAndReturnsFolderScope()
    {
        var mockSsh = new Mock<ISshService>();
        string executedCmd = string.Empty;

        mockSsh.Setup(s => s.ExecuteCommandAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .Callback<string, CancellationToken>((cmd, _) => executedCmd = cmd)
               .ReturnsAsync(new CommandResult { ExitCode = 0, Output = "123456\n" });

        var dbService = new DatabaseService(mockSsh.Object);
        var backup = await dbService.CreateFolderBackupAsync("/var/www/uploads", "media_assets");

        Assert.NotNull(backup);
        Assert.Equal(BackupScope.Folder, backup.Scope);
        Assert.Contains("backup_folder_media_assets", backup.FileName);
    }

    [Fact]
    public async Task DatabaseService_GetCronHistory_ParsesStructuredLogLines()
    {
        var mockSsh = new Mock<ISshService>();
        string mockLog = 
            "2026-10-08 02:00:00|SUCCESS|Database|app_db|/var/backups/vpsmanager/backup_db_postgres_app_db.sql.gz|15482910|OK\n" +
            "2026-10-08 03:00:00|FAILED|Folder|uploads|-|0|tar exited with error code 1\n";

        mockSsh.Setup(s => s.ExecuteCommandAsync(It.Is<string>(cmd => cmd.Contains("cron_history.log")), It.IsAny<CancellationToken>()))
               .ReturnsAsync(new CommandResult { ExitCode = 0, Output = mockLog });

        var dbService = new DatabaseService(mockSsh.Object);
        var history = (await dbService.GetCronHistoryAsync()).ToList();

        Assert.Equal(2, history.Count);
        // Ordered descending: 03:00:00 is first
        Assert.False(history[0].IsSuccess);
        Assert.Equal("Folder", history[0].Scope);
        Assert.Contains("error code 1", history[0].Message);

        // 02:00:00 is second
        Assert.True(history[1].IsSuccess);
        Assert.Equal("Database", history[1].Scope);
        Assert.Equal("app_db", history[1].TargetName);
    }

    [Theory]
    [InlineData(BackupScope.Database, true, false, false)]
    [InlineData(BackupScope.Folder, false, true, false)]
    [InlineData(BackupScope.Full, false, false, true)]
    public void DatabaseBackupInfo_ScopeBooleans_MatchExpectedScope(
        BackupScope scope,
        bool expectDatabase,
        bool expectFolder,
        bool expectFull)
    {
        var backup = new DatabaseBackupInfo { Scope = scope };

        Assert.Equal(expectDatabase, backup.IsDatabaseScope);
        Assert.Equal(expectFolder, backup.IsFolderScope);
        Assert.Equal(expectFull, backup.IsFullScope);
    }

    [Theory]
    [InlineData(true, false, "SUCCESS")]
    [InlineData(false, true, "FAILED")]
    public void CronExecutionRecord_IsFailed_InvertsIsSuccess(bool isSuccess, bool expectFailed, string expectStatus)
    {
        var record = new CronExecutionRecord { IsSuccess = isSuccess };

        Assert.Equal(expectFailed, record.IsFailed);
        Assert.Equal(expectStatus, record.StatusText);
    }
}
