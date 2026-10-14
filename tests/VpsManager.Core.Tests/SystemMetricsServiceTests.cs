using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using VpsManager.Core.Models;
using VpsManager.Core.Services;

namespace VpsManager.Core.Tests;

public class SystemMetricsServiceTests
{
    [Fact]
    public async Task GetMetricsAsync_ParsesInvariantValuesAndAvailableMemory()
    {
        var mockSsh = new Mock<ISshService>();
        mockSsh.SetupGet(service => service.IsConnected).Returns(true);
        mockSsh.Setup(service => service.ExecuteCommandAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult
            {
                ExitCode = 0,
                Output = "CPU=12.5\nLOAD=0.50 0.40 0.30\nCORES=4\nRAM=1.25 8.00\nDISK=20.00 100.00 /\nUPTIME=up 2 hours\n"
            });

        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var service = new SystemMetricsService(mockSsh.Object);

            var metrics = await service.GetMetricsAsync();

            Assert.True(metrics.HasCpuMetrics);
            Assert.Equal(12.5, metrics.CpuUsagePercent);
            Assert.Equal(4, metrics.CpuCoreCount);
            Assert.Equal("0.50 0.40 0.30", metrics.LoadAverage);
            Assert.True(metrics.HasRamMetrics);
            Assert.Equal(1.25, metrics.RamUsedGb);
            Assert.Equal(6.75, metrics.RamAvailableGb);
            Assert.Equal(15.625, metrics.RamUsagePercent, 3);
            Assert.True(metrics.HasDiskMetrics);
            Assert.Equal(80, metrics.DiskFreeGb);
            Assert.Equal("/", metrics.DiskMountPoint);
            Assert.Equal("up 2 hours", metrics.Uptime);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public async Task GetMetricsAsync_ThrowsWhenNoMetricsCanBeParsed()
    {
        var mockSsh = new Mock<ISshService>();
        mockSsh.SetupGet(service => service.IsConnected).Returns(true);
        mockSsh.Setup(service => service.ExecuteCommandAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult { ExitCode = 0, Output = "not metrics" });

        var service = new SystemMetricsService(mockSsh.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetMetricsAsync());
    }

    [Fact]
    public async Task GetMetricsAsync_ThrowsWhenRemoteCommandFails()
    {
        var mockSsh = new Mock<ISshService>();
        mockSsh.SetupGet(service => service.IsConnected).Returns(true);
        mockSsh.Setup(service => service.ExecuteCommandAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult { ExitCode = 1, Error = "permission denied" });

        var service = new SystemMetricsService(mockSsh.Object);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetMetricsAsync());

        Assert.Contains("permission denied", exception.Message, StringComparison.Ordinal);
    }
}
