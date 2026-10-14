using VpsManager.Core.Helpers;
using VpsManager.Core.Models;

namespace VpsManager.Core.Services;

public interface ISideloadService
{
    Task<CommandResult> SideloadImageAsync(string localTarPath, string remoteStagingPath, IProgress<double>? progress = null, CancellationToken cancellationToken = default);
}

public class SideloadService : ISideloadService
{
    private readonly ISshService _sshService;

    public SideloadService(ISshService sshService)
    {
        _sshService = sshService ?? throw new ArgumentNullException(nameof(sshService));
    }

    public async Task<CommandResult> SideloadImageAsync(string localTarPath, string remoteStagingPath, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(localTarPath))
        {
            throw new FileNotFoundException($"Local Docker image archive not found: {localTarPath}");
        }

        string fileName = Path.GetFileName(localTarPath);
        string remoteFilePath = $"{remoteStagingPath}/{fileName}";

        using var fileStream = File.OpenRead(localTarPath);
        await _sshService.UploadFileAsync(fileStream, remoteFilePath, progress, cancellationToken);

        string loadCmd = $"docker load -i {ShellQuote.Quote(remoteFilePath)} && rm -f {ShellQuote.Quote(remoteFilePath)}";
        return await _sshService.ExecuteCommandAsync(loadCmd, cancellationToken);
    }
}
