using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VpsManager.Core.Models;

namespace VpsManager.Core.Services;

/// <summary>
/// Service responsible for constructing defensive VPS provisioning steps,
/// running preflight system inspection probes, and executing provisioning commands over SSH.
/// </summary>
public interface ISetupProvisioningService
{
    /// <summary>
    /// Generates the sequential provisioning steps based on the given configuration plan.
    /// </summary>
    IReadOnlyList<ProvisioningStepDefinition> BuildSteps(ProvisioningPlanRequest request);

    /// <summary>
    /// Probes remote VPS system details (OS, kernel, architecture, memory, disk, uptime).
    /// </summary>
    Task<HostPreflightInfo> RunPreflightProbeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes an individual provisioning step on the remote VPS.
    /// </summary>
    Task<CommandResult> ExecuteStepAsync(ProvisioningStepDefinition step, CancellationToken cancellationToken = default);
}
