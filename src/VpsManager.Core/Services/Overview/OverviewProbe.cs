using System.Globalization;
using System.Text;
using VpsManager.Core.Helpers;
using VpsManager.Core.Models;

namespace VpsManager.Core.Services.Overview;

/// <summary>
/// Collects all Overview telemetry in a single remote command and parses it into a snapshot.
/// </summary>
public interface IOverviewProbe
{
    Task<OverviewSnapshot> ProbeAsync(OverviewProbeRequest request, CancellationToken cancellationToken = default);
}

public sealed class OverviewProbe : IOverviewProbe
{
    public const string MetricsSection = "METRICS";
    public const string DockerSection = "DOCKER";
    public const string ServicesSection = "SERVICES";
    public const string FailedSection = "FAILED";
    public const string ProcessesSection = "PROCESSES";
    public const string TopProcessesSection = "TOPPROC";
    public const string DiskSection = "DISK";
    public const string DockerDiskSection = "DOCKERDF";
    public const string HealthSection = "HEALTH";
    public const string ExitMarker = "##EXIT=";

    private const string SectionPrefix = "##";
    private const int TopProcessLimit = 5;

    private static readonly string[] ProcessNoise = { "ps", "grep", "awk", "systemctl", "bash", "sh", "head", "sed" };

    private readonly ISshService _sshService;

    public OverviewProbe(ISshService sshService)
    {
        _sshService = sshService ?? throw new ArgumentNullException(nameof(sshService));
    }

    public async Task<OverviewSnapshot> ProbeAsync(OverviewProbeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_sshService.IsConnected)
        {
            throw new InvalidOperationException("Cannot probe the server while SSH is disconnected.");
        }

        var result = await _sshService.ExecuteCommandAsync(BuildScript(request), cancellationToken);
        if (!result.IsSuccess && string.IsNullOrWhiteSpace(result.Output))
        {
            throw new InvalidOperationException($"Overview probe failed: {result.Error}");
        }

        return Parse(result.Output);
    }

    /// <summary>
    /// Builds the single shell script whose output is split into named sections.
    /// Exactly one remote command is executed per refresh.
    /// </summary>
    public static string BuildScript(OverviewProbeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.TargetOS == HostOS.Windows)
        {
            var winBuilder = new StringBuilder();
            winBuilder.Append($"echo '{SectionPrefix}{MetricsSection}'\n");
            winBuilder.Append(SystemMetricsService.BuildMetricsCommand(HostOS.Windows)).Append('\n');

            winBuilder.Append($"echo '{SectionPrefix}{DockerSection}'\n");
            var winComposeFilePath = RemotePath.ResolveComposeFilePath(request.ComposeFileName, request.RemoteProjectPath);
            if (!string.IsNullOrWhiteSpace(winComposeFilePath))
            {
                winBuilder.Append(RuntimeInventoryService.BuildDockerInventoryCommand(winComposeFilePath)).Append('\n');
            }

            winBuilder.Append($"echo '{SectionPrefix}{ServicesSection}'\n");
            winBuilder.Append("powershell -NoProfile -Command \"Get-Service | Where-Object {$_.Status -eq 'Running'} | Select-Object -First 20 -ExpandProperty Name\" 2>nul || true\n");

            winBuilder.Append($"echo '{SectionPrefix}{FailedSection}'\n");
            winBuilder.Append("powershell -NoProfile -Command \"Get-Service | Where-Object {$_.Status -eq 'Stopped' -and $_.StartType -eq 'Automatic'} | Select-Object -First 10 -ExpandProperty Name\" 2>nul || true\n");

            winBuilder.Append($"echo '{SectionPrefix}{TopProcessesSection}'\n");
            winBuilder.Append("powershell -NoProfile -Command \"Get-Process | Sort-Object -Descending CPU | Select-Object -First 10 | ForEach-Object { '{0} {1:N1} {2:N1}' -f $_.ProcessName, $_.CPU, ($_.WorkingSet/1MB) }\" 2>nul || true\n");

            winBuilder.Append($"echo '{SectionPrefix}{DiskSection}'\n");
            winBuilder.Append("powershell -NoProfile -Command \"Get-CimInstance Win32_LogicalDisk | ForEach-Object { '{0}|{1}|{2:N2}|{3:N2}' -f $_.DeviceID, $_.DeviceID, (($_.Size - $_.FreeSpace)/1GB), ($_.Size/1GB) }\" 2>nul || true\n");

            winBuilder.Append($"echo '{SectionPrefix}{DockerDiskSection}'\n");
            winBuilder.Append("docker system df --format \"{{.Type}}|{{.Size}}|{{.Reclaimable}}\" 2>nul || true\n");

            if (!string.IsNullOrWhiteSpace(request.HealthProbeCommand))
            {
                winBuilder.Append($"echo '{SectionPrefix}{HealthSection}'\n");
                winBuilder.Append($"{request.HealthProbeCommand}\n");
            }

            return winBuilder.ToString().Replace("\r\n", "\n").Replace('\r', '\n');
        }

        var builder = new StringBuilder();
        builder.Append($"echo '{SectionPrefix}{MetricsSection}'\n");
        builder.Append(SystemMetricsService.BuildMetricsCommand(HostOS.Linux)).Append('\n');

        builder.Append($"echo '{SectionPrefix}{DockerSection}'\n");
        var composeFilePath = RemotePath.ResolveComposeFilePath(request.ComposeFileName, request.RemoteProjectPath);
        if (!string.IsNullOrWhiteSpace(composeFilePath))
        {
            builder.Append(RuntimeInventoryService.BuildDockerInventoryCommand(composeFilePath)).Append('\n');
        }

        builder.Append($"echo '{SectionPrefix}{ServicesSection}'\n");
        builder.Append(RuntimeInventoryService.BuildSystemServicesCommand()).Append('\n');

        builder.Append($"echo '{SectionPrefix}{FailedSection}'\n");
        builder.Append("systemctl --failed --no-legend --no-pager 2>/dev/null | head -20 || true\n");

        builder.Append($"echo '{SectionPrefix}{ProcessesSection}'\n");
        builder.Append(RuntimeInventoryService.BuildProcessInventoryCommand(request.RemoteProjectPath)).Append('\n');

        builder.Append($"echo '{SectionPrefix}{TopProcessesSection}'\n");
        builder.Append("ps -eo comm=,%cpu=,%mem= --no-headers --sort=-%cpu 2>/dev/null | head -12 || true\n");

        builder.Append($"echo '{SectionPrefix}{DiskSection}'\n");
        builder.Append("df -Pk 2>/dev/null | awk 'NR>1 {printf \"%s|%s|%.2f|%.2f\\n\", $1, $6, $3/1048576, $2/1048576}' | head -20 || true\n");

        builder.Append($"echo '{SectionPrefix}{DockerDiskSection}'\n");
        builder.Append("docker system df --format '{{.Type}}|{{.Size}}|{{.Reclaimable}}' 2>/dev/null || true\n");

        if (!string.IsNullOrWhiteSpace(request.HealthProbeCommand))
        {
            builder.Append($"echo '{SectionPrefix}{HealthSection}'\n");
            builder.Append($"{request.HealthProbeCommand}; printf '\\n{ExitMarker}%s\\n' \"$?\"\n");
        }

        return builder.ToString().Replace("\r\n", "\n").Replace('\r', '\n');
    }

    /// <summary>
    /// Pure parsing of probe output into a snapshot, with per-section error reporting so a
    /// partial failure degrades instead of blanking the tab.
    /// </summary>
    public static OverviewSnapshot Parse(string output)
    {
        var sections = ReadSections(output, out var healthExitCode);

        var metrics = SystemMetricsService.ParseMetrics(Join(sections, MetricsSection));

        var workloads = new List<RuntimeInfo>();
        workloads.AddRange(RuntimeInventoryService.ParseDockerInventory(Lines(sections, DockerSection)));
        workloads.AddRange(RuntimeInventoryService.ParseSystemServices(Lines(sections, ServicesSection)));
        workloads.AddRange(RuntimeInventoryService.ParseProcessInventory(Lines(sections, ProcessesSection)));

        var errors = new List<string>();
        if (!sections.ContainsKey(MetricsSection))
        {
            errors.Add("Resource metrics could not be collected.");
        }
        else if (!metrics.HasCpuMetrics && !metrics.HasRamMetrics && !metrics.HasDiskMetrics)
        {
            errors.Add("Resource metrics were returned in an unexpected format.");
        }

        if (!sections.ContainsKey(DockerSection))
        {
            errors.Add("Docker workload inventory could not be collected.");
        }

        if (!sections.ContainsKey(ProcessesSection))
        {
            errors.Add("Process inventory could not be collected.");
        }

        return new OverviewSnapshot
        {
            CapturedAt = DateTime.Now,
            Metrics = metrics,
            Workloads = workloads,
            FailedUnits = Lines(sections, FailedSection)
                .Select(ParseFailedUnit)
                .Where(unit => !string.IsNullOrWhiteSpace(unit))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            TopProcesses = ParseTopProcesses(Lines(sections, TopProcessesSection)),
            Mounts = ParseMounts(Lines(sections, DiskSection)),
            DockerDiskUsageLines = Lines(sections, DockerDiskSection).ToList(),
            HealthOutput = sections.ContainsKey(HealthSection) ? Join(sections, HealthSection) : null,
            HealthExitCode = healthExitCode,
            SectionErrors = errors
        };
    }

    private static Dictionary<string, List<string>> ReadSections(string output, out int? healthExitCode)
    {
        var sections = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        healthExitCode = null;
        List<string>? current = null;

        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.Trim();

            if (trimmed.StartsWith(SectionPrefix, StringComparison.Ordinal))
            {
                if (trimmed.StartsWith(ExitMarker, StringComparison.Ordinal))
                {
                    var exitValue = trimmed[ExitMarker.Length..];
                    if (int.TryParse(exitValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedExitCode))
                    {
                        healthExitCode = parsedExitCode;
                    }

                    continue;
                }

                var name = trimmed[SectionPrefix.Length..];
                if (!sections.TryGetValue(name, out var section))
                {
                    section = new List<string>();
                    sections[name] = section;
                }

                current = section;
                continue;
            }

            if (current != null && trimmed.Length > 0)
            {
                current.Add(trimmed);
            }
        }

        return sections;
    }

    private static IEnumerable<string> Lines(Dictionary<string, List<string>> sections, string name)
    {
        return sections.TryGetValue(name, out var lines) ? lines : Enumerable.Empty<string>();
    }

    private static string Join(Dictionary<string, List<string>> sections, string name)
    {
        return string.Join('\n', Lines(sections, name));
    }

    private static string ParseFailedUnit(string line)
    {
        var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return fields.Length > 0 ? fields[0] : string.Empty;
    }

    private static IReadOnlyList<ProcessSample> ParseTopProcesses(IEnumerable<string> lines)
    {
        var samples = new List<ProcessSample>();
        foreach (var line in lines)
        {
            var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 3)
            {
                continue;
            }

            var command = fields[0];
            if (ProcessNoise.Any(noise => command.Equals(noise, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (double.TryParse(fields[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var cpu)
                && double.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var memory))
            {
                samples.Add(new ProcessSample(command, cpu, memory));
            }

            if (samples.Count == TopProcessLimit)
            {
                break;
            }
        }

        return samples;
    }

    private static IReadOnlyList<DiskMount> ParseMounts(IEnumerable<string> lines)
    {
        var mounts = new List<DiskMount>();
        foreach (var line in lines)
        {
            var fields = line.Split('|');
            if (fields.Length < 4)
            {
                continue;
            }

            if (double.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var used)
                && double.TryParse(fields[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var total)
                && total > 0)
            {
                mounts.Add(new DiskMount(fields[0], fields[1], used, total));
            }
        }

        return mounts;
    }
}
