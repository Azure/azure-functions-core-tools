// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workers;

namespace Azure.Functions.Cli.Workloads.Python;

/// <summary>
/// Python Functions project. Before the host runs, resolves the project's
/// virtual environment (honoring <c>$VIRTUAL_ENV</c> or a pre-existing
/// <c>.venv</c> / <c>venv</c> / <c>env</c> folder, otherwise creating
/// <c>.venv</c>), installs <c>requirements.txt</c> into it, and points the
/// worker at the venv interpreter so the user's deps are resolvable.
/// </summary>
internal sealed class PythonFunctionsProject : FunctionsProject
{
    internal const string DefaultVenvFolderName = ".venv";
    internal const string IsolateWorkerDepsEnvVar = "PYTHON_ISOLATE_WORKER_DEPENDENCIES";
    internal const string WorkerExecutablePathEnvVar = "languageWorkers__python__defaultExecutablePath";
    internal const string WorkerRuntimeVersionEnvVar = "FUNCTIONS_WORKER_RUNTIME_VERSION";
    internal const string VirtualEnvEnvVar = "VIRTUAL_ENV";
    private const string RequirementsFileName = "requirements.txt";

    // Conventional venv folder names to look for in the project root before
    // creating a new one. Order matters: most-common first.
    private static readonly string[] _venvFolderCandidates = [".venv", "venv", "env", ".virtualenv"];

    private readonly WorkingDirectory _workingDirectory;
    private readonly FunctionsWorkerReference _workerReference;

    public PythonFunctionsProject(WorkingDirectory workingDirectory)
    {
        _workingDirectory = workingDirectory ?? throw new ArgumentNullException(nameof(workingDirectory));
        _workerReference = FunctionsWorkerReference.FromWorkload("python");
    }

    // Internal seams so tests can stub interpreter / pip invocations. stdout and stderr lines are streamed
    // to the callbacks as the process produces them, and the task yields the process exit code.
    internal Func<string, string, Action<string>, Action<string>, CancellationToken, Task<int>> RunCreateVenv { get; set; } = DefaultRunCreateVenv;

    internal Func<string, string, string, Action<string>, Action<string>, CancellationToken, Task<int>> RunPipInstall { get; set; } = DefaultRunPipInstall;

    internal Func<string, CancellationToken, Task<string?>> ReadPythonVersion { get; set; } = DefaultReadPythonVersion;

    internal Func<string, string?> ReadEnvironmentVariable { get; set; } = Environment.GetEnvironmentVariable;

    public override WorkingDirectory WorkingDirectory => _workingDirectory;

    public override string StackName => "python";

    public override string StackDisplayName => "Python";

    public override bool SupportsExtensionBundles => true;

    public override FunctionsWorkerReference WorkerReference => _workerReference;

    public override async Task PrepareForHostRunAsync(FunctionsProjectHostRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        // Python has no compile step, so --no-build (context.SkipBuild) is a no-op
        // here: venv creation and pip install are restore, not build.
        string root = _workingDirectory.Info.FullName;
        (string venvPath, bool venvAlreadyExisted) = ResolveVenvPath(root);

        if (!venvAlreadyExisted)
        {
            context.Reporter.ReportStatus($"Creating Python virtual environment in {Path.GetFileName(venvPath)}");

            int exitCode = await RunCreateVenv(
                root,
                venvPath,
                line => context.Reporter.WriteLog(line, FunctionsProjectReportSeverity.Info),
                line => context.Reporter.WriteLog(line, FunctionsProjectReportSeverity.Error),
                cancellationToken).ConfigureAwait(false);

            if (exitCode != 0)
            {
                throw new GracefulException(
                    $"'python -m venv {Path.GetFileName(venvPath)}' failed (exit {exitCode}).",
                    isUserError: true);
            }
        }

        string requirementsPath = Path.Combine(root, RequirementsFileName);
        if (File.Exists(requirementsPath))
        {
            context.Reporter.ReportStatus($"Installing Python dependencies from {RequirementsFileName}");

            string pipPath = GetVenvExecutablePath(venvPath, "pip");
            int exitCode = await RunPipInstall(
                root,
                pipPath,
                requirementsPath,
                line => context.Reporter.WriteLog(line, FunctionsProjectReportSeverity.Info),
                line => context.Reporter.WriteLog(line, FunctionsProjectReportSeverity.Error),
                cancellationToken).ConfigureAwait(false);

            if (exitCode != 0)
            {
                throw new GracefulException(
                    $"'pip install -r {RequirementsFileName}' failed (exit {exitCode}).",
                    isUserError: true);
            }
        }

        // Pin worker deps to the venv so a global site-packages can't shadow it.
        context.EnvironmentVariables[IsolateWorkerDepsEnvVar] = "1";

        // Point the host at the venv interpreter so the worker process actually has
        // the packages we just pip-installed. Without this, the host falls back to
        // the system Python and `import <user-dep>` fails at runtime.
        string venvPython = GetVenvExecutablePath(venvPath, "python");
        context.EnvironmentVariables[WorkerExecutablePathEnvVar] = venvPython;

        // Tell the host which Python worker to load (3.9 vs 3.13 etc.). Best-effort:
        // if we can't parse `python --version`, we leave it unset and let the host
        // fall back to whatever default it would pick.
        string? majorMinor = await ReadPythonVersion(venvPython, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(majorMinor))
        {
            context.EnvironmentVariables[WorkerRuntimeVersionEnvVar] = majorMinor;
        }
    }

    internal static string GetVenvExecutablePath(string venvPath, string executable)
    {
        string folder = OperatingSystem.IsWindows() ? "Scripts" : "bin";
        string name = OperatingSystem.IsWindows() ? executable + ".exe" : executable;
        return Path.Combine(venvPath, folder, name);
    }

    // Pick the venv to use, in priority order:
    //   1. $VIRTUAL_ENV (set by the `activate` script) so an explicitly-activated
    //      venv wins, even if it lives outside the project.
    //   2. Any conventional folder already present at the project root
    //      (.venv, venv, env, .virtualenv).
    //   3. Fall back to creating .venv inside the project root.
    // Returns the resolved venv path and whether it already exists on disk; the
    // caller skips `python -m venv` when it does.
    private (string Path, bool AlreadyExists) ResolveVenvPath(string projectRoot)
    {
        string? activated = ReadEnvironmentVariable(VirtualEnvEnvVar);
        if (!string.IsNullOrWhiteSpace(activated) && Directory.Exists(activated))
        {
            return (activated, true);
        }

        foreach (string candidate in _venvFolderCandidates)
        {
            string candidatePath = Path.Combine(projectRoot, candidate);
            if (Directory.Exists(candidatePath))
            {
                return (candidatePath, true);
            }
        }

        return (Path.Combine(projectRoot, DefaultVenvFolderName), false);
    }

    private static async Task<int> DefaultRunCreateVenv(
        string workingDirectory,
        string venvPath,
        Action<string> onOutputLine,
        Action<string> onErrorLine,
        CancellationToken cancellationToken)
    {
        // Prefer `python3` on POSIX so we don't pick up a Python 2 shim. If the
        // interpreter isn't on PATH (Win32Exception from Process.Start), retry
        // with `python` so we work on Windows and pyenv setups that only expose
        // the unversioned name.
        string[] interpreters = OperatingSystem.IsWindows()
            ? ["python", "python3"]
            : ["python3", "python"];

        foreach (string interpreter in interpreters)
        {
            (int exitCode, bool launched) = await TryRunProcessStreamingAsync(
                interpreter,
                ["-m", "venv", venvPath],
                workingDirectory,
                onOutputLine,
                onErrorLine,
                cancellationToken).ConfigureAwait(false);

            if (launched)
            {
                return exitCode;
            }
        }

        onErrorLine("No Python interpreter was found.");
        return -1;
    }

    private static async Task<int> DefaultRunPipInstall(
        string workingDirectory,
        string pipPath,
        string requirementsPath,
        Action<string> onOutputLine,
        Action<string> onErrorLine,
        CancellationToken cancellationToken)
    {
        (int exitCode, _) = await TryRunProcessStreamingAsync(
            pipPath,
            ["install", "-r", requirementsPath],
            workingDirectory,
            onOutputLine,
            onErrorLine,
            cancellationToken).ConfigureAwait(false);

        return exitCode;
    }

    private static async Task<string?> DefaultReadPythonVersion(string pythonPath, CancellationToken cancellationToken)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = pythonPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("--version");

            using var process = Process.Start(psi);
            if (process is null)
            {
                return null;
            }

            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                return null;
            }

            // `python --version` historically printed to stderr (Python 2) and now
            // prints to stdout (Python 3). Inspect both so we don't miss it.
            string raw = stdoutTask.Result + " " + stderrTask.Result;
            Match match = Regex.Match(raw, @"Python\s+(\d+)\.(\d+)");
            return match.Success ? $"{match.Groups[1].Value}.{match.Groups[2].Value}" : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    // Streams each line of stdout and stderr to the callbacks as the process produces them, waits for exit,
    // and returns the exit code plus whether the process actually launched. A failure to launch (interpreter
    // not on PATH) is reported via Launched=false so the caller can fall back to another interpreter.
    private static async Task<(int ExitCode, bool Launched)> TryRunProcessStreamingAsync(
        string fileName,
        IReadOnlyList<string> args,
        string workingDirectory,
        Action<string> onOutputLine,
        Action<string> onErrorLine,
        CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (string arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = psi };

        // stdout and stderr are delivered on separate thread-pool threads, so each stream gets its own
        // completion signal; each line callback is invoked only from its own stream's thread.
        TaskCompletionSource stdoutComplete = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource stderrComplete = new(TaskCreationOptions.RunContinuationsAsynchronously);

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                stdoutComplete.TrySetResult();
            }
            else
            {
                onOutputLine(e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                stderrComplete.TrySetResult();
            }
            else
            {
                onErrorLine(e.Data);
            }
        };

        try
        {
            if (!process.Start())
            {
                onErrorLine($"Failed to start '{fileName}' process.");
                return (-1, true);
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            // WaitForExitAsync does not guarantee the async stream readers have drained, so wait for the
            // trailing (null Data) sentinel on both streams to ensure every line has been delivered.
            await stdoutComplete.Task.ConfigureAwait(false);
            await stderrComplete.Task.ConfigureAwait(false);

            return (process.ExitCode, true);
        }
        catch (Win32Exception)
        {
            // Interpreter wasn't on PATH; let the caller try a fallback.
            return (-1, false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            onErrorLine(ex.Message);
            return (-1, true);
        }
        finally
        {
            KillProcess(process);
        }
    }

    private static void KillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception)
        {
            // Best effort: don't mask the original outcome if the process has already exited.
        }
    }
}
