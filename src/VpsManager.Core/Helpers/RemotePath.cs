namespace VpsManager.Core.Helpers;

/// <summary>
/// Shared rules for composing paths on the remote host. Kept in one place so every
/// caller resolves the same file for a given project.
/// </summary>
public static class RemotePath
{
    /// <summary>
    /// Resolves a compose file name against a project directory. Absolute names are
    /// returned unchanged; relative names are joined onto the project path.
    /// </summary>
    public static string ResolveComposeFilePath(string composeFileName, string projectPath)
    {
        if (string.IsNullOrWhiteSpace(composeFileName)
            || composeFileName.StartsWith('/')
            || composeFileName.StartsWith('\\')
            || (composeFileName.Length > 2 && composeFileName[1] == ':')
            || string.IsNullOrWhiteSpace(projectPath))
        {
            return composeFileName;
        }

        var trimmedProject = projectPath.TrimEnd('/', '\\');
        var trimmedCompose = composeFileName.TrimStart('/', '\\');

        if (projectPath.Contains('\\'))
        {
            return $"{trimmedProject}\\{trimmedCompose}";
        }

        return $"{trimmedProject}/{trimmedCompose}";
    }
}
