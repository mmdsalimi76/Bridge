using System;

namespace VpsManager.Core.Models;

/// <summary>
/// Configuration payload for building server provisioning steps.
/// </summary>
public record ProvisioningPlanRequest
{
    public bool UpdateSystem { get; init; } = true;
    public bool InstallEssentials { get; init; } = true;
    public bool InstallDocker { get; init; } = true;
    public bool ConfigureFirewall { get; init; }
    public bool InstallFail2Ban { get; init; }
    public bool EnableUnattendedUpgrades { get; init; } = true;
    public bool HardenSshSecurity { get; init; }
    public bool CreateSwap { get; init; }
    public bool PrepareProjectDirectory { get; init; } = true;
    public bool InstallNodeJs { get; init; }
    public bool InstallNginx { get; init; }

    public string DockerSource { get; init; } = "Docker official repository";
    public string DockerRepositoryUrl { get; init; } = "https://download.docker.com/linux/ubuntu";
    public string PackageMirrorUrl { get; init; } = "https://archive.ubuntu.com/ubuntu";
    public int SwapSizeGb { get; init; } = 2;
    public string ProjectDirectory { get; init; } = "/var/www/my_app";
}

/// <summary>
/// Definition of a single provisioning execution step.
/// </summary>
public record ProvisioningStepDefinition(string Id, string Name, string Command);

/// <summary>
/// Host hardware and distribution preflight telemetry parsed from remote server.
/// </summary>
public record HostPreflightInfo
{
    public string Os { get; init; } = string.Empty;
    public string Kernel { get; init; } = string.Empty;
    public string Arch { get; init; } = string.Empty;
    public string MemoryAvailable { get; init; } = string.Empty;
    public string MemoryTotal { get; init; } = string.Empty;
    public string MemorySummary => !string.IsNullOrEmpty(MemoryAvailable) && !string.IsNullOrEmpty(MemoryTotal)
        ? $"{MemoryAvailable} available ({MemoryTotal} total)"
        : MemoryTotal;
    public string DiskFree { get; init; } = string.Empty;
    public string Uptime { get; init; } = string.Empty;
}
