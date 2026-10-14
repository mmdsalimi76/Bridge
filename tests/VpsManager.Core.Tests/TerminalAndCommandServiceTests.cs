using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using VpsManager.Core.Models;
using VpsManager.Core.Services;
using Xunit;

namespace VpsManager.Core.Tests;

public class TerminalAndCommandServiceTests
{
    [Fact]
    public async Task TerminalService_StartSession_ThrowsWhenNotConnected()
    {
        var mockSsh = new Mock<ISshService>();
        mockSsh.Setup(s => s.IsConnected).Returns(false);

        using var service = new TerminalService(mockSsh.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartTerminalSessionAsync());
    }

    [Fact]
    public async Task TerminalService_StartSession_SetsSessionActiveAndResolvesWorkingDir()
    {
        var mockSsh = new Mock<ISshService>();
        mockSsh.Setup(s => s.IsConnected).Returns(true);
        mockSsh.Setup(s => s.ExecuteCommandAsync("cd \"/var/www/app\" && pwd", It.IsAny<CancellationToken>()))
               .ReturnsAsync(new CommandResult { ExitCode = 0, Output = "/var/www/app\n" });

        using var service = new TerminalService(mockSsh.Object);

        await service.StartTerminalSessionAsync("/var/www/app");

        Assert.True(service.IsSessionActive);
        Assert.Equal("/var/www/app", service.CurrentWorkingDirectory);
    }

    [Fact]
    public async Task TerminalService_ExecuteCommand_PrefixesCurrentWorkingDirectory()
    {
        var mockSsh = new Mock<ISshService>();
        mockSsh.Setup(s => s.IsConnected).Returns(true);
        mockSsh.Setup(s => s.ExecuteCommandAsync("cd \"/app\" && pwd", It.IsAny<CancellationToken>()))
               .ReturnsAsync(new CommandResult { ExitCode = 0, Output = "/app\n" });

        string executed = string.Empty;
        mockSsh.Setup(s => s.ExecuteCommandAsync("cd \"/app\" && ls -la", It.IsAny<CancellationToken>()))
               .Callback<string, CancellationToken>((cmd, _) => executed = cmd)
               .ReturnsAsync(new CommandResult { ExitCode = 0, Output = "total 0" });

        using var service = new TerminalService(mockSsh.Object);
        await service.StartTerminalSessionAsync("/app");

        var result = await service.ExecuteCommandAsync("ls -la");

        Assert.Equal("cd \"/app\" && ls -la", executed);
        Assert.Contains("total 0", result);
    }

    [Fact]
    public async Task TerminalService_ExecuteCommand_UpdatesWorkingDirectoryOnCd()
    {
        var mockSsh = new Mock<ISshService>();
        mockSsh.Setup(s => s.IsConnected).Returns(true);
        mockSsh.Setup(s => s.ExecuteCommandAsync("cd \"/app\" && pwd", It.IsAny<CancellationToken>()))
               .ReturnsAsync(new CommandResult { ExitCode = 0, Output = "/app\n" });
        mockSsh.Setup(s => s.ExecuteCommandAsync("cd \"/app\" && cd src && pwd", It.IsAny<CancellationToken>()))
               .ReturnsAsync(new CommandResult { ExitCode = 0, Output = "/app/src\n" });

        using var service = new TerminalService(mockSsh.Object);
        await service.StartTerminalSessionAsync("/app");

        await service.ExecuteCommandAsync("cd src");

        Assert.Equal("/app/src", service.CurrentWorkingDirectory);
    }

    [Fact]
    public async Task TerminalService_ExecuteCommand_ThrowsOperationCanceledExceptionOnTokenCancellation()
    {
        var mockSsh = new Mock<ISshService>();
        mockSsh.Setup(s => s.IsConnected).Returns(true);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        mockSsh.Setup(s => s.ExecuteCommandAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ThrowsAsync(new OperationCanceledException());

        using var service = new TerminalService(mockSsh.Object);
        await service.StartTerminalSessionAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.ExecuteCommandAsync("ping 8.8.8.8", cts.Token));
        
        var output = await service.GetOutputAsync();
        Assert.Contains("^C", output);
    }

    [Fact]
    public async Task TerminalService_ClearOutput_ClearsBuffer()
    {
        var mockSsh = new Mock<ISshService>();
        mockSsh.Setup(s => s.IsConnected).Returns(true);
        mockSsh.Setup(s => s.ExecuteCommandAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(new CommandResult { ExitCode = 0, Output = "Hello world" });

        using var service = new TerminalService(mockSsh.Object);
        await service.StartTerminalSessionAsync();
        await service.ExecuteCommandAsync("echo test");

        await service.ClearOutputAsync();
        var output = await service.GetOutputAsync();

        Assert.Equal("Terminal output cleared.\n", output);
    }

    [Fact]
    public async Task SavedCommandService_LoadsDefaultCuratedCommandsWhenEmpty()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_cmds_{Guid.NewGuid():N}.json");
        try
        {
            var service = new SavedCommandService(tempFile);
            var commands = await service.LoadCommandsAsync();

            Assert.NotEmpty(commands);
            Assert.Contains(commands, c => c.Category == "Docker");
            Assert.Contains(commands, c => c.Category == "System");
            Assert.Contains(commands, c => c.Category == "Logs");
            Assert.Contains(commands, c => c.Category == "Network");
            Assert.Contains(commands, c => c.Category == "Services");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task SavedCommandService_AddAndFilterCommandsByCategory()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_cmds_{Guid.NewGuid():N}.json");
        try
        {
            var service = new SavedCommandService(tempFile);
            await service.AddCommandAsync("Clean Logs", "truncate -s 0 /var/log/syslog", "Truncate logs", "Logs");
            await service.AddCommandAsync("Custom Build", "cargo build --release", "Build rust app", "Custom");

            var allLogs = await service.SearchCommandsAsync("", "Logs");
            Assert.All(allLogs, c => Assert.Equal("Logs", c.Category));
            Assert.Contains(allLogs, c => c.Name == "Clean Logs");

            var searchResult = await service.SearchCommandsAsync("cargo", "All");
            Assert.Single(searchResult);
            Assert.Equal("Custom Build", searchResult[0].Name);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task SavedCommandService_DeleteCommand_RemovesFromCollection()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_cmds_{Guid.NewGuid():N}.json");
        try
        {
            var service = new SavedCommandService(tempFile);
            var added = await service.AddCommandAsync("To Delete", "rm -rf /tmp/junk", "Temporary command");
            
            await service.DeleteCommandAsync(added.Id);
            var remaining = await service.LoadCommandsAsync();

            Assert.DoesNotContain(remaining, c => c.Id == added.Id);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task SavedCommandService_UpdateCommand_UpdatesExistingCommandDetails()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_cmds_{Guid.NewGuid():N}.json");
        try
        {
            var service = new SavedCommandService(tempFile);
            var added = await service.AddCommandAsync("Old Name", "echo 1", "Old desc", "Custom");

            added.Name = "New Name";
            added.Command = "echo 2";
            added.Category = "Docker";
            added.Description = "New desc";

            await service.UpdateCommandAsync(added);
            var updatedList = await service.LoadCommandsAsync();
            var found = updatedList.FirstOrDefault(c => c.Id == added.Id);

            Assert.NotNull(found);
            Assert.Equal("New Name", found.Name);
            Assert.Equal("echo 2", found.Command);
            Assert.Equal("Docker", found.Category);
            Assert.Equal("New desc", found.Description);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void SavedCommand_ShowDescription_TogglesCorrectly()
    {
        var cmd = new SavedCommand { Name = "Test", Command = "ls" };
        Assert.False(cmd.ShowDescription);

        cmd.ShowDescription = true;
        Assert.True(cmd.ShowDescription);
    }
}

