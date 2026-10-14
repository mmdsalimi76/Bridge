using Moq;
using VpsManager.Core.Models;
using VpsManager.Core.Security;
using VpsManager.Core.Services;
using VpsManager.Core.Services.Session;
using Xunit;

namespace VpsManager.Core.Tests;

public sealed class ActiveProfileContextTests : IDisposable
{
    private readonly string _tempConfigPath;
    private readonly ConfigService _configService;
    private readonly Mock<ICredentialStore> _credentialStoreMock;

    public ActiveProfileContextTests()
    {
        _tempConfigPath = Path.Combine(Path.GetTempPath(), $"vps_profile_test_{Guid.NewGuid():N}.json");
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
    public async Task LoadInitialProfileAsync_LoadsConfigAndFiresEvents()
    {
        var profile1 = new VpsProfile { Id = "p1", Name = "Server 1", VpsIp = "10.0.0.1" };
        var profile2 = new VpsProfile { Id = "p2", Name = "Server 2", VpsIp = "10.0.0.2" };

        var config = new AppConfig
        {
            VpsProfiles = [profile1, profile2],
            ActiveProfileId = "p2"
        };
        await _configService.SaveConfigAsync(config);

        var context = new ActiveProfileContext(_configService, _credentialStoreMock.Object);

        VpsProfile? receivedProfile = null;
        bool profilesChangedFired = false;

        context.ActiveProfileChanged += (_, p) => receivedProfile = p;
        context.ProfilesChanged += (_, _) => profilesChangedFired = true;

        var initial = await context.LoadInitialProfileAsync();

        Assert.NotNull(initial);
        Assert.Equal("p2", initial.Id);
        Assert.Equal("p2", context.CurrentProfile?.Id);
        Assert.Equal(2, context.Profiles.Count);
        Assert.Equal("p2", receivedProfile?.Id);
        Assert.True(profilesChangedFired);
    }

    [Fact]
    public async Task SetActiveProfileAsync_SwitchesCurrentProfileAndSyncsLegacyFields()
    {
        var profile1 = new VpsProfile { Id = "p1", Name = "Prod", VpsIp = "1.2.3.4", SshPort = 2222 };
        var profile2 = new VpsProfile { Id = "p2", Name = "Stage", VpsIp = "5.6.7.8", SshPort = 22 };

        var config = new AppConfig
        {
            VpsProfiles = [profile1, profile2],
            ActiveProfileId = "p1"
        };
        await _configService.SaveConfigAsync(config);

        var context = new ActiveProfileContext(_configService, _credentialStoreMock.Object);
        await context.LoadInitialProfileAsync();

        VpsProfile? notified = null;
        context.ActiveProfileChanged += (_, p) => notified = p;

        await context.SetActiveProfileAsync(profile2);

        Assert.Equal("p2", context.CurrentProfile?.Id);
        Assert.Equal("p2", notified?.Id);

        // Verify config on disk was updated with new active ID and synced legacy fields
        var loaded = await _configService.LoadConfigAsync();
        Assert.Equal("p2", loaded.ActiveProfileId);
        Assert.Equal("5.6.7.8", loaded.VpsIp);
        Assert.Equal(22, loaded.SshPort);
    }

    [Fact]
    public async Task SaveProfileAsync_PersistsProfileAndStoresCredentials()
    {
        var context = new ActiveProfileContext(_configService, _credentialStoreMock.Object);
        await context.LoadInitialProfileAsync();

        var newProfile = new VpsProfile
        {
            Id = "new-srv",
            Name = "New VPS",
            VpsIp = "192.168.1.50",
            VpsUser = "ubuntu"
        };

        await context.SaveProfileAsync(newProfile, "SuperSecretPassword123!");

        Assert.Equal("new-srv", context.CurrentProfile?.Id);
        Assert.Single(context.Profiles);

        _credentialStoreMock.Verify(s => s.SaveCredential(
            SessionCoordinator.CredentialTargetName("new-srv"),
            "ubuntu",
            "SuperSecretPassword123!"), Times.Once);

        var loaded = await _configService.LoadConfigAsync();
        Assert.Equal("new-srv", loaded.ActiveProfileId);
        Assert.Equal("192.168.1.50", loaded.VpsIp);
        Assert.Equal("ubuntu", loaded.VpsUser);
    }

    [Fact]
    public async Task DeleteProfileAsync_RemovesProfileAndDeletesCredentials()
    {
        var profile1 = new VpsProfile { Id = "p1", Name = "Server 1" };
        var profile2 = new VpsProfile { Id = "p2", Name = "Server 2" };

        var config = new AppConfig
        {
            VpsProfiles = [profile1, profile2],
            ActiveProfileId = "p1"
        };
        await _configService.SaveConfigAsync(config);

        var context = new ActiveProfileContext(_configService, _credentialStoreMock.Object);
        await context.LoadInitialProfileAsync();

        await context.DeleteProfileAsync("p1");

        Assert.Single(context.Profiles);
        Assert.Equal("p2", context.CurrentProfile?.Id);

        _credentialStoreMock.Verify(s => s.DeleteCredential(
            SessionCoordinator.CredentialTargetName("p1")), Times.Once);

        var loaded = await _configService.LoadConfigAsync();
        Assert.Equal("p2", loaded.ActiveProfileId);
        Assert.Single(loaded.VpsProfiles);
    }

    [Fact]
    public async Task DuplicateProfileAsync_ClonesSettingsAndCopiesCredentials()
    {
        var original = new VpsProfile
        {
            Id = "orig",
            Name = "Original Server",
            VpsIp = "10.10.10.10",
            VpsUser = "admin",
            RemoteProjectPath = "/var/www/app",
            DbName = "app_prod"
        };

        var config = new AppConfig
        {
            VpsProfiles = [original],
            ActiveProfileId = "orig"
        };
        await _configService.SaveConfigAsync(config);

        _credentialStoreMock.Setup(s => s.GetCredential(SessionCoordinator.CredentialTargetName("orig")))
            .Returns(("admin", "OrigPassword"));

        var context = new ActiveProfileContext(_configService, _credentialStoreMock.Object);
        await context.LoadInitialProfileAsync();

        var duplicate = await context.DuplicateProfileAsync(original);

        Assert.NotEqual(original.Id, duplicate.Id);
        Assert.Equal("Original Server (Copy)", duplicate.Name);
        Assert.Equal("10.10.10.10", duplicate.VpsIp);
        Assert.Equal("/var/www/app", duplicate.RemoteProjectPath);
        Assert.Equal("app_prod", duplicate.DbName);

        // Verify password was copied for duplicate
        _credentialStoreMock.Verify(s => s.SaveCredential(
            SessionCoordinator.CredentialTargetName(duplicate.Id),
            "admin",
            "OrigPassword"), Times.Once);
    }
}
