using System.Globalization;
using VpsManager.Core.Models;

namespace VpsManager.Core.Services;

public interface ISystemMetricsService
{
    Task<SystemResourceUsage> GetMetricsAsync(CancellationToken cancellationToken = default);
}

public class SystemMetricsService : ISystemMetricsService
{
    private readonly ISshService _sshService;

    public SystemMetricsService(ISshService sshService)
    {
        _sshService = sshService ?? throw new ArgumentNullException(nameof(sshService));
    }

    public async Task<SystemResourceUsage> GetMetricsAsync(CancellationToken cancellationToken = default)
    {
        if (!_sshService.IsConnected)
        {
            throw new InvalidOperationException("Cannot retrieve system metrics while SSH is disconnected.");
        }

        var result = await _sshService.ExecuteCommandAsync(BuildMetricsCommand(), cancellationToken);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"System metrics command failed: {result.Error}");
        }

        var metrics = ParseMetrics(result.Output);
        if (!metrics.HasCpuMetrics && !metrics.HasRamMetrics && !metrics.HasDiskMetrics)
        {
            throw new InvalidOperationException("The VPS returned no parseable CPU, memory, or disk metrics.");
        }

        return metrics;
    }

    /// <summary>
    /// Shell script that emits key=value lines for CPU, load, cores, memory, disk and uptime.
    /// Shared with the batched Overview probe so both paths agree exactly.
    /// </summary>
    public static string BuildMetricsCommand(HostOS targetOs = HostOS.Linux)
    {
        if (targetOs == HostOS.Windows)
        {
            return "powershell -NoProfile -Command \"" +
                "$cpu = (Get-CimInstance Win32_Processor | Measure-Object -Property LoadPercentage -Average).Average; " +
                "$os = Get-CimInstance Win32_OperatingSystem; " +
                "$totMem = [math]::Round($os.TotalVisibleMemorySize / 1048576, 2); " +
                "$freeMem = [math]::Round($os.FreePhysicalMemory / 1048576, 2); " +
                "$usedMem = [math]::Round($totMem - $freeMem, 2); " +
                "$disk = Get-CimInstance Win32_LogicalDisk -Filter 'DeviceID=\\\"C:\\\"'; " +
                "$freeD = [math]::Round($disk.FreeSpace / 1073741824, 2); " +
                "$totD = [math]::Round($disk.Size / 1073741824, 2); " +
                "$usedD = [math]::Round($totD - $freeD, 2); " +
                "$uptime = (Get-Date) - $os.LastBootUpTime; " +
                "Write-Output ('CPU=' + [math]::Round($cpu, 1)); " +
                "Write-Output ('RAM=' + $usedMem + ' ' + $totMem); " +
                "Write-Output ('DISK=' + $usedD + ' ' + $totD + ' C:'); " +
                "Write-Output ('CORES=' + [System.Environment]::ProcessorCount); " +
                "Write-Output ('UPTIME=' + $uptime.Days + ' days, ' + $uptime.Hours + ' hours')\"";
        }

        return
            "read -r _ user nice system idle iowait irq softirq steal _ _ < /proc/stat; " +
            "total1=$((user + nice + system + idle + iowait + irq + softirq + steal)); idle1=$((idle + iowait)); " +
            "sleep 0.25; " +
            "read -r _ user nice system idle iowait irq softirq steal _ _ < /proc/stat; " +
            "total2=$((user + nice + system + idle + iowait + irq + softirq + steal)); idle2=$((idle + iowait)); " +
            "awk -v t1=\"$total1\" -v t2=\"$total2\" -v i1=\"$idle1\" -v i2=\"$idle2\" " +
            "'BEGIN { if (t2 > t1) printf \"CPU=%.1f\\n\", (1 - (i2-i1)/(t2-t1))*100 }'; " +
            "read -r load1 load5 load15 _ < /proc/loadavg; printf 'LOAD=%s %s %s\\n' \"$load1\" \"$load5\" \"$load15\"; " +
            "printf 'CORES=%s\\n' \"$(nproc 2>/dev/null || getconf _NPROCESSORS_ONLN 2>/dev/null)\"; " +
            "awk '/^MemTotal:/ {total=$2} /^MemAvailable:/ {available=$2} " +
            "END {if (total > 0 && available >= 0) printf \"RAM=%.2f %.2f\\n\", (total-available)/1048576, total/1048576}' /proc/meminfo; " +
            "df -Pk / | awk 'NR == 2 {printf \"DISK=%.2f %.2f %s\\n\", $3/1048576, $2/1048576, $6}'; " +
            "awk -F': ' '/model name/ {print \"CPUMODEL=\" $2; exit}' /proc/cpuinfo 2>/dev/null; " +
            "awk -F': ' '/cpu MHz/ {printf \"CPUFREQ=%.1f GHz\\n\", $2/1000; exit}' /proc/cpuinfo 2>/dev/null; " +
            "awk '/^Active:/ {active=$2} /^Buffers:/ {buffers=$2} /^Cached:/ {cached=$2} /^SwapTotal:/ {st=$2} /^SwapFree:/ {sf=$2} END {printf \"RAMDETAIL=%.2f %.2f %.2f %.2f\\n\", active/1048576, (buffers+cached)/1048576, (st-sf)/1048576, st/1048576}' /proc/meminfo 2>/dev/null; " +
            "awk 'NR>2 && !/lo:|docker|br-|veth/ {sub(/:/, \"\", $1); printf \"NET=%s %s %s %s\\n\", $1, $2, $10, $4+$12; exit}' /proc/net/dev 2>/dev/null; " +
            "uptime -p 2>/dev/null | sed 's/^/UPTIME=/';";
    }

    /// <summary>
    /// Parses the key=value metrics output. Pure and independently testable.
    /// </summary>
    public static SystemResourceUsage ParseMetrics(string output)
    {
        var metrics = new SystemResourceUsage();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator];
            var value = line[(separator + 1)..].Trim();
            switch (key)
            {
                case "CPU" when TryParse(value, out var cpu):
                    metrics.CpuUsagePercent = cpu;
                    metrics.HasCpuMetrics = true;
                    break;
                case "LOAD":
                    metrics.LoadAverage = value;
                    break;
                case "CPUMODEL":
                    metrics.CpuModel = value;
                    break;
                case "CPUFREQ":
                    metrics.CpuFrequencyGhz = value;
                    break;
                case "CORES" when int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cores):
                    metrics.CpuCoreCount = cores;
                    break;
                case "RAM":
                    var memory = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (memory.Length >= 2 && TryParse(memory[0], out var used) && TryParse(memory[1], out var total) && total > 0)
                    {
                        metrics.RamUsedGb = used;
                        metrics.RamTotalGb = total;
                        metrics.RamAvailableGb = Math.Max(0, total - used);
                        metrics.HasRamMetrics = true;
                    }
                    break;
                case "RAMDETAIL":
                    var ramDetails = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (ramDetails.Length >= 2 && TryParse(ramDetails[0], out var active) && TryParse(ramDetails[1], out var buffers))
                    {
                        metrics.RamActiveGb = active;
                        metrics.RamBuffersGb = buffers;
                        if (ramDetails.Length >= 4 && TryParse(ramDetails[2], out var swapUsed) && TryParse(ramDetails[3], out var swapTotal))
                        {
                            metrics.SwapUsedGb = swapUsed;
                            metrics.SwapTotalGb = swapTotal;
                        }
                    }
                    break;
                case "DISK":
                    var disk = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (disk.Length >= 2 && TryParse(disk[0], out var diskUsed) && TryParse(disk[1], out var diskTotal) && diskTotal > 0)
                    {
                        metrics.DiskUsedGb = diskUsed;
                        metrics.DiskTotalGb = diskTotal;
                        metrics.DiskFreeGb = Math.Max(0, diskTotal - diskUsed);
                        metrics.DiskMountPoint = disk.Length >= 3 ? disk[2] : "/";
                        metrics.HasDiskMetrics = true;
                    }
                    break;
                case "NET":
                    var net = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (net.Length >= 3)
                    {
                        metrics.NetInterface = net[0];
                        if (TryParse(net[1], out var rxBytes))
                        {
                            metrics.NetRxMbps = Math.Round((rxBytes * 8) / 1_000_000d, 1);
                        }
                        if (TryParse(net[2], out var txBytes))
                        {
                            metrics.NetTxMbps = Math.Round((txBytes * 8) / 1_000_000d, 1);
                        }
                        if (net.Length >= 4 && long.TryParse(net[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var drops))
                        {
                            metrics.NetDrops = drops;
                        }
                        metrics.HasNetMetrics = true;
                    }
                    break;
                case "UPTIME":
                    metrics.Uptime = value;
                    break;
            }
        }

        return metrics;
    }

    private static bool TryParse(string value, out double parsed)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed);
    }
}
