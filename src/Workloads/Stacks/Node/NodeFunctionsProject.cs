// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;
using System.Text.Json;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workers;

namespace Azure.Functions.Cli.Workloads.Node;

/// <summary>
/// Node.js Functions project. Before the host runs, restores
/// <c>node_modules</c> (when missing) and runs the project's <c>build</c>
/// script (when defined) so the host can launch compiled output.
/// </summary>
internal sealed class NodeFunctionsProject : FunctionsProject
{
    private const string PackageJsonFileName = "package.json";
    private const string NodeModulesFolderName = "node_modules";

    private readonly WorkingDirectory _workingDirectory;
    private readonly FunctionsWorkerReference _workerReference;

    public NodeFunctionsProject(WorkingDirectory workingDirectory)
    {
        _workingDirectory = workingDirectory ?? throw new ArgumentNullException(nameof(workingDirectory));
        _workerReference = FunctionsWorkerReference.FromWorkload("node");
    }

    // Internal seam so tests can stub npm invocations without spawning real
    // processes. Args is forwarded as a single argv list to keep tests
    // simple to assert against (e.g. ["install"], ["run", "build"]).
    internal Func<string, IReadOnlyList<string>, CancellationToken, Task<(int ExitCode, string Stderr)>> RunNpm { get; set; } = DefaultRunNpm;

    public override WorkingDirectory WorkingDirectory => _workingDirectory;

    public override string StackName => "node";

    public override string StackDisplayName => "Node.js";

    public override bool SupportsExtensionBundles => true;

    public override FunctionsWorkerReference WorkerReference => _workerReference;

    public override async Task PrepareForHostRunAsync(FunctionsProjectHostRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        string root = _workingDirectory.Info.FullName;
        string packageJsonPath = Path.Combine(root, PackageJsonFileName);
        if (!File.Exists(packageJsonPath))
        {
            // Lone .js/.ts entry point with no package.json: nothing to install or build.
            return;
        }

        if (!Directory.Exists(Path.Combine(root, NodeModulesFolderName)))
        {
            await RunAsync(root, ["install"], "npm install", cancellationToken).ConfigureAwait(false);
        }

        if (HasBuildScript(packageJsonPath))
        {
            await RunAsync(root, ["run", "build"], "npm run build", cancellationToken).ConfigureAwait(false);
        }
    }

    internal static bool HasBuildScript(string packageJsonPath)
    {
        try
        {
            using FileStream stream = File.OpenRead(packageJsonPath);
            using var doc = JsonDocument.Parse(stream);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("scripts", out JsonElement scripts)
                || scripts.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            return scripts.TryGetProperty("build", out JsonElement build)
                && build.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(build.GetString());
        }
        catch (JsonException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private async Task RunAsync(string root, IReadOnlyList<string> args, string display, CancellationToken cancellationToken)
    {
        (int exitCode, string stderr) = await RunNpm(root, args, cancellationToken).ConfigureAwait(false);
        if (exitCode != 0)
        {
            string detail = string.IsNullOrWhiteSpace(stderr) ? "see output above." : stderr.Trim();
            throw new GracefulException(
                $"'{display}' failed (exit {exitCode}). {detail}",
                isUserError: true);
        }
    }

    private static async Task<(int ExitCode, string Stderr)> DefaultRunNpm(
        string workingDirectory,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        try
        {
            // Windows resolves `npm` via the `npm.cmd` shim, which only works
            // when launched through the shell. Direct Process.Start of "npm"
            // fails on Windows otherwise.
            bool isWindows = OperatingSystem.IsWindows();
            var psi = new ProcessStartInfo
            {
                FileName = isWindows ? "cmd.exe" : "npm",
                WorkingDirectory = workingDirectory,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            if (isWindows)
            {
                psi.ArgumentList.Add("/c");
                psi.ArgumentList.Add("npm");
            }

            foreach (string arg in args)
            {
                psi.ArgumentList.Add(arg);
            }

            using var process = Process.Start(psi);
            if (process is null)
            {
                return (-1, "Failed to start 'npm' process.");
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
