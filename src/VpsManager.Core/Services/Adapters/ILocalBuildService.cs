using System.Diagnostics;

namespace VpsManager.Core.Services.Adapters;

public interface ILocalBuildService
{
    Task<LocalBuildResult> ExecuteLocalBuildAsync(Models.UniversalPipelineConfig config, CancellationToken cancellationToken = default);
}

public class LocalBuildService : ILocalBuildService
{
    public async Task<LocalBuildResult> ExecuteLocalBuildAsync(Models.UniversalPipelineConfig config, CancellationToken cancellationToken = default)
    {
        if (config.BuildStrategy == Models.LocalBuildStrategy.None)
        {
            return new LocalBuildResult { IsSuccess = true, Output = "Local build skipped (Strategy: None)." };
        }

        string command = string.Empty;
        string args = string.Empty;

        if (!string.IsNullOrWhiteSpace(config.LocalBuildCommand))
        {
            var parts = config.LocalBuildCommand.Split(' ', 2);
            command = parts[0];
            args = parts.Length > 1 ? parts[1] : string.Empty;
        }
        else
        {
            switch (config.BuildStrategy)
            {
                case Models.LocalBuildStrategy.NodeNpm:
                    command = "npm";
                    args = "run build";
                    break;
                case Models.LocalBuildStrategy.DotnetPublish:
                    command = "dotnet";
                    args = "publish -c Release -o ./publish";
                    break;
                case Models.LocalBuildStrategy.GoBuild:
                    command = "go";
                    args = "build -o bin/app .";
                    break;
                case Models.LocalBuildStrategy.RustCargo:
                    command = "cargo";
                    args = "build --release";
                    break;
                case Models.LocalBuildStrategy.PythonVenv:
                    command = "python";
                    args = "-m pip freeze > requirements.txt";
                    break;
                default:
                    return new LocalBuildResult { IsSuccess = true, Output = "No build required." };
            }
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = command,
                Arguments = args,
                WorkingDirectory = config.LocalProjectPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi };
            process.Start();

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

            await process.WaitForExitAsync(cancellationToken);

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (process.ExitCode != 0)
            {
                return new LocalBuildResult
                {
                    IsSuccess = false,
                    ErrorMessage = $"Local build command '{command} {args}' failed with code {process.ExitCode}.\nErrors:\n{stderr}"
                };
            }

            return new LocalBuildResult
            {
                IsSuccess = true,
                Output = stdout
            };
        }
        catch (Exception ex)
        {
            return new LocalBuildResult
            {
                IsSuccess = false,
                ErrorMessage = $"Failed to launch local build process '{command}': {ex.Message}"
            };
        }
    }
}

public class LocalBuildResult
{
    public bool IsSuccess { get; set; }
    public string Output { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
}
