using VpsManager.Core.Models;
using VpsManager.Core.Security;
using VpsManager.Core.Services.Session;

namespace VpsManager.Core.Services;

/// <summary>
/// Thread-safe implementation of IActiveProfileContext that coordinates profile persistence,
/// credential vault storage, and cross-application notifications.
/// </summary>
public sealed class ActiveProfileContext : IActiveProfileContext
{
    private readonly ConfigService _configService;
    private readonly ICredentialStore _credentialStore;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private List<VpsProfile> _profiles = [];
    private VpsProfile? _currentProfile;

    public ActiveProfileContext(ConfigService configService, ICredentialStore credentialStore)
    {
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _credentialStore = credentialStore ?? throw new ArgumentNullException(nameof(credentialStore));
    }

    public VpsProfile? CurrentProfile => _currentProfile;

    public IReadOnlyList<VpsProfile> Profiles
    {
        get
        {
            lock (_profiles)
            {
                return _profiles.ToList();
            }
        }
    }

    public event EventHandler<VpsProfile?>? ActiveProfileChanged;
    public event EventHandler? ProfilesChanged;

    public async Task<VpsProfile?> LoadInitialProfileAsync(CancellationToken cancellationToken = default)
    {
        VpsProfile? activeToNotify = null;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var config = await _configService.LoadConfigAsync(cancellationToken);
            lock (_profiles)
            {
                _profiles = config.VpsProfiles.ToList();
            }

            var active = config.GetActiveProfile();
            _currentProfile = active;
            activeToNotify = _currentProfile;
            return _currentProfile;
        }
        finally
        {
            _gate.Release();
            ActiveProfileChanged?.Invoke(this, activeToNotify);
            ProfilesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task ReloadProfilesAsync(CancellationToken cancellationToken = default)
    {
        bool profileChanged = false;
        VpsProfile? activeToNotify = null;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var config = await _configService.LoadConfigAsync(cancellationToken);
            lock (_profiles)
            {
                _profiles = config.VpsProfiles.ToList();
            }

            var previousId = _currentProfile?.Id;
            var active = config.GetActiveProfile();
            _currentProfile = active;
            activeToNotify = _currentProfile;
            profileChanged = _currentProfile?.Id != previousId;
        }
        finally
        {
            _gate.Release();
            ProfilesChanged?.Invoke(this, EventArgs.Empty);
            if (profileChanged)
            {
                ActiveProfileChanged?.Invoke(this, activeToNotify);
            }
        }
    }

    public async Task SetActiveProfileAsync(VpsProfile? profile, CancellationToken cancellationToken = default)
    {
        VpsProfile? activeToNotify = null;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (profile == null)
            {
                _currentProfile = null;
                activeToNotify = null;
                return;
            }

            var config = await _configService.LoadConfigAsync(cancellationToken);
            var matched = config.VpsProfiles.FirstOrDefault(p => p.Id == profile.Id) ?? profile;

            config.ActiveProfileId = matched.Id;
            SyncLegacyConfig(config, matched);
            await _configService.SaveConfigAsync(config, cancellationToken);

            lock (_profiles)
            {
                _profiles = config.VpsProfiles.ToList();
            }

            _currentProfile = matched;
            activeToNotify = _currentProfile;
        }
        finally
        {
            _gate.Release();
            ActiveProfileChanged?.Invoke(this, activeToNotify);
        }
    }

    public async Task SetActiveProfileByIdAsync(string profileId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        VpsProfile? target;
        lock (_profiles)
        {
            target = _profiles.FirstOrDefault(p => p.Id == profileId);
        }

        if (target != null)
        {
            await SetActiveProfileAsync(target, cancellationToken);
        }
    }

    public async Task SaveProfileAsync(VpsProfile profile, string? password = null, string? keyPassphrase = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var config = await _configService.LoadConfigAsync(cancellationToken);

            var existingIndex = config.VpsProfiles.FindIndex(p => p.Id == profile.Id);
            if (existingIndex >= 0)
            {
                config.VpsProfiles[existingIndex] = profile;
            }
            else
            {
                config.VpsProfiles.Add(profile);
            }

            config.ActiveProfileId = profile.Id;
            SyncLegacyConfig(config, profile);
            await _configService.SaveConfigAsync(config, cancellationToken);

            if (!string.IsNullOrEmpty(password))
            {
                _credentialStore.SaveCredential(
                    SessionCoordinator.CredentialTargetName(profile.Id),
                    profile.VpsUser,
                    password);
            }

            if (!string.IsNullOrEmpty(keyPassphrase))
            {
                _credentialStore.SaveCredential(
                    SessionCoordinator.PassphraseCredentialTargetName(profile.Id),
                    profile.VpsUser,
                    keyPassphrase);
            }

            lock (_profiles)
            {
                _profiles = config.VpsProfiles.ToList();
            }

            _currentProfile = profile;
        }
        finally
        {
            _gate.Release();
            ProfilesChanged?.Invoke(this, EventArgs.Empty);
            ActiveProfileChanged?.Invoke(this, _currentProfile);
        }
    }

    public async Task DeleteProfileAsync(string profileId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var config = await _configService.LoadConfigAsync(cancellationToken);
            var toRemove = config.VpsProfiles.FirstOrDefault(p => p.Id == profileId);
            if (toRemove == null || config.VpsProfiles.Count <= 1)
            {
                return;
            }

            config.VpsProfiles.Remove(toRemove);
            _credentialStore.DeleteCredential(SessionCoordinator.CredentialTargetName(profileId));
            _credentialStore.DeleteCredential(SessionCoordinator.PassphraseCredentialTargetName(profileId));

            if (config.ActiveProfileId == profileId)
            {
                var nextActive = config.VpsProfiles[0];
                config.ActiveProfileId = nextActive.Id;
                SyncLegacyConfig(config, nextActive);
                _currentProfile = nextActive;
            }

            await _configService.SaveConfigAsync(config, cancellationToken);

            lock (_profiles)
            {
                _profiles = config.VpsProfiles.ToList();
            }
        }
        finally
        {
            _gate.Release();
            ProfilesChanged?.Invoke(this, EventArgs.Empty);
            ActiveProfileChanged?.Invoke(this, _currentProfile);
        }
    }

    public async Task<VpsProfile> DuplicateProfileAsync(VpsProfile sourceProfile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceProfile);

        var clone = new VpsProfile
        {
            Name = $"{sourceProfile.Name} (Copy)",
            VpsIp = sourceProfile.VpsIp,
            SshPort = sourceProfile.SshPort,
            VpsUser = sourceProfile.VpsUser,
            AuthMethod = sourceProfile.AuthMethod,
            PrivateKeyPath = sourceProfile.PrivateKeyPath,
            LocalRootPath = sourceProfile.LocalRootPath,
            RemoteProjectPath = sourceProfile.RemoteProjectPath,
            ComposeFileName = sourceProfile.ComposeFileName,
            DbContainerName = sourceProfile.DbContainerName,
            DbName = sourceProfile.DbName,
            DbUser = sourceProfile.DbUser,
            TargetOS = sourceProfile.TargetOS,
            ProjectType = sourceProfile.ProjectType,
            DbEngine = sourceProfile.DbEngine,
            IsDbContainerized = sourceProfile.IsDbContainerized,
            DbHost = sourceProfile.DbHost,
            DbPort = sourceProfile.DbPort,
            ServiceName = sourceProfile.ServiceName,
            StartCommand = sourceProfile.StartCommand,
            StopCommand = sourceProfile.StopCommand,
            RestartCommand = sourceProfile.RestartCommand,
            BuildStrategy = sourceProfile.BuildStrategy,
            LocalBuildCommand = sourceProfile.LocalBuildCommand,
            BuildArtifactPath = sourceProfile.BuildArtifactPath,
            HealthCheckType = sourceProfile.HealthCheckType,
            HealthCheckUrl = sourceProfile.HealthCheckUrl,
            HealthCheckPort = sourceProfile.HealthCheckPort,
            HealthCheckProcessName = sourceProfile.HealthCheckProcessName,
            HealthCheckCustomCommand = sourceProfile.HealthCheckCustomCommand
        };

        var existingPassword = GetProfilePassword(sourceProfile.Id);
        var existingPassphrase = GetProfileKeyPassphrase(sourceProfile.Id);
        await SaveProfileAsync(clone, existingPassword, existingPassphrase, cancellationToken);
        return clone;
    }

    public string? GetProfilePassword(string profileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        var creds = _credentialStore.GetCredential(SessionCoordinator.CredentialTargetName(profileId));
        return creds?.Secret;
    }

    public string? GetProfileKeyPassphrase(string profileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        var creds = _credentialStore.GetCredential(SessionCoordinator.PassphraseCredentialTargetName(profileId));
        return creds?.Secret;
    }

    public async Task<IReadOnlyList<string>> GetGlobalSyncIgnorePatternsAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var config = await _configService.LoadConfigAsync(cancellationToken);
            return config.SyncIgnorePatterns.ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveGlobalSyncIgnorePatternsAsync(IEnumerable<string> patterns, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(patterns);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var config = await _configService.LoadConfigAsync(cancellationToken);
            config.SyncIgnorePatterns = patterns.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).Distinct().ToList();
            await _configService.SaveConfigAsync(config, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static void SyncLegacyConfig(AppConfig config, VpsProfile profile)
    {
        config.VpsIp = profile.VpsIp;
        config.SshPort = profile.SshPort;
        config.VpsUser = profile.VpsUser;
        config.LocalRootPath = profile.LocalRootPath;
        config.RemoteProjectPath = profile.RemoteProjectPath;
        config.ComposeFileName = profile.ComposeFileName;
        config.DbContainerName = profile.DbContainerName;
        config.DbName = profile.DbName;
        config.DbUser = profile.DbUser;
    }
}
