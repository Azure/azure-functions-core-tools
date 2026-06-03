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
    // simple to assert against (e.g. ["install"], ["run", "build"]). stdout and
    // stderr lines are streamed to the callbacks as the process produces them.
    internal Func<string, IReadOnlyList<string>, Action<string>, Action<string>, CancellationToken, Task<int>> RunNpm { get; set; } = DefaultRunNpm;

    public override WorkingDirectory WorkingDirectory => _workingDirectory;

    public override string StackName => "node";

    public override string StackDisplayName => "Node.js";

    public override bool SupportsExtensionBundles => true;

    public override FunctionsWorkerReference WorkerReference => _workerReference;

    public override string? Language
    {
        get
        {
            string root = _workingDirectory.Info.FullName;

            // tsconfig.json (or a top-level .ts file) is the strongest signal
            // that this is a TS project. Otherwise fall back to JavaScript;
            // package.json / .js / .mjs / .cjs all map there, and so does a
            // detected Node project with no further fingerprint (the factory
            // already vouched that this is a Node project at all).
            if (File.Exists(Path.Combine(root, "tsconfig.json"))
                || Directory.EnumerateFiles(root, "*.ts", SearchOption.TopDirectoryOnly).Any())
            {
                return "TypeScript";
            }

            return "JavaScript";
        }
    }

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
            await RunAsync(root, ["install"], "npm install", context.Reporter, cancellationToken).ConfigureAwait(false);
        }

        // --no-build skips compilation only; npm install above is restore, not build.
        if (!context.SkipBuild && HasBuildScript(packageJsonPath))
        {
            await RunAsync(root, ["run", "build"], "npm run build", context.Reporter, cancellationToken).ConfigureAwait(false);
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

    private async Task RunAsync(string root, IReadOnlyList<string> args, string display, IFunctionsProjectHostRunReporter reporter, CancellationToken cancellationToken)
    {
        reporter.ReportStatus($"Running {display}");

        // Stream stdout as informational and stderr as error output as the process produces it, so the
        // build/restore progress shows up live under the spinner instead of appearing all at once on exit.
        int exitCode = await RunNpm(
            root,
            args,
            line => reporter.WriteLog(line, FunctionsProjectReportSeverity.Info),
            line => reporter.WriteLog(line, FunctionsProjectReportSeverity.Error),
            cancellationToken).ConfigureAwait(false);

        if (exitCode != 0)
        {
            // Output has already been streamed live through the callbacks above, so the message points the
            // user there instead of repeating it.
            throw new GracefulException(
                $"'{display}' failed (exit {exitCode}).",
                isUserError: true);
        }
    }

    private static async Task<int> DefaultRunNpm(
        string workingDirectory,
        IReadOnlyList<string> args,
        Action<string> onOutputLine,
        Action<string> onErrorLine,
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

            return await RunProcessStreamingAsync(psi, onOutputLine, onErrorLine, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            onErrorLine(ex.Message);
            return -1;
        }
    }

    // Streams each line of stdout and stderr to the supplied callbacks as the process produces them, waits
    // for exit, and returns the exit code. Mirrors the live-streaming process core used by the .NET stack.
    private static async Task<int> RunProcessStreamingAsync(
        ProcessStartInfo startInfo,
        Action<string> onOutputLine,
        Action<string> onErrorLine,
        CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = startInfo };

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

        if (!process.Start())
        {
            onErrorLine("Failed to start 'npm' process.");
            return -1;
        }

        try
        {
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            // WaitForExitAsync does not guarantee the async stream readers have drained, so wait for the
            // trailing (null Data) sentinel on both streams to ensure every line has been delivered.
            await stdoutComplete.Task.ConfigureAwait(false);
            await stderrComplete.Task.ConfigureAwait(false);

            return process.ExitCode;
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
