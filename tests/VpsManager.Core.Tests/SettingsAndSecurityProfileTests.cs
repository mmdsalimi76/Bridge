using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using VpsManager.Core.Models;
using VpsManager.Core.Security;
using VpsManager.Core.Services;
using VpsManager.Core.Services.Session;
using Xunit;

namespace VpsManager.Core.Tests;

public sealed class SettingsAndSecurityProfileTests : IDisposable
{
    private readonly string _tempConfigPath;
    private readonly ConfigService _configService;
    private readonly Mock<ICredentialStore> _credentialStoreMock;

    public SettingsAndSecurityProfileTests()
    {
        _tempConfigPath = Path.Combine(Path.GetTempPath(), $"settings_sec_test_{Guid.NewGuid():N}.json");
        _configService = new ConfigService(_tempConfigPath);
        _credentialStoreMock = new Mock<ICredentialStore>();
    }

    public void Dispose()
    {
        if (File.Exists(_tempConfigPath))
        {
            File.Delete(_tempConfigPath);
        }
    }

    [Fact]
    public void VpsProfile_DefaultAuthMethod_IsPassword()
    {
        var profile = new VpsProfile();
        Assert.Equal(SshAuthMethod.Password, profile.AuthMethod);
        Assert.Empty(profile.PrivateKeyPath);
    }

    [Fact]
    public async Task SaveProfileAsync_WithPasswordAndPassphrase_PersistsBothCredentials()
    {
        var context = new ActiveProfileContext(_configService, _credentialStoreMock.Object);
        var profile = new VpsProfile
        {
            Id = "srv_test",
            Name = "Production Key Server",
            VpsUser = "deploy",
            AuthMethod = SshAuthMethod.PrivateKey,
            PrivateKeyPath = "/home/user/.ssh/id_rsa"
        };

        await context.SaveProfileAsync(profile, "pwd123", "passphrase456");

        _credentialStoreMock.Verify(s => s.SaveCredential(
            SessionCoordinator.CredentialTargetName("srv_test"),
            "deploy",
            "pwd123"), Times.Once);

        _credentialStoreMock.Verify(s => s.SaveCredential(
            SessionCoordinator.PassphraseCredentialTargetName("srv_test"),
            "deploy",
            "passphrase456"), Times.Once);
    }

    [Fact]
    public async Task DeleteProfileAsync_RemovesBothPasswordAndPassphrase()
    {
        var profile1 = new VpsProfile { Id = "srv_1", Name = "Server 1" };
        var profile2 = new VpsProfile { Id = "srv_2", Name = "Server 2" };

        var config = new AppConfig
        {
            VpsProfiles = [profile1, profile2],
            ActiveProfileId = "srv_1"
        };
        await _configService.SaveConfigAsync(config);

        var context = new ActiveProfileContext(_configService, _credentialStoreMock.Object);
        await context.LoadInitialProfileAsync();

        await context.DeleteProfileAsync("srv_2");

        _credentialStoreMock.Verify(s => s.DeleteCredential(SessionCoordinator.CredentialTargetName("srv_2")), Times.Once);
        _credentialStoreMock.Verify(s => s.DeleteCredential(SessionCoordinator.PassphraseCredentialTargetName("srv_2")), Times.Once);
    }

    [Fact]
    public async Task DuplicateProfileAsync_CopiesAuthMethodAndPrivateKeyPath()
    {
        var profile = new VpsProfile
        {
            Id = "srv_orig",
            Name = "Original Server",
            AuthMethod = SshAuthMethod.PrivateKey,
            PrivateKeyPath = "C:\\Keys\\prod.pem",
            VpsUser = "admin"
        };

        var config = new AppConfig
        {
            VpsProfiles = [profile],
            ActiveProfileId = "srv_orig"
        };
        await _configService.SaveConfigAsync(config);

        _credentialStoreMock.Setup(s => s.GetCredential(SessionCoordinator.CredentialTargetName("srv_orig")))
            .Returns(("admin", "pwd999"));
        _credentialStoreMock.Setup(s => s.GetCredential(SessionCoordinator.PassphraseCredentialTargetName("srv_orig")))
            .Returns(("admin", "keypass111"));

        var context = new ActiveProfileContext(_configService, _credentialStoreMock.Object);
        await context.LoadInitialProfileAsync();

        var clone = await context.DuplicateProfileAsync(profile);

        Assert.Equal(SshAuthMethod.PrivateKey, clone.AuthMethod);
        Assert.Equal("C:\\Keys\\prod.pem", clone.PrivateKeyPath);
        Assert.NotEqual("srv_orig", clone.Id);

        _credentialStoreMock.Verify(s => s.SaveCredential(
            SessionCoordinator.PassphraseCredentialTargetName(clone.Id),
            "admin",
            "keypass111"), Times.Once);
    }

    [Fact]
    public async Task GlobalSyncIgnorePatterns_GetAndSave_PersistsAndDeduplicates()
    {
        var context = new ActiveProfileContext(_configService, _credentialStoreMock.Object);

        var initial = await context.GetGlobalSyncIgnorePatternsAsync();
        Assert.Contains(".git", initial);

        var newPatterns = new[] { ".git", "node_modules", "custom_folder/", " .git ", "*.secret" };
        await context.SaveGlobalSyncIgnorePatternsAsync(newPatterns);

        var reloaded = await context.GetGlobalSyncIgnorePatternsAsync();
        Assert.Contains("custom_folder/", reloaded);
        Assert.Contains("*.secret", reloaded);
        Assert.Equal(1, reloaded.Count(p => p == ".git"));
    }

    [Fact]
    public void SessionCoordinator_HasStoredCredential_WithPrivateKey_ChecksFileExistence()
    {
        var sshMock = new Mock<ISshService>();
        var coordinator = new SessionCoordinator(sshMock.Object, _credentialStoreMock.Object);

        var missingKeyProfile = new VpsProfile
        {
            AuthMethod = SshAuthMethod.PrivateKey,
            PrivateKeyPath = "C:\\non_existent_key_file_" + Guid.NewGuid() + ".pem"
        };

        Assert.False(coordinator.HasStoredCredential(missingKeyProfile));

        var tempKeyFile = Path.GetTempFileName();
        try
        {
            var validKeyProfile = new VpsProfile
            {
                AuthMethod = SshAuthMethod.PrivateKey,
                PrivateKeyPath = tempKeyFile
            };

            Assert.True(coordinator.HasStoredCredential(validKeyProfile));
        }
        finally
        {
            if (File.Exists(tempKeyFile)) File.Delete(tempKeyFile);
        }
    }

    [Fact]
    public async Task SessionCoordinator_ConnectAsync_WithMissingPrivateKey_FailsGracefully()
    {
        var sshMock = new Mock<ISshService>();
        var coordinator = new SessionCoordinator(sshMock.Object, _credentialStoreMock.Object);

        var profile = new VpsProfile
        {
            Id = "srv_bad_key",
            Name = "Bad Key Host",
            VpsIp = "10.0.0.1",
            SshPort = 22,
            VpsUser = "root",
            AuthMethod = SshAuthMethod.PrivateKey,
            PrivateKeyPath = "C:\\missing_key_file.pem"
        };

        var result = await coordinator.ConnectAsync(profile);

        Assert.False(result);
        Assert.Equal(SessionState.CredentialsRequired, coordinator.State);
        Assert.Equal("SSH Key Missing", coordinator.StatusText);
        sshMock.Verify(s => s.ConnectAsync(It.IsAny<SshConnectionConfig>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SessionCoordinator_ConnectAsync_WithValidPrivateKey_PassesConfigToSsh()
    {
        var sshMock = new Mock<ISshService>();
        sshMock.SetupGet(s => s.IsConnected).Returns(true);
        sshMock.SetupGet(s => s.ConnectedHostKeyFingerprint).Returns("SHA256:abc123test");

        var tempKeyFile = Path.GetTempFileName();
        try
        {
            _credentialStoreMock.Setup(s => s.GetCredential(SessionCoordinator.PassphraseCredentialTargetName("srv_valid_key")))
                .Returns(("root", "secret_passphrase"));

            var coordinator = new SessionCoordinator(sshMock.Object, _credentialStoreMock.Object);

            var profile = new VpsProfile
            {
                Id = "srv_valid_key",
                Name = "Valid Key Host",
                VpsIp = "10.0.0.5",
                SshPort = 2222,
                VpsUser = "ubuntu",
                AuthMethod = SshAuthMethod.PrivateKey,
                PrivateKeyPath = tempKeyFile
            };

            var connected = await coordinator.ConnectAsync(profile);

            Assert.True(connected);
            Assert.Equal(SessionState.Connected, coordinator.State);
            Assert.Equal("SHA256:abc123test", coordinator.HostKeyFingerprint);

            sshMock.Verify(s => s.ConnectAsync(It.Is<SshConnectionConfig>(c =>
                c.Host == "10.0.0.5" &&
                c.Port == 2222 &&
                c.Username == "ubuntu" &&
                c.PrivateKeyPath == tempKeyFile &&
                c.Passphrase == "secret_passphrase"
            ), It.IsAny<CancellationToken>()), Times.Once);
        }
        finally
        {
            if (File.Exists(tempKeyFile)) File.Delete(tempKeyFile);
        }
    }
}
