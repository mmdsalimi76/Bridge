using Microsoft.Extensions.DependencyInjection;
using VpsManager.Client.Security;
using VpsManager.Client.ViewModels;
using VpsManager.Core.Security;
using VpsManager.Core.Services;
using VpsManager.Core.Services.Adapters;
using VpsManager.Core.Services.Locking;
using VpsManager.Core.Services.Logging;
using VpsManager.Core.Services.Overview;
using VpsManager.Core.Services.Session;

namespace VpsManager.Client;

/// <summary>
/// The composition root. Every service and view model is registered here so adding a
/// feature does not mean editing a hand-wired constructor somewhere else.
/// </summary>
public static class BridgeServiceCollection
{
    public static IServiceCollection AddBridgeApplication(this IServiceCollection services)
    {
        // ── Security & platform ────────────────────────────────
        services.AddSingleton<IKnownHostsStore>(_ => new JsonKnownHostsStore());
        services.AddSingleton<IHostKeyTrustPrompt, UiHostKeyTrustPrompt>();
        services.AddSingleton<IHostKeyVerifier>(provider => new HostKeyPolicy(
            provider.GetRequiredService<IKnownHostsStore>(),
            provider.GetRequiredService<IHostKeyTrustPrompt>()));
        services.AddSingleton<ICredentialStore>(_ => CredentialStoreFactory.Create());
        services.AddSingleton<IAppLogger, AppLogger>();
        services.AddSingleton<IOperationLock, OperationLock>();

        // ── Transport ──────────────────────────────────────────
        services.AddSingleton<ISshService>(provider => new SshService(provider.GetRequiredService<IHostKeyVerifier>()));
        services.AddSingleton<ISshServiceFactory, SshServiceFactory>();
        services.AddSingleton<ISessionCoordinator, SessionCoordinator>();

        // ── Domain services ────────────────────────────────────
        services.AddSingleton<ConfigService>();
        services.AddSingleton<IActiveProfileContext, ActiveProfileContext>();
        services.AddSingleton<IDockerService, DockerService>();
        services.AddSingleton<IDatabaseService, DatabaseService>();
        services.AddSingleton<IDeploymentPipelineService, DeploymentPipelineService>();
        services.AddSingleton<ILocalBuildService, LocalBuildService>();
        services.AddSingleton<IRuntimeInventoryService, RuntimeInventoryService>();
        services.AddSingleton<IPatchService, PatchService>();
        services.AddSingleton<ISyncIgnoreService, SyncIgnoreService>();
        services.AddSingleton<ITerminalService, TerminalService>();
        services.AddSingleton<SavedCommandService>();
        services.AddSingleton<ISystemMetricsService, SystemMetricsService>();
        services.AddSingleton<IHealthCheckService, UniversalHealthCheckService>();
        services.AddSingleton<ISetupProvisioningService, SetupProvisioningService>();

        // ── Overview pipeline ──────────────────────────────────
        services.AddSingleton<IOverviewProbe, OverviewProbe>();
        services.AddSingleton(_ => new MetricHistoryStore());
        services.AddSingleton(_ => new AttentionThresholds());

        // ── View models ────────────────────────────────────────
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<DeployViewModel>();
        services.AddSingleton<PatchViewModel>();
        services.AddSingleton<DatabaseViewModel>();
        services.AddSingleton<DockerViewModel>();
        services.AddSingleton<TerminalViewModel>();
        services.AddSingleton<SetupViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<MainWindowViewModel>();

        return services;
    }
}
