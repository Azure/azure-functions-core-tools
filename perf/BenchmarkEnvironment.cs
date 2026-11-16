using System.Diagnostics;

namespace Func.Performance;

internal class BenchmarkEnvironment : IDisposable
{
    private string? _root;

    internal virtual ProcessStartInfo Prepare(CommandCase command)
    {
        string executable = Path.Combine(AppContext.BaseDirectory, "cli", OperatingSystem.IsWindows() ? "func.exe" : "func");
        if (!File.Exists(executable))
        {
            throw new FileNotFoundException("Publish and stage the runtime-specific self-contained CLI before benchmarking.", executable);
        }

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(executable, File.GetUnixFileMode(executable) | UnixFileMode.UserExecute);
        }

        _root = Path.Combine(Path.GetTempPath(), "func-perf-" + Guid.NewGuid().ToString("N"));
        string home = Path.Combine(_root, "home");
        string work = Path.Combine(_root, "project");
        string workloads = Path.Combine(_root, "workloads");
        Directory.CreateDirectory(home);
        Directory.CreateDirectory(work);
        Directory.CreateDirectory(workloads);
        // Keep the existing update advisory path offline without changing product behavior.
        File.WriteAllText(Path.Combine(home, ".version-check"), "0.0.0");

        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = work,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in command.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["FUNC_CLI_HOME"] = home;
        startInfo.Environment["FUNC_CLI_WORKLOADS_HOME"] = workloads;
        startInfo.Environment["FUNC_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["NO_COLOR"] = "1";
        return startInfo;
    }

    public void Dispose()
    {
        if (_root is not null)
        {
            Directory.Delete(_root, recursive: true);
            _root = null;
        }
    }
}
