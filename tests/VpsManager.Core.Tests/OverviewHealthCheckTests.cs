using System.Threading;
using System.Threading.Tasks;
using Moq;
using VpsManager.Core.Models;
using VpsManager.Core.Services;
using VpsManager.Core.Services.Adapters;

namespace VpsManager.Core.Tests;

public class OverviewHealthCheckTests
{
    [Fact]
    public async Task DockerHealthCheck_DoesNotTreatUnrelatedHostContainersAsProjectHealth()
    {
        var mockSsh = new Mock<ISshService>();
        mockSsh.Setup(service => service.ExecuteCommandAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult { ExitCode = 0, Output = string.Empty });
        var mockDocker = new Mock<IDockerService>();
        mockDocker.Setup(service => service.GetContainersAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ContainerInfo>());

        var service = new UniversalHealthCheckService();
        var result = await service.VerifyHealthAsync(
            mockSsh.Object,
            mockDocker.Object,
            new UniversalPipelineConfig
            {
                ProjectType = ProjectType.DockerCompose,
                ComposeFilePath = "/var/www/app/docker-compose.yml",
                HealthCheckType = HealthCheckType.DockerContainer
            });

        Assert.False(result.IsHealthy);
        Assert.Contains("selected Compose project", result.Message, StringComparison.Ordinal);
        mockSsh.Verify(
            ssh => ssh.ExecuteCommandAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
