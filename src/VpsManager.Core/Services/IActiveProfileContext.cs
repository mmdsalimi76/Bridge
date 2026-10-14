using VpsManager.Core.Models;

namespace VpsManager.Core.Services;

/// <summary>
/// Central shared state context for the currently active VPS profile and profile collection.
/// Exposes reactive notifications when the active profile changes or profiles are updated.
/// </summary>
public interface IActiveProfileContext
{
    /// <summary>
    /// The currently selected VPS profile, or null if no profile exists.
    /// </summary>
    VpsProfile? CurrentProfile { get; }

    /// <summary>
    /// Read-only snapshot of all registered VPS profiles.
    /// </summary>
    IReadOnlyList<VpsProfile> Profiles { get; }

    /// <summary>
    /// Raised whenever the active VPS profile changes.
    /// </summary>
    event EventHandler<VpsProfile?>? ActiveProfileChanged;

    /// <summary>
    /// Raised whenever the profile list is modified (added, removed, or edited).
    /// </summary>
    event EventHandler? ProfilesChanged;

    /// <summary>
    /// Loads profiles from storage and establishes the initial active profile.
    /// </summary>
    Task<VpsProfile?> LoadInitialProfileAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Reloads profiles from storage.
    /// </summary>
    Task ReloadProfilesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the active profile and persists the choice.
    /// </summary>
    Task SetActiveProfileAsync(VpsProfile? profile, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the active profile by ID and persists the choice.
    /// </summary>
    Task SetActiveProfileByIdAsync(string profileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves or updates a profile in config storage, optionally saving credentials (password and/or key passphrase).
    /// </summary>
    Task SaveProfileAsync(VpsProfile profile, string? password = null, string? keyPassphrase = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a profile from config storage and removes any associated credentials.
    /// </summary>
    Task DeleteProfileAsync(string profileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clones an existing profile, assigns a new ID and persists it.
    /// </summary>
    Task<VpsProfile> DuplicateProfileAsync(VpsProfile sourceProfile, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the stored SSH password for a specific profile, if present.
    /// </summary>
    string? GetProfilePassword(string profileId);

    /// <summary>
    /// Retrieves the stored SSH private key passphrase for a specific profile, if present.
    /// </summary>
    string? GetProfileKeyPassphrase(string profileId);

    /// <summary>
    /// Retrieves global file synchronization ignore patterns.
    /// </summary>
    Task<IReadOnlyList<string>> GetGlobalSyncIgnorePatternsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists updated global file synchronization ignore patterns.
    /// </summary>
    Task SaveGlobalSyncIgnorePatternsAsync(IEnumerable<string> patterns, CancellationToken cancellationToken = default);
}
