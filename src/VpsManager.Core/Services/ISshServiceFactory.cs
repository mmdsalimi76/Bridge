using VpsManager.Core.Security;

namespace VpsManager.Core.Services;

public interface ISshServiceFactory
{
    ISshService Create();
}

public sealed class SshServiceFactory : ISshServiceFactory
{
    private readonly IHostKeyVerifier _hostKeyVerifier;

    public SshServiceFactory(IHostKeyVerifier hostKeyVerifier)
    {
        _hostKeyVerifier = hostKeyVerifier ?? throw new ArgumentNullException(nameof(hostKeyVerifier));
    }

    public ISshService Create() => new SshService(_hostKeyVerifier);
}
