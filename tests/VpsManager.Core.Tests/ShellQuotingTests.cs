using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using VpsManager.Core.Helpers;
using VpsManager.Core.Models;
using VpsManager.Core.Services;
using VpsManager.Core.Services.Adapters;

namespace VpsManager.Core.Tests;

public class ShellQuotingTests
{
    [Fact]
    public void Quote_WrapsValueAndEscapesEmbeddedSingleQuotes()
    {
        Assert.Equal("'plain'", ShellQuote.Quote("plain"));
        Assert.Equal("'it'\\''s'", ShellQuote.Quote("it's"));
    }

    [Theory]
    [InlineData("a(b", "a\\(b")]
    [InlineData("/var/www/app.v2", "/var/www/app\\.v2")]
    [InlineData("name|other", "name\\|other")]
    [InlineData("plain/path", "plain/path")]
    public void EscapeExtendedRegex_NeutralizesPatternMetacharacters(string input, string expected)
    {
        Assert.Equal(expected, ShellQuote.EscapeExtendedRegex(input));
    }

    [Fact]
    public async Task HttpHealthCheck_QuotesUrlSoCommandSubstitutionCannotRun()
    {
        var (ssh, capture) = CreateCapturingSsh();
        var service = new UniversalHealthCheckService();

        var result = await service.VerifyHealthAsync(ssh.Object, new Mock<IDockerService>().Object, new UniversalPipelineConfig
        {
            HealthCheckType = HealthCheckType.HttpEndpoint,
            HealthCheckUrl = "http://localhost:8080/health$(id)"
        });

        Assert.True(result.IsHealthy);
        Assert.Contains("'http://localhost:8080/health$(id)'", capture.Command, System.StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessHealthCheck_QuotesProcessNameSoShellMetacharactersCannotRun()
    {
        var (ssh, capture) = CreateCapturingSsh();
        var service = new UniversalHealthCheckService();

        await service.VerifyHealthAsync(ssh.Object, new Mock<IDockerService>().Object, new UniversalPipelineConfig
        {
            HealthCheckType = HealthCheckType.ProcessState,
            HealthCheckProcessName = "my_app; rm -rf /"
        });

        Assert.Contains("pgrep -f 'my_app; rm -rf /'", capture.Command, System.StringComparison.Ordinal);
    }

    [Fact]
    public async Task ContainerHealthCheck_QuotesComposePath()
    {
        var mockSsh = new Mock<ISshService>();
        string? composePath = null;
        var mockDocker = new Mock<IDockerService>();
        mockDocker.Setup(d => d.GetContainersAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((path, _) => composePath = path)
            .ReturnsAsync(new List<ContainerInfo>());

        var service = new UniversalHealthCheckService();
        await service.VerifyHealthAsync(mockSsh.Object, mockDocker.Object, new UniversalPipelineConfig
        {
            HealthCheckType = HealthCheckType.DockerContainer,
            ComposeFilePath = "/var/www/my app/docker-compose.yml"
        });

        Assert.Equal("/var/www/my app/docker-compose.yml", composePath);
    }

    private static (Mock<ISshService> Ssh, CommandCapture Capture) CreateCapturingSsh()
    {
        var capture = new CommandCapture();
        var mockSsh = new Mock<ISshService>();
        mockSsh.Setup(s => s.ExecuteCommandAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((cmd, _) => capture.Command = cmd)
            .ReturnsAsync(new CommandResult { ExitCode = 0, Output = "200" });
        return (mockSsh, capture);
    }

    private sealed class CommandCapture
    {
        public string Command { get; set; } = string.Empty;
    }
}
