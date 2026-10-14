using VpsManager.Core.Models;

namespace VpsManager.Core.Services;

public interface IPatchService
{
    Task<List<FilePatchNode>> ScanLocalChangesAsync(string rootPath, List<string> ignorePatterns, CancellationToken cancellationToken = default);
    Task<List<FilePatchNode>> ScanGitChangesAsync(string rootPath, List<string> ignorePatterns, CancellationToken cancellationToken = default);
    Task<GitWorkingTreeStatus> GetGitStatusAsync(string rootPath, CancellationToken cancellationToken = default);

    Task<CommandResult> DeploySelectivePatchAsync(List<FilePatchNode> selectedFiles, string rootPath, string remoteTargetPath, CancellationToken cancellationToken = default);
    Task<CommandResult> DeploySelectivePatchWithOptionsAsync(
        List<FilePatchNode> selectedFiles,
        string rootPath,
        string remoteTargetPath,
        bool createBackupSnapshot,
        bool autoReloadService,
        ProjectType projectType,
        string serviceName,
        CancellationToken cancellationToken = default);

    Task<PatchDeploymentResult> DeployPatchAsync(
        List<FilePatchNode> selectedFiles,
        string rootPath,
        string remoteTargetPath,
        PatchDeploymentOptions options,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);

    Task<CommandResult> RollbackPatchAsync(
        string remoteTargetPath,
        string backupPath,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}
