using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using VpsManager.Core.Models;
using VpsManager.Core.Services;
using VpsManager.Core.Services.Adapters;
using VpsManager.Core.Services.Locking;
using VpsManager.Core.Services.Logging;
using Xunit;

namespace VpsManager.Core.Tests;

public class OperationLockTests
{
    [Fact]
    public void AcquireLock_WhenUnlocked_SetsIsLockedAndReturnsDisposableToken()
    {
        var opLock = new OperationLock();
        Assert.False(opLock.IsLocked);
        Assert.Null(opLock.ActiveOperationDescription);

        using (var handle = opLock.AcquireLock("Deploying App"))
        {
            Assert.True(opLock.IsLocked);
            Assert.Equal("Deploying App", opLock.ActiveOperationDescription);
            Assert.NotNull(opLock.LockedAt);
        }

        Assert.False(opLock.IsLocked);
        Assert.Null(opLock.ActiveOperationDescription);
    }

    [Fact]
    public void AcquireLock_WhenAlreadyLocked_ThrowsInvalidOperationException()
    {
        var opLock = new OperationLock();
        using var handle = opLock.AcquireLock("First Task");

        var ex = Assert.Throws<InvalidOperationException>(() => opLock.AcquireLock("Second Task"));
        Assert.Contains("First Task", ex.Message);
        Assert.Contains("Second Task", ex.Message);
    }

    [Fact]
    public void TryAcquireLock_WhenAlreadyLocked_ReturnsFalseAndNullToken()
    {
        var opLock = new OperationLock();
        using var handle1 = opLock.AcquireLock("Deploy Pipeline");

        bool acquired = opLock.TryAcquireLock("Run Backup", out var handle2);

        Assert.False(acquired);
        Assert.Null(handle2);
        Assert.True(opLock.IsLocked);
        Assert.Equal("Deploy Pipeline", opLock.ActiveOperationDescription);
    }

    [Fact]
    public void TryAcquireLock_WhenUnlocked_Succeeds()
    {
        var opLock = new OperationLock();
        bool acquired = opLock.TryAcquireLock("Provisioning VPS", out var handle);

        Assert.True(acquired);
        Assert.NotNull(handle);
        Assert.True(opLock.IsLocked);
        Assert.Equal("Provisioning VPS", opLock.ActiveOperationDescription);

        handle.Dispose();
        Assert.False(opLock.IsLocked);
    }

    [Fact]
    public void Dispose_MultipleCalls_IsIdempotent()
    {
        var opLock = new OperationLock();
        var handle = opLock.AcquireLock("Task");

        handle.Dispose();
        Assert.False(opLock.IsLocked);

        // Second dispose should not throw
        var ex = Record.Exception(() => handle.Dispose());
        Assert.Null(ex);
        Assert.False(opLock.IsLocked);
    }

    [Fact]
    public void LockChanged_FiresOnAcquireAndRelease()
    {
        var opLock = new OperationLock();
        var events = new System.Collections.Generic.List<OperationLockChangedEventArgs>();

        opLock.LockChanged += (_, args) => events.Add(args);

        using (opLock.AcquireLock("Restoring Database"))
        {
            Assert.Single(events);
            Assert.True(events[0].IsLocked);
            Assert.Equal("Restoring Database", events[0].OperationDescription);
        }

        Assert.Equal(2, events.Count);
        Assert.False(events[1].IsLocked);
        Assert.Null(events[1].OperationDescription);
    }
}

public class AppLoggerTests : IDisposable
{
    private readonly string _testLogDir;

    public AppLoggerTests()
    {
        _testLogDir = Path.Combine(Path.GetTempPath(), "BridgeTests_" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testLogDir))
            {
                Directory.Delete(_testLogDir, true);
            }
        }
        catch { }
    }

    [Fact]
    public void Logger_WritesFormattedEntriesToRingBuffer()
    {
        using var logger = new AppLogger(_testLogDir);

        logger.Info("Starting service probe", "HealthMonitor");
        logger.Warn("Port responded with 302 redirect", caller: "HealthMonitor");
        logger.Error("Failed to bind host port", new TimeoutException("Socket timed out"), "DeployEngine");

        var recent = logger.GetRecentLogLines(10);

        Assert.Equal(3, recent.Count);
        Assert.Contains("[INFO ]", recent[0]);
        Assert.Contains("Starting service probe", recent[0]);
        Assert.Contains("[WARN ]", recent[1]);
        Assert.Contains("Port responded with 302 redirect", recent[1]);
        Assert.Contains("[ERROR]", recent[2]);
        Assert.Contains("Failed to bind host port", recent[2]);
        Assert.Contains("TimeoutException", recent[2]);
    }

    [Fact]
    public void Logger_WritesToFileInDirectory()
    {
        using var logger = new AppLogger(_testLogDir);

        logger.Info("Disk check healthy", "DiskProbe");

        string expectedFile = Path.Combine(_testLogDir, $"bridge-{DateTime.Now:yyyyMMdd}.log");
        Assert.True(File.Exists(expectedFile), $"Expected log file {expectedFile} to exist");

        string content = File.ReadAllText(expectedFile);
        Assert.Contains("Disk check healthy", content);
        Assert.Contains("[INFO ]", content);
    }

    [Fact]
    public void Logger_PurgesFilesOlderThanRetentionThreshold()
    {
        Directory.CreateDirectory(_testLogDir);

        // Create an old simulated log file (20 days old)
        string oldFile = Path.Combine(_testLogDir, "bridge-20250101.log");
        File.WriteAllText(oldFile, "historical logs");
        File.SetLastWriteTime(oldFile, DateTime.Now.AddDays(-20));

        // Create a recent simulated log file (2 days old)
        string recentFile = Path.Combine(_testLogDir, "bridge-20261012.log");
        File.WriteAllText(recentFile, "recent logs");
        File.SetLastWriteTime(recentFile, DateTime.Now.AddDays(-2));

        // Instantiating AppLogger triggers PurgeOldLogs()
        using var logger = new AppLogger(_testLogDir);

        Assert.False(File.Exists(oldFile), "Old log file past 14-day retention should have been purged");
        Assert.True(File.Exists(recentFile), "Recent log file within 14 days should be retained");
    }
}

public class CrashReporterTests
{
    [Fact]
    public void CreateCrashDump_GeneratesFileWithTelemetryAndExceptionData()
    {
        var inner = new ArgumentNullException("remotePath", "Remote path cannot be null");
        var outer = new InvalidOperationException("Deployment pipeline aborted due to invalid arguments", inner);

        string tempLogDir = Path.Combine(Path.GetTempPath(), "BridgeCrashTest_" + Guid.NewGuid().ToString("N"));
        using var logger = new AppLogger(tempLogDir);
        logger.Info("Syncing workspace files...");
        logger.Warn("Encountered permission warning on /var/www");

        string crashDumpPath = CrashReporter.CreateCrashDump(outer, logger);

        try
        {
            Assert.True(File.Exists(crashDumpPath), $"Crash dump file {crashDumpPath} should exist");

            string content = File.ReadAllText(crashDumpPath);
            Assert.Contains("BRIDGE CRASH DIAGNOSTIC REPORT", content);
            Assert.Contains(".NET Runtime:", content);
            Assert.Contains("Process ID:", content);
            Assert.Contains("InvalidOperationException", content);
            Assert.Contains("Deployment pipeline aborted due to invalid arguments", content);
            Assert.Contains("Inner Exception (Level 1)", content);
            Assert.Contains("ArgumentNullException", content);
            Assert.Contains("Syncing workspace files...", content);
            Assert.Contains("Encountered permission warning on /var/www", content);
        }
        finally
        {
            try
            {
                if (File.Exists(crashDumpPath)) File.Delete(crashDumpPath);
                if (Directory.Exists(tempLogDir)) Directory.Delete(tempLogDir, true);
            }
            catch { }
        }
    }
}

public class HealthCheckRetryTests
{
    [Fact]
    public async Task VerifyHealthAsync_WhenSkipHealthCheckIsTrue_ReturnsHealthyWithoutCallingServices()
    {
        var service = new UniversalHealthCheckService();
        var mockSsh = new Mock<ISshService>();
        var mockDocker = new Mock<IDockerService>();

        var config = new UniversalPipelineConfig
        {
            SkipHealthCheck = true,
            HealthCheckType = HealthCheckType.HttpEndpoint
        };

        var result = await service.VerifyHealthAsync(mockSsh.Object, mockDocker.Object, config);

        Assert.True(result.IsHealthy);
        mockSsh.Verify(s => s.ExecuteCommandAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task VerifyHealthAsync_WhenFirstAttemptFailsAndSecondSucceeds_RetriesAndReturnsHealthy()
    {
        var service = new UniversalHealthCheckService();
        var mockSsh = new Mock<ISshService>();
        var mockDocker = new Mock<IDockerService>();

        var config = new UniversalPipelineConfig
        {
            SkipHealthCheck = false,
            HealthCheckType = HealthCheckType.HttpEndpoint,
            HealthCheckUrl = "http://localhost:5000/health"
        };

        int callCount = 0;
        mockSsh.Setup(s => s.ExecuteCommandAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    // First attempt: port not listening yet (exit code 7)
                    return new CommandResult { ExitCode = 7, Output = "000" };
                }
                // Second attempt: warm-up complete, returns 200 OK
                return new CommandResult { ExitCode = 0, Output = "200" };
            });

        var result = await service.VerifyHealthAsync(mockSsh.Object, mockDocker.Object, config);

        Assert.True(result.IsHealthy);
        Assert.Contains("200", result.Message);
        Assert.Equal(2, callCount);
    }
}
