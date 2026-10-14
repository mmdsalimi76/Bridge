using VpsManager.Core.Helpers;
using VpsManager.Core.Models;

namespace VpsManager.Core.Services.Adapters;

public interface IDatabaseAdapter
{
    DatabaseEngine Engine { get; }
    Task<DatabaseBackupResult> BackupAsync(ISshService sshService, UniversalPipelineConfig config, CancellationToken cancellationToken = default);
    Task<CommandResult> RunMigrationAsync(ISshService sshService, UniversalPipelineConfig config, CancellationToken cancellationToken = default);
}

public class UniversalDatabaseAdapter : IDatabaseAdapter
{
    private readonly DatabaseEngine _engine;

    public UniversalDatabaseAdapter(DatabaseEngine engine)
    {
        _engine = engine;
    }

    public DatabaseEngine Engine => _engine;

    public async Task<DatabaseBackupResult> BackupAsync(ISshService sshService, UniversalPipelineConfig config, CancellationToken cancellationToken = default)
    {
        if (config.SkipDatabaseBackup || _engine == DatabaseEngine.None)
        {
            return new DatabaseBackupResult { IsSuccess = true, BackupPath = "Skipped" };
        }

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var backupFileName = $"backup_{config.DbName}_{timestamp}";

        string backupCmd = string.Empty;

        if (!string.IsNullOrWhiteSpace(config.CustomBackupCommand))
        {
            backupCmd = config.CustomBackupCommand.Replace("{db_name}", config.DbName).Replace("{db_user}", config.DbUser);
        }
        else if (config.IsDbContainerized && !string.IsNullOrWhiteSpace(config.DbContainerName))
        {
            switch (_engine)
            {
                case DatabaseEngine.PostgreSQL:
                    backupFileName += ".sql";
                    backupCmd = $"docker exec {ShellQuote.Quote(config.DbContainerName)} pg_dump -U {ShellQuote.Quote(config.DbUser)} {ShellQuote.Quote(config.DbName)} > \"/tmp/{backupFileName}\"";
                    break;
                case DatabaseEngine.MySQL:
                    backupFileName += ".sql";
                    backupCmd = $"docker exec {ShellQuote.Quote(config.DbContainerName)} mysqldump -u {ShellQuote.Quote(config.DbUser)} {ShellQuote.Quote(config.DbName)} > \"/tmp/{backupFileName}\"";
                    break;
                case DatabaseEngine.MongoDB:
                    backupFileName += ".gz";
                    backupCmd = $"docker exec {ShellQuote.Quote(config.DbContainerName)} mongodump --db {ShellQuote.Quote(config.DbName)} --archive=\"/tmp/{backupFileName}\" --gzip";
                    break;
                case DatabaseEngine.Redis:
                    backupFileName += ".rdb";
                    backupCmd = $"docker exec {ShellQuote.Quote(config.DbContainerName)} redis-cli bgsave";
                    break;
                case DatabaseEngine.SQLite:
                    backupFileName += ".sqlite";
                    backupCmd = $"cp \"{config.RemoteProjectPath}/{config.DbName}\" \"/tmp/{backupFileName}\"";
                    break;
                default:
                    return new DatabaseBackupResult { IsSuccess = true, BackupPath = "No standard command defined" };
            }
        }
        else
        {
            // Host Native DB
            switch (_engine)
            {
                case DatabaseEngine.PostgreSQL:
                    backupFileName += ".sql";
                    backupCmd = $"pg_dump -U {ShellQuote.Quote(config.DbUser)} -d {ShellQuote.Quote(config.DbName)} -f \"/tmp/{backupFileName}\"";
                    break;
                case DatabaseEngine.MySQL:
                    backupFileName += ".sql";
                    backupCmd = $"mysqldump -u {ShellQuote.Quote(config.DbUser)} {ShellQuote.Quote(config.DbName)} > \"/tmp/{backupFileName}\"";
                    break;
                case DatabaseEngine.SQLite:
                    backupFileName += ".db";
                    backupCmd = $"sqlite3 \"{config.RemoteProjectPath}/{config.DbName}\" \".backup '/tmp/{backupFileName}'\"";
                    break;
                case DatabaseEngine.MongoDB:
                    backupFileName += ".gz";
                    backupCmd = $"mongodump --db {ShellQuote.Quote(config.DbName)} --archive=\"/tmp/{backupFileName}\" --gzip";
                    break;
                case DatabaseEngine.MSSQL:
                    backupFileName += ".bak";
                    backupCmd = $"sqlcmd -Q \"BACKUP DATABASE [{config.DbName}] TO DISK = N'/tmp/{backupFileName}' WITH FORMAT;\"";
                    break;
                default:
                    return new DatabaseBackupResult { IsSuccess = true, BackupPath = "Skipped" };
            }
        }

        var result = await sshService.ExecuteCommandAsync(backupCmd, cancellationToken);
        if (result.ExitCode != 0)
        {
            return new DatabaseBackupResult
            {
                IsSuccess = false,
                ErrorMessage = $"Backup command failed: {result.ErrorOutput}"
            };
        }

        return new DatabaseBackupResult
        {
            IsSuccess = true,
            BackupPath = $"/tmp/{backupFileName}"
        };
    }

    public async Task<CommandResult> RunMigrationAsync(ISshService sshService, UniversalPipelineConfig config, CancellationToken cancellationToken = default)
    {
        if (_engine == DatabaseEngine.None)
            return new CommandResult { ExitCode = 0, Output = "Skipped" };

        if (!string.IsNullOrWhiteSpace(config.CustomMigrationCommand))
        {
            return await sshService.ExecuteCommandAsync(config.CustomMigrationCommand, cancellationToken);
        }

        // Default framework migration fallbacks if custom command not set
        string migrationCmd;
        if (config.ProjectType == ProjectType.DockerCompose && !string.IsNullOrEmpty(config.ContainerName))
        {
            migrationCmd = $"docker exec {ShellQuote.Quote(config.ContainerName)} python manage.py migrate --noinput || docker exec {ShellQuote.Quote(config.ContainerName)} npx prisma migrate deploy || true";
        }
        else
        {
            migrationCmd = $"cd {ShellQuote.Quote(config.RemoteProjectPath)} && (python3 manage.py migrate --noinput || npx prisma migrate deploy || dotnet ef database update || true)";
        }

        return await sshService.ExecuteCommandAsync(migrationCmd, cancellationToken);
    }
}

public class DatabaseBackupResult
{
    public bool IsSuccess { get; set; }
    public string BackupPath { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
}
