using System.Text.Json;
using VpsManager.Core.Models;

namespace VpsManager.Core.Services;

/// <summary>
/// Service for managing saved terminal commands.
/// </summary>
public class SavedCommandService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _commandsFilePath;

    public SavedCommandService(string? customPath = null)
    {
        if (!string.IsNullOrEmpty(customPath))
        {
            _commandsFilePath = customPath;
        }
        else
        {
            string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VpsManager");
            Directory.CreateDirectory(appData);
            _commandsFilePath = Path.Combine(appData, "saved_commands.json");
        }
    }

    /// <summary>
    /// Default curated snippets for high-value server management tasks.
    /// </summary>
    public static List<SavedCommand> GetDefaultCuratedCommands() => new()
    {
        // Docker
        new SavedCommand { Name = "Docker Container Status", Command = "docker ps -a --format \"table {{.Names}}\\t{{.Status}}\\t{{.Ports}}\"", Description = "List all containers with status and port mappings", Category = "Docker" },
        new SavedCommand { Name = "Docker Compose Status", Command = "docker compose ps", Description = "Inspect active compose stack status", Category = "Docker" },
        new SavedCommand { Name = "Docker Resource Stats", Command = "docker stats --no-stream", Description = "Snapshot of container CPU, RAM, and IO", Category = "Docker" },
        new SavedCommand { Name = "Docker System Prune", Command = "docker system prune -af --volumes", Description = "Purge stopped containers, unused networks & volumes", Category = "Docker" },
        new SavedCommand { Name = "Docker Compose Logs", Command = "docker compose logs --tail=100 -f", Description = "Stream recent 100 log lines from compose stack", Category = "Docker" },

        // System & Resources
        new SavedCommand { Name = "Disk Space Utilization", Command = "df -h", Description = "Display filesystem disk space usage", Category = "System" },
        new SavedCommand { Name = "Memory & Swap Usage", Command = "free -h", Description = "Display physical RAM and swap statistics", Category = "System" },
        new SavedCommand { Name = "Top Processes Snapshot", Command = "top -b -n 1 | head -n 25", Description = "Snapshot of top processes by CPU and memory", Category = "System" },
        new SavedCommand { Name = "System Uptime & Load", Command = "uptime", Description = "Show uptime and 1/5/15 minute load averages", Category = "System" },
        new SavedCommand { Name = "OS & Kernel Info", Command = "uname -a && cat /etc/os-release", Description = "Print Linux kernel and OS distribution details", Category = "System" },

        // Logs & Diagnostics
        new SavedCommand { Name = "System Journal Errors", Command = "journalctl -xe -p 3 -n 50 --no-pager", Description = "View the last 50 error entries from systemd journal", Category = "Logs" },
        new SavedCommand { Name = "Recent System Log", Command = "tail -n 50 /var/log/syslog 2>/dev/null || journalctl -n 50 --no-pager", Description = "Inspect recent syslog lines", Category = "Logs" },
        new SavedCommand { Name = "Auth & SSH Logins", Command = "tail -n 50 /var/log/auth.log 2>/dev/null || journalctl -u ssh -n 50 --no-pager", Description = "Inspect authentication and SSH login events", Category = "Logs" },
        new SavedCommand { Name = "Kernel Buffer (dmesg)", Command = "dmesg -T | tail -n 50", Description = "Inspect recent kernel logs with human timestamps", Category = "Logs" },

        // Network
        new SavedCommand { Name = "Listening Ports & Sockets", Command = "ss -tulpn", Description = "Display all listening TCP/UDP ports and processes", Category = "Network" },
        new SavedCommand { Name = "UFW Firewall Status", Command = "ufw status verbose", Description = "Inspect firewall status and allowed ports", Category = "Network" },
        new SavedCommand { Name = "Local HTTP Probe", Command = "curl -I http://localhost", Description = "Probe local web server HTTP headers", Category = "Network" },
        new SavedCommand { Name = "External Public IP", Command = "curl -s ifconfig.me", Description = "Query the public IPv4 address of this VPS", Category = "Network" },

        // Services
        new SavedCommand { Name = "Nginx Test & Reload", Command = "nginx -t && systemctl reload nginx", Description = "Validate Nginx configuration syntax and reload", Category = "Services" },
        new SavedCommand { Name = "Docker Daemon Status", Command = "systemctl status docker --no-pager", Description = "Check systemd status of the Docker daemon", Category = "Services" },
        new SavedCommand { Name = "Active System Services", Command = "systemctl list-units --type=service --state=running --no-pager", Description = "List all running systemd background services", Category = "Services" }
    };

    /// <summary>
    /// Load all saved commands from storage, seeding curated defaults if none exist.
    /// </summary>
    public async Task<List<SavedCommand>> LoadCommandsAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_commandsFilePath))
        {
            var defaults = GetDefaultCuratedCommands();
            await SaveCommandsAsync(defaults, cancellationToken);
            return defaults;
        }

        try
        {
            using var stream = File.OpenRead(_commandsFilePath);
            var commands = await JsonSerializer.DeserializeAsync<List<SavedCommand>>(stream, JsonOptions, cancellationToken);
            if (commands == null || commands.Count == 0)
            {
                var defaults = GetDefaultCuratedCommands();
                await SaveCommandsAsync(defaults, cancellationToken);
                return defaults;
            }
            return commands;
        }
        catch
        {
            return GetDefaultCuratedCommands();
        }
    }

    /// <summary>
    /// Save all commands to storage.
    /// </summary>
    public async Task SaveCommandsAsync(List<SavedCommand> commands, CancellationToken cancellationToken = default)
    {
        using var stream = File.Create(_commandsFilePath);
        await JsonSerializer.SerializeAsync(stream, commands, JsonOptions, cancellationToken);
    }

    /// <summary>
    /// Add a new command to the saved commands.
    /// </summary>
    public async Task<SavedCommand> AddCommandAsync(string name, string command, string description = "", string category = "Custom", CancellationToken cancellationToken = default)
    {
        var commands = await LoadCommandsAsync(cancellationToken);
        
        var newCommand = new SavedCommand
        {
            Name = name,
            Command = command,
            Description = description,
            Category = string.IsNullOrWhiteSpace(category) ? "Custom" : category,
            CreatedAt = DateTime.UtcNow,
            LastUsed = DateTime.UtcNow
        };

        commands.Add(newCommand);
        await SaveCommandsAsync(commands, cancellationToken);
        
        return newCommand;
    }

    /// <summary>
    /// Update an existing command.
    /// </summary>
    public async Task UpdateCommandAsync(SavedCommand command, CancellationToken cancellationToken = default)
    {
        var commands = await LoadCommandsAsync(cancellationToken);
        var existing = commands.FirstOrDefault(c => c.Id == command.Id);
        
        if (existing != null)
        {
            var index = commands.IndexOf(existing);
            commands[index] = command;
            await SaveCommandsAsync(commands, cancellationToken);
        }
    }

    /// <summary>
    /// Delete a command by ID.
    /// </summary>
    public async Task DeleteCommandAsync(string commandId, CancellationToken cancellationToken = default)
    {
        var commands = await LoadCommandsAsync(cancellationToken);
        var command = commands.FirstOrDefault(c => c.Id == commandId);
        
        if (command != null)
        {
            commands.Remove(command);
            await SaveCommandsAsync(commands, cancellationToken);
        }
    }

    /// <summary>
    /// Record that a command was used (updates usage count and last used time).
    /// </summary>
    public async Task RecordCommandUsageAsync(string commandId, CancellationToken cancellationToken = default)
    {
        var commands = await LoadCommandsAsync(cancellationToken);
        var command = commands.FirstOrDefault(c => c.Id == commandId);
        
        if (command != null)
        {
            command.UsageCount++;
            command.LastUsed = DateTime.UtcNow;
            await SaveCommandsAsync(commands, cancellationToken);
        }
    }

    /// <summary>
    /// Search commands by name, description, or category.
    /// </summary>
    public async Task<List<SavedCommand>> SearchCommandsAsync(string searchTerm, string category = "All", CancellationToken cancellationToken = default)
    {
        var commands = await LoadCommandsAsync(cancellationToken);
        
        var query = commands.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(category) && !category.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(c => c.Category.Equals(category, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            query = query.Where(c => 
                c.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                c.Description.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                c.Command.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                c.Category.Contains(searchTerm, StringComparison.OrdinalIgnoreCase));
        }

        return query.ToList();
    }
}