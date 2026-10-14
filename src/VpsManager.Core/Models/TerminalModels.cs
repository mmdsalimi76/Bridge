using System;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace VpsManager.Core.Models;

/// <summary>
/// Represents a saved terminal command that can be quickly injected.
/// </summary>
public partial class SavedCommand : ObservableObject
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _command = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _category = "Custom";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastUsed { get; set; } = DateTime.UtcNow;
    public int UsageCount { get; set; } = 0;

    /// <summary>
    /// UI-only state to toggle description and command preview visibility in the sidebar.
    /// </summary>
    [JsonIgnore]
    [ObservableProperty]
    private bool _showDescription = false;
}

/// <summary>
/// Configuration for terminal sessions.
/// </summary>
public class TerminalConfig
{
    public string WorkingDirectory { get; set; } = string.Empty; // Project root or VPS root
    public bool UseProjectRoot { get; set; } = true; // Use project root if available
    public string ShellType { get; set; } = "/bin/bash"; // Default shell
    public int TerminalHistorySize { get; set; } = 100;
}