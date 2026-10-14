using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VpsManager.Core.Helpers;
using VpsManager.Core.Models;

namespace VpsManager.Core.Services;

public class SetupProvisioningService : ISetupProvisioningService
{
    private readonly ISshService _sshService;

    public const string PreflightProbeScript =
        "printf \"OS=%s\\n\" \"$((. /etc/os-release 2>/dev/null && echo \"$PRETTY_NAME\") || uname -s)\"; " +
        "printf \"KERNEL=%s\\n\" \"$(uname -sr)\"; " +
        "printf \"ARCH=%s\\n\" \"$(uname -m)\"; " +
        "printf \"MEM_AVAIL=%s\\n\" \"$(free -h 2>/dev/null | awk '/^Mem:/ {print $7}')\"; " +
        "printf \"MEM_TOTAL=%s\\n\" \"$(free -h 2>/dev/null | awk '/^Mem:/ {print $2}')\"; " +
        "printf \"DISK_FREE=%s\\n\" \"$(df -h / 2>/dev/null | awk 'NR==2 {print $4 \" free of \" $2}')\"; " +
        "printf \"UPTIME=%s\\n\" \"$(uptime -p 2>/dev/null || uptime)\"";

    public SetupProvisioningService(ISshService sshService)
    {
        _sshService = sshService ?? throw new ArgumentNullException(nameof(sshService));
    }

    public IReadOnlyList<ProvisioningStepDefinition> BuildSteps(ProvisioningPlanRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var steps = new List<ProvisioningStepDefinition>();

        if (request.UpdateSystem)
        {
            steps.Add(new ProvisioningStepDefinition(
                "system_update",
                "Update package lists and upgrade system",
                BuildSystemUpdateCommand(request)));
        }

        if (request.InstallEssentials)
        {
            steps.Add(new ProvisioningStepDefinition(
                "essentials",
                "Install essential server tools",
                BuildEssentialsCommand(request)));
        }

        if (request.InstallDocker)
        {
            steps.Add(new ProvisioningStepDefinition(
                "docker",
                "Install Docker Engine & Compose plugin",
                BuildDockerCommand(request)));
        }

        if (request.ConfigureFirewall)
        {
            steps.Add(new ProvisioningStepDefinition(
                "ufw",
                "Configure UFW firewall for SSH and web traffic",
                BuildFirewallCommand()));
        }

        if (request.InstallFail2Ban)
        {
            steps.Add(new ProvisioningStepDefinition(
                "fail2ban",
                "Install and enable fail2ban brute-force protection",
                BuildFail2BanCommand()));
        }

        if (request.EnableUnattendedUpgrades)
        {
            steps.Add(new ProvisioningStepDefinition(
                "unattended_upgrades",
                "Configure automatic unattended security upgrades",
                BuildUnattendedUpgradesCommand()));
        }

        if (request.HardenSshSecurity)
        {
            steps.Add(new ProvisioningStepDefinition(
                "ssh_hardening",
                "Apply SSH server security hardening baseline",
                BuildSshHardeningCommand()));
        }

        if (request.CreateSwap)
        {
            var swapSize = Math.Clamp(request.SwapSizeGb, 1, 64);
            steps.Add(new ProvisioningStepDefinition(
                "swap",
                $"Create {swapSize} GB swap file",
                BuildSwapCommand(swapSize)));
        }

        if (request.PrepareProjectDirectory)
        {
            var directory = request.ProjectDirectory.Trim();
            if (!string.IsNullOrWhiteSpace(directory) && (directory.StartsWith('/') || directory.StartsWith('\\') || (directory.Length > 2 && directory[1] == ':')))
            {
                steps.Add(new ProvisioningStepDefinition(
                    "project_dir",
                    "Prepare project workspace directory",
                    BuildProjectDirectoryCommand(directory)));
            }
        }

        if (request.InstallNodeJs)
        {
            steps.Add(new ProvisioningStepDefinition(
                "nodejs",
                "Install Node.js runtime and npm",
                BuildNodeJsCommand()));
        }

        if (request.InstallNginx)
        {
            steps.Add(new ProvisioningStepDefinition(
                "nginx",
                "Install and enable Nginx web server",
                BuildNginxCommand()));
        }

        return steps;
    }

    public async Task<HostPreflightInfo> RunPreflightProbeAsync(CancellationToken cancellationToken = default)
    {
        var result = await _sshService.ExecuteCommandAsync(PreflightProbeScript, cancellationToken);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"Preflight probe failed with exit code {result.ExitCode}: {result.Error}");
        }

        return ParsePreflightOutput(result.Output);
    }

    public Task<CommandResult> ExecuteStepAsync(ProvisioningStepDefinition step, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(step);
        return _sshService.ExecuteCommandAsync(step.Command, cancellationToken);
    }

    public static HostPreflightInfo ParsePreflightOutput(string output)
    {
        string os = "", kernel = "", arch = "", memAvail = "", memTotal = "", diskFree = "", uptime = "";

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split('=', 2);
            if (parts.Length != 2) continue;

            var key = parts[0].Trim();
            var val = parts[1].Trim();

            switch (key)
            {
                case "OS": os = val; break;
                case "KERNEL": kernel = val; break;
                case "ARCH": arch = val; break;
                case "MEM_AVAIL": memAvail = val; break;
                case "MEM_TOTAL": memTotal = val; break;
                case "DISK_FREE": diskFree = val; break;
                case "UPTIME": uptime = val; break;
            }
        }

        return new HostPreflightInfo
        {
            Os = os,
            Kernel = kernel,
            Arch = arch,
            MemoryAvailable = memAvail,
            MemoryTotal = memTotal,
            DiskFree = diskFree,
            Uptime = uptime
        };
    }

    public static string BuildAptLockWait()
    {
        return "while sudo fuser /var/lib/dpkg/lock-frontend /var/lib/apt/lists/lock /var/lib/dpkg/lock >/dev/null 2>&1; do echo 'Waiting for package manager lock to release...'; sleep 2; done";
    }

    private static string BuildSystemUpdateCommand(ProvisioningPlanRequest request)
    {
        return $"{BuildPackageMirrorPrefix(request.PackageMirrorUrl)}{BuildAptLockWait()} && sudo DEBIAN_FRONTEND=noninteractive apt-get update && sudo DEBIAN_FRONTEND=noninteractive apt-get -y -o Dpkg::Options::=\"--force-confdef\" -o Dpkg::Options::=\"--force-confold\" upgrade";
    }

    private static string BuildEssentialsCommand(ProvisioningPlanRequest request)
    {
        return $"{BuildPackageMirrorPrefix(request.PackageMirrorUrl)}{BuildAptLockWait()} && sudo DEBIAN_FRONTEND=noninteractive apt-get install -y -o Dpkg::Options::=\"--force-confdef\" -o Dpkg::Options::=\"--force-confold\" ca-certificates curl git unzip wget vim htop ncdu rsync ufw";
    }

    private static string BuildDockerCommand(ProvisioningPlanRequest request)
    {
        if (request.DockerSource == "Ubuntu packages (docker.io)")
        {
            return $"{BuildAptLockWait()} && sudo DEBIAN_FRONTEND=noninteractive apt-get install -y -o Dpkg::Options::=\"--force-confdef\" -o Dpkg::Options::=\"--force-confold\" docker.io docker-compose-v2 && sudo systemctl enable --now docker && sudo usermod -aG docker \"$USER\"";
        }

        var repositoryUrl = NormalizeUrl(request.DockerRepositoryUrl, "Docker repository URL");
        return $"{BuildAptLockWait()} && sudo DEBIAN_FRONTEND=noninteractive apt-get install -y -o Dpkg::Options::=\"--force-confdef\" -o Dpkg::Options::=\"--force-confold\" ca-certificates curl gnupg && sudo install -m 0755 -d /etc/apt/keyrings && curl -fsSL '{repositoryUrl}/gpg' | sudo gpg --dearmor --yes -o /etc/apt/keyrings/docker.gpg && sudo chmod a+r /etc/apt/keyrings/docker.gpg && echo \"deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] {repositoryUrl} $(. /etc/os-release && echo $VERSION_CODENAME) stable\" | sudo tee /etc/apt/sources.list.d/docker.list > /dev/null && sudo apt-get update && sudo DEBIAN_FRONTEND=noninteractive apt-get install -y -o Dpkg::Options::=\"--force-confdef\" -o Dpkg::Options::=\"--force-confold\" docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin && sudo systemctl enable --now docker && sudo usermod -aG docker \"$USER\"";
    }

    private static string BuildFirewallCommand()
    {
        return "sudo ufw allow OpenSSH && sudo ufw allow 80/tcp && sudo ufw allow 443/tcp && sudo ufw --force enable";
    }

    private static string BuildFail2BanCommand()
    {
        return $"{BuildAptLockWait()} && sudo DEBIAN_FRONTEND=noninteractive apt-get install -y -o Dpkg::Options::=\"--force-confdef\" -o Dpkg::Options::=\"--force-confold\" fail2ban && sudo systemctl enable --now fail2ban";
    }

    private static string BuildUnattendedUpgradesCommand()
    {
        return $"{BuildAptLockWait()} && sudo DEBIAN_FRONTEND=noninteractive apt-get install -y -o Dpkg::Options::=\"--force-confdef\" -o Dpkg::Options::=\"--force-confold\" unattended-upgrades && sudo systemctl enable --now unattended-upgrades";
    }

    private static string BuildSshHardeningCommand()
    {
        return "sudo mkdir -p /etc/ssh/sshd_config.d && printf 'PermitEmptyPasswords no\\nClientAliveInterval 300\\nClientAliveCountMax 2\\n' | sudo tee /etc/ssh/sshd_config.d/99-vpsmanager-hardening.conf >/dev/null && (sudo sshd -t && (sudo systemctl reload ssh 2>/dev/null || sudo systemctl reload sshd 2>/dev/null || true))";
    }

    private static string BuildSwapCommand(int swapSize)
    {
        return $"if ! swapon --show | grep -q '^'; then sudo fallocate -l {swapSize}G /swapfile && sudo chmod 600 /swapfile && sudo mkswap /swapfile && sudo swapon /swapfile && echo '/swapfile none swap sw 0 0' | sudo tee -a /etc/fstab; else echo 'Swap already active'; fi";
    }

    private static string BuildProjectDirectoryCommand(string directory)
    {
        var quotedDirectory = ShellQuote.Quote(directory);
        return $"sudo mkdir -p -- {quotedDirectory} && sudo chown -R \"$USER\":\"$USER\" -- {quotedDirectory}";
    }

    private static string BuildNodeJsCommand()
    {
        return $"{BuildAptLockWait()} && sudo DEBIAN_FRONTEND=noninteractive apt-get install -y -o Dpkg::Options::=\"--force-confdef\" -o Dpkg::Options::=\"--force-confold\" nodejs npm";
    }

    private static string BuildNginxCommand()
    {
        return $"{BuildAptLockWait()} && sudo DEBIAN_FRONTEND=noninteractive apt-get install -y -o Dpkg::Options::=\"--force-confdef\" -o Dpkg::Options::=\"--force-confold\" nginx && sudo systemctl enable --now nginx";
    }

    private static string BuildPackageMirrorPrefix(string packageMirrorUrl)
    {
        var mirror = packageMirrorUrl?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(mirror))
        {
            return string.Empty;
        }

        mirror = NormalizeUrl(mirror, "APT package mirror URL");
        var quotedMirror = ShellQuote.Quote(mirror);
        return $"sudo find /etc/apt -type f \\( -name '*.list' -o -name '*.sources' \\) -exec sudo sed -i -E 's#https?://[^ ]+#{quotedMirror}#g' {{}} + && ";
    }

    public static string NormalizeUrl(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(value.Trim().TrimEnd('/'), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException($"{fieldName} must be a valid HTTP or HTTPS URL.");
        }

        return uri.ToString().TrimEnd('/');
    }
}
