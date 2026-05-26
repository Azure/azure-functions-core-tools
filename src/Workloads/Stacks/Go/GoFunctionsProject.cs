// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workers;

namespace Azure.Functions.Cli.Workloads.Go;

/// <summary>
/// Go Functions project. Before the host runs, builds the user's Go module
/// to <c>bin/&lt;module&gt;</c> so the Go worker can launch it via the
/// <c>defaultExecutablePath</c> in <c>worker.config.json</c>.
/// </summary>
internal sealed class GoFunctionsProject : FunctionsProject
{
    internal const string DefaultExecutableName = "app";
    private const string ExecutableRelativeFolder = "bin";
    private const string GoModFileName = "go.mod";

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
        string executableName = ResolveExecutableName(root);
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

    internal static string ResolveExecutableName(string projectRoot)
    {
        // Match the Go worker's default executable name (bin/app) when we can't
        // read a module name from go.mod. The worker.config.json hard-codes
        // bin/app; once that becomes module-aware we can switch to module name.
        string goModPath = Path.Combine(projectRoot, GoModFileName);
        if (!File.Exists(goModPath))
        {
            return DefaultExecutableName;
        }

        try
        {
            foreach (string line in File.ReadLines(goModPath))
            {
                string trimmed = line.Trim();
                if (!trimmed.StartsWith("module ", StringComparison.Ordinal))
                {
                    continue;
                }

                string modulePath = trimmed[("module ".Length)..].Trim();
                if (modulePath.Length == 0)
                {
                    return DefaultExecutableName;
                }

                int lastSlash = modulePath.LastIndexOf('/');
                string moduleName = lastSlash >= 0 ? modulePath[(lastSlash + 1)..] : modulePath;
                return string.IsNullOrWhiteSpace(moduleName) ? DefaultExecutableName : moduleName;
            }
        }
        catch (IOException)
        {
            // Falling back keeps the build attempt alive; the worker will surface a clearer error.
        }

        return DefaultExecutableName;
    }

    private static async Task<(int ExitCode, string Stderr)> DefaultRunGoBuild(
        string workingDirectory,
        string outputPath,
        CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

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

            string stderr = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return (process.ExitCode, stderr);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (-1, ex.Message);
        }
    }
}
