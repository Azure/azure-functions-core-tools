// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;
using System.Text.RegularExpressions;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workers;

namespace Azure.Functions.Cli.Workloads.Go;

/// <summary>
/// Go Functions project. Before the host runs, validates the Go toolchain and
/// builds the user's Go module to <c>bin/app</c> (or <c>bin\app.exe</c> on
/// Windows) so the Go worker can launch it via the <c>defaultExecutablePath</c>
/// in <c>worker.config.json</c>.
/// </summary>
internal sealed class GoFunctionsProject : FunctionsProject
{
    // Hardcoded to match the Go worker's worker.config.json `defaultExecutablePath = bin/app`.
    // Don't derive from go.mod: the worker spawns this exact name regardless of module.
    internal const string DefaultExecutableName = "app";
    internal const int MinimumGoMajorVersion = 1;
    internal const int MinimumGoMinorVersion = 24;

    private const string ExecutableRelativeFolder = "bin";

    private readonly WorkingDirectory _workingDirectory;
    private readonly FunctionsWorkerReference _workerReference;

    public GoFunctionsProject(WorkingDirectory workingDirectory)
    {
        _workingDirectory = workingDirectory ?? throw new ArgumentNullException(nameof(workingDirectory));
        // The Go worker's worker.config.json declares language "native"; the host indexes WorkerConfig
        // by that language. The workload id stays "go" (matches the package and install command).
        _workerReference = FunctionsWorkerReference.FromWorkload("go", workerRuntime: "native");
    }

    // Internal seam so tests can stub out the `go build` invocation
    // without spawning real processes. stdout and stderr lines are streamed to
    // the callbacks as the process produces them, and the task yields the exit code.
    internal Func<string, string, Action<string>, Action<string>, CancellationToken, Task<int>> RunGoBuild { get; set; } = DefaultRunGoBuildAsync;

    internal Func<CancellationToken, Task<(int Major, int Minor)?>> ReadGoVersion { get; set; } = DefaultReadGoVersionAsync;

    // Run `go mod tidy` before building so missing/stale module entries are resolved
    // (especially for apps scaffolded from the version-less go.mod template). Internal
    // seam for tests.
    internal Func<string, Action<string>, Action<string>, CancellationToken, Task<int>> RunGoModTidy { get; set; } = DefaultRunGoModTidyAsync;

    public override WorkingDirectory WorkingDirectory => _workingDirectory;

    public override string StackName => "go";

    public override string StackDisplayName => "Go";

    public override bool SupportsExtensionBundles => true;

    public override FunctionsWorkerReference WorkerReference => _workerReference;

    public override async Task PrepareForHostRunAsync(FunctionsProjectHostRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        string root = _workingDirectory.Info.FullName;
        string executableName = OperatingSystem.IsWindows() ? DefaultExecutableName + ".exe" : DefaultExecutableName;
        string binDirectory = Path.Combine(root, ExecutableRelativeFolder);
        string outputPath = Path.Combine(binDirectory, executableName);

        if (context.SkipBuild)
        {
            // Go projects compile to bin/app; with --no-build the user is
            // asserting that binary already exists. Trust them and skip the
            // build. Leave StartupDirectory at the project root so host.json
            // is found there and the worker's `defaultExecutablePath = bin/app`
            // resolves to <project>/bin/app.
            return;
        }

        context.Reporter.ReportStatus("Checking Go toolchain");
        (int Major, int Minor)? version = await ReadGoVersion(cancellationToken).ConfigureAwait(false);
        if (version is null)
        {
            throw new GracefulException(
                $"Could not find a Go installation. Go {MinimumGoMajorVersion}.{MinimumGoMinorVersion} or later is required. Install Go from https://go.dev/dl/.",
                isUserError: true);
        }

        (int major, int minor) = version.Value;
        if (major < MinimumGoMajorVersion || (major == MinimumGoMajorVersion && minor < MinimumGoMinorVersion))
        {
            throw new GracefulException(
                $"Go {major}.{minor} is not supported. Go {MinimumGoMajorVersion}.{MinimumGoMinorVersion} or later is required. Update Go from https://go.dev/dl/.",
                isUserError: true);
        }

        // Tidy modules before building: the scaffold's go.mod omits the worker SDK
        // require line on purpose, so `go mod tidy` resolves the latest tag from the
        // imports in main.go. Also rescues apps whose go.sum has drifted.
        context.Reporter.ReportStatus("Running go mod tidy");
        int tidyExit = await RunGoModTidy(
            root,
            line => context.Reporter.WriteLog(line, FunctionsProjectReportSeverity.Info),
            line => context.Reporter.WriteLog(line, FunctionsProjectReportSeverity.Error),
            cancellationToken).ConfigureAwait(false);
        if (tidyExit != 0)
        {
            throw new GracefulException(
                $"'go mod tidy' failed (exit {tidyExit}).",
                isUserError: true);
        }

        context.Reporter.ReportStatus("Running go build");
        int exitCode = await RunGoBuild(
            root,
            outputPath,
            line => context.Reporter.WriteLog(line, FunctionsProjectReportSeverity.Info),
            line => context.Reporter.WriteLog(line, FunctionsProjectReportSeverity.Error),
            cancellationToken).ConfigureAwait(false);

        if (exitCode != 0)
        {
            throw new GracefulException(
                $"'go build' failed (exit {exitCode}).",
                isUserError: true);
        }

        // StartupDirectory stays at the project root: host.json lives there, and the Go
        // worker's `defaultExecutablePath = bin/app` is resolved relative to the script
        // root, so AzureWebJobsScriptRoot pointing at the project root yields the
        // correct <project>/bin/app launch path.
    }

    private static async Task<(int Major, int Minor)?> DefaultReadGoVersionAsync(CancellationToken cancellationToken)
    {
        try
        {
            var psi = new ProcessStartInfo("go")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("version");

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

            // `go version` prints e.g. "go version go1.24.3 darwin/arm64"
            Match match = Regex.Match(stdoutTask.Result, @"go(\d+)\.(\d+)");
            if (!match.Success)
            {
                return null;
            }

            return (int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    private static async Task<int> DefaultRunGoBuildAsync(
        string workingDirectory,
        string outputPath,
        Action<string> onOutputLine,
        Action<string> onErrorLine,
        CancellationToken cancellationToken)
    {
        string? outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        return await RunGoAsync(workingDirectory, ["build", "-o", outputPath, "."], onOutputLine, onErrorLine, cancellationToken).ConfigureAwait(false);
    }

    private static Task<int> DefaultRunGoModTidyAsync(
        string workingDirectory,
        Action<string> onOutputLine,
        Action<string> onErrorLine,
        CancellationToken cancellationToken)
        => RunGoAsync(workingDirectory, ["mod", "tidy"], onOutputLine, onErrorLine, cancellationToken);

    // Streams each line of stdout and stderr to the callbacks as `go` produces them, waits for exit, and
    // returns the exit code. Mirrors the live-streaming process core used by the .NET stack.
    private static async Task<int> RunGoAsync(
        string workingDirectory,
        string[] arguments,
        Action<string> onOutputLine,
        Action<string> onErrorLine,
        CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo("go")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string arg in arguments)
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
                onErrorLine("Failed to start 'go' process.");
                return -1;
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            // WaitForExitAsync does not guarantee the async stream readers have drained, so wait for the
            // trailing (null Data) sentinel on both streams to ensure every line has been delivered.
            await stdoutComplete.Task.ConfigureAwait(false);
            await stderrComplete.Task.ConfigureAwait(false);

            return process.ExitCode;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            onErrorLine(ex.Message);
            return -1;
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
