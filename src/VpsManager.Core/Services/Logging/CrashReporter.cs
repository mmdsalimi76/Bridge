using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace VpsManager.Core.Services.Logging;

public static class CrashReporter
{
    public static string CreateCrashDump(Exception ex, IAppLogger? logger = null)
    {
        string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VpsManager");
        string crashDir = Path.Combine(appData, "crashes");

        try
        {
            Directory.CreateDirectory(crashDir);
            string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string crashFilePath = Path.Combine(crashDir, $"CRASH-{timestamp}.txt");

            var sb = new StringBuilder();
            sb.AppendLine("================================================================================");
            sb.AppendLine("                      BRIDGE CRASH DIAGNOSTIC REPORT");
            sb.AppendLine("================================================================================");
            sb.AppendLine($"Timestamp:         {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
            sb.AppendLine($"OS Platform:       {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
            sb.AppendLine($".NET Runtime:      {Environment.Version}");
            sb.AppendLine($"Process ID:        {Environment.ProcessId}");
            sb.AppendLine($"Machine Name:      {Environment.MachineName}");
            sb.AppendLine("================================================================================");
            sb.AppendLine("EXCEPTION DETAILS:");
            sb.AppendLine($"Type:    {ex.GetType().FullName}");
            sb.AppendLine($"Message: {ex.Message}");
            sb.AppendLine("Stack Trace:");
            sb.AppendLine(ex.StackTrace);

            var inner = ex.InnerException;
            int depth = 1;
            while (inner != null)
            {
                sb.AppendLine();
                sb.AppendLine($"--- Inner Exception (Level {depth++}) ---");
                sb.AppendLine($"Type:    {inner.GetType().FullName}");
                sb.AppendLine($"Message: {inner.Message}");
                sb.AppendLine("Stack Trace:");
                sb.AppendLine(inner.StackTrace);
                inner = inner.InnerException;
            }

            if (logger != null)
            {
                sb.AppendLine();
                sb.AppendLine("================================================================================");
                sb.AppendLine("RECENT APPLICATION LOG ENTRIES:");
                sb.AppendLine("================================================================================");
                var recentLogs = logger.GetRecentLogLines(50);
                foreach (var line in recentLogs)
                {
                    sb.AppendLine(line);
                }
            }

            sb.AppendLine("================================================================================");

            File.WriteAllText(crashFilePath, sb.ToString(), Encoding.UTF8);
            return crashFilePath;
        }
        catch (Exception dumpEx)
        {
            return $"Failed to write crash dump: {dumpEx.Message}";
        }
    }
}
