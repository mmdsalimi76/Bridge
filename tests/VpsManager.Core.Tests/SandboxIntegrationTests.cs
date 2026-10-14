using System;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;
using VpsManager.Core.Models;
using VpsManager.Core.Services;
using VpsManager.Core.Services.Adapters;
using Xunit;

namespace VpsManager.Core.Tests;

public class SandboxIntegrationTests
{
    private static readonly SshConnectionConfig SandboxConfig = new()
    {
        Host = "127.0.0.1",
        Port = 2222,
        Username = "root",
        Password = "rootpass"
    };

    private static bool IsSandboxAvailable()
    {
        try
        {
            using var client = new TcpClient();
            var ar = client.BeginConnect(SandboxConfig.Host, SandboxConfig.Port, null, null);
            bool success = ar.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(1));
            return success && client.Connected;
        }
        catch
        {
            return false;
        }
    }

    private static string GetFixturePath(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "VpsManager.slnx")))
        {
            dir = dir.Parent;
        }
        return dir != null ? Path.Combine(dir.FullName, "test-fixtures", relative) : string.Empty;
    }

    [Fact]
    public async Task Sandbox_SshService_ExecutesCommandsSuccessfully()
    {
        if (!IsSandboxAvailable())
        {
            return;
        }

        using var sshService = new SshService();
        await sshService.ConnectAsync(SandboxConfig);
        Assert.True(sshService.IsConnected);

        var whoami = await sshService.ExecuteCommandAsync("whoami");
        Assert.True(whoami.IsSuccess);
        Assert.Equal("root", whoami.Output.Trim());

        var uname = await sshService.ExecuteCommandAsync("uname -s");
        Assert.True(uname.IsSuccess);
        Assert.Contains("Linux", uname.Output);
    }

    [Fact]
    public async Task Sandbox_SystemMetricsService_ParsesRealTelemetry()
    {
        if (!IsSandboxAvailable())
        {
            return;
        }

        using var sshService = new SshService();
        await sshService.ConnectAsync(SandboxConfig);

        var metricsService = new SystemMetricsService(sshService);
        var metrics = await metricsService.GetMetricsAsync();

        Assert.NotNull(metrics);
        Assert.True(metrics.RamTotalGb > 0, "Total RAM should be greater than 0");
        Assert.True(metrics.DiskTotalGb > 0, "Total Disk should be greater than 0");
        Assert.False(string.IsNullOrWhiteSpace(metrics.Uptime), "Uptime should be populated");
    }

    [Fact]
    public async Task Sandbox_PatchService_UploadsAndDeletesFileOverSftp()
    {
        if (!IsSandboxAvailable())
        {
            return;
        }

        using var sshService = new SshService();
        await sshService.ConnectAsync(SandboxConfig);

        // Create a temporary local file to patch
        string tempLocalFile = Path.Combine(Path.GetTempPath(), $"patch_test_{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(tempLocalFile, "Bridge Sandbox Hot Patch Content 2026");

        try
        {
            string remotePath = "/var/www/my_app/hotpatch_test.txt";

            // Upload via SFTP stream using SshService
            using (var stream = File.OpenRead(tempLocalFile))
            {
                await sshService.UploadFileAsync(stream, remotePath);
            }

            // Verify content remotely
            var catResult = await sshService.ExecuteCommandAsync($"cat {remotePath}");
            Assert.True(catResult.IsSuccess);
            Assert.Contains("Bridge Sandbox Hot Patch Content 2026", catResult.Output);

            // Clean up
            await sshService.ExecuteCommandAsync($"rm -f {remotePath}");
        }
        finally
        {
            if (File.Exists(tempLocalFile))
            {
                File.Delete(tempLocalFile);
            }
        }
    }

    [Fact]
    public async Task Sandbox_DatabaseService_FolderBackup_CreatesArchiveInVault()
    {
        if (!IsSandboxAvailable())
        {
            return;
        }

        using var sshService = new SshService();
        await sshService.ConnectAsync(SandboxConfig);

        // Ensure source files exist in /var/www/uploads
        await sshService.ExecuteCommandAsync("mkdir -p /var/www/uploads && echo 'test document' > /var/www/uploads/test.txt");

        var dbService = new DatabaseService(sshService);
        var backup = await dbService.CreateFolderBackupAsync("/var/www/uploads", "media_test");

        Assert.NotNull(backup);
        Assert.False(string.IsNullOrWhiteSpace(backup.RemotePath));

        // Verify the file exists on the remote system in /var/backups/vpsmanager
        var checkFile = await sshService.ExecuteCommandAsync($"ls -lh {backup.RemotePath}");
        Assert.True(checkFile.IsSuccess);
        Assert.Contains(backup.FileName, checkFile.Output);

        // Clean up created backup file
        await sshService.ExecuteCommandAsync($"rm -f {backup.RemotePath}");
    }

    [Fact]
    public async Task Sandbox_DockerService_DiscoversContainers()
    {
        if (!IsSandboxAvailable())
        {
            return;
        }

        using var sshService = new SshService();
        await sshService.ConnectAsync(SandboxConfig);

        var dockerService = new DockerService(sshService);
        var containers = await dockerService.GetAllHostContainersAsync();

        Assert.NotNull(containers);
        // At minimum, vps-test-node itself is discovered via docker ps
        Assert.NotEmpty(containers);
    }

    [Fact]
    public async Task Sandbox_CronJob_SetupAndVerify()
    {
        if (!IsSandboxAvailable())
        {
            return;
        }

        using var sshService = new SshService();
        await sshService.ConnectAsync(SandboxConfig);

        var dbService = new DatabaseService(sshService);
        var setupResult = await dbService.SetupCronJobAsync("*/5 * * * *", "echo 'cron test' >> /tmp/cron_test.log");
        Assert.True(setupResult.IsSuccess);

        // Verify crontab entry was registered
        var crontab = await sshService.ExecuteCommandAsync("crontab -l");
        Assert.True(crontab.IsSuccess);
        Assert.Contains("cron_backup_runner.sh", crontab.Output);

        // Verify script content on disk
        var script = await sshService.ExecuteCommandAsync("cat /var/backups/vpsmanager/cron_backup_runner.sh");
        Assert.True(script.IsSuccess);
        Assert.Contains("cron_test.log", script.Output);

        // Clean up
        await sshService.ExecuteCommandAsync("crontab -r || true");
        await sshService.ExecuteCommandAsync("rm -f /var/backups/vpsmanager/cron_backup_runner.sh");
    }

    [Fact]
    public async Task Sandbox_TerminalService_StartAndExecute()
    {
        if (!IsSandboxAvailable())
        {
            return;
        }

        using var sshService = new SshService();
        await sshService.ConnectAsync(SandboxConfig);

        using var terminalService = new TerminalService(sshService);
        await terminalService.StartTerminalSessionAsync("/var/www");
        Assert.True(terminalService.IsSessionActive);

        var output = await terminalService.ExecuteCommandAsync("echo 'Terminal Stream Active'");
        Assert.Contains("Terminal Stream Active", output);

        await terminalService.StopTerminalSessionAsync();
        Assert.False(terminalService.IsSessionActive);
    }

    [Fact]
    public async Task Sandbox_DeployPipeline_NativeLinuxService_Healthy_Succeeds()
    {
        if (!IsSandboxAvailable())
        {
            return;
        }

        using var sshService = new SshService();
        await sshService.ConnectAsync(SandboxConfig);

        var dbService = new DatabaseService(sshService);
        var dockerService = new DockerService(sshService);
        var patchService = new PatchService(sshService);
        var localBuildService = new LocalBuildService();
        var healthCheckService = new UniversalHealthCheckService();

        var pipelineService = new DeploymentPipelineService(
            sshService,
            dbService,
            dockerService,
            patchService,
            localBuildService,
            healthCheckService);

        string localAppPath = GetFixturePath("native-app");
        Assert.True(Directory.Exists(localAppPath), $"Fixture path not found: {localAppPath}");

        var config = new UniversalPipelineConfig
        {
            LocalProjectPath = localAppPath,
            RemoteProjectPath = "/var/www/test_native_healthy",
            ProjectType = ProjectType.NativeLinuxService,
            IsDockerized = false,
            TargetOS = HostOS.Linux,
            BuildStrategy = LocalBuildStrategy.None,
            DbEngine = DatabaseEngine.None,
            SkipDatabaseBackup = true,
            SkipHealthCheck = false,
            LaunchCommand = "./start.sh",
            HealthCheckType = HealthCheckType.TcpPortProbe,
            HealthCheckPort = 8085
        };

        try
        {
            var result = await pipelineService.ExecutePipelineAsync(config);
            Assert.True(result.Success, $"Pipeline failed: {result.ErrorMessage}");
            Assert.Equal(PipelineStatus.Completed, result.Status);
        }
        finally
        {
            await sshService.ExecuteCommandAsync("pkill -f 'python3 /var/www/test_native_healthy/main.py' || true");
            await sshService.ExecuteCommandAsync("rm -rf /var/www/test_native_healthy");
        }
    }

    [Fact]
    public async Task Sandbox_DeployPipeline_NativeLinuxService_Unhealthy_FailsGracefully()
    {
        if (!IsSandboxAvailable())
        {
            return;
        }

        using var sshService = new SshService();
        await sshService.ConnectAsync(SandboxConfig);

        var dbService = new DatabaseService(sshService);
        var dockerService = new DockerService(sshService);
        var patchService = new PatchService(sshService);
        var localBuildService = new LocalBuildService();
        var healthCheckService = new UniversalHealthCheckService();

        var pipelineService = new DeploymentPipelineService(
            sshService,
            dbService,
            dockerService,
            patchService,
            localBuildService,
            healthCheckService);

        string localAppPath = GetFixturePath("native-app");
        Assert.True(Directory.Exists(localAppPath), $"Fixture path not found: {localAppPath}");

        var config = new UniversalPipelineConfig
        {
            LocalProjectPath = localAppPath,
            RemoteProjectPath = "/var/www/test_native_unhealthy",
            ProjectType = ProjectType.NativeLinuxService,
            IsDockerized = false,
            TargetOS = HostOS.Linux,
            BuildStrategy = LocalBuildStrategy.None,
            DbEngine = DatabaseEngine.None,
            SkipDatabaseBackup = true,
            SkipHealthCheck = false,
            LaunchCommand = "nohup sleep 10 >/dev/null 2>&1 & sleep 1",
            HealthCheckType = HealthCheckType.TcpPortProbe,
            HealthCheckPort = 8099
        };

        try
        {
            var result = await pipelineService.ExecutePipelineAsync(config);
            Assert.False(result.Success, "Unhealthy pipeline should report failure");
            Assert.Equal(PipelineStatus.Failed, result.Status);
            Assert.Contains("Health verification failed", result.ErrorMessage);
        }
        finally
        {
            await sshService.ExecuteCommandAsync("rm -rf /var/www/test_native_unhealthy");
        }
    }

    [Fact]
    public async Task Sandbox_DeployPipeline_DockerCompose_Healthy_Succeeds()
    {
        if (!IsSandboxAvailable())
        {
            return;
        }

        using var sshService = new SshService();
        await sshService.ConnectAsync(SandboxConfig);

        var dbService = new DatabaseService(sshService);
        var dockerService = new DockerService(sshService);
        var patchService = new PatchService(sshService);
        var localBuildService = new LocalBuildService();
        var healthCheckService = new UniversalHealthCheckService();

        var pipelineService = new DeploymentPipelineService(
            sshService,
            dbService,
            dockerService,
            patchService,
            localBuildService,
            healthCheckService);

        string localAppPath = GetFixturePath("dockerized-app");
        Assert.True(Directory.Exists(localAppPath), $"Fixture path not found: {localAppPath}");

        var config = new UniversalPipelineConfig
        {
            LocalProjectPath = localAppPath,
            RemoteProjectPath = "/var/www/test_docker_healthy",
            ComposeFilePath = "/var/www/test_docker_healthy/docker-compose.yml",
            ProjectType = ProjectType.DockerCompose,
            IsDockerized = true,
            TargetOS = HostOS.Linux,
            BuildStrategy = LocalBuildStrategy.None,
            DbEngine = DatabaseEngine.None,
            SkipDatabaseBackup = true,
            SkipHealthCheck = false,
            HealthCheckType = HealthCheckType.DockerContainer
        };

        try
        {
            var result = await pipelineService.ExecutePipelineAsync(config);
            Assert.True(result.Success, $"Pipeline failed: {result.ErrorMessage}");
            Assert.Equal(PipelineStatus.Completed, result.Status);

            // Verify container is running in Docker
            var containers = await dockerService.GetContainersAsync(config.ComposeFilePath);
            Assert.NotEmpty(containers);
            Assert.Contains(containers, c => c.Status.Contains("Up", StringComparison.OrdinalIgnoreCase) || c.State.Equals("running", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            await dockerService.ExecuteComposeActionAsync(config.ComposeFilePath, DockerComposeAction.Down);
            await sshService.ExecuteCommandAsync("rm -rf /var/www/test_docker_healthy");
        }
    }
}
