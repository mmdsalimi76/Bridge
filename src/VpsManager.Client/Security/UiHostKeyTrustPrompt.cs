using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using VpsManager.Client.Views;
using VpsManager.Core.Security;

namespace VpsManager.Client.Security;

/// <summary>
/// Presents host key trust decisions as a modal dialog. Evaluated on the SSH connection
/// thread, so the call is marshalled onto the UI thread and fails closed when no window
/// is available to ask.
/// </summary>
public sealed class UiHostKeyTrustPrompt : IHostKeyTrustPrompt
{
    public bool ShouldTrustUnknownHost(HostKeyInfo hostKey) => Show(hostKey, previousFingerprint: null);

    public bool ShouldTrustChangedHost(HostKeyInfo hostKey, string previousFingerprint) => Show(hostKey, previousFingerprint);

    private static bool Show(HostKeyInfo hostKey, string? previousFingerprint)
    {
        var owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (owner == null || !owner.IsVisible)
        {
            return false;
        }

        try
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                // Cannot synchronously block UI thread while awaiting modal dialog
                return false;
            }

            return Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var window = new HostKeyTrustWindow(hostKey, previousFingerprint);
                return await window.ShowDialog<bool>(owner);
            }).GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // Never trust a key because the prompt failed to appear.
            return false;
        }
    }
}
