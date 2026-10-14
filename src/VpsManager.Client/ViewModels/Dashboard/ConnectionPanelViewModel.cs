using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VpsManager.Core.Models;
using VpsManager.Core.Services.Session;

namespace VpsManager.Client.ViewModels.Dashboard;

/// <summary>
/// Presents the SSH session: status, credential entry, and the connect/disconnect actions.
/// Owns no connection logic itself; <see cref="ISessionCoordinator"/> is the single owner.
/// </summary>
public partial class ConnectionPanelViewModel : ViewModelBase
{
    private readonly ISessionCoordinator _session;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private bool _rememberPassword;

    [ObservableProperty]
    private bool _isCredentialPromptVisible;

    [ObservableProperty]
    private bool _isConnecting;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private string _statusText = "Disconnected";

    [ObservableProperty]
    private string _detail = "Connect to this server to load live telemetry.";

    [ObservableProperty]
    private string _fingerprint = "Not verified";

    [ObservableProperty]
    private string _targetSummary = "No server selected";

    [ObservableProperty]
    private string _pathSummary = string.Empty;

    public ConnectionPanelViewModel(ISessionCoordinator session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));

        RememberPassword = _session.CanRememberCredentials;
        _session.StateChanged += OnSessionStateChanged;
    }

    /// <summary>Supplies the profile the user is currently looking at.</summary>
    public Func<VpsProfile?>? ActiveProfileProvider { get; set; }

    /// <summary>Raised after a successful connect so the dashboard can load telemetry.</summary>
    public event Func<Task>? Connected;

    /// <summary>Raised after a deliberate disconnect.</summary>
    public event Action? Disconnected;

    /// <summary>True when this platform can store credentials securely between sessions.</summary>
    public bool CanRememberPassword => _session.CanRememberCredentials;

    public string ActionLabel => IsConnected ? "Reconnect" : "Connect";

    public string ToggleActionLabel => IsConnecting ? "Connecting..." : (IsConnected ? "Disconnect" : "Connect");

    public bool HasPathSummary => !string.IsNullOrWhiteSpace(PathSummary);

    partial void OnIsConnectedChanged(bool value)
    {
        OnPropertyChanged(nameof(ActionLabel));
        OnPropertyChanged(nameof(ToggleActionLabel));
    }

    partial void OnIsConnectingChanged(bool value)
    {
        OnPropertyChanged(nameof(ToggleActionLabel));
    }

    partial void OnStatusTextChanged(string value)
    {
        OnPropertyChanged(nameof(ActionLabel));
        OnPropertyChanged(nameof(ToggleActionLabel));
    }

    [RelayCommand]
    private async Task ToggleConnectionAsync()
    {
        if (IsConnected)
        {
            await DisconnectAsync();
        }
        else
        {
            await ConnectAsync();
        }
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        var profile = ActiveProfileProvider?.Invoke();
        if (profile == null)
        {
            Detail = "Select a server profile before connecting.";
            return;
        }

        var password = string.IsNullOrWhiteSpace(Password) ? null : Password;

        IsConnecting = true;
        try
        {
            var connected = await _session.ConnectAsync(profile, password, RememberPassword);
            if (connected)
            {
                // The password has served its purpose; do not keep it in memory or on screen.
                Password = string.Empty;
                IsCredentialPromptVisible = false;
                if (Connected != null)
                {
                    await Connected.Invoke();
                }
            }
            else if (_session.State == SessionState.CredentialsRequired)
            {
                IsCredentialPromptVisible = true;
            }
        }
        finally
        {
            IsConnecting = false;
        }
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        Password = string.Empty;
        IsCredentialPromptVisible = false;
        await _session.DisconnectAsync();
        Disconnected?.Invoke();
    }

    /// <summary>Refreshes the target summary shown on the connection card.</summary>
    public void UpdateProfileSummary(VpsProfile? profile)
    {
        if (profile == null)
        {
            TargetSummary = "No server selected";
            PathSummary = string.Empty;
            OnPropertyChanged(nameof(HasPathSummary));
            return;
        }

        TargetSummary = $"{profile.VpsUser}@{profile.VpsIp}:{profile.SshPort}";
        PathSummary = string.IsNullOrWhiteSpace(profile.RemoteProjectPath)
            ? "No remote project path configured"
            : profile.RemoteProjectPath;
        OnPropertyChanged(nameof(HasPathSummary));
    }

    private void OnSessionStateChanged(object? sender, SessionStateChangedEventArgs e)
    {
        // Session events can originate from background SSH threads.
        Dispatcher.UIThread.Post(() =>
        {
            StatusText = e.StatusText;
            IsConnected = _session.IsConnected;
            IsConnecting = e.State == SessionState.Connecting;
            IsCredentialPromptVisible = e.State == SessionState.CredentialsRequired;

            if (!string.IsNullOrWhiteSpace(e.Detail))
            {
                Detail = e.Detail;
            }

            Fingerprint = string.IsNullOrWhiteSpace(e.HostKeyFingerprint)
                ? "Not verified"
                : e.HostKeyFingerprint;
        });
    }
}
