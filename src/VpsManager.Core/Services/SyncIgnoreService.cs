using System.Text;
using VpsManager.Core.Models;

namespace VpsManager.Core.Services;

public class SyncIgnoreAnalysis
{
    public bool HasSyncIgnoreFile { get; set; }
    public string SyncIgnorePath { get; set; } = string.Empty;
    public List<string> ActivePatterns { get; set; } = new();
    public List<string> DetectedMediaWarnings { get; set; } = new();
    public List<string> DetectedSensitiveWarnings { get; set; } = new();
    public bool HasWarnings => DetectedMediaWarnings.Count > 0 || DetectedSensitiveWarnings.Count > 0;
}

public interface ISyncIgnoreService
{
    SyncIgnoreAnalysis AnalyzeProjectWorkspace(string localProjectPath, List<string>? globalPatterns = null);
    Task<string> CreateStandardSyncIgnoreAsync(string localProjectPath, CancellationToken cancellationToken = default);
    Task AppendPatternsAsync(string localProjectPath, IEnumerable<string> patternsToAppend, CancellationToken cancellationToken = default);
    Task<List<FilePatchNode>> ScanWorkspaceNodesAsync(string localProjectPath, CancellationToken cancellationToken = default);
    Task WriteSyncIgnoreContentAsync(string localProjectPath, string content, CancellationToken cancellationToken = default);
}

public class SyncIgnoreService : ISyncIgnoreService
{
    private static readonly string[] StandardMediaDirectories = ["media", "uploads", "storage", "data", "public/uploads", "static/uploads"];
    private static readonly string[] SensitivePatterns = [".env", ".env.local", ".env.production", "*.pem", "*.key", "id_rsa*"];

    public const string StandardSyncIgnoreContent = """
        # ===================================================================
        # Bridge Sync Ignore Configuration (.syncignore)
        # ===================================================================

        # --- Persistent Remote Assets & Media (NEVER OVERWRITE ON SERVER) ---
        media/
        uploads/
        storage/
        data/
        *.sqlite
        *.sqlite3
        *.db

        # --- Local Secrets & Credentials ---
        .env.local
        .env.development
        *.pem
        *.key
        id_rsa*

        # --- Dependencies & Build Output Artifacts ---
        .git/
        node_modules/
        vendor/
        bin/
        obj/
        __pycache__/
        *.pyc
        .venv/
        .idea/
        .vscode/
        *.log
        """;

    public SyncIgnoreAnalysis AnalyzeProjectWorkspace(string localProjectPath, List<string>? globalPatterns = null)
    {
        var result = new SyncIgnoreAnalysis();
        if (string.IsNullOrWhiteSpace(localProjectPath) || !Directory.Exists(localProjectPath))
        {
            return result;
        }

        string syncIgnorePath = Path.Combine(localProjectPath, ".syncignore");
        string gitIgnorePath = Path.Combine(localProjectPath, ".gitignore");

        var patterns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Include global patterns
        if (globalPatterns != null)
        {
            foreach (var p in globalPatterns)
                if (!string.IsNullOrWhiteSpace(p)) patterns.Add(p.Trim());
        }

        if (File.Exists(syncIgnorePath))
        {
            result.HasSyncIgnoreFile = true;
            result.SyncIgnorePath = syncIgnorePath;
            foreach (var line in File.ReadAllLines(syncIgnorePath))
            {
                var trimmed = line.Trim();
                if (!string.IsNullOrEmpty(trimmed) && !trimmed.StartsWith('#'))
                {
                    patterns.Add(trimmed);
                }
            }
        }
        else if (File.Exists(gitIgnorePath))
        {
            // Fallback to .gitignore if no .syncignore
            result.SyncIgnorePath = gitIgnorePath;
            foreach (var line in File.ReadAllLines(gitIgnorePath))
            {
                var trimmed = line.Trim();
                if (!string.IsNullOrEmpty(trimmed) && !trimmed.StartsWith('#'))
                {
                    patterns.Add(trimmed);
                }
            }
        }

        result.ActivePatterns = patterns.ToList();

        // Check for media folders in local project that are NOT in active ignore patterns
        foreach (var mediaDir in StandardMediaDirectories)
        {
            string fullDir = Path.Combine(localProjectPath, mediaDir.Replace('/', Path.DirectorySeparatorChar));
            if (Directory.Exists(fullDir))
            {
                // Is this directory ignored?
                bool isIgnored = patterns.Any(p =>
                {
                    string clean = p.Trim('/', '*', '\\');
                    return mediaDir.Equals(clean, StringComparison.OrdinalIgnoreCase) ||
                           mediaDir.StartsWith(clean + "/", StringComparison.OrdinalIgnoreCase);
                });

                if (!isIgnored)
                {
                    result.DetectedMediaWarnings.Add(mediaDir);
                }
            }
        }

        // Check for sensitive files that might be uploaded
        string envFile = Path.Combine(localProjectPath, ".env");
        if (File.Exists(envFile) && !patterns.Contains(".env"))
        {
            result.DetectedSensitiveWarnings.Add(".env");
        }

        return result;
    }

    public async Task<string> CreateStandardSyncIgnoreAsync(string localProjectPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(localProjectPath))
            throw new ArgumentException("Local project path is required", nameof(localProjectPath));

        if (!Directory.Exists(localProjectPath))
            Directory.CreateDirectory(localProjectPath);

        string syncIgnorePath = Path.Combine(localProjectPath, ".syncignore");
        await File.WriteAllTextAsync(syncIgnorePath, StandardSyncIgnoreContent, Encoding.UTF8, cancellationToken);
        return syncIgnorePath;
    }

    public async Task AppendPatternsAsync(string localProjectPath, IEnumerable<string> patternsToAppend, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(localProjectPath)) return;

        string syncIgnorePath = Path.Combine(localProjectPath, ".syncignore");
        if (!File.Exists(syncIgnorePath))
        {
            await CreateStandardSyncIgnoreAsync(localProjectPath, cancellationToken);
        }

        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("# Added by Bridge Workstation");
        foreach (var p in patternsToAppend)
        {
            sb.AppendLine(p.EndsWith('/') ? p : p + "/");
        }

        await File.AppendAllTextAsync(syncIgnorePath, sb.ToString(), Encoding.UTF8, cancellationToken);
    }

    public async Task WriteSyncIgnoreContentAsync(string localProjectPath, string content, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(localProjectPath)) return;
        if (!Directory.Exists(localProjectPath))
            Directory.CreateDirectory(localProjectPath);

        string syncIgnorePath = Path.Combine(localProjectPath, ".syncignore");
        await File.WriteAllTextAsync(syncIgnorePath, content, Encoding.UTF8, cancellationToken);
    }

    public async Task<List<FilePatchNode>> ScanWorkspaceNodesAsync(string localProjectPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(localProjectPath) || !Directory.Exists(localProjectPath))
            return new List<FilePatchNode>();

        var analysis = AnalyzeProjectWorkspace(localProjectPath);
        var activePatterns = analysis.ActivePatterns;

        return await Task.Run(() =>
        {
            var nodes = new List<FilePatchNode>();
            var directories = new Dictionary<string, FilePatchNode>(StringComparer.OrdinalIgnoreCase);
            var rootDirInfo = new DirectoryInfo(localProjectPath);

            // Enumerate directories
            try
            {
                foreach (var dir in rootDirInfo.GetDirectories("*", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    // Skip hidden git internals
                    if (dir.FullName.Contains(Path.DirectorySeparatorChar + ".git")) continue;

                    string relDir = Path.GetRelativePath(localProjectPath, dir.FullName).Replace('\\', '/');
                    if (string.IsNullOrWhiteSpace(relDir) || relDir == ".") continue;

                    bool isIgnored = IsPathIgnored(relDir + "/", activePatterns);

                    if (!directories.TryGetValue(relDir, out var dirNode))
                    {
                        dirNode = new FilePatchNode
                        {
                            Name = dir.Name,
                            RelativePath = relDir + "/",
                            FullPath = dir.FullName,
                            IsDirectory = true,
                            IsSelected = isIgnored
                        };
                        directories[relDir] = dirNode;

                        // Attach to parent or root
                        var lastSlash = relDir.LastIndexOf('/');
                        if (lastSlash > 0)
                        {
                            var parentRel = relDir.Substring(0, lastSlash);
                            if (directories.TryGetValue(parentRel, out var parentNode))
                            {
                                parentNode.AddChild(dirNode);
                            }
                            else
                            {
                                nodes.Add(dirNode);
                            }
                        }
                        else
                        {
                            nodes.Add(dirNode);
                        }
                    }
                }
            }
            catch
            {
                // Ignore directory enumeration access exceptions
            }

            // Enumerate files
            try
            {
                foreach (var file in rootDirInfo.GetFiles("*", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (file.FullName.Contains(Path.DirectorySeparatorChar + ".git")) continue;

                    string relFile = Path.GetRelativePath(localProjectPath, file.FullName).Replace('\\', '/');
                    if (string.IsNullOrWhiteSpace(relFile)) continue;

                    bool isIgnored = IsPathIgnored(relFile, activePatterns);

                    var fileNode = new FilePatchNode
                    {
                        Name = file.Name,
                        RelativePath = relFile,
                        FullPath = file.FullName,
                        FileSizeBytes = file.Length,
                        LastModified = file.LastWriteTimeUtc,
                        IsDirectory = false,
                        IsSelected = isIgnored
                    };

                    var lastSlash = relFile.LastIndexOf('/');
                    if (lastSlash > 0)
                    {
                        var parentRel = relFile.Substring(0, lastSlash);
                        if (directories.TryGetValue(parentRel, out var parentNode))
                        {
                            parentNode.AddChild(fileNode);
                        }
                        else
                        {
                            nodes.Add(fileNode);
                        }
                    }
                    else
                    {
                        nodes.Add(fileNode);
                    }
                }
            }
            catch
            {
                // Ignore file enumeration access exceptions
            }

            SortNodes(nodes);
            return nodes;
        }, cancellationToken);
    }

    private static bool IsPathIgnored(string relativePath, List<string> patterns)
    {
        if (patterns == null || patterns.Count == 0) return false;
        string cleanPath = relativePath.TrimStart('/');

        foreach (var p in patterns)
        {
            var pattern = p.Trim();
            if (string.IsNullOrEmpty(pattern) || pattern.StartsWith('#')) continue;

            if (pattern.EndsWith('/'))
            {
                string dirPrefix = pattern.TrimEnd('/');
                if (cleanPath.Equals(dirPrefix, StringComparison.OrdinalIgnoreCase) ||
                    cleanPath.StartsWith(dirPrefix + "/", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            else if (pattern.StartsWith('*'))
            {
                string ext = pattern.TrimStart('*');
                if (cleanPath.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            else
            {
                if (cleanPath.Equals(pattern, StringComparison.OrdinalIgnoreCase) ||
                    Path.GetFileName(cleanPath).Equals(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static void SortNodes(IList<FilePatchNode> nodes)
    {
        var sorted = nodes
            .OrderByDescending(n => n.IsDirectory)
            .ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        nodes.Clear();
        foreach (var node in sorted)
        {
            nodes.Add(node);
            if (node.IsDirectory) SortNodes(node.Children);
        }
    }
}
