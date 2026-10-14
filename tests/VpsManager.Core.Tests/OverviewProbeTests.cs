using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using VpsManager.Core.Models;
using VpsManager.Core.Services;
using VpsManager.Core.Services.Overview;

namespace VpsManager.Core.Tests;

public class OverviewProbeTests
{
    private const string FullProbeOutput =
        "##METRICS\n" +
        "CPU=12.5\n" +
        "LOAD=0.50 0.40 0.30\n" +
        "CORES=4\n" +
        "RAM=1.25 8.00\n" +
        "DISK=20.00 100.00 /\n" +
        "UPTIME=up 2 hours\n" +
        "##DOCKER\n" +
        "app_web-1\trunning\tnginx:latest\tUp 3 hours\t0.0.0.0:80->80/tcp\n" +
        "##SERVICES\n" +
        "nginx.service\trunning\tnginx\n" +
        "##FAILED\n" +
        "postgres.service\n" +
        "##PROCESSES\n" +
        "1234 node 01:20 3.5 2.1 node server.js\n" +
        "##TOPPROC\n" +
        "node 3.5 2.1\n" +
        "ps 0.0 0.0\n" +
        "##DISK\n" +
        "/dev/sda1|/|20.00|100.00\n" +
        "##DOCKERDF\n" +
        "Images|1.2GB|500MB\n" +
        "##HEALTH\n" +
        "200\n" +
        "##EXIT=0\n";

    [Fact]
    public async Task ProbeAsync_ExecutesExactlyOneRemoteCommand()
    {
        var mockSsh = new Mock<ISshService>();
        mockSsh.SetupGet(service => service.IsConnected).Returns(true);

        var commandCount = 0;
        mockSsh.Setup(service => service.ExecuteCommandAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((_, _) => commandCount++)
            .ReturnsAsync(new CommandResult { ExitCode = 0, Output = FullProbeOutput });

        var probe = new OverviewProbe(mockSsh.Object);
        var snapshot = await probe.ProbeAsync(new OverviewProbeRequest
        {
            ComposeFileName = "docker-compose.yml",
            RemoteProjectPath = "/var/www/app",
            HealthProbeCommand = "true"
        });

        Assert.Equal(1, commandCount);
        Assert.Equal(1, snapshot.RemoteCommandCount);
    }

    [Fact]
    public void Parse_ExtractsEverySection()
    {
        var snapshot = OverviewProbe.Parse(FullProbeOutput);

        Assert.True(snapshot.Metrics.HasCpuMetrics);
        Assert.Equal(12.5, snapshot.Metrics.CpuUsagePercent);
        Assert.True(snapshot.Metrics.HasRamMetrics);
        Assert.Equal(4, snapshot.Metrics.CpuCoreCount);

        Assert.Contains(snapshot.Workloads, workload => workload.Name == "app_web-1" && workload.Source == "Docker");
        Assert.Contains(snapshot.Workloads, workload => workload.Name == "nginx.service" && workload.Source == "Web gateway");
        Assert.Contains(snapshot.Workloads, workload => workload.Source == "Process");

        Assert.Equal(new[] { "postgres.service" }, snapshot.FailedUnits);
        Assert.Single(snapshot.TopProcesses);
        Assert.Equal("node", snapshot.TopProcesses[0].Command);

        var mount = Assert.Single(snapshot.Mounts);
        Assert.Equal("/", mount.MountPoint);
        Assert.Equal(20, mount.UsedGb);
        Assert.Equal(80, mount.FreeGb);
        Assert.Equal(20, mount.UsagePercent, 3);

        Assert.Equal(new[] { "Images|1.2GB|500MB" }, snapshot.DockerDiskUsageLines);
        Assert.Equal(0, snapshot.HealthExitCode);
        Assert.Contains("200", snapshot.HealthOutput, System.StringComparison.Ordinal);
        Assert.Empty(snapshot.SectionErrors);
    }

    [Fact]
    public void Parse_ReportsSectionErrors_WhenSectionsAreMissing()
    {
        var snapshot = OverviewProbe.Parse("##METRICS\nCPU=10.0\n");

        Assert.Equal(2, snapshot.SectionErrors.Count);
        Assert.Contains(snapshot.SectionErrors, error => error.Contains("Docker workload", System.StringComparison.Ordinal));
        Assert.Contains(snapshot.SectionErrors, error => error.Contains("Process inventory", System.StringComparison.Ordinal));
        Assert.True(snapshot.Metrics.HasCpuMetrics);
    }

    [Fact]
    public void BuildScript_UsesOneBatchedScriptWithHealthExitMarker()
    {
        var script = OverviewProbe.BuildScript(new OverviewProbeRequest
        {
            ComposeFileName = "docker-compose.yml",
            RemoteProjectPath = "/var/www/app",
            HealthProbeCommand = "pgrep -f 'my_app'"
        });

        Assert.Contains("echo '##METRICS'", script, System.StringComparison.Ordinal);
        Assert.Contains("echo '##HEALTH'", script, System.StringComparison.Ordinal);
        Assert.Contains("pgrep -f 'my_app'", script, System.StringComparison.Ordinal);
        Assert.Contains(OverviewProbe.ExitMarker, script, System.StringComparison.Ordinal);
        Assert.Contains("/var/www/app/docker-compose.yml", script, System.StringComparison.Ordinal);
    }

    [Fact]
    public void BuildScript_OmitsHealthSectionWhenNoProbeIsConfigured()
    {
        var script = OverviewProbe.BuildScript(new OverviewProbeRequest());

        Assert.DoesNotContain("echo '##HEALTH'", script, System.StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProbeAsync_ThrowsWhenTheConnectionIsDown()
    {
        var mockSsh = new Mock<ISshService>();
        mockSsh.SetupGet(service => service.IsConnected).Returns(false);

        var probe = new OverviewProbe(mockSsh.Object);

        await Assert.ThrowsAsync<System.InvalidOperationException>(() => probe.ProbeAsync(new OverviewProbeRequest()));
    }
}
