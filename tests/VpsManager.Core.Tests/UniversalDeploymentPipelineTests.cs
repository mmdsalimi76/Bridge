using Moq;
using VpsManager.Core.Models;
using VpsManager.Core.Services;
using VpsManager.Core.Services.Adapters;
using Xunit;

namespace VpsManager.Core.Tests;

public class UniversalDeploymentPipelineTests
{
    [Fact]
    public void GetDynamicPipelineSteps_DockerCompose_GeneratesTailoredSteps()
    {
        var sshMock = new Mock<ISshService>();
        var dbMock = new Mock<IDatabaseService>();
        var dockerMock = new Mock<IDockerService>();
        var patchMock = new Mock<IPatchService>();

        var pipelineService = new DeploymentPipelineService(
            sshMock.Object,
            dbMock.Object,
            dockerMock.Object,
            patchMock.Object,
            Mock.Of<ILocalBuildService>(),
            Mock.Of<IHealthCheckService>());

        var config = new UniversalPipelineConfig
        {
            ProjectType = ProjectType.DockerCompose,
            DbEngine = DatabaseEngine.PostgreSQL,
            BuildStrategy = LocalBuildStrategy.None,
            HealthCheckType = HealthCheckType.DockerContainer,
            SkipDatabaseBackup = false,
            SkipHealthCheck = false
        };

        var steps = pipelineService.GetDynamicPipelineSteps(config);

        Assert.Equal(7, steps.Count);
        Assert.Contains(steps, s => s.Name == "Pre-flight & Docker Host Check");
        Assert.Contains(steps, s => s.Name == "PostgreSQL Dump Backup Gate");
        Assert.Contains(steps, s => s.Name == "Sync Source & Docker Context");
        Assert.Contains(steps, s => s.Name == "Docker Compose Build Images");
        Assert.Contains(steps, s => s.Name == "Docker Compose Up Services");
        Assert.Contains(steps, s => s.Name == "Docker Container State Probe");
        Assert.Contains(steps, s => s.Name == "Run Container Database Migrations");
    }

    [Fact]
    public void GetDynamicPipelineSteps_TelegramBot_GeneratesTailoredSteps()
    {
        var sshMock = new Mock<ISshService>();
        var dbMock = new Mock<IDatabaseService>();
        var dockerMock = new Mock<IDockerService>();
        var patchMock = new Mock<IPatchService>();

        var pipelineService = new DeploymentPipelineService(
            sshMock.Object,
            dbMock.Object,
            dockerMock.Object,
            patchMock.Object,
            Mock.Of<ILocalBuildService>(),
            Mock.Of<IHealthCheckService>());

        var config = new UniversalPipelineConfig
        {
            ProjectType = ProjectType.TelegramBot,
            DbEngine = DatabaseEngine.SQLite,
            BuildStrategy = LocalBuildStrategy.PythonVenv,
            HealthCheckType = HealthCheckType.ProcessState,
            ServiceName = "my_telegram_bot"
        };

        var steps = pipelineService.GetDynamicPipelineSteps(config);

        Assert.Contains(steps, s => s.Name == "Freeze Requirements & Venv Prep");
        Assert.Contains(steps, s => s.Name == "SQLite Atomic File Snapshot");
        Assert.Contains(steps, s => s.Name == "Restart Bot Daemon & Kill Previous PID");
        Assert.Contains(steps, s => s.Name == "Process PID & Active Daemon Check");
        Assert.DoesNotContain(steps, s => s.Name.Contains("Docker"));
    }

    [Fact]
    public async Task LocalBuildService_StrategyNone_ReturnsSuccess()
    {
        var buildService = new LocalBuildService();
        var config = new UniversalPipelineConfig
        {
            BuildStrategy = LocalBuildStrategy.None
        };

        var result = await buildService.ExecuteLocalBuildAsync(config);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void PipelineStep_StateTransitions_UpdatesPropertiesAndRaisesNotifications()
    {
        var step = new PipelineStep
        {
            Id = "1",
            Name = "Sync Source & Docker Context",
            Status = PipelineStepStatus.Pending
        };

        var changedProperties = new List<string>();
        step.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName != null)
                changedProperties.Add(e.PropertyName);
        };

        Assert.True(step.IsPending);
        Assert.False(step.IsCompleted);
        Assert.Equal("⏳", step.StatusIcon);
        Assert.Equal("Pending", step.DurationDisplay);

        // Transition to InProgress
        step.StartedAt = DateTime.UtcNow;
        step.Status = PipelineStepStatus.InProgress;

        Assert.True(step.IsInProgress);
        Assert.False(step.IsCompleted);
        Assert.Equal("⚡", step.StatusIcon);
        Assert.Equal("Running...", step.DurationDisplay);

        // Transition to Completed
        step.CompletedAt = step.StartedAt.Value.AddSeconds(1.5);
        step.Status = PipelineStepStatus.Completed;

        Assert.True(step.IsCompleted);
        Assert.False(step.IsInProgress);
        Assert.Equal("✅", step.StatusIcon);
        Assert.Equal("1.5s", step.DurationDisplay);
        Assert.Contains(nameof(PipelineStep.Status), changedProperties);
        Assert.Contains(nameof(PipelineStep.IsCompleted), changedProperties);
        Assert.Contains(nameof(PipelineStep.StatusIcon), changedProperties);
        Assert.Contains(nameof(PipelineStep.DurationDisplay), changedProperties);
    }
}
