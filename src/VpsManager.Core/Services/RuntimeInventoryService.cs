using System.Globalization;
using System.Text.Json;
using VpsManager.Core.Helpers;
using VpsManager.Core.Models;

namespace VpsManager.Core.Services;

public interface IRuntimeInventoryService
{
    Task<List<RuntimeInfo>> GetInventoryAsync(string composeFileName, string projectPath, CancellationToken cancellationToken = default);
    Task<string> GetLogsAsync(RuntimeInfo runtime, int lineCount = 100, CancellationToken cancellationToken = default);
}

public sealed class RuntimeInventoryService : IRuntimeInventoryService
{
    private readonly ISshService _sshService;

    public RuntimeInventoryService(ISshService sshService)
    {
        _sshService = sshService ?? throw new ArgumentNullException(nameof(sshService));
    }

    public async Task<List<RuntimeInfo>> GetInventoryAsync(string composeFileName, string projectPath, CancellationToken cancellationToken = default)
    {
        var inventory = new List<RuntimeInfo>();

        var composeFilePath = RemotePath.ResolveComposeFilePath(composeFileName, projectPath);
        if (!string.IsNullOrWhiteSpace(composeFilePath))
        {
            var dockerResult = await _sshService.ExecuteCommandAsync(BuildDockerInventoryCommand(composeFilePath), cancellationToken);
            inventory.AddRange(ParseDockerInventory(SplitLines(dockerResult.Output)));
        }

        var servicesResult = await _sshService.ExecuteCommandAsync(BuildSystemServicesCommand(), cancellationToken);
        inventory.AddRange(ParseSystemServices(SplitLines(servicesResult.Output)));

        var processesResult = await _sshService.ExecuteCommandAsync(BuildProcessInventoryCommand(projectPath), cancellationToken);
        inventory.AddRange(ParseProcessInventory(SplitLines(processesResult.Output)));

        return inventory;
    }

    public async Task<string> GetLogsAsync(RuntimeInfo runtime, int lineCount = 100, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        if (string.IsNullOrWhiteSpace(runtime.LogsHint))
        {
            return "No automatic log source is configured for this runtime.";
        }

        var boundedLineCount = Math.Clamp(lineCount, 1, 1000);
        var command = runtime.LogsHint.Replace("{lineCount}", boundedLineCount.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        var result = await _sshService.ExecuteCommandAsync(command, cancellationToken);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"Unable to fetch logs for {runtime.Name}: {result.Error}");
        }
        return string.IsNullOrWhiteSpace(result.Output) ? result.Error : result.Output;
    }

    // ── Shared command builders and parsers ────────────────────────────────
    // Used both by the individual inventory calls above and by the batched
    // Overview probe, so command text and parsing exist exactly once.

    public static string BuildDockerInventoryCommand(string composeFilePath)
    {
        // Query docker compose ps for the configured project stack; fall back to docker ps if compose fails or file is missing
        string quoted = ShellQuote.Quote(composeFilePath);
        return $"docker compose -f {quoted} ps --format json 2>/dev/null || docker ps --format json 2>/dev/null || true";
    }

    public static IReadOnlyList<RuntimeInfo> ParseDockerInventory(IEnumerable<string> lines)
    {
        var workloads = new List<RuntimeInfo>();
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (line[0] == '{' || line[0] == '[')
            {
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var element in doc.RootElement.EnumerateArray())
                        {
                            AddDockerWorkload(workloads, element);
                        }
                    }
                    else if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        AddDockerWorkload(workloads, doc.RootElement);
                    }
                }
                catch (JsonException)
                {
                    // Fall through to the tab-separated renderer below for non-JSON output.
                }

                continue;
            }

            var fields = line.Split('\t', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length >= 2)
            {
                var name = fields[0];
                var status = fields[1];
                var image = fields.Length > 2 ? fields[2] : string.Empty;
                var details = fields.Length > 3 ? fields[3] : string.Empty;
                var ports = fields.Length > 4 ? fields[4] : string.Empty;

                if (!string.IsNullOrWhiteSpace(name))
                {
                    workloads.Add(new RuntimeInfo
                    {
                        Name = name,
                        Source = "Docker",
                        Status = status,
                        Image = image,
                        Details = details,
                        Ports = ports,
                        Uptime = details,
                        LogsHint = $"docker logs --tail {{lineCount}} {ShellQuote.Quote(name)}"
                    });
                }
            }
        }

        return workloads;
    }

    private static void AddDockerWorkload(List<RuntimeInfo> workloads, JsonElement container)
    {
        if (container.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var name = ReadString(container, "Name");
        if (string.IsNullOrWhiteSpace(name))
        {
            name = ReadString(container, "Names");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        workloads.Add(new RuntimeInfo
        {
            Name = name,
            Source = "Docker",
            Status = ReadString(container, "State"),
            Image = ReadString(container, "Image"),
            Details = ReadString(container, "Status"),
            Uptime = ReadString(container, "Status"),
            Ports = ReadString(container, "Ports"),
            LogsHint = $"docker logs --tail {{lineCount}} {ShellQuote.Quote(name)}"
        });
    }

    private static string ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    public static string BuildSystemServicesCommand()
    {
        return "systemctl list-units --type=service --state=running --no-legend --no-pager 2>/dev/null | grep -Ei 'nginx|apache|caddy|gunicorn|uvicorn|node|dotnet|django|python|app|worker' | awk '{print $1 \"\\t\" $3 \"\\t\" $4}' || true";
    }

    public static IReadOnlyList<RuntimeInfo> ParseSystemServices(IEnumerable<string> lines)
    {
        var workloads = new List<RuntimeInfo>();
        foreach (var line in lines)
        {
            var fields = line.Split('\t');
            if (fields.Length == 0)
            {
                continue;
            }

            var unit = fields[0];
            workloads.Add(new RuntimeInfo
            {
                Name = unit,
                Source = IsWebGateway(unit) ? "Web gateway" : "systemd",
                Status = fields.Length > 1 ? fields[1] : "running",
                Details = fields.Length > 2 ? fields[2] : "Managed service",
                LogsHint = $"journalctl -u {ShellQuote.Quote(unit)} -n {{lineCount}} --no-pager"
            });
        }

        return workloads;
    }

    public static string BuildProcessInventoryCommand(string projectPath)
    {
        // The alternation is ours; the project path is escaped as a literal so a path
        // containing regex metacharacters cannot corrupt the pattern.
        var filter = string.IsNullOrWhiteSpace(projectPath)
            ? "python|node|npm|dotnet|gunicorn|uvicorn"
            : $"python|node|npm|dotnet|gunicorn|uvicorn|{ShellQuote.EscapeExtendedRegex(projectPath)}";
        return $"ps -eo pid=,comm=,etime=,%cpu=,%mem=,args= --no-headers | grep -Ei {ShellQuote.Quote(filter)} | grep -vE 'grep|systemctl|docker compose' | head -50 || true";
    }

    public static IReadOnlyList<RuntimeInfo> ParseProcessInventory(IEnumerable<string> lines)
    {
        var workloads = new List<RuntimeInfo>();
        foreach (var line in lines)
        {
            var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 6)
            {
                continue;
            }

            workloads.Add(new RuntimeInfo
            {
                Name = $"{fields[1]} ({fields[0]})",
                Source = "Process",
                Status = "running",
                Uptime = fields[2],
                Details = $"CPU {fields[3]}% / RAM {fields[4]}%",
                LogsHint = ""
            });
        }

        return workloads;
    }

    public static IEnumerable<string> SplitLines(string output)
    {
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public static bool IsWebGateway(string unit)
    {
        return unit.Contains("nginx", StringComparison.OrdinalIgnoreCase)
            || unit.Contains("apache", StringComparison.OrdinalIgnoreCase)
            || unit.Contains("caddy", StringComparison.OrdinalIgnoreCase);
    }
}
