using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using VpsManager.Core.Security;

namespace VpsManager.Client.Views;

public partial class HostKeyTrustWindow : Window
{
    public HostKeyTrustWindow()
    {
        InitializeComponent();
    }

    public HostKeyTrustWindow(HostKeyInfo hostKey, string? previousFingerprint)
        : this()
    {
        ServerText.Text = hostKey.Endpoint;
        AlgorithmText.Text = string.IsNullOrWhiteSpace(hostKey.KeyAlgorithm)
            ? "Unknown"
            : hostKey.KeyAlgorithm;
        FingerprintText.Text = hostKey.Fingerprint;

        if (!string.IsNullOrWhiteSpace(previousFingerprint))
        {
            ChangedKeyWarning.IsVisible = true;
            ChangedKeyDetail.Text =
                $"The previously trusted key was {previousFingerprint}. Accepting a changed key can expose this " +
                "connection and your credentials to interception. Only continue if you rebuilt or reinstalled this server.";
        }
    }

    private void OnTrustClick(object? sender, RoutedEventArgs e) => Close(true);

    private void OnRejectClick(object? sender, RoutedEventArgs e) => Close(false);
}
