using System.Threading;
using Moq;
using VpsManager.Core.Models;
using VpsManager.Core.Security;
using VpsManager.Core.Services;
using VpsManager.Core.Services.Session;

namespace VpsManager.Core.Tests;

public sealed class SessionCoordinatorTests
{
    [Fact]
    public async Task ConnectAsync_SupersedesPreviousAttemptAndSerializesTransportAccess()
    {
        var firstAttemptStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var connected = false;
        var activeConnections = 0;
        var maxConcurrentConnections = 0;

        var ssh = new Mock<ISshService>();
        ssh.SetupGet(service => service.IsConnected).Returns(() => connected);
        ssh.Setup(service => service.ConnectAsync(It.IsAny<SshConnectionConfig>(), It.IsAny<CancellationToken>()))
            .Returns(async (SshConnectionConfig config, CancellationToken cancellationToken) =>
            {
                var currentConnections = Interlocked.Increment(ref activeConnections);
                UpdateMaximum(ref maxConcurrentConnections, currentConnections);
                try
                {
                    if (config.Host == "first.example")
                    {
                        firstAttemptStarted.SetResult();
                        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    }

                    connected = true;
                }
                finally
                {
                    Interlocked.Decrement(ref activeConnections);
                }
            });

        var credentials = new Mock<ICredentialStore>();
        credentials.SetupGet(store => store.SupportsPersistence).Returns(false);
        credentials.Setup(store => store.GetCredential(It.IsAny<string>()))
            .Returns(((string Username, string Secret)?)null);

        var coordinator = new SessionCoordinator(ssh.Object, credentials.Object);

        var firstAttempt = coordinator.ConnectAsync(
            new VpsProfile { Id = "first", Name = "First", VpsIp = "first.example" },
            "password");

        await firstAttemptStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var secondProfile = new VpsProfile { Id = "second", Name = "Second", VpsIp = "second.example" };
        var secondAttempt = coordinator.ConnectAsync(secondProfile, "password");

        Assert.False(await firstAttempt);
        Assert.True(await secondAttempt);
        Assert.Same(secondProfile, coordinator.ActiveProfile);
        Assert.True(coordinator.IsConnected);
        Assert.Equal(1, maxConcurrentConnections);
    }

    private static void UpdateMaximum(ref int maximum, int candidate)
    {
        int current;
        do
        {
            current = Volatile.Read(ref maximum);
            if (candidate <= current)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref maximum, candidate, current) != current);
    }
}
