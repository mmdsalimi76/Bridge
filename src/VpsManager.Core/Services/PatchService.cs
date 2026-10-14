using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using VpsManager.Core.Helpers;
using VpsManager.Core.Models;

namespace VpsManager.Core.Services;

public class PatchService : IPatchService
{
    private readonly ISshService _sshService;

    public PatchService(ISshService sshService)
    {
        _sshService = sshService ?? throw new ArgumentNullException(nameof(sshService));
    }

    // -------------------------------------------------------------------------
    // Git Working Tree Status
    // -------------------------------------------------------------------------
    public async Task<GitWorkingTreeStatus> GetGitStatusAsync(string rootPath, CancellationToken cancellationToken = default)
    {
        var status = new GitWorkingTreeStatus();
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
        {
            status.Summary = "Workspace path not found.";
            return status;
        }

        bool hasGit = Directory.Exists(Path.Combine(rootPath, ".git")) || File.Exists(Path.Combine(rootPath, ".git"));
        if (!hasGit)
        {
            status.IsGitRepository = false;
            status.BranchName = "No Git Repo";
            status.Summary = "Project directory is not tracked by Git. Use 'Scan All Files'.";
            return status;
        }

        status.IsGitRepository = true;

        try
        {
            // Branch name
            string branch = await RunGitCommandAsync(rootPath, "branch --show-current", cancellationToken);
            status.BranchName = string.IsNullOrWhiteSpace(branch) ? "HEAD (detached)" : branch.Trim();

            // Status porcelain
            var gitChanges = await RunGitStatusAsync(rootPath, cancellationToken);
            status.ModifiedCount = gitChanges.Values.Count(s => s == FileChangeStatus.Modified);
            status.NewCount = gitChanges.Values.Count(s => s == FileChangeStatus.New);
            status.DeletedCount = gitChanges.Values.Count(s => s == FileChangeStatus.Deleted);
            status.HasUncommittedChanges = gitChanges.Count > 0;

            if (status.HasUncommittedChanges)
            {
                status.Summary = $"{status.ModifiedCount} modified, {status.NewCount} new, {status.DeletedCount} deleted";
            }
            else
            {
                status.Summary = "Working tree clean. All changes committed.";
            }
        }
        catch (Exception ex)
        {
            status.Summary = $"Git error: {ex.Message}";
        }

        return status;
    }

    // -------------------------------------------------------------------------
    // Scan: All local files (no git needed)
    // -------------------------------------------------------------------------
    public async Task<List<FilePatchNode>> ScanLocalChangesAsync(
        string rootPath,
        List<string> ignorePatterns,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(rootPath)) return [];

        var nodes = new List<FilePatchNode>();
        var rootDirInfo = new DirectoryInfo(rootPath);

        await Task.Run(() =>
        {
            var directories = new Dictionary<string, FilePatchNode>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in rootDirInfo.GetFiles("*", SearchOption.AllDirectories))
            {
                string relPath = Path.GetRelativePath(rootPath, file.FullName).Replace('\\', '/');
                if (ShouldIgnore(relPath, ignorePatterns)) continue;

                var fileNode = new FilePatchNode
                {
                    Name = file.Name,
                    RelativePath = relPath,
                    FullPath = file.FullName,
                    FileSizeBytes = file.Length,
                    LastModified = file.LastWriteTimeUtc,
                    IsDirectory = false,
                    IsSelected = false,
                    ChangeStatus = FileChangeStatus.Unchanged
                };

                EnsureParentDirectories(nodes, directories, rootPath, relPath, fileNode);
            }

            SortNodes(nodes);
        }, cancellationToken);

        return nodes;
    }

    // -------------------------------------------------------------------------
    // Scan: Git-tracked changes only (uses `git status --porcelain`)
    // -------------------------------------------------------------------------
    public async Task<List<FilePatchNode>> ScanGitChangesAsync(
        string rootPath,
        List<string> ignorePatterns,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(rootPath)) return [];

        if (!Directory.Exists(Path.Combine(rootPath, ".git")) &&
            !File.Exists(Path.Combine(rootPath, ".git")))
        {
            throw new InvalidOperationException(
                "The project directory is not a Git repository. Use 'Scan All Files' instead.");
        }

        var gitChanges = await RunGitStatusAsync(rootPath, cancellationToken);
        if (gitChanges.Count == 0) return [];

        var nodes = new List<FilePatchNode>();
        var directories = new Dictionary<string, FilePatchNode>(StringComparer.OrdinalIgnoreCase);

        foreach (var (relPath, status) in gitChanges)
        {
            if (ShouldIgnore(relPath, ignorePatterns)) continue;

            string fullPath = Path.Combine(rootPath, relPath.Replace('/', Path.DirectorySeparatorChar));
            var fileInfo = File.Exists(fullPath) ? new FileInfo(fullPath) : null;

            var fileNode = new FilePatchNode
            {
                Name = Path.GetFileName(relPath),
                RelativePath = relPath,
                FullPath = fullPath,
                FileSizeBytes = fileInfo?.Length ?? 0,
                LastModified = fileInfo?.LastWriteTimeUtc ?? DateTime.UtcNow,
                IsDirectory = false,
                IsSelected = true,   // Pre-select git-changed files by default
                ChangeStatus = status
            };

            EnsureParentDirectories(nodes, directories, rootPath, relPath, fileNode);
        }

        SortNodes(nodes);
        return nodes;
    }

    // -------------------------------------------------------------------------
    // Deploy: Basic (backward-compatible)
    // -------------------------------------------------------------------------
    public async Task<CommandResult> DeploySelectivePatchAsync(
        List<FilePatchNode> selectedFiles,
        string rootPath,
        string remoteTargetPath,
        CancellationToken cancellationToken = default)
    {
        var options = new PatchDeploymentOptions
        {
            CreateBackupSnapshot = false,
            AutoReloadService = false
        };

        var res = await DeployPatchAsync(selectedFiles, rootPath, remoteTargetPath, options, null, cancellationToken);
        return new CommandResult
        {
            ExitCode = res.IsSuccess ? 0 : 1,
            Output = res.Output,
            Error = res.Error
        };
    }

    // -------------------------------------------------------------------------
    // Deploy: Full options (backward-compatible)
    // -------------------------------------------------------------------------
    public async Task<CommandResult> DeploySelectivePatchWithOptionsAsync(
        List<FilePatchNode> selectedFiles,
        string rootPath,
        string remoteTargetPath,
        bool createBackupSnapshot,
        bool autoReloadService,
        ProjectType projectType,
        string serviceName,
        CancellationToken cancellationToken = default)
    {
        var options = new PatchDeploymentOptions
        {
            CreateBackupSnapshot = createBackupSnapshot,
            AutoReloadService = autoReloadService,
            ProjectType = projectType,
            ServiceName = serviceName,
            IsDockerized = projectType == ProjectType.DockerCompose || projectType == ProjectType.SingleDockerfile
        };

        var res = await DeployPatchAsync(selectedFiles, rootPath, remoteTargetPath, options, null, cancellationToken);
        return new CommandResult
        {
            ExitCode = res.IsSuccess ? 0 : 1,
            Output = res.Output,
            Error = res.Error
        };
    }

    // -------------------------------------------------------------------------
    // Deploy: Full Engine with universal tar.gz, deletions, progress, and rollback support
    // -------------------------------------------------------------------------
    public async Task<PatchDeploymentResult> DeployPatchAsync(
        List<FilePatchNode> selectedFiles,
        string rootPath,
        string remoteTargetPath,
        PatchDeploymentOptions options,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var result = new PatchDeploymentResult();

        if (selectedFiles == null || selectedFiles.Count == 0)
        {
            result.IsSuccess = true;
            result.Output = "No files selected for patching.";
            return result;
        }

        string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");

        try
        {
            // --- Step 1: Remote backup snapshot (optional) ---
            if (options.CreateBackupSnapshot)
            {
                progress?.Report($"🛡️ Creating remote backup snapshot for {remoteTargetPath}...");
                string backupDir = $"{remoteTargetPath.TrimEnd('/')}_backup_{timestamp}";
                string backupCmd = $"cp -a {ShellQuote.Quote(remoteTargetPath)} {ShellQuote.Quote(backupDir)}";
                var backupResult = await _sshService.ExecuteCommandAsync(backupCmd, cancellationToken);
                if (!backupResult.IsSuccess)
                {
                    result.IsSuccess = false;
                    result.Error = $"Backup snapshot failed: {backupResult.Error}";
                    progress?.Report($"❌ Backup snapshot failed: {backupResult.Error}");
                    return result;
                }

                result.BackupPath = backupDir;
                progress?.Report($"✅ Snapshot safely created at: {backupDir}");
            }

            // Separate deployable files and deleted files
            var filesToDeploy = selectedFiles.Where(f => !f.IsDirectory && f.ChangeStatus != FileChangeStatus.Deleted).ToList();
            var filesToDelete = selectedFiles.Where(f => !f.IsDirectory && f.ChangeStatus == FileChangeStatus.Deleted).ToList();

            result.FilesDeployedCount = filesToDeploy.Count;
            result.FilesDeletedCount = filesToDelete.Count;

            // --- Step 2: Package and upload tar.gz payload ---
            if (filesToDeploy.Count > 0)
            {
                progress?.Report($"📦 Packaging {filesToDeploy.Count} file(s) into compressed tar.gz archive...");

                using var memoryStream = new MemoryStream();
                using (var gzipStream = new GZipStream(memoryStream, CompressionLevel.Optimal, leaveOpen: true))
                using (var tarWriter = new TarWriter(gzipStream, TarEntryFormat.Pax, leaveOpen: true))
                {
                    foreach (var node in filesToDeploy)
                    {
                        if (!File.Exists(node.FullPath)) continue;

                        string unixRelPath = node.RelativePath.Replace('\\', '/');
                        using var fileStream = File.OpenRead(node.FullPath);
                        var entry = new PaxTarEntry(TarEntryType.RegularFile, unixRelPath)
                        {
                            DataStream = fileStream,
                            Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead
                        };

                        // Grant execute permissions for shell scripts or files without extension
                        if (unixRelPath.EndsWith(".sh", StringComparison.OrdinalIgnoreCase) ||
                            unixRelPath.EndsWith(".bash", StringComparison.OrdinalIgnoreCase) ||
                            !Path.HasExtension(unixRelPath))
                        {
                            entry.Mode |= UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
                        }

                        tarWriter.WriteEntry(entry);
                    }
                }

                memoryStream.Position = 0;
                result.PayloadSizeBytes = memoryStream.Length;
                string patchArchiveName = $"patch_{timestamp}.tar.gz";
                string remoteTmpArchive = $"/tmp/{patchArchiveName}";

                progress?.Report($"📤 Uploading patch archive ({FilePatchNode.FormatSize(result.PayloadSizeBytes)}) to VPS via SFTP...");
                await _sshService.UploadFileAsync(memoryStream, remoteTmpArchive, null, cancellationToken);

                // --- Step 3: Extract on remote target ---
                progress?.Report($"📂 Extracting patch into {remoteTargetPath}...");
                string extractCmd = $"mkdir -p {ShellQuote.Quote(remoteTargetPath)} && tar -xzf {ShellQuote.Quote(remoteTmpArchive)} -C {ShellQuote.Quote(remoteTargetPath)} && rm -f {ShellQuote.Quote(remoteTmpArchive)}";
                var deployResult = await _sshService.ExecuteCommandAsync(extractCmd, cancellationToken);
                if (!deployResult.IsSuccess)
                {
                    result.IsSuccess = false;
                    result.Error = $"Remote archive extraction failed: {deployResult.Error}";
                    progress?.Report($"❌ Extraction failed: {deployResult.Error}");
                    return result;
                }
                progress?.Report("✅ Files extracted and synchronized successfully.");
            }

            // --- Step 4: Handle deleted files on remote ---
            if (filesToDelete.Count > 0)
            {
                progress?.Report($"🗑️ Removing {filesToDelete.Count} obsolete file(s) on remote server...");
                foreach (var del in filesToDelete)
                {
                    string normalizedRel = del.RelativePath.Replace('\\', '/').TrimStart('/');
                    string targetFile = $"{remoteTargetPath.TrimEnd('/')}/{normalizedRel}";
                    await _sshService.ExecuteCommandAsync($"rm -f {ShellQuote.Quote(targetFile)}", cancellationToken);
                    progress?.Report($"   - Removed {del.RelativePath}");
                }
                progress?.Report("✅ Obsolete files removed.");
            }

            // --- Step 5: Auto-reload service (optional) ---
            if (options.AutoReloadService)
            {
                string reloadCmd = BuildReloadCommand(options, remoteTargetPath);
                if (!string.IsNullOrWhiteSpace(reloadCmd))
                {
                    progress?.Report($"🔄 Reloading service on VPS: {reloadCmd}...");
                    var reloadResult = await _sshService.ExecuteCommandAsync(reloadCmd, cancellationToken);
                    if (!reloadResult.IsSuccess)
                    {
                        result.IsSuccess = false;
                        result.Error = $"Patch deployed, but service reload failed: {reloadResult.Error}";
                        progress?.Report($"⚠️ Service reload failed: {reloadResult.Error}");
                        return result;
                    }
                    progress?.Report("✅ Service restarted successfully.");
                }
            }

            sw.Stop();
            result.Duration = sw.Elapsed;
            result.IsSuccess = true;
            result.Output = $"Successfully deployed patch ({result.FilesDeployedCount} updated, {result.FilesDeletedCount} deleted) in {result.Duration.TotalSeconds:F1}s.";
            progress?.Report($"✨ {result.Output}");
        }
        catch (Exception ex)
        {
            sw.Stop();
            result.Duration = sw.Elapsed;
            result.IsSuccess = false;
            result.Error = ex.Message;
            progress?.Report($"❌ Hot-patch exception: {ex.Message}");
        }

        return result;
    }

    // -------------------------------------------------------------------------
    // Rollback: Restore remote target folder from snapshot
    // -------------------------------------------------------------------------
    public async Task<CommandResult> RollbackPatchAsync(
        string remoteTargetPath,
        string backupPath,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(backupPath))
            return new CommandResult { ExitCode = 1, Error = "No backup snapshot path provided for rollback." };

        progress?.Report($"⏪ Verifying backup snapshot at {backupPath}...");
        var checkBackup = await _sshService.ExecuteCommandAsync($"test -d {ShellQuote.Quote(backupPath)}", cancellationToken);
        if (!checkBackup.IsSuccess)
        {
            progress?.Report($"❌ Snapshot directory not found on VPS: {backupPath}");
            return new CommandResult { ExitCode = 1, Error = $"Snapshot directory not found: {backupPath}" };
        }

        progress?.Report($"⏪ Restoring {remoteTargetPath} from snapshot...");
        string restoreCmd = $"cp -a {ShellQuote.Quote(backupPath)}/. {ShellQuote.Quote(remoteTargetPath)}/";
        var restoreResult = await _sshService.ExecuteCommandAsync(restoreCmd, cancellationToken);

        if (restoreResult.IsSuccess)
        {
            progress?.Report($"✨ Rollback restored successfully from {backupPath}!");
        }
        else
        {
            progress?.Report($"❌ Rollback restore failed: {restoreResult.Error}");
        }

        return restoreResult;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------
    public static string BuildReloadCommand(PatchDeploymentOptions options, string remoteTargetPath)
    {
        if (options.IsDockerized)
        {
            string composeFile = string.IsNullOrWhiteSpace(options.ComposeFileName) ? "docker-compose.yml" : options.ComposeFileName;
            if (!string.IsNullOrWhiteSpace(options.ServiceName))
            {
                return $"cd {ShellQuote.Quote(remoteTargetPath)} && docker compose -f {ShellQuote.Quote(composeFile)} restart {ShellQuote.Quote(options.ServiceName)}";
            }
            return $"cd {ShellQuote.Quote(remoteTargetPath)} && docker compose -f {ShellQuote.Quote(composeFile)} restart";
        }

        if (!string.IsNullOrWhiteSpace(options.RestartCommand))
        {
            return $"cd {ShellQuote.Quote(remoteTargetPath)} && {options.RestartCommand}";
        }

        if (!string.IsNullOrWhiteSpace(options.LaunchCommand))
        {
            return $"cd {ShellQuote.Quote(remoteTargetPath)} && {options.LaunchCommand}";
        }

        if (!string.IsNullOrWhiteSpace(options.ServiceName))
        {
            return $"systemctl restart {ShellQuote.Quote(options.ServiceName)}";
        }

        return string.Empty;
    }

    private static async Task<string> RunGitCommandAsync(string rootPath, string arguments, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo("git")
        {
            Arguments = arguments,
            WorkingDirectory = rootPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start git process.");

        string output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return output.Trim();
    }

    private static async Task<Dictionary<string, FileChangeStatus>> RunGitStatusAsync(
        string rootPath,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, FileChangeStatus>(StringComparer.OrdinalIgnoreCase);

        var psi = new ProcessStartInfo("git")
        {
            Arguments = "status --porcelain -u",
            WorkingDirectory = rootPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start git process.");

        string output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            string err = await process.StandardError.ReadToEndAsync(cancellationToken);
            throw new InvalidOperationException($"git status failed: {err}");
        }

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length < 4) continue;

            char indexStatus = line[0];
            char workTreeStatus = line[1];
            string filePath = line[3..].Trim().Trim('"');

            // Handle renames: "old -> new"
            if (filePath.Contains(" -> "))
                filePath = filePath.Split(" -> ")[^1].Trim();

            var status = (indexStatus, workTreeStatus) switch
            {
                ('A', _) or ('?', '?') => FileChangeStatus.New,
                ('D', _) or (_, 'D') => FileChangeStatus.Deleted,
                _ => FileChangeStatus.Modified
            };

            result[filePath] = status;
        }

        return result;
    }

    private static void EnsureParentDirectories(
        IList<FilePatchNode> rootNodes,
        Dictionary<string, FilePatchNode> directories,
        string rootPath,
        string relPath,
        FilePatchNode fileNode)
    {
        var pathParts = relPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        IList<FilePatchNode> parent = rootNodes;
        string parentPath = string.Empty;

        for (int i = 0; i < pathParts.Length - 1; i++)
        {
            parentPath = string.IsNullOrEmpty(parentPath)
                ? pathParts[i]
                : $"{parentPath}/{pathParts[i]}";

            if (!directories.TryGetValue(parentPath, out var dir))
            {
                dir = new FilePatchNode
                {
                    Name = pathParts[i],
                    RelativePath = parentPath,
                    FullPath = Path.Combine(rootPath, parentPath.Replace('/', Path.DirectorySeparatorChar)),
                    IsDirectory = true
                };
                directories[parentPath] = dir;
                parent.Add(dir);
            }

            parent = dir.Children;
        }

        parent.Add(fileNode);
    }

    private static void SortNodes(IList<FilePatchNode> nodes)
    {
        var sorted = nodes
            .OrderByDescending(n => n.IsDirectory)
            .ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        nodes.Clear();
        foreach (var node in sorted)
        {
            nodes.Add(node);
            if (node.IsDirectory) SortNodes(node.Children);
        }
    }

    private static bool ShouldIgnore(string relativePath, List<string> ignorePatterns)
    {
        if (ignorePatterns == null) return false;
        foreach (var pattern in ignorePatterns)
        {
            string clean = pattern.TrimStart('*', '.');
            if (relativePath.Contains(clean, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
