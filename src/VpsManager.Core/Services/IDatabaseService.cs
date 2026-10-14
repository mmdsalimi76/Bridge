using VpsManager.Core.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace VpsManager.Core.Services;

public interface IDatabaseService
{
    Task<DatabaseBackupInfo> CreateRemoteBackupAsync(string dbContainerName, string dbUser, string dbPassword, string dbName, string engine, CancellationToken cancellationToken = default);
    Task<DatabaseBackupInfo> CreateRemoteBackupAsync(string dbContainerName, string dbUser, string dbName, CancellationToken cancellationToken = default);
    Task<DatabaseBackupInfo> CreateFolderBackupAsync(string remoteFolderPath, string backupName = "media", CancellationToken cancellationToken = default);
    Task<DatabaseBackupInfo> CreateFullBackupAsync(string dbContainerName, string dbUser, string dbPassword, string dbName, string engine, string remoteFolderPath, string projectName = "project", CancellationToken cancellationToken = default);
    Task<bool> VerifyMd5ChecksumAsync(string remoteFilePath, string localFilePath, CancellationToken cancellationToken = default);
    Task<CommandResult> RestoreBackupAsync(string dbContainerName, string dbUser, string dbPassword, string dbName, string engine, string backupFilePath, CancellationToken cancellationToken = default);
    Task<CommandResult> RestoreBackupAsync(string dbContainerName, string dbUser, string dbName, string backupFilePath, CancellationToken cancellationToken = default);
    Task<IEnumerable<DatabaseBackupInfo>> GetBackupsAsync(string backupDirectory = "/var/backups/vpsmanager", CancellationToken cancellationToken = default);
    Task<CommandResult> DeleteBackupAsync(string remoteFilePath, CancellationToken cancellationToken = default);
    Task DownloadBackupAsync(string remoteFilePath, string localDestinationPath, IProgress<double>? progress = null, CancellationToken cancellationToken = default);
    Task<CommandResult> SetupCronJobAsync(string cronExpression, string scriptContent, CancellationToken cancellationToken = default);
    Task<CommandResult> SetupScheduledBackupAsync(string cronExpression, BackupScope scope, string dbContainerName, string dbUser, string dbPassword, string dbName, string engine, string remoteFolderPath, CancellationToken cancellationToken = default);
    Task<IEnumerable<CronExecutionRecord>> GetCronHistoryAsync(CancellationToken cancellationToken = default);
}
