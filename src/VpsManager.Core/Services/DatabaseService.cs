using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VpsManager.Core.Helpers;
using VpsManager.Core.Models;

namespace VpsManager.Core.Services;

public class DatabaseService : IDatabaseService
{
    private readonly ISshService _sshService;
    public const string DefaultBackupDirectory = "/var/backups/vpsmanager";
    public const string HistoryLogFile = "/var/backups/vpsmanager/cron_history.log";
    public const string CronScriptPath = "/var/backups/vpsmanager/cron_backup_runner.sh";
    private const string SystemdCredentialDirectory = "/etc/credstore.encrypted";

    public DatabaseService(ISshService sshService)
    {
        _sshService = sshService ?? throw new ArgumentNullException(nameof(sshService));
    }

    private async Task<CommandResult> ExecuteDatabaseDumpAsync(
        string container,
        string user,
        string password,
        string database,
        string engine,
        string remotePath,
        CancellationToken cancellationToken)
    {
        var credentialContent = CreateCredentialContent(engine, user, password, database);
        var credentialPath = CreateTemporaryCredentialPath();
        var consumerScript = BuildCredentialConsumerScript(
            credentialPath,
            BuildDumpToolCommand(engine, user, database));
        var dockerCommand = $"docker exec -i {ShellQuote.Quote(container)} sh -c {ShellQuote.Quote(consumerScript)}";
        var command = $"bash -o pipefail -c {ShellQuote.Quote($"{dockerCommand} | gzip > {ShellQuote.Quote(remotePath)}")}";

        try
        {
            return await _sshService.ExecuteCommandWithInputAsync(command, credentialContent, cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(credentialContent);
        }
    }

    private static byte[] CreateCredentialContent(string engine, string user, string password, string database)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(database);

        string content = engine switch
        {
            "MySQL" => $"[client]\nuser=\"{EscapeMySqlOption(user)}\"\npassword=\"{EscapeMySqlOption(password)}\"\n",
            "MongoDB" => JsonSerializer.Serialize(new
            {
                username = user,
                password,
                authenticationDatabase = database
            }),
            "SQLite" => string.Empty,
            _ => $"{EscapePgPassField("*")}:{EscapePgPassField("*")}:{EscapePgPassField(database)}:{EscapePgPassField(user)}:{EscapePgPassField(password)}\n"
        };

        return Encoding.UTF8.GetBytes(content);
    }

    private static string EscapeMySqlOption(string value)
    {
        if (value.Contains('\0'))
        {
            throw new ArgumentException("Database credentials cannot contain a null character.", nameof(value));
        }

        return value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
    }

    private static string EscapePgPassField(string value)
    {
        if (value.Contains('\0') || value.Contains('\r') || value.Contains('\n'))
        {
            throw new ArgumentException("PostgreSQL credentials and database names cannot contain line breaks or null characters.", nameof(value));
        }

        return value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace(":", "\\:", StringComparison.Ordinal);
    }

    private static string CreateTemporaryCredentialPath() =>
        $"/dev/shm/vpsmanager-db-{Guid.NewGuid():N}.conf";

    private static string BuildCredentialConsumerScript(string credentialPath, string databaseToolCommand)
    {
        var path = ShellQuote.Quote(credentialPath);
        return $"umask 077; credential_file={path}; trap 'rm -f \"$credential_file\"' EXIT HUP INT TERM; " +
            "cat > \"$credential_file\" || exit 1; chmod 600 \"$credential_file\" || exit 1; " +
            $"{databaseToolCommand}; result=$?; rm -f \"$credential_file\"; trap - EXIT HUP INT TERM; exit \"$result\"";
    }

    private static string BuildCredentialReaderScript(string credentialPath, string databaseToolCommand)
    {
        var path = ShellQuote.Quote(credentialPath);
        return $"credential_file={path}; trap 'rm -f \"$credential_file\"' EXIT HUP INT TERM; " +
            $"{databaseToolCommand}; result=$?; rm -f \"$credential_file\"; trap - EXIT HUP INT TERM; exit \"$result\"";
    }

    private static string BuildDumpToolCommand(string engine, string user, string database)
    {
        var credentialFile = "\"$credential_file\"";
        return engine switch
        {
            "MySQL" => $"mysqldump --defaults-extra-file={credentialFile} -- {ShellQuote.Quote(database)}",
            "MongoDB" => $"mongodump --config={credentialFile} --db {ShellQuote.Quote(database)} --archive --gzip",
            _ => $"PGPASSFILE={credentialFile} pg_dump -U {ShellQuote.Quote(user)} -- {ShellQuote.Quote(database)}"
        };
    }

    private static string BuildRestoreToolCommand(string engine, string user, string database)
    {
        var credentialFile = "\"$credential_file\"";
        return engine switch
        {
            "MySQL" => $"mysql --defaults-extra-file={credentialFile} -- {ShellQuote.Quote(database)}",
            "MongoDB" => $"mongorestore --config={credentialFile} --db {ShellQuote.Quote(database)} --archive --gzip",
            _ => $"PGPASSFILE={credentialFile} psql -U {ShellQuote.Quote(user)} -d {ShellQuote.Quote(database)}"
        };
    }

    public async Task<DatabaseBackupInfo> CreateRemoteBackupAsync(string dbContainerName, string dbUser, string dbPassword, string dbName, string engine, CancellationToken cancellationToken = default)
    {
        await EnsureBackupDirectoryAsync(cancellationToken);

        string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        string enginePrefix = engine.ToLowerInvariant();
        string safeDbName = Path.GetFileName(dbName);
        string fileName = $"backup_db_{enginePrefix}_{safeDbName}_{timestamp}.sql.gz";
        string remotePath = $"{DefaultBackupDirectory}/{fileName}";

        var dumpResult = engine == "SQLite"
            ? await _sshService.ExecuteCommandAsync(
                $"docker exec {ShellQuote.Quote(dbContainerName)} sqlite3 {ShellQuote.Quote(dbName)} .dump | gzip > {ShellQuote.Quote(remotePath)}",
                cancellationToken)
            : await ExecuteDatabaseDumpAsync(dbContainerName, dbUser, dbPassword, dbName, engine, remotePath, cancellationToken);
        if (!dumpResult.IsSuccess)
        {
            throw new InvalidOperationException($"{engine} dump failed: {dumpResult.Error}");
        }

        long sizeBytes = await GetRemoteFileSizeAsync(remotePath, cancellationToken);
        string md5Hash = await GetRemoteMd5Async(remotePath, cancellationToken);

        return new DatabaseBackupInfo
        {
            FileName = fileName,
            RemotePath = remotePath,
            SizeBytes = sizeBytes,
            Md5Hash = md5Hash,
            CreatedAt = DateTime.UtcNow,
            Scope = BackupScope.Database
        };
    }

    public Task<DatabaseBackupInfo> CreateRemoteBackupAsync(string dbContainerName, string dbUser, string dbName, CancellationToken cancellationToken = default)
    {
        return CreateRemoteBackupAsync(dbContainerName, dbUser, "", dbName, "PostgreSQL", cancellationToken);
    }

    public async Task<DatabaseBackupInfo> CreateFolderBackupAsync(string remoteFolderPath, string backupName = "media", CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(remoteFolderPath))
        {
            throw new ArgumentException("Remote folder path cannot be empty.", nameof(remoteFolderPath));
        }

        await EnsureBackupDirectoryAsync(cancellationToken);

        string cleanPath = remoteFolderPath.TrimEnd('/', '\\');
        string parentDir = Path.GetDirectoryName(cleanPath)?.Replace('\\', '/') ?? "/";
        string folderName = Path.GetFileName(cleanPath);
        if (string.IsNullOrEmpty(folderName)) folderName = "data";

        string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        string safeName = string.IsNullOrWhiteSpace(backupName) ? folderName : backupName.Trim();
        string fileName = $"backup_folder_{safeName}_{timestamp}.tar.gz";
        string remotePath = $"{DefaultBackupDirectory}/{fileName}";

        string tarCmd = $"tar -czf {ShellQuote.Quote(remotePath)} -C {ShellQuote.Quote(parentDir)} {ShellQuote.Quote(folderName)}";
        var tarResult = await _sshService.ExecuteCommandAsync(tarCmd, cancellationToken);
        if (!tarResult.IsSuccess)
        {
            throw new InvalidOperationException($"Folder backup failed: {tarResult.Error}");
        }

        long sizeBytes = await GetRemoteFileSizeAsync(remotePath, cancellationToken);
        string md5Hash = await GetRemoteMd5Async(remotePath, cancellationToken);

        return new DatabaseBackupInfo
        {
            FileName = fileName,
            RemotePath = remotePath,
            SizeBytes = sizeBytes,
            Md5Hash = md5Hash,
            CreatedAt = DateTime.UtcNow,
            Scope = BackupScope.Folder
        };
    }

    public async Task<DatabaseBackupInfo> CreateFullBackupAsync(string dbContainerName, string dbUser, string dbPassword, string dbName, string engine, string remoteFolderPath, string projectName = "project", CancellationToken cancellationToken = default)
    {
        await EnsureBackupDirectoryAsync(cancellationToken);

        string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        string safeProject = string.IsNullOrWhiteSpace(projectName) ? "project" : projectName.Trim();
        string fileName = $"backup_full_{safeProject}_{timestamp}.tar.gz";
        string remotePath = $"{DefaultBackupDirectory}/{fileName}";
        string tempDbDump = $"{DefaultBackupDirectory}/_temp_db_{timestamp}.sql.gz";

        // Step 1: Dump DB to temp file
        var dumpResult = engine == "SQLite"
            ? await _sshService.ExecuteCommandAsync(
                $"docker exec {ShellQuote.Quote(dbContainerName)} sqlite3 {ShellQuote.Quote(dbName)} .dump | gzip > {ShellQuote.Quote(tempDbDump)}",
                cancellationToken)
            : await ExecuteDatabaseDumpAsync(dbContainerName, dbUser, dbPassword, dbName, engine, tempDbDump, cancellationToken);
        if (!dumpResult.IsSuccess)
        {
            throw new InvalidOperationException($"Full backup: DB dump failed: {dumpResult.Error}");
        }

        // Step 2: Package both folder and db dump together
        string cleanFolder = remoteFolderPath.TrimEnd('/', '\\');
        string parentDir = Path.GetDirectoryName(cleanFolder)?.Replace('\\', '/') ?? "/";
        string folderName = Path.GetFileName(cleanFolder);

        string packageCmd = $"tar -czf {ShellQuote.Quote(remotePath)} -C {ShellQuote.Quote(parentDir)} {ShellQuote.Quote(folderName)} -C {ShellQuote.Quote(DefaultBackupDirectory)} {ShellQuote.Quote(Path.GetFileName(tempDbDump))} && rm -f {ShellQuote.Quote(tempDbDump)}";
        var packageResult = await _sshService.ExecuteCommandAsync(packageCmd, cancellationToken);
        if (!packageResult.IsSuccess)
        {
            // Clean up temp file on failure
            await _sshService.ExecuteCommandAsync($"rm -f {ShellQuote.Quote(tempDbDump)}", cancellationToken);
            throw new InvalidOperationException($"Full backup packaging failed: {packageResult.Error}");
        }

        long sizeBytes = await GetRemoteFileSizeAsync(remotePath, cancellationToken);
        string md5Hash = await GetRemoteMd5Async(remotePath, cancellationToken);

        return new DatabaseBackupInfo
        {
            FileName = fileName,
            RemotePath = remotePath,
            SizeBytes = sizeBytes,
            Md5Hash = md5Hash,
            CreatedAt = DateTime.UtcNow,
            Scope = BackupScope.Full
        };
    }

    public async Task<bool> VerifyMd5ChecksumAsync(string remoteFilePath, string localFilePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(localFilePath)) return false;

        string remoteHash = await GetRemoteMd5Async(remoteFilePath, cancellationToken);

        using var md5 = MD5.Create();
        using var stream = File.OpenRead(localFilePath);
        byte[] localHashBytes = await md5.ComputeHashAsync(stream, cancellationToken);
        string localHash = Convert.ToHexString(localHashBytes).ToLowerInvariant();

        return string.Equals(remoteHash, localHash, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<CommandResult> RestoreBackupAsync(string dbContainerName, string dbUser, string dbPassword, string dbName, string engine, string backupFilePath, CancellationToken cancellationToken = default)
    {
        if (engine == "SQLite")
        {
            var sqliteCommand = $"gunzip -c {ShellQuote.Quote(backupFilePath)} | docker exec -i {ShellQuote.Quote(dbContainerName)} sqlite3 {ShellQuote.Quote(dbName)}";
            return await _sshService.ExecuteCommandAsync(sqliteCommand, cancellationToken);
        }

        var credentialContent = CreateCredentialContent(engine, dbUser, dbPassword, dbName);
        var credentialPath = CreateTemporaryCredentialPath();
        var container = ShellQuote.Quote(dbContainerName);
        var quotedPath = ShellQuote.Quote(credentialPath);
        var provisionScript = $"umask 077; credential_file={quotedPath}; trap {ShellQuote.Quote($"rm -f {credentialPath}")} HUP INT TERM; " +
            "cat > \"$credential_file\" || exit 1; chmod 600 \"$credential_file\" || { rm -f \"$credential_file\"; exit 1; }";
        var provisionCommand = $"docker exec -i {container} sh -c {ShellQuote.Quote(provisionScript)}";
        CommandResult provisionResult;

        try
        {
            provisionResult = await _sshService.ExecuteCommandWithInputAsync(provisionCommand, credentialContent, cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(credentialContent);
        }

        if (!provisionResult.IsSuccess)
        {
            return provisionResult;
        }

        var restoreScript = BuildCredentialReaderScript(
            credentialPath,
            BuildRestoreToolCommand(engine, dbUser, dbName));
        var restoreDockerCommand = $"docker exec -i {container} sh -c {ShellQuote.Quote(restoreScript)}";
        var restorePipeline = $"gunzip -c {ShellQuote.Quote(backupFilePath)} | {restoreDockerCommand}";
        return await _sshService.ExecuteCommandAsync(
            $"bash -o pipefail -c {ShellQuote.Quote(restorePipeline)}",
            cancellationToken);
    }

    public Task<CommandResult> RestoreBackupAsync(string dbContainerName, string dbUser, string dbName, string backupFilePath, CancellationToken cancellationToken = default)
    {
        return RestoreBackupAsync(dbContainerName, dbUser, "", dbName, "PostgreSQL", backupFilePath, cancellationToken);
    }

    public async Task<IEnumerable<DatabaseBackupInfo>> GetBackupsAsync(string backupDirectory = DefaultBackupDirectory, CancellationToken cancellationToken = default)
    {
        var escapedDirectory = backupDirectory.Replace("'", "'\\''", StringComparison.Ordinal);
        var listCmd = $"if [ -d '{escapedDirectory}' ]; then find '{escapedDirectory}' -maxdepth 1 -type f -name 'backup_*' -printf '%s|%TY-%Tm-%Td %TH:%TM:%TS|%p\\n'; fi";
        var result = await _sshService.ExecuteCommandAsync(listCmd, cancellationToken);

        var backups = new List<DatabaseBackupInfo>();
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"Unable to list remote backups: {result.Error}");
        }

        var lines = result.Output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var parts = line.Split('|', 3, StringSplitOptions.TrimEntries);
            if (parts.Length == 3
                && long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long size)
                && DateTime.TryParse(parts[1], CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTime createdAt))
            {
                string remotePath = parts[2];
                string fileName = Path.GetFileName(remotePath);

                BackupScope scope = BackupScope.Database;
                if (fileName.Contains("backup_folder_", StringComparison.Ordinal))
                {
                    scope = BackupScope.Folder;
                }
                else if (fileName.Contains("backup_full_", StringComparison.Ordinal))
                {
                    scope = BackupScope.Full;
                }

                backups.Add(new DatabaseBackupInfo
                {
                    FileName = fileName,
                    RemotePath = remotePath,
                    SizeBytes = size,
                    CreatedAt = createdAt,
                    Md5Hash = "Check available",
                    Scope = scope
                });
            }
        }
        return backups.OrderByDescending(b => b.CreatedAt);
    }

    public async Task<CommandResult> DeleteBackupAsync(string remoteFilePath, CancellationToken cancellationToken = default)
    {
        var deleteCmd = $"rm -f {ShellQuote.Quote(remoteFilePath)}";
        return await _sshService.ExecuteCommandAsync(deleteCmd, cancellationToken);
    }

    public async Task DownloadBackupAsync(string remoteFilePath, string localDestinationPath, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(remoteFilePath))
            throw new ArgumentException("Remote file path is required.", nameof(remoteFilePath));
        if (string.IsNullOrWhiteSpace(localDestinationPath))
            throw new ArgumentException("Local destination path is required.", nameof(localDestinationPath));

        string? dir = Path.GetDirectoryName(localDestinationPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var fileStream = File.Create(localDestinationPath);
        await _sshService.DownloadFileAsync(remoteFilePath, fileStream, progress, cancellationToken);
    }

    public async Task<CommandResult> SetupCronJobAsync(string cronExpression, string scriptContent, CancellationToken cancellationToken = default)
    {
        ValidateCronExpression(cronExpression);
        await EnsureBackupDirectoryAsync(cancellationToken);
        string scriptPath = ShellQuote.Quote(CronScriptPath);
        string completeScript = scriptContent.StartsWith("#!", StringComparison.Ordinal)
            ? scriptContent
            : $"#!/bin/bash\n{scriptContent}";
        var scriptBytes = Encoding.UTF8.GetBytes(completeScript);
        var writeScriptCmd = $"umask 077; cat > {scriptPath} && chmod 700 {scriptPath}";
        var scriptResult = await _sshService.ExecuteCommandWithInputAsync(writeScriptCmd, scriptBytes, cancellationToken);
        if (!scriptResult.IsSuccess) return scriptResult;

        string cronCmd = $"(crontab -l 2>/dev/null | grep -Fv -- {scriptPath}; printf '%s %s >/dev/null 2>&1\\n' {ShellQuote.Quote(cronExpression)} {scriptPath}) | crontab -";
        return await _sshService.ExecuteCommandAsync(cronCmd, cancellationToken);
    }

    public async Task<CommandResult> SetupScheduledBackupAsync(string cronExpression, BackupScope scope, string dbContainerName, string dbUser, string dbPassword, string dbName, string engine, string remoteFolderPath, CancellationToken cancellationToken = default)
    {
        ValidateCronExpression(cronExpression);
        await EnsureBackupDirectoryAsync(cancellationToken);

        var credentialName = scope == BackupScope.Folder || engine == "SQLite"
            ? null
            : await StoreScheduledCredentialAsync(engine, dbContainerName, dbUser, dbPassword, dbName, cancellationToken);

        string targetName = scope switch
        {
            BackupScope.Folder => Path.GetFileName(remoteFolderPath.TrimEnd('/', '\\')),
            BackupScope.Full => $"{dbName}+{Path.GetFileName(remoteFolderPath.TrimEnd('/', '\\'))}",
            _ => dbName
        };
        targetName = SanitizeFileComponent(targetName);
        string quotedFolderPath = ShellQuote.Quote(remoteFolderPath);
        string folderPathSetup = $"REMOTE_FOLDER={quotedFolderPath}\nPARENT_DIR=$(dirname -- \"$REMOTE_FOLDER\")\nFOLDER_NAME=$(basename -- \"$REMOTE_FOLDER\")";
        string folderBackupPrefix = ShellQuote.Quote($"{DefaultBackupDirectory}/backup_folder_");
        string fullBackupPrefix = ShellQuote.Quote($"{DefaultBackupDirectory}/backup_full_");
        string databaseBackupPrefix = ShellQuote.Quote($"{DefaultBackupDirectory}/backup_db_{SanitizeFileComponent(engine.ToLowerInvariant())}_");
        string fullDatabaseDumpScript = engine == "SQLite"
            ? $"docker exec {ShellQuote.Quote(dbContainerName)} sqlite3 {ShellQuote.Quote(dbName)} .dump | gzip > \"$TEMP_DB\""
            : credentialName == null
                ? string.Empty
                : GetEncryptedEngineDumpScript(engine, dbContainerName, dbUser, dbName, credentialName, "\"$TEMP_DB\"");
        string databaseDumpScript = engine == "SQLite"
            ? $"docker exec {ShellQuote.Quote(dbContainerName)} sqlite3 {ShellQuote.Quote(dbName)} .dump | gzip > \"$OUT_FILE\""
            : credentialName == null
                ? string.Empty
                : GetEncryptedEngineDumpScript(engine, dbContainerName, dbUser, dbName, credentialName, "\"$OUT_FILE\"");

        string backupSnippet = scope switch
        {
            BackupScope.Folder => $@"
TARGET_NAME={ShellQuote.Quote(targetName)}
SCOPE=""Folder""
OUT_FILE={folderBackupPrefix}""$TARGET_NAME""_""$DATE_SHORT.tar.gz""
{folderPathSetup}
tar -czf ""$OUT_FILE"" -C ""$PARENT_DIR"" ""$FOLDER_NAME"" >/dev/null 2>&1
EXIT_CODE=$?
",
            BackupScope.Full => $@"
TARGET_NAME={ShellQuote.Quote(targetName)}
SCOPE=""Full""
OUT_FILE={fullBackupPrefix}""$TARGET_NAME""_""$DATE_SHORT.tar.gz""
TEMP_DB={ShellQuote.Quote($"{DefaultBackupDirectory}/_temp_db_")}""$DATE_SHORT.sql.gz""
{folderPathSetup}
{fullDatabaseDumpScript}
if [ $? -eq 0 ]; then
    tar -czf ""$OUT_FILE"" -C ""$PARENT_DIR"" ""$FOLDER_NAME"" -C ""{DefaultBackupDirectory}"" ""$(basename -- ""$TEMP_DB"")"" >/dev/null 2>&1
    EXIT_CODE=$?
    rm -f ""$TEMP_DB""
else
    EXIT_CODE=1
fi
",
            _ => $@"
TARGET_NAME={ShellQuote.Quote(targetName)}
SCOPE=""Database""
OUT_FILE={databaseBackupPrefix}""$TARGET_NAME""_""$DATE_SHORT.sql.gz""
{databaseDumpScript}
EXIT_CODE=$?
"
        };

        string fullScript = $@"
set -o pipefail
mkdir -p ""{DefaultBackupDirectory}""
LOG_FILE=""{HistoryLogFile}""
TIMESTAMP=$(date '+%Y-%m-%d %H:%M:%S')
DATE_SHORT=$(date '+%Y%m%d_%H%M%S')

{backupSnippet}

if [ $EXIT_CODE -eq 0 ]; then
    FILE_SIZE=$(stat -c%s ""$OUT_FILE"" 2>/dev/null || echo 0)
    echo ""$TIMESTAMP|SUCCESS|$SCOPE|$TARGET_NAME|$OUT_FILE|$FILE_SIZE|OK"" >> ""$LOG_FILE""
else
    echo ""$TIMESTAMP|FAILED|$SCOPE|$TARGET_NAME|-|0|Process exited with error code $EXIT_CODE"" >> ""$LOG_FILE""
fi
";

        var scheduleResult = await SetupCronJobAsync(cronExpression, fullScript, cancellationToken);
        if (!scheduleResult.IsSuccess)
        {
            return scheduleResult;
        }

        return scheduleResult;
    }

    public async Task<IEnumerable<CronExecutionRecord>> GetCronHistoryAsync(CancellationToken cancellationToken = default)
    {
        var readCmd = $"cat {ShellQuote.Quote(HistoryLogFile)} 2>/dev/null | tail -n 50";
        var result = await _sshService.ExecuteCommandAsync(readCmd, cancellationToken);

        var history = new List<CronExecutionRecord>();
        if (!result.IsSuccess || string.IsNullOrWhiteSpace(result.Output))
            return history;

        var lines = result.Output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var parts = line.Split('|');
            if (parts.Length >= 7)
            {
                DateTime.TryParse(parts[0], out DateTime ts);
                bool isSuccess = parts[1].Trim().Equals("SUCCESS", StringComparison.OrdinalIgnoreCase);
                string scope = parts[2].Trim();
                string target = parts[3].Trim();
                string file = parts[4].Trim();
                long.TryParse(parts[5].Trim(), out long size);
                string msg = parts[6].Trim();

                history.Add(new CronExecutionRecord
                {
                    Timestamp = ts,
                    IsSuccess = isSuccess,
                    Scope = scope,
                    TargetName = target,
                    OutputFile = file,
                    SizeBytes = size,
                    Message = msg
                });
            }
        }

        return history.OrderByDescending(h => h.Timestamp);
    }

    private async Task<string> StoreScheduledCredentialAsync(
        string engine,
        string container,
        string user,
        string password,
        string database,
        CancellationToken cancellationToken)
    {
        var credentialName = GetScheduledCredentialName(engine, container, user, database);
        var credentialPath = $"{SystemdCredentialDirectory}/{credentialName}.cred";
        var command =
            $"command -v systemd-creds >/dev/null 2>&1 || {{ printf '%s\\n' 'systemd-creds is required for scheduled database backups.' >&2; exit 127; }}; " +
            $"umask 077 && install -d -m 0700 {ShellQuote.Quote(SystemdCredentialDirectory)} && " +
            $"systemd-creds encrypt --name={ShellQuote.Quote(credentialName)} - {ShellQuote.Quote(credentialPath)} && " +
            $"systemd-creds decrypt --name={ShellQuote.Quote(credentialName)} {ShellQuote.Quote(credentialPath)} - >/dev/null";
        var credentialContent = CreateCredentialContent(engine, user, password, database);

        try
        {
            var result = await _sshService.ExecuteCommandWithInputAsync(command, credentialContent, cancellationToken);
            if (!result.IsSuccess)
            {
                throw new InvalidOperationException(
                    $"Unable to provision the VPS encrypted credential for scheduled backups: {result.Error}");
            }

            return credentialName;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(credentialContent);
        }
    }

    private static string GetEncryptedEngineDumpScript(
        string engine,
        string container,
        string user,
        string database,
        string? credentialName,
        string outputPathExpression)
    {
        if (string.IsNullOrWhiteSpace(credentialName))
        {
            throw new InvalidOperationException("An encrypted credential must be provisioned before scheduling database backups.");
        }

        var credentialPath = $"{SystemdCredentialDirectory}/{credentialName}.cred";
        var credentialFile = CreateTemporaryCredentialPath();
        var consumerScript = BuildCredentialConsumerScript(
            credentialFile,
            BuildDumpToolCommand(engine, user, database));
        var decryptCommand =
            $"systemd-creds decrypt --name={ShellQuote.Quote(credentialName)} {ShellQuote.Quote(credentialPath)} -";
        var dockerCommand = $"docker exec -i {ShellQuote.Quote(container)} sh -c {ShellQuote.Quote(consumerScript)}";

        return $"{decryptCommand} | {dockerCommand} | gzip > {outputPathExpression}";
    }

    private static string GetScheduledCredentialName(string engine, string container, string user, string database)
    {
        var identity = Encoding.UTF8.GetBytes($"{engine}\0{container}\0{user}\0{database}");
        var hash = Convert.ToHexString(SHA256.HashData(identity)).ToLowerInvariant();
        CryptographicOperations.ZeroMemory(identity);
        return $"vpsmanager-db-{hash[..24]}";
    }

    private static string SanitizeFileComponent(string value)
    {
        var safe = new string(value.Where(character =>
            char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-').Take(80).ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "backup" : safe;
    }

    private static void ValidateCronExpression(string cronExpression)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cronExpression);

        var fields = cronExpression.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 5 || fields.Any(field => field.Any(character =>
                !(char.IsAsciiDigit(character) || character is '*' or '/' or ',' or '-'))))
        {
            throw new ArgumentException("Cron schedules must contain five numeric fields using only digits, '*', '/', ',' and '-'.", nameof(cronExpression));
        }
    }

    private async Task EnsureBackupDirectoryAsync(CancellationToken cancellationToken)
    {
        var result = await _sshService.ExecuteCommandAsync(
            $"mkdir -p {ShellQuote.Quote(DefaultBackupDirectory)}",
            cancellationToken);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"Unable to create the remote backup directory: {result.Error}");
        }
    }

    private async Task<long> GetRemoteFileSizeAsync(string remotePath, CancellationToken cancellationToken)
    {
        string sizeCmd = $"stat -c%s {ShellQuote.Quote(remotePath)} 2>/dev/null || stat -f%z {ShellQuote.Quote(remotePath)} 2>/dev/null || echo 0";
        var res = await _sshService.ExecuteCommandAsync(sizeCmd, cancellationToken);
        if (res?.Output != null && long.TryParse(res.Output.Trim(), out long size))
        {
            return size;
        }
        return 0;
    }

    private async Task<string> GetRemoteMd5Async(string remotePath, CancellationToken cancellationToken)
    {
        string md5Cmd = $"md5sum {ShellQuote.Quote(remotePath)} 2>/dev/null | awk '{{print $1}}'";
        var md5Result = await _sshService.ExecuteCommandAsync(md5Cmd, cancellationToken);
        return md5Result?.Output?.Trim() ?? string.Empty;
    }
}
