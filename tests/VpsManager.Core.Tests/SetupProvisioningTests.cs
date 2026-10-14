using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using VpsManager.Core.Models;
using VpsManager.Core.Services;
using Xunit;

namespace VpsManager.Core.Tests;

public class SetupProvisioningTests
{
    private readonly Mock<ISshService> _sshMock = new();

    private SetupProvisioningService CreateService() => new(_sshMock.Object);

    [Fact]
    public void BuildSteps_WithAllModulesEnabled_GeneratesExpectedStepsInOrder()
    {
        var service = CreateService();
        var request = new ProvisioningPlanRequest
        {
            UpdateSystem = true,
            InstallEssentials = true,
            InstallDocker = true,
            ConfigureFirewall = true,
            InstallFail2Ban = true,
            EnableUnattendedUpgrades = true,
            HardenSshSecurity = true,
            CreateSwap = true,
            PrepareProjectDirectory = true,
            InstallNodeJs = true,
            InstallNginx = true,
            ProjectDirectory = "/var/www/app"
        };

        var steps = service.BuildSteps(request);

        Assert.Equal(11, steps.Count);
        Assert.Equal("system_update", steps[0].Id);
        Assert.Equal("essentials", steps[1].Id);
        Assert.Equal("docker", steps[2].Id);
        Assert.Equal("ufw", steps[3].Id);
        Assert.Equal("fail2ban", steps[4].Id);
        Assert.Equal("unattended_upgrades", steps[5].Id);
        Assert.Equal("ssh_hardening", steps[6].Id);
        Assert.Equal("swap", steps[7].Id);
        Assert.Equal("project_dir", steps[8].Id);
        Assert.Equal("nodejs", steps[9].Id);
        Assert.Equal("nginx", steps[10].Id);
    }

    [Fact]
    public void BuildSteps_WithSelectiveModules_OnlyIncludesSelectedSteps()
    {
        var service = CreateService();
        var request = new ProvisioningPlanRequest
        {
            UpdateSystem = false,
            InstallEssentials = false,
            InstallDocker = true,
            ConfigureFirewall = false,
            InstallFail2Ban = false,
            EnableUnattendedUpgrades = false,
            HardenSshSecurity = true,
            CreateSwap = false,
            PrepareProjectDirectory = false,
            InstallNodeJs = false,
            InstallNginx = false
        };

        var steps = service.BuildSteps(request);

        Assert.Equal(2, steps.Count);
        Assert.Equal("docker", steps[0].Id);
        Assert.Equal("ssh_hardening", steps[1].Id);
    }

    [Fact]
    public void BuildSteps_WithUbuntuDockerPackage_UsesDockerIoCommand()
    {
        var service = CreateService();
        var request = new ProvisioningPlanRequest
        {
            UpdateSystem = false,
            InstallEssentials = false,
            InstallDocker = true,
            DockerSource = "Ubuntu packages (docker.io)",
            ConfigureFirewall = false,
            InstallFail2Ban = false,
            EnableUnattendedUpgrades = false,
            HardenSshSecurity = false,
            CreateSwap = false,
            PrepareProjectDirectory = false,
            InstallNodeJs = false,
            InstallNginx = false
        };

        var steps = service.BuildSteps(request);

        var dockerStep = Assert.Single(steps);
        Assert.Contains("docker.io", dockerStep.Command);
        Assert.Contains("docker-compose-v2", dockerStep.Command);
    }

    [Fact]
    public void BuildSteps_WithDockerOfficialRepo_ContainsGpgKeyringAndAptRepo()
    {
        var service = CreateService();
        var request = new ProvisioningPlanRequest
        {
            UpdateSystem = false,
            InstallEssentials = false,
            InstallDocker = true,
            DockerSource = "Docker official repository",
            DockerRepositoryUrl = "https://download.docker.com/linux/ubuntu",
            ConfigureFirewall = false,
            InstallFail2Ban = false,
            EnableUnattendedUpgrades = false,
            HardenSshSecurity = false,
            CreateSwap = false,
            PrepareProjectDirectory = false,
            InstallNodeJs = false,
            InstallNginx = false
        };

        var steps = service.BuildSteps(request);

        var dockerStep = Assert.Single(steps);
        Assert.Contains("docker.gpg", dockerStep.Command);
        Assert.Contains("docker-ce", dockerStep.Command);
        Assert.Contains("docker-buildx-plugin", dockerStep.Command);
        Assert.Contains("docker-compose-plugin", dockerStep.Command);
    }

    [Fact]
    public void BuildSteps_SwapCommand_ClampsSizeBetween1And64Gb()
    {
        var service = CreateService();

        var lowRequest = new ProvisioningPlanRequest
        {
            UpdateSystem = false,
            InstallEssentials = false,
            InstallDocker = false,
            EnableUnattendedUpgrades = false,
            CreateSwap = true,
            SwapSizeGb = -10,
            PrepareProjectDirectory = false
        };
        var lowStep = Assert.Single(service.BuildSteps(lowRequest));
        Assert.Contains("fallocate -l 1G", lowStep.Command);

        var highRequest = new ProvisioningPlanRequest
        {
            UpdateSystem = false,
            InstallEssentials = false,
            InstallDocker = false,
            EnableUnattendedUpgrades = false,
            CreateSwap = true,
            SwapSizeGb = 120,
            PrepareProjectDirectory = false
        };
        var highStep = Assert.Single(service.BuildSteps(highRequest));
        Assert.Contains("fallocate -l 64G", highStep.Command);
    }

    [Fact]
    public void BuildSteps_ProjectDirectory_QuotesPathSafely()
    {
        var service = CreateService();
        var request = new ProvisioningPlanRequest
        {
            UpdateSystem = false,
            InstallEssentials = false,
            InstallDocker = false,
            EnableUnattendedUpgrades = false,
            PrepareProjectDirectory = true,
            ProjectDirectory = "/var/www/my cool project"
        };

        var steps = service.BuildSteps(request);
        var dirStep = Assert.Single(steps);

        Assert.Equal("project_dir", dirStep.Id);
        Assert.Contains("'/var/www/my cool project'", dirStep.Command);
    }

    [Fact]
    public void BuildSteps_ProjectDirectory_IgnoresRelativePath()
    {
        var service = CreateService();
        var request = new ProvisioningPlanRequest
        {
            UpdateSystem = false,
            InstallEssentials = false,
            InstallDocker = false,
            EnableUnattendedUpgrades = false,
            PrepareProjectDirectory = true,
            ProjectDirectory = "relative/path"
        };

        var steps = service.BuildSteps(request);
        Assert.Empty(steps);
    }

    [Fact]
    public void BuildSteps_AptCommands_IncludeLockWaitAndNonInteractiveFlags()
    {
        var service = CreateService();
        var request = new ProvisioningPlanRequest
        {
            UpdateSystem = true,
            InstallEssentials = true,
            InstallDocker = true,
            InstallFail2Ban = true,
            EnableUnattendedUpgrades = true,
            InstallNodeJs = true,
            InstallNginx = true,
            PrepareProjectDirectory = false
        };

        var steps = service.BuildSteps(request);

        foreach (var step in steps)
        {
            if (step.Id is "system_update" or "essentials" or "docker" or "fail2ban" or "unattended_upgrades" or "nodejs" or "nginx")
            {
                Assert.Contains("lock-frontend", step.Command);
                Assert.Contains("DEBIAN_FRONTEND=noninteractive", step.Command);
                Assert.Contains("--force-confdef", step.Command);
                Assert.Contains("--force-confold", step.Command);
            }
        }
    }

    [Fact]
    public void ParsePreflightOutput_ParsesStandardLinuxMetricsCorrectly()
    {
        const string rawOutput =
            "OS=Ubuntu 24.04.1 LTS\n" +
            "KERNEL=Linux 6.8.0-40-generic\n" +
            "ARCH=x86_64\n" +
            "MEM_AVAIL=6.2Gi\n" +
            "MEM_TOTAL=7.7Gi\n" +
            "DISK_FREE=35G free of 50G\n" +
            "UPTIME=up 3 weeks, 2 days\n";

        var info = SetupProvisioningService.ParsePreflightOutput(rawOutput);

        Assert.Equal("Ubuntu 24.04.1 LTS", info.Os);
        Assert.Equal("Linux 6.8.0-40-generic", info.Kernel);
        Assert.Equal("x86_64", info.Arch);
        Assert.Equal("6.2Gi", info.MemoryAvailable);
        Assert.Equal("7.7Gi", info.MemoryTotal);
        Assert.Equal("6.2Gi available (7.7Gi total)", info.MemorySummary);
        Assert.Equal("35G free of 50G", info.DiskFree);
        Assert.Equal("up 3 weeks, 2 days", info.Uptime);
    }

    [Fact]
    public async Task RunPreflightProbeAsync_WhenSuccessful_ExecutesScriptAndParsesResult()
    {
        const string rawOutput =
            "OS=Debian GNU/Linux 12\n" +
            "KERNEL=Linux 6.1.0-18-amd64\n" +
            "ARCH=x86_64\n" +
            "MEM_AVAIL=3.1Gi\n" +
            "MEM_TOTAL=3.9Gi\n" +
            "DISK_FREE=18G free of 25G\n" +
            "UPTIME=up 10 hours\n";

        _sshMock.Setup(s => s.ExecuteCommandAsync(SetupProvisioningService.PreflightProbeScript, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult { ExitCode = 0, Output = rawOutput });

        var service = CreateService();
        var info = await service.RunPreflightProbeAsync();

        Assert.Equal("Debian GNU/Linux 12", info.Os);
        Assert.Equal("x86_64", info.Arch);
        Assert.Equal("18G free of 25G", info.DiskFree);
    }

    [Fact]
    public async Task RunPreflightProbeAsync_WhenCommandFails_ThrowsInvalidOperationException()
    {
        _sshMock.Setup(s => s.ExecuteCommandAsync(SetupProvisioningService.PreflightProbeScript, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult { ExitCode = 1, Error = "Permission denied" });

        var service = CreateService();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RunPreflightProbeAsync());
    }

    [Fact]
    public async Task ExecuteStepAsync_DelegatesToSshService()
    {
        var step = new ProvisioningStepDefinition("test", "Test Step", "echo 'hello'");
        using var cts = new CancellationTokenSource();

        _sshMock.Setup(s => s.ExecuteCommandAsync("echo 'hello'", cts.Token))
            .ReturnsAsync(new CommandResult { ExitCode = 0, Output = "hello\n" });

        var service = CreateService();
        var result = await service.ExecuteStepAsync(step, cts.Token);

        Assert.True(result.IsSuccess);
        Assert.Equal("hello\n", result.Output);
        _sshMock.Verify(s => s.ExecuteCommandAsync("echo 'hello'", cts.Token), Times.Once);
    }

    [Theory]
    [InlineData("https://example.com/repo", "https://example.com/repo")]
    [InlineData("http://example.com/repo/", "http://example.com/repo")]
    public void NormalizeUrl_ValidHttpUrls_ReturnsNormalized(string input, string expected)
    {
        var normalized = SetupProvisioningService.NormalizeUrl(input, "Test URL");
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ftp://example.com")]
    [InlineData("not a url")]
    public void NormalizeUrl_InvalidUrls_ThrowsInvalidOperationException(string input)
    {
        Assert.Throws<InvalidOperationException>(() => SetupProvisioningService.NormalizeUrl(input, "Test URL"));
    }
}
