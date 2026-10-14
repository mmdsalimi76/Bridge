using System;
using System.Diagnostics;
using Avalonia.Styling;

namespace VpsManager.Client.Styles;

public enum AppTheme
{
    System,
    Light,
    Dark
}

public static class ThemeManager
{
    public static ThemeVariant DetectSystemTheme()
    {
        // Bridge is an integrated dark-canvas workstation.
        // Return ThemeVariant.Dark so system light mode doesn't flip controls to black.
        return ThemeVariant.Dark;
    }

    public static ThemeVariant ResolveTheme(AppTheme requestedTheme = AppTheme.Dark)
    {
        return requestedTheme switch
        {
            AppTheme.Light => ThemeVariant.Light,
            AppTheme.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Dark
        };
    }

    public static void InitializeFromSystem()
    {
        if (Avalonia.Application.Current != null)
        {
            Avalonia.Application.Current.RequestedThemeVariant = ThemeVariant.Dark;
        }
    }

    public static void ApplyTheme(ThemeVariant variant)
    {
        if (Avalonia.Application.Current != null)
        {
            Avalonia.Application.Current.RequestedThemeVariant = variant;
        }
    }

    private static bool TryReadCommandOutput(string fileName, string[] args, out string output)
    {
        output = string.Empty;

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (var arg in args)
            {
                startInfo.ArgumentList.Add(arg);
            }

            using var process = new Process { StartInfo = startInfo };
            process.Start();
            output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            return process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output);
        }
        catch
        {
            return false;
        }
    }
}
