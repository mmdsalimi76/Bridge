namespace VpsManager.Core.Models;

public class SystemResourceUsage
{
    // CPU
    public double CpuUsagePercent { get; set; }
    public int CpuCoreCount { get; set; }
    public string LoadAverage { get; set; } = string.Empty;
    public string CpuModel { get; set; } = "vCPU Host";
    public string CpuFrequencyGhz { get; set; } = string.Empty;
    public string CpuTemperature { get; set; } = "Normal";
    public string Uptime { get; set; } = string.Empty;
    public bool HasCpuMetrics { get; set; }

    // Memory
    public double RamUsedGb { get; set; }
    public double RamTotalGb { get; set; }
    public double RamAvailableGb { get; set; }
    public double RamActiveGb { get; set; }
    public double RamBuffersGb { get; set; }
    public string RamType { get; set; } = "System RAM";
    public double SwapUsedGb { get; set; }
    public double SwapTotalGb { get; set; }
    public double RamUsagePercent => RamTotalGb > 0 ? (RamUsedGb / RamTotalGb) * 100 : 0;
    public bool HasRamMetrics { get; set; }

    // Disk
    public double DiskUsedGb { get; set; }
    public double DiskTotalGb { get; set; }
    public double DiskFreeGb { get; set; }
    public string DiskMountPoint { get; set; } = "/";
    public string DiskFilesystem { get; set; } = "ext4";
    public double DiskReadMbSec { get; set; }
    public double DiskWriteMbSec { get; set; }
    public double DiskUsagePercent => DiskTotalGb > 0 ? (DiskUsedGb / DiskTotalGb) * 100 : 0;
    public bool HasDiskMetrics { get; set; }

    // Network
    public string NetInterface { get; set; } = "eth0";
    public double NetRxMbps { get; set; }
    public double NetTxMbps { get; set; }
    public long NetDrops { get; set; }
    public double NetPacketsSec { get; set; }
    public bool HasNetMetrics { get; set; }
}
