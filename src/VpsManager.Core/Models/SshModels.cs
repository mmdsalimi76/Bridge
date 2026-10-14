namespace VpsManager.Core.Models;

public class SshConnectionConfig
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 22;
    public string Username { get; set; } = string.Empty;
    public string? Password { get; set; }
    public string? PrivateKeyPath { get; set; }
    public string? Passphrase { get; set; }
}

public class CommandResult
{
    public int ExitCode { get; set; }
    public string Output { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
    public string ErrorOutput { get => Error; set => Error = value; }
    public bool IsSuccess => ExitCode == 0;
}
