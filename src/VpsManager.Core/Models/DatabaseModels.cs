namespace VpsManager.Core.Models;

public enum BackupScope
{
    Database,
    Folder,
    Full
}

public class DatabaseBackupInfo
{
    public string FileName { get; set; } = string.Empty;
    public string RemotePath { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string Md5Hash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public BackupScope Scope { get; set; } = BackupScope.Database;

    public string FormattedSize => SizeBytes switch
    {
        >= 1024 * 1024 * 1024 => $"{(SizeBytes / (1024.0 * 1024 * 1024)):F2} GB",
        >= 1024 * 1024 => $"{(SizeBytes / (1024.0 * 1024)):F2} MB",
        >= 1024 => $"{(SizeBytes / 1024.0):F1} KB",
        _ => $"{SizeBytes} B"
    };

    public string ScopeDisplayName => Scope switch
    {
        BackupScope.Folder => "Files / Media",
        BackupScope.Full => "Full Project",
        _ => "Database"
    };

    public string ScopeIcon => Scope switch
    {
        BackupScope.Folder => "📁",
        BackupScope.Full => "📦",
        _ => "🗄️"
    };

    public bool IsDatabaseScope => Scope == BackupScope.Database;
    public bool IsFolderScope => Scope == BackupScope.Folder;
    public bool IsFullScope => Scope == BackupScope.Full;
}

public class CronExecutionRecord
{
    public DateTime Timestamp { get; set; }
    public string Scope { get; set; } = "Database";
    public string TargetName { get; set; } = string.Empty;
    public bool IsSuccess { get; set; }
    public bool IsFailed => !IsSuccess;
    public string StatusText => IsSuccess ? "SUCCESS" : "FAILED";
    public string OutputFile { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string Message { get; set; } = string.Empty;

    public string FormattedSize => SizeBytes switch
    {
        >= 1024 * 1024 * 1024 => $"{(SizeBytes / (1024.0 * 1024 * 1024)):F2} GB",
        >= 1024 * 1024 => $"{(SizeBytes / (1024.0 * 1024)):F2} MB",
        >= 1024 => $"{(SizeBytes / 1024.0):F1} KB",
        > 0 => $"{SizeBytes} B",
        _ => "-"
    };
}
