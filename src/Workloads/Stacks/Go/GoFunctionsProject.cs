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
        _workerReference = FunctionsWorkerReference.FromWorkload("go");
    }

    // Internal seam so tests can stub out the `go build` invocation
    // without spawning real processes.
    internal Func<string, string, CancellationToken, Task<(int ExitCode, string Stderr)>> RunGoBuild { get; set; } = DefaultRunGoBuild;

    internal Func<CancellationToken, Task<(int Major, int Minor)?>> ReadGoVersion { get; set; } = DefaultReadGoVersion;

    public override WorkingDirectory WorkingDirectory => _workingDirectory;

    public override string StackName => "go";

    public override string StackDisplayName => "Go";

    public override bool SupportsExtensionBundles => true;

    public override FunctionsWorkerReference WorkerReference => _workerReference;

    public override async Task PrepareForHostRunAsync(FunctionsProjectHostRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (context.SkipBuild)
        {
            // Go projects compile to bin/app; with --no-build the user is
            // asserting that binary already exists. Trust them and skip.
            return;
        }

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

        string root = _workingDirectory.Info.FullName;
        string executableName = OperatingSystem.IsWindows() ? DefaultExecutableName + ".exe" : DefaultExecutableName;
        string outputPath = Path.Combine(root, ExecutableRelativeFolder, executableName);

        (int exitCode, string stderr) = await RunGoBuild(root, outputPath, cancellationToken).ConfigureAwait(false);
        if (exitCode != 0)
        {
            string detail = string.IsNullOrWhiteSpace(stderr) ? "see build output above." : stderr.Trim();
            throw new GracefulException(
                $"'go build' failed (exit {exitCode}). {detail}",
                isUserError: true);
        }
    }

    private static async Task<(int Major, int Minor)?> DefaultReadGoVersion(CancellationToken cancellationToken)
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

    private static async Task<(int ExitCode, string Stderr)> DefaultRunGoBuild(
        string workingDirectory,
        string outputPath,
        CancellationToken cancellationToken)
    {
        try
        {
            string? outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            var psi = new ProcessStartInfo("go")
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("build");
            psi.ArgumentList.Add("-o");
            psi.ArgumentList.Add(outputPath);
            psi.ArgumentList.Add(".");

            using var process = Process.Start(psi);
            if (process is null)
            {
                return (-1, "Failed to start 'go' process.");
            }

            // Drain stdout and stderr in parallel: if either pipe buffer (~64KB) fills
            // while we're only reading the other, go blocks on write and we deadlock.
            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return (process.ExitCode, await stderrTask.ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (-1, ex.Message);
        }
    }
}
