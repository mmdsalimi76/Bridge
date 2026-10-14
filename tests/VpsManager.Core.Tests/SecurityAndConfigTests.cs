using VpsManager.Core.Models;
using VpsManager.Core.Security;
using VpsManager.Core.Services;
using Xunit;

namespace VpsManager.Core.Tests;

public class SecurityAndConfigTests
{
    [Fact]
    public async Task ConfigService_SaveAndLoad_ShouldPreserveData()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"vps_test_{Guid.NewGuid()}.json");
        try
        {
            var service = new ConfigService(tempPath);
            var config = new AppConfig
            {
                VpsIp = "192.168.1.100",
                VpsUser = "admin",
                SshPort = 2222
            };

            await service.SaveConfigAsync(config);
            var loaded = await service.LoadConfigAsync();

            Assert.Equal("192.168.1.100", loaded.VpsIp);
            Assert.Equal("admin", loaded.VpsUser);
            Assert.Equal(2222, loaded.SshPort);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void WindowsCredentialStore_SaveAndRetrieve_ShouldWork()
    {
        if (!OperatingSystem.IsWindows()) return;

        var store = new WindowsCredentialStore();
        string target = $"TestTarget_{Guid.NewGuid()}";
        string user = "testuser";
        string pass = "SecretPass123!";

        try
        {
            store.SaveCredential(target, user, pass);
            var creds = store.GetCredential(target);

            Assert.NotNull(creds);
            Assert.Equal(user, creds.Value.Username);
            Assert.Equal(pass, creds.Value.Secret);
        }
        finally
        {
            store.DeleteCredential(target);
        }
    }
}
