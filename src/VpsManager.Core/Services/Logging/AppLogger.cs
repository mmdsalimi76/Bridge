using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;

namespace VpsManager.Core.Services.Logging;

public sealed class AppLogger : IAppLogger, IDisposable
{
    private readonly string _logDirectory;
    private readonly object _lock = new();
    private readonly ConcurrentQueue<string> _memoryBuffer = new();
    private const int MaxMemoryBufferLines = 250;
    private const int RetentionDays = 14;
    private bool _disposed;

    public string LogDirectory => _logDirectory;

    public AppLogger(string? customLogDirectory = null)
    {
        if (!string.IsNullOrWhiteSpace(customLogDirectory))
        {
            _logDirectory = customLogDirectory;
        }
        else
        {
            string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VpsManager");
            _logDirectory = Path.Combine(appData, "logs");
        }

        try
        {
            Directory.CreateDirectory(_logDirectory);
            PurgeOldLogs();
        }
        catch
        {
            // Logging initialization should not crash the host
        }
    }

    public void Log(LogLevel level, string message, Exception? exception = null, [CallerMemberName] string? caller = null)
    {
        if (_disposed) return;

        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        string levelStr = level == LogLevel.Warning ? "WARN " : level.ToString().ToUpperInvariant().PadRight(5);
        string source = string.IsNullOrWhiteSpace(caller) ? "App" : caller;

        var sb = new StringBuilder();
        sb.Append($"[{timestamp}] [{levelStr}] [{source}] {message}");

        if (exception != null)
        {
            sb.AppendLine();
            sb.Append($"Exception: {exception.GetType().FullName}: {exception.Message}");
            sb.AppendLine();
            sb.Append(exception.StackTrace);
        }

        string entry = sb.ToString();

        // Enqueue into in-memory ring buffer
        _memoryBuffer.Enqueue(entry);
        while (_memoryBuffer.Count > MaxMemoryBufferLines && _memoryBuffer.TryDequeue(out _)) { }

        // Write to daily file
        try
        {
            lock (_lock)
            {
                string logFile = Path.Combine(_logDirectory, $"bridge-{DateTime.Now:yyyyMMdd}.log");
                File.AppendAllText(logFile, entry + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // Suppress IO failures in logger to prevent cascading failure
        }
    }

    public void Debug(string message, [CallerMemberName] string? caller = null) =>
        Log(LogLevel.Debug, message, null, caller);

    public void Info(string message, [CallerMemberName] string? caller = null) =>
        Log(LogLevel.Info, message, null, caller);

    public void Warn(string message, Exception? exception = null, [CallerMemberName] string? caller = null) =>
        Log(LogLevel.Warning, message, exception, caller);

    public void Error(string message, Exception? exception = null, [CallerMemberName] string? caller = null) =>
        Log(LogLevel.Error, message, exception, caller);

    public void Fatal(string message, Exception? exception = null, [CallerMemberName] string? caller = null) =>
        Log(LogLevel.Fatal, message, exception, caller);

    public IReadOnlyList<string> GetRecentLogLines(int maxLines = 100)
    {
        return _memoryBuffer.Reverse().Take(maxLines).Reverse().ToList();
    }

    private void PurgeOldLogs()
    {
        try
        {
            var cutoff = DateTime.Now.AddDays(-RetentionDays);
            var dir = new DirectoryInfo(_logDirectory);
            if (!dir.Exists) return;

            foreach (var file in dir.GetFiles("bridge-*.log"))
            {
                if (file.LastWriteTime < cutoff)
                {
                    try { file.Delete(); } catch { }
                }
            }
        }
        catch
        {
            // Ignore purge cleanup errors
        }
    }

    public void Dispose()
    {
        _disposed = true;
    }
}
