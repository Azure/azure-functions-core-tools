#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Azure.Functions.Cli.Performance.Startup;

// Public bridge types are shared by PowerShell Add-Type and the generated benchmark host.
public sealed class StartupObservation(double elapsedMs, string status, int? exitCode, string stdout, string stderr, int processId)
{
    public double ElapsedMs { get; } = elapsedMs;

    public string Status { get; } = status;

    public int? ExitCode { get; } = exitCode;

    public string Stdout { get; } = stdout;

    public string Stderr { get; } = stderr;

    public int ProcessId { get; } = processId;
}

public sealed class StartupProcess : IDisposable
{
    private readonly NativeStartupProcess _process;
    private readonly int _timeoutMilliseconds;
    private readonly Stopwatch _timer = new();
    private Task<string>? _stdout;
    private Task<string>? _stderr;
    private bool _started;
    private bool _exited;
    private bool _disposed;

    public bool HasStarted => _started;

    public bool ExitedWithinTimeout => _exited;

    public StartupProcess(string executable, string[] arguments, string directory, IDictionary<string, string> environmentVariables, int timeoutMilliseconds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(environmentVariables);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMilliseconds);
        ValidateNativeString(executable, nameof(executable));
        ValidateNativeString(directory, nameof(directory));
        foreach (string argument in arguments)
        {
            ArgumentNullException.ThrowIfNull(argument);
            ValidateNativeString(argument, nameof(arguments));
        }
        _timeoutMilliseconds = timeoutMilliseconds;
        var environment = new Dictionary<string, string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (string? key in new[] { "PATH", "SystemRoot", "WINDIR", "ProgramFiles", "ProgramFiles(x86)", "ProgramW6432",
            "DOTNET_ROOT", "DOTNET_ROOT_X64", "DOTNET_ROOT_ARM64", "LD_LIBRARY_PATH", "DYLD_LIBRARY_PATH" })
        {
            if (Environment.GetEnvironmentVariable(key) is { } value)
            {
                environment[key] = value;
            }
        }

        foreach (KeyValuePair<string, string> entry in environmentVariables)
        {
            ArgumentException.ThrowIfNullOrEmpty(entry.Key);
            ArgumentNullException.ThrowIfNull(entry.Value);
            ValidateNativeString(entry.Key, nameof(environmentVariables));
            ValidateNativeString(entry.Value, nameof(environmentVariables));
            if (entry.Key.Contains('='))
            {
                throw new ArgumentException("Environment keys must not contain '='.", nameof(environmentVariables));
            }
            environment[entry.Key] = entry.Value;
        }

        _process = OperatingSystem.IsWindows()
            ? new WindowsStartupProcess(executable, arguments, directory, environment)
            : OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()
                ? new UnixStartupProcess(executable, arguments, directory, environment)
                : throw new PlatformNotSupportedException("Startup containment requires Windows, Linux or macOS.");
    }

    public void StartAndWait()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
        {
            throw new InvalidOperationException("A measurement must launch a fresh process.");
        }

        _timer.Start();
        _process.Start();
        _started = true;
        _stdout = _process.Output.ReadToEndAsync();
        _stderr = _process.Error.ReadToEndAsync();
        _exited = _process.WaitForExit(_timeoutMilliseconds);
        _timer.Stop();
    }

    public StartupObservation Capture()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_started || _stdout is null || _stderr is null)
        {
            throw new InvalidOperationException("Process output capture was not started.");
        }

        if (!_exited)
        {
            _process.Terminate();
        }

        Task.WhenAll(_stdout, _stderr).WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        return new StartupObservation(_timer.Elapsed.TotalMilliseconds, _exited ? "exited" : "timeout",
            _exited ? _process.ExitCode : null, _stdout.GetAwaiter().GetResult(), _stderr.GetAwaiter().GetResult(), _process.Id);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            if (_started)
            {
                _process.Terminate();
                if (_stdout is not null && _stderr is not null)
                {
                    Task.WhenAll(_stdout, _stderr).WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
                }
            }
        }
        finally
        {
            try { _process.Dispose(); }
            finally { _disposed = true; }
        }
    }

    private static void ValidateNativeString(string value, string parameter)
    {
        if (value.Contains('\0'))
        {
            throw new ArgumentException("Native process inputs must not contain NUL characters.", parameter);
        }
    }
}

public sealed class StartupFixture
{
    public StartupFixture(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        DirectoryPath = directory;
        if (Directory.Exists(directory))
        {
            throw new IOException($"Startup fixture directory already exists: '{directory}'.");
        }

        Directory.CreateDirectory(directory);
        EnvironmentVariables = new Dictionary<string, string>
        {
            ["FUNC_CLI_TELEMETRY_OPTOUT"] = "true",
            ["FUNCTIONS_CORE_TOOLS_TELEMETRY_OPTOUT"] = "true",
            ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "true",
            ["NO_COLOR"] = "1",
            ["TERM"] = "dumb",
        };
        foreach (string? key in new[] { "FUNC_CLI_HOME", "FUNC_CLI_WORKLOADS_HOME", "HOME", "USERPROFILE", "APPDATA", "LOCALAPPDATA",
            "TEMP", "TMP", "TMPDIR", "XDG_CONFIG_HOME", "XDG_CACHE_HOME", "XDG_DATA_HOME", "XDG_STATE_HOME", "DOTNET_BUNDLE_EXTRACT_BASE_DIR" })
        {
            string path = Path.Combine(directory, key.ToLowerInvariant());
            Directory.CreateDirectory(path);
            EnvironmentVariables[key] = path;
        }

        File.WriteAllText(Path.Combine(EnvironmentVariables["FUNC_CLI_HOME"], ".version-check"), "0.0.0");
    }

    public string DirectoryPath { get; }

    public Dictionary<string, string> EnvironmentVariables { get; }
}
