using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VpsManager.Core.Models;
using VpsManager.Core.Services;
using VpsManager.Core.Services.Session;

namespace VpsManager.Client.ViewModels;

public partial class TerminalViewModel : ViewModelBase
{
    private readonly IActiveProfileContext _profileContext;
    private readonly ISessionCoordinator _session;
    private readonly ITerminalService _terminalService;
    private readonly SavedCommandService _savedCommandService;
    private readonly ConfigService _configService;
    private VpsProfile? _currentProfile;

    // Command history for Up/Down arrow navigation
    private readonly List<string> _commandHistory = new();
    private int _historyIndex = -1;
    private CancellationTokenSource? _commandCts;

    public event Func<string, Task>? ClipboardRequested;

    [ObservableProperty]
    private string _terminalOutput = "Terminal not connected. Start a session to begin.\n";

    [ObservableProperty]
    private string _currentCommand = string.Empty;

    [ObservableProperty]
    private string _workingDirectory = string.Empty;

    [ObservableProperty]
    private bool _isSessionActive = false;

    [ObservableProperty]
    private bool _isProcessingCommand = false;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private bool _autoScroll = true;

    [ObservableProperty]
    private bool _wordWrap = true;

    [ObservableProperty]
    private double _terminalFontSize = 12.0;

    [ObservableProperty]
    private bool _isSidebarVisible = true;

    [ObservableProperty]
    private string _selectedCategory = "All";

    [ObservableProperty]
    private bool _isNewCommandExpanded = false;

    // Saved Commands
    [ObservableProperty]
    private ObservableCollection<SavedCommand> _savedCommands = new();

    [ObservableProperty]
    private ObservableCollection<SavedCommand> _filteredSavedCommands = new();

    [ObservableProperty]
    private SavedCommand? _selectedCommand;

    [ObservableProperty]
    private string _newCommandName = string.Empty;

    [ObservableProperty]
    private string _newCommandText = string.Empty;

    [ObservableProperty]
    private string _newCommandDescription = string.Empty;

    [ObservableProperty]
    private string _newCommandCategory = "Custom";

    [ObservableProperty]
    private string _searchTerm = string.Empty;

    // Snippet Editing State
    [ObservableProperty]
    private bool _isEditingSnippet = false;

    [ObservableProperty]
    private SavedCommand? _editingSnippet;

    [ObservableProperty]
    private string _editCommandName = string.Empty;

    [ObservableProperty]
    private string _editCommandText = string.Empty;

    [ObservableProperty]
    private string _editCommandDescription = string.Empty;

    [ObservableProperty]
    private string _editCommandCategory = "Custom";

    public ObservableCollection<string> AvailableCategories { get; } = new()
    {
        "All", "Docker", "System", "Logs", "Network", "Services", "Custom"
    };

    public ObservableCollection<string> FixedCategories { get; } = new()
    {
        "Docker", "System", "Logs", "Network", "Services", "Custom"
    };

    public string ProfileName => _currentProfile?.Name ?? "No profile selected";
    public string ServerAddress => _currentProfile != null ? $"{_currentProfile.VpsIp}:{_currentProfile.SshPort}" : "Not configured";
    public bool IsConnected => _session.IsConnected;
    public string ConnectionStatusText => !IsConnected ? "SSH Disconnected" : $"Connected to {ServerAddress}";
    public string SessionStateBadgeText => IsSessionActive ? "Session Active" : "Standby";

    public TerminalViewModel(
        IActiveProfileContext profileContext,
        ISessionCoordinator session,
        ITerminalService terminalService,
        SavedCommandService savedCommandService,
        ConfigService configService)
    {
        _profileContext = profileContext ?? throw new ArgumentNullException(nameof(profileContext));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _terminalService = terminalService ?? throw new ArgumentNullException(nameof(terminalService));
        _savedCommandService = savedCommandService ?? throw new ArgumentNullException(nameof(savedCommandService));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));

        // Subscribe to terminal output events
        _terminalService.OutputReceived += OnTerminalOutputReceived;

        _profileContext.ActiveProfileChanged += (_, profile) =>
        {
            if (profile != null)
            {
                _ = SetCurrentProfileAsync(profile);
            }
        };

        _session.StateChanged += (_, e) =>
        {
            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(ConnectionStatusText));

            if (e.State != SessionState.Connected && IsSessionActive)
            {
                IsSessionActive = false;
                OnPropertyChanged(nameof(SessionStateBadgeText));
                StatusMessage = $"SSH {e.StatusText}.";
                TerminalOutput += $"\n[Session terminated: {e.StatusText}]\n";
            }
            else if (IsConnected)
            {
                StatusMessage = $"SSH Connected ({_currentProfile?.Name ?? _session.ActiveProfile?.Name})";
            }
            else
            {
                StatusMessage = "SSH Disconnected";
            }
        };

        _ = LoadSavedCommandsAsync();
        if (_profileContext.CurrentProfile is { } initial)
        {
            _ = SetCurrentProfileAsync(initial);
        }
    }

    private void OnTerminalOutputReceived(object? sender, string output)
    {
        TerminalOutput += output;
    }

    public async Task SetCurrentProfileAsync(VpsProfile profile)
    {
        _currentProfile = profile;
        OnPropertyChanged(nameof(ProfileName));
        OnPropertyChanged(nameof(ServerAddress));
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(ConnectionStatusText));
        
        if (profile != null)
        {
            WorkingDirectory = string.IsNullOrEmpty(profile.RemoteProjectPath) ? "/root" : profile.RemoteProjectPath;
            StatusMessage = IsConnected ? $"Connected to {profile.Name} ({profile.VpsIp})" : $"Target: {profile.Name}";
        }
        await Task.CompletedTask;
    }

    // Command history navigation
    public void NavigateHistoryUp()
    {
        if (_commandHistory.Count == 0) return;
        if (_historyIndex < _commandHistory.Count - 1)
        {
            _historyIndex++;
            CurrentCommand = _commandHistory[_commandHistory.Count - 1 - _historyIndex];
        }
    }

    public void NavigateHistoryDown()
    {
        if (_historyIndex <= 0)
        {
            _historyIndex = -1;
            CurrentCommand = string.Empty;
            return;
        }
        _historyIndex--;
        CurrentCommand = _commandHistory[_commandHistory.Count - 1 - _historyIndex];
    }

    [RelayCommand]
    private async Task StartSessionAsync()
    {
        if (_currentProfile == null)
        {
            StatusMessage = "No VPS profile selected. Please select a profile in Settings.";
            return;
        }

        if (!_session.IsConnected)
        {
            StatusMessage = "SSH is disconnected. Connect in Overview first.";
            TerminalOutput += "\n[Error: SSH connection is not active. Connect to this VPS in Overview first.]\n";
            return;
        }

        try
        {
            TerminalOutput = "Starting terminal session...\n";
            IsProcessingCommand = true;
            StatusMessage = "Starting terminal session...";

            var workingDir = string.IsNullOrEmpty(_currentProfile.RemoteProjectPath) 
                ? "/root" 
                : _currentProfile.RemoteProjectPath;

            await _terminalService.StartTerminalSessionAsync(workingDir);
            
            IsSessionActive = _terminalService.IsSessionActive;
            WorkingDirectory = _terminalService.CurrentWorkingDirectory;
            OnPropertyChanged(nameof(SessionStateBadgeText));
            StatusMessage = $"Terminal session active in {WorkingDirectory}";
        }
        catch (Exception ex)
        {
            TerminalOutput += $"\nError starting session: {ex.Message}\n";
            StatusMessage = $"Failed to start session: {ex.Message}";
        }
        finally
        {
            IsProcessingCommand = false;
        }
    }

    [RelayCommand]
    private async Task ExecuteCommandAsync()
    {
        if (string.IsNullOrWhiteSpace(CurrentCommand))
            return;

        if (!IsSessionActive)
        {
            StatusMessage = "No active terminal session. Start a session first.";
            return;
        }

        if (IsProcessingCommand)
            return;

        var cmdToRun = CurrentCommand.Trim();
        CurrentCommand = string.Empty;

        // Add to history (deduplicate identical consecutive commands)
        if (_commandHistory.Count == 0 || _commandHistory[^1] != cmdToRun)
        {
            _commandHistory.Add(cmdToRun);
        }
        _historyIndex = -1;

        _commandCts = new CancellationTokenSource();
        try
        {
            IsProcessingCommand = true;
            StatusMessage = $"Executing: {cmdToRun}";

            await _terminalService.ExecuteCommandAsync(cmdToRun, _commandCts.Token);
            WorkingDirectory = _terminalService.CurrentWorkingDirectory;
            StatusMessage = "Ready";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Command aborted by user";
        }
        catch (Exception ex)
        {
            TerminalOutput += $"\nError executing command: {ex.Message}\n";
            StatusMessage = $"Command failed: {ex.Message}";
        }
        finally
        {
            _commandCts?.Dispose();
            _commandCts = null;
            IsProcessingCommand = false;
        }
    }

    [RelayCommand]
    private void CancelCommand()
    {
        if (_commandCts != null && !_commandCts.IsCancellationRequested)
        {
            _commandCts.Cancel();
            StatusMessage = "Canceling active command...";
        }
    }

    [RelayCommand]
    private async Task RunSavedCommandDirectAsync(SavedCommand? command)
    {
        if (command == null) return;

        if (!IsSessionActive)
        {
            if (IsConnected)
            {
                await StartSessionAsync();
            }
            else
            {
                StatusMessage = "Please connect to VPS and start a session first.";
                return;
            }
        }

        CurrentCommand = command.Command;
        await _savedCommandService.RecordCommandUsageAsync(command.Id);
        await ExecuteCommandAsync();
    }

    [RelayCommand]
    private async Task StageCommandAsync(SavedCommand? command)
    {
        if (command == null) return;

        CurrentCommand = command.Command;
        await _savedCommandService.RecordCommandUsageAsync(command.Id);
        StatusMessage = $"Staged '{command.Name}' into command line";
    }

    [RelayCommand]
    private async Task StopSessionAsync()
    {
        try
        {
            await _terminalService.StopTerminalSessionAsync();
            IsSessionActive = _terminalService.IsSessionActive;
            OnPropertyChanged(nameof(SessionStateBadgeText));
            StatusMessage = "Terminal session stopped";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error stopping session: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ClearTerminalAsync()
    {
        await _terminalService.ClearOutputAsync();
        TerminalOutput = string.Empty;
        StatusMessage = "Terminal cleared";
    }

    [RelayCommand]
    private async Task CopyTerminalOutputAsync()
    {
        if (ClipboardRequested != null && !string.IsNullOrEmpty(TerminalOutput))
        {
            await ClipboardRequested(TerminalOutput);
            StatusMessage = "Terminal output copied to clipboard";
        }
    }

    [RelayCommand]
    private void IncreaseFontSize()
    {
        TerminalFontSize = Math.Min(24.0, TerminalFontSize + 1.0);
    }

    [RelayCommand]
    private void DecreaseFontSize()
    {
        TerminalFontSize = Math.Max(9.0, TerminalFontSize - 1.0);
    }

    [RelayCommand]
    private void ToggleWordWrap()
    {
        WordWrap = !WordWrap;
    }

    [RelayCommand]
    private void ToggleAutoScroll()
    {
        AutoScroll = !AutoScroll;
    }

    [RelayCommand]
    private void ToggleSidebar()
    {
        IsSidebarVisible = !IsSidebarVisible;
    }

    [RelayCommand]
    private void ToggleNewCommandForm()
    {
        IsNewCommandExpanded = !IsNewCommandExpanded;
        if (IsNewCommandExpanded)
        {
            IsEditingSnippet = false;
        }
    }

    [RelayCommand]
    private void ToggleSnippetDescription(SavedCommand? command)
    {
        if (command != null)
        {
            command.ShowDescription = !command.ShowDescription;
        }
    }

    [RelayCommand]
    private void StartEditSnippet(SavedCommand? command)
    {
        if (command == null) return;
        EditingSnippet = command;
        EditCommandName = command.Name;
        EditCommandText = command.Command;
        EditCommandDescription = command.Description;
        EditCommandCategory = string.IsNullOrWhiteSpace(command.Category) ? "Custom" : command.Category;
        IsEditingSnippet = true;
        IsNewCommandExpanded = false;
    }

    [RelayCommand]
    private async Task SaveEditSnippetAsync()
    {
        if (EditingSnippet == null) return;

        if (string.IsNullOrWhiteSpace(EditCommandName) || string.IsNullOrWhiteSpace(EditCommandText))
        {
            StatusMessage = "Please provide both snippet name and command text.";
            return;
        }

        try
        {
            EditingSnippet.Name = EditCommandName.Trim();
            EditingSnippet.Command = EditCommandText.Trim();
            EditingSnippet.Description = EditCommandDescription.Trim();
            EditingSnippet.Category = string.IsNullOrWhiteSpace(EditCommandCategory) ? "Custom" : EditCommandCategory;
            EditingSnippet.LastUsed = DateTime.UtcNow;

            await _savedCommandService.UpdateCommandAsync(EditingSnippet);
            await LoadSavedCommandsAsync();

            IsEditingSnippet = false;
            EditingSnippet = null;
            StatusMessage = $"Updated snippet '{EditCommandName}'";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error updating snippet: {ex.Message}";
        }
    }

    [RelayCommand]
    private void CancelEditSnippet()
    {
        IsEditingSnippet = false;
        EditingSnippet = null;
    }

    [RelayCommand]
    private async Task SaveNewCommandAsync()
    {
        if (string.IsNullOrWhiteSpace(NewCommandName) || string.IsNullOrWhiteSpace(NewCommandText))
        {
            StatusMessage = "Please provide both name and command text.";
            return;
        }

        try
        {
            var category = string.IsNullOrWhiteSpace(NewCommandCategory) ? "Custom" : NewCommandCategory;
            await _savedCommandService.AddCommandAsync(
                NewCommandName.Trim(), 
                NewCommandText.Trim(), 
                NewCommandDescription.Trim(),
                category);
            
            NewCommandName = string.Empty;
            NewCommandText = string.Empty;
            NewCommandDescription = string.Empty;
            NewCommandCategory = "Custom";
            IsNewCommandExpanded = false;
            
            await LoadSavedCommandsAsync();
            StatusMessage = "Command saved to library";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error saving command: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DeleteSavedCommandAsync(SavedCommand? command)
    {
        if (command == null)
            return;

        try
        {
            await _savedCommandService.DeleteCommandAsync(command.Id);
            await LoadSavedCommandsAsync();
            StatusMessage = $"Command '{command.Name}' deleted";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error deleting command: {ex.Message}";
        }
    }

    public async Task LoadSavedCommandsAsync()
    {
        try
        {
            var commands = await _savedCommandService.LoadCommandsAsync();
            SavedCommands.Clear();
            foreach (var cmd in commands.OrderByDescending(c => c.LastUsed))
            {
                SavedCommands.Add(cmd);
            }
            FilterCommands();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading commands: {ex.Message}";
        }
    }

    private void FilterCommands()
    {
        var query = SavedCommands.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(SelectedCategory) && !SelectedCategory.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(c => c.Category.Equals(SelectedCategory, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(SearchTerm))
        {
            query = query.Where(c =>
                c.Name.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase) ||
                c.Description.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase) ||
                c.Command.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase) ||
                c.Category.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase));
        }

        FilteredSavedCommands.Clear();
        foreach (var cmd in query)
        {
            FilteredSavedCommands.Add(cmd);
        }
    }

    partial void OnSelectedCategoryChanged(string value)
    {
        FilterCommands();
    }

    partial void OnSearchTermChanged(string value)
    {
        FilterCommands();
    }

    partial void OnSelectedCommandChanged(SavedCommand? value)
    {
        if (value != null)
        {
            CurrentCommand = value.Command;
        }
    }
}