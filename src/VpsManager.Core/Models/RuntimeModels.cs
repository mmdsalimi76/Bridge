namespace VpsManager.Core.Models;

public class RuntimeInfo
{
    public string Name { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public string Image { get; set; } = string.Empty;
    public string Ports { get; set; } = string.Empty;
    public string Uptime { get; set; } = string.Empty;
    public string LogsHint { get; set; } = string.Empty;
}
