using System;
using System.Collections.Generic;

namespace VpsManager.Core.Services.Logging;

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error,
    Fatal
}

public interface IAppLogger
{
    void Log(LogLevel level, string message, Exception? exception = null, string? caller = null);
    void Debug(string message, string? caller = null);
    void Info(string message, string? caller = null);
    void Warn(string message, Exception? exception = null, string? caller = null);
    void Error(string message, Exception? exception = null, string? caller = null);
    void Fatal(string message, Exception? exception = null, string? caller = null);
    string LogDirectory { get; }
    IReadOnlyList<string> GetRecentLogLines(int maxLines = 100);
}
