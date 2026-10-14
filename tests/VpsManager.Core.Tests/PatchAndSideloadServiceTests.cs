using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using VpsManager.Core.Models;
using VpsManager.Core.Services;
using Xunit;

namespace VpsManager.Core.Tests;

public class PatchAndSideloadServiceTests
{
    // =========================================================================
    // 1. Scan & Ignore Tests
    // =========================================================================

    [Fact]
    public async Task PatchService_ScanLocalChanges_IgnoresSpecifiedPatterns()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"patch_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        Directory.CreateDirectory(Path.Combine(tempDir, ".git"));
        
        File.WriteAllText(Path.Combine(tempDir, "app.py"), "print('hello')");
        File.WriteAllText(Path.Combine(tempDir, ".git", "config"), "gitconfig");

        try
        {
            var mockSsh = new Mock<ISshService>();
            var patchService = new PatchService(mockSsh.Object);
            var ignoreList = new List<string> { ".git" };

            var scanned = await patchService.ScanLocalChangesAsync(tempDir, ignoreList);

            Assert.Single(scanned);
            Assert.Equal("app.py", scanned[0].RelativePath);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task PatchService_GetGitStatusAsync_DetectsNonGitRepository()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"patch_git_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var mockSsh = new Mock<ISshService>();
            var patchService = new PatchService(mockSsh.Object);

            var status = await patchService.GetGitStatusAsync(tempDir);

            Assert.False(status.IsGitRepository);
            Assert.Equal("No Git Repo", status.BranchName);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    // =========================================================================
    // 2. Command Builder Tests (DRY & Profile-aware)
    // =========================================================================

    [Fact]
    public void PatchService_BuildReloadCommand_DockerStack_IncludesServiceNameAndCompose()
    {
        var optionsWithService = new PatchDeploymentOptions
        {
            IsDockerized = true,
            ComposeFileName = "docker-compose.prod.yml",
            ServiceName = "api"
        };

        string cmd = PatchService.BuildReloadCommand(optionsWithService, "/opt/app");

        Assert.Equal("cd '/opt/app' && docker compose -f 'docker-compose.prod.yml' restart 'api'", cmd);

        var optionsAllServices = new PatchDeploymentOptions
        {
            IsDockerized = true,
            ComposeFileName = "docker-compose.yml",
            ServiceName = ""
        };

        string cmdAll = PatchService.BuildReloadCommand(optionsAllServices, "/opt/app");
        Assert.Equal("cd '/opt/app' && docker compose -f 'docker-compose.yml' restart", cmdAll);
    }

    [Fact]
    public void PatchService_BuildReloadCommand_HostService_PrioritizesRestartAndLaunchCommands()
    {
        var optionsRestart = new PatchDeploymentOptions
        {
            IsDockerized = false,
            RestartCommand = "systemctl restart myapp"
        };
        string cmd1 = PatchService.BuildReloadCommand(optionsRestart, "/var/www/site");
        Assert.Equal("cd '/var/www/site' && systemctl restart myapp", cmd1);

        var optionsLaunch = new PatchDeploymentOptions
        {
            IsDockerized = false,
            LaunchCommand = "pm2 reload all"
        };
        string cmd2 = PatchService.BuildReloadCommand(optionsLaunch, "/var/www/site");
        Assert.Equal("cd '/var/www/site' && pm2 reload all", cmd2);

        var optionsSystemd = new PatchDeploymentOptions
        {
            IsDockerized = false,
            ServiceName = "nginx"
        };
        string cmd3 = PatchService.BuildReloadCommand(optionsSystemd, "/var/www/site");
        Assert.Equal("systemctl restart 'nginx'", cmd3);
    }

    // =========================================================================
    // 3. Deployment Engine Tests (Native tar.gz, Deletions, Snapshots)
    // =========================================================================

    [Fact]
    public async Task PatchService_DeployPatchAsync_UploadsTarGz_AndExtractsWithPOSIXTar()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"patch_deploy_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        string srcFilePath = Path.Combine(tempDir, "server.js");
        File.WriteAllText(srcFilePath, "console.log('patched');");

        try
        {
            var mockSsh = new Mock<ISshService>();
            byte[]? uploadedBytes = null;
            var executedCommands = new List<string>();

            mockSsh.Setup(s => s.UploadFileAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<IProgress<double>>(), It.IsAny<CancellationToken>()))
                   .Callback<Stream, string, IProgress<double>, CancellationToken>((stream, path, prog, token) =>
                   {
                       using var ms = new MemoryStream();
                       stream.CopyTo(ms);
                       uploadedBytes = ms.ToArray();
                   })
                   .Returns(Task.CompletedTask);

            mockSsh.Setup(s => s.ExecuteCommandAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                   .Callback<string, CancellationToken>((cmd, token) => executedCommands.Add(cmd))
                   .ReturnsAsync(new CommandResult { ExitCode = 0, Output = "OK" });

            var patchService = new PatchService(mockSsh.Object);
            var selectedNodes = new List<FilePatchNode>
            {
                new()
                {
                    Name = "server.js",
                    RelativePath = "server.js",
                    FullPath = srcFilePath,
                    FileSizeBytes = new FileInfo(srcFilePath).Length,
                    ChangeStatus = FileChangeStatus.Modified
                }
            };

            var options = new PatchDeploymentOptions
            {
                CreateBackupSnapshot = false,
                AutoReloadService = false
            };

            var result = await patchService.DeployPatchAsync(selectedNodes, tempDir, "/opt/app", options);

            Assert.True(result.IsSuccess);
            Assert.Equal(1, result.FilesDeployedCount);
            Assert.Equal(0, result.FilesDeletedCount);

            // Verify upload occurred and payload is a valid gzip tar
            Assert.NotNull(uploadedBytes);
            Assert.True(uploadedBytes.Length > 0);

            using (var archiveStream = new MemoryStream(uploadedBytes))
            using (var gzipStream = new GZipStream(archiveStream, CompressionMode.Decompress))
            using (var tarReader = new TarReader(gzipStream))
            {
                var entry = tarReader.GetNextEntry();
                Assert.NotNull(entry);
                Assert.Equal("server.js", entry.Name);
            }

            // Verify remote extraction command used POSIX tar -xzf and removed tmp archive
            Assert.Contains(executedCommands, cmd => cmd.Contains("tar -xzf") && cmd.Contains("/opt/app"));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task PatchService_DeployPatchAsync_CreatesBackupSnapshot_WhenEnabled()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"patch_snap_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        string srcFilePath = Path.Combine(tempDir, "index.html");
        File.WriteAllText(srcFilePath, "<h1>V2</h1>");

        try
        {
            var mockSsh = new Mock<ISshService>();
            var executedCommands = new List<string>();

            mockSsh.Setup(s => s.UploadFileAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<IProgress<double>>(), It.IsAny<CancellationToken>()))
                   .Returns(Task.CompletedTask);

            mockSsh.Setup(s => s.ExecuteCommandAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                   .Callback<string, CancellationToken>((cmd, token) => executedCommands.Add(cmd))
                   .ReturnsAsync(new CommandResult { ExitCode = 0, Output = "OK" });

            var patchService = new PatchService(mockSsh.Object);
            var selectedNodes = new List<FilePatchNode>
            {
                new()
                {
                    Name = "index.html",
                    RelativePath = "index.html",
                    FullPath = srcFilePath,
                    FileSizeBytes = 10,
                    ChangeStatus = FileChangeStatus.Modified
                }
            };

            var options = new PatchDeploymentOptions
            {
                CreateBackupSnapshot = true,
                AutoReloadService = false
            };

            var result = await patchService.DeployPatchAsync(selectedNodes, tempDir, "/opt/site", options);

            Assert.True(result.IsSuccess);
            Assert.False(string.IsNullOrWhiteSpace(result.BackupPath));
            Assert.Contains("/opt/site_backup_", result.BackupPath);

            // Verify remote backup command executed cp -a
            Assert.Contains(executedCommands, cmd => cmd.StartsWith("cp -a '/opt/site' '/opt/site_backup_"));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task PatchService_DeployPatchAsync_RemovesRemoteDeletedFiles()
    {
        var mockSsh = new Mock<ISshService>();
        var executedCommands = new List<string>();

        mockSsh.Setup(s => s.ExecuteCommandAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .Callback<string, CancellationToken>((cmd, token) => executedCommands.Add(cmd))
               .ReturnsAsync(new CommandResult { ExitCode = 0, Output = "OK" });

        var patchService = new PatchService(mockSsh.Object);
        var selectedNodes = new List<FilePatchNode>
        {
            new()
            {
                Name = "old_util.py",
                RelativePath = "utils/old_util.py",
                FullPath = "dummy/path",
                ChangeStatus = FileChangeStatus.Deleted
            }
        };

        var options = new PatchDeploymentOptions
        {
            CreateBackupSnapshot = false,
            AutoReloadService = false
        };

        var result = await patchService.DeployPatchAsync(selectedNodes, "dummy", "/var/www/app", options);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.FilesDeployedCount);
        Assert.Equal(1, result.FilesDeletedCount);

        // Verify remote deletion rm -f executed
        Assert.Contains(executedCommands, cmd => cmd == "rm -f '/var/www/app/utils/old_util.py'");
    }

    // =========================================================================
    // 4. Rollback Tests
    // =========================================================================

    [Fact]
    public async Task PatchService_RollbackPatchAsync_VerifiesAndRestoresDirectory()
    {
        var mockSsh = new Mock<ISshService>();
        var executedCommands = new List<string>();

        mockSsh.Setup(s => s.ExecuteCommandAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .Callback<string, CancellationToken>((cmd, token) => executedCommands.Add(cmd))
               .ReturnsAsync(new CommandResult { ExitCode = 0, Output = "OK" });

        var patchService = new PatchService(mockSsh.Object);
        var result = await patchService.RollbackPatchAsync("/opt/app", "/opt/app_backup_20261013");

        Assert.True(result.IsSuccess);
        Assert.Contains(executedCommands, cmd => cmd == "test -d '/opt/app_backup_20261013'");
        Assert.Contains(executedCommands, cmd => cmd == "cp -a '/opt/app_backup_20261013'/. '/opt/app'/");
    }

    // =========================================================================
    // 5. FilePatchNode Data Model & Reactivity Tests
    // =========================================================================

    [Fact]
    public void FilePatchNode_CascadingSelection_SyncsParentAndChildren()
    {
        var rootDir = new FilePatchNode { Name = "src", IsDirectory = true };
        var file1 = new FilePatchNode { Name = "main.go", FileSizeBytes = 500, IsDirectory = false };
        var file2 = new FilePatchNode { Name = "util.go", FileSizeBytes = 300, IsDirectory = false };

        rootDir.AddChild(file1);
        rootDir.AddChild(file2);

        // Selecting root selects both children
        rootDir.IsSelected = true;
        Assert.True(file1.IsSelected);
        Assert.True(file2.IsSelected);
        Assert.Equal(800, rootDir.GetSelectedSizeBytes());

        // Deselecting one child updates parent
        file1.IsSelected = false;
        Assert.False(rootDir.IsSelected);
        Assert.Equal(300, rootDir.GetSelectedSizeBytes());

        // Selecting child again selects parent
        file1.IsSelected = true;
        Assert.True(rootDir.IsSelected);
    }

    [Fact]
    public void FilePatchNode_ChangeStatusProperties_RaiseNotifications()
    {
        var node = new FilePatchNode { Name = "test.txt" };
        var changedProps = new List<string>();
        node.PropertyChanged += (s, e) => changedProps.Add(e.PropertyName!);

        node.ChangeStatus = FileChangeStatus.Modified;

        Assert.True(node.IsModified);
        Assert.False(node.IsNew);
        Assert.False(node.IsDeleted);
        Assert.False(node.IsUnchanged);
        Assert.Equal("MODIFIED", node.StatusBadgeText);

        Assert.Contains(nameof(FilePatchNode.IsModified), changedProps);
        Assert.Contains(nameof(FilePatchNode.StatusBadgeText), changedProps);
    }

    [Fact]
    public void FilePatchNode_FormatSize_RendersReadableUnits()
    {
        Assert.Equal("512 B", FilePatchNode.FormatSize(512));
        Assert.Equal("2.0 KB", FilePatchNode.FormatSize(2048));
        Assert.Equal("4.5 MB", FilePatchNode.FormatSize((long)(4.5 * 1024 * 1024)));
        Assert.Equal("1.50 GB", FilePatchNode.FormatSize((long)(1.5 * 1024 * 1024 * 1024)));
    }

    // =========================================================================
    // 6. Sideload Service Tests
    // =========================================================================

    [Fact]
    public async Task SideloadService_SideloadImage_TriggersUploadAndLoad()
    {
        string tempFile = Path.GetTempFileName();
        try
        {
            var mockSsh = new Mock<ISshService>();
            mockSsh.Setup(s => s.ExecuteCommandAsync(It.Is<string>(cmd => cmd.Contains("docker load")), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new CommandResult { ExitCode = 0, Output = "Loaded image" });

            var sideloadService = new SideloadService(mockSsh.Object);
            var result = await sideloadService.SideloadImageAsync(tempFile, "/tmp");

            Assert.True(result.IsSuccess);
            mockSsh.Verify(s => s.UploadFileAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<IProgress<double>>(), It.IsAny<CancellationToken>()), Times.Once);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
