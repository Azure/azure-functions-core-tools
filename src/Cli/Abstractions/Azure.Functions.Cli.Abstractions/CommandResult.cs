using System.Diagnostics;

public readonly struct CommandResult(ProcessStartInfo startInfo, int exitCode, string? stdOut, string? stdErr)
{
    public static readonly CommandResult Empty = new();

    public ProcessStartInfo StartInfo { get; } = startInfo;
    public int ExitCode { get; } = exitCode;
    public string? StdOut { get; } = stdOut;
    public string? StdErr { get; } = stdErr;
}
