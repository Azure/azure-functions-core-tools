// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.ComponentModel;
using System.Diagnostics;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workers;

namespace Azure.Functions.Cli.Workloads.Python;

/// <summary>
/// Python Functions project. Before the host runs, creates a project-local
/// <c>.venv</c> (when missing) and installs <c>requirements.txt</c> into it,
/// then points the worker at the venv so the user's deps are resolvable.
/// </summary>
internal sealed class PythonFunctionsProject : FunctionsProject
{
    internal const string VenvFolderName = ".venv";
    internal const string IsolateWorkerDepsEnvVar = "PYTHON_ISOLATE_WORKER_DEPENDENCIES";
    private const string RequirementsFileName = "requirements.txt";

    private readonly WorkingDirectory _workingDirectory;
    private readonly FunctionsWorkerReference _workerReference;

    public PythonFunctionsProject(WorkingDirectory workingDirectory)
    {
        _workingDirectory = workingDirectory ?? throw new ArgumentNullException(nameof(workingDirectory));
        _workerReference = FunctionsWorkerReference.FromWorkload("python");
    }

    // Internal seams so tests can stub interpreter / pip invocations.
    internal Func<string, string, CancellationToken, Task<(int ExitCode, string Stderr)>> RunCreateVenv { get; set; } = DefaultRunCreateVenv;

    internal Func<string, string, string, CancellationToken, Task<(int ExitCode, string Stderr)>> RunPipInstall { get; set; } = DefaultRunPipInstall;

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
        string venvPath = Path.Combine(root, VenvFolderName);

        if (!Directory.Exists(venvPath))
        {
            (int exitCode, string stderr) = await RunCreateVenv(root, venvPath, cancellationToken).ConfigureAwait(false);
            if (exitCode != 0)
            {
                string detail = string.IsNullOrWhiteSpace(stderr) ? "see output above." : stderr.Trim();
                throw new GracefulException(
                    $"'python -m venv {VenvFolderName}' failed (exit {exitCode}). {detail}",
                    isUserError: true);
            }
        }

        string requirementsPath = Path.Combine(root, RequirementsFileName);
        if (File.Exists(requirementsPath))
        {
            string pipPath = GetVenvExecutablePath(venvPath, "pip");
            (int exitCode, string stderr) = await RunPipInstall(root, pipPath, requirementsPath, cancellationToken).ConfigureAwait(false);
            if (exitCode != 0)
            {
                string detail = string.IsNullOrWhiteSpace(stderr) ? "see output above." : stderr.Trim();
                throw new GracefulException(
                    $"'pip install -r {RequirementsFileName}' failed (exit {exitCode}). {detail}",
                    isUserError: true);
            }
        }

        // Pin worker deps to the venv so a global site-packages can't shadow it.
        context.EnvironmentVariables[IsolateWorkerDepsEnvVar] = "1";
    }

    internal static string GetVenvExecutablePath(string venvPath, string executable)
    {
        string folder = OperatingSystem.IsWindows() ? "Scripts" : "bin";
        string name = OperatingSystem.IsWindows() ? executable + ".exe" : executable;
        return Path.Combine(venvPath, folder, name);
    }

    private static async Task<(int ExitCode, string Stderr)> DefaultRunCreateVenv(
        string workingDirectory,
        string venvPath,
        CancellationToken cancellationToken)
    {
        // Prefer `python3` on POSIX so we don't pick up a Python 2 shim. If the
        // interpreter isn't on PATH (Win32Exception from Process.Start), retry
        // with `python` so we work on Windows and pyenv setups that only expose
        // the unversioned name.
        string[] interpreters = OperatingSystem.IsWindows()
            ? ["python", "python3"]
            : ["python3", "python"];

        (int ExitCode, string Stderr) last = (-1, "No Python interpreter was found.");
        foreach (string interpreter in interpreters)
        {
            (int exitCode, string stderr, bool launched) = await TryRunProcessAsync(
                interpreter,
                ["-m", "venv", venvPath],
                workingDirectory,
                cancellationToken).ConfigureAwait(false);

            if (launched)
            {
                return (exitCode, stderr);
            }

            last = (exitCode, stderr);
        }

        return last;
    }

    private static Task<(int ExitCode, string Stderr)> DefaultRunPipInstall(
        string workingDirectory,
        string pipPath,
        string requirementsPath,
        CancellationToken cancellationToken)
    {
        return RunProcessAsync(pipPath, ["install", "-r", requirementsPath], workingDirectory, cancellationToken);
    }

    private static async Task<(int ExitCode, string Stderr)> RunProcessAsync(
        string fileName,
        IReadOnlyList<string> args,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        (int exitCode, string stderr, _) = await TryRunProcessAsync(fileName, args, workingDirectory, cancellationToken).ConfigureAwait(false);
        return (exitCode, stderr);
    }

    private static async Task<(int ExitCode, string Stderr, bool Launched)> TryRunProcessAsync(
        string fileName,
        IReadOnlyList<string> args,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        try
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

            using var process = Process.Start(psi);
            if (process is null)
            {
                return (-1, $"Failed to start '{fileName}' process.", false);
            }

            // Drain stdout and stderr in parallel: if either pipe buffer (~64KB) fills
            // while we're only reading the other, the child blocks on write and we deadlock.
            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return (process.ExitCode, await stderrTask.ConfigureAwait(false), true);
        }
        catch (Win32Exception ex)
        {
            // Interpreter wasn't on PATH; let the caller try a fallback.
            return (-1, ex.Message, false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (-1, ex.Message, true);
        }
    }
}
