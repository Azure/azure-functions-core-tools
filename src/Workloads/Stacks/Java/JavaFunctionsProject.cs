// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workers;

namespace Azure.Functions.Cli.Workloads.Java;

/// <summary>
/// Java Functions project. Before the host runs, builds the user's Maven module
/// with <c>mvn clean package</c> so the Azure Functions Maven plugin stages the
/// app under <c>target/azure-functions/&lt;appName&gt;</c>, then points the host
/// startup directory at that staged output.
/// </summary>
internal sealed class JavaFunctionsProject : FunctionsProject
{
    private const string StagedAppRootRelativePath = "target/azure-functions";
    private const string HostJsonFileName = "host.json";

    private readonly WorkingDirectory _workingDirectory;
    private readonly FunctionsWorkerReference _workerReference;

    public JavaFunctionsProject(WorkingDirectory workingDirectory)
    {
        _workingDirectory = workingDirectory ?? throw new ArgumentNullException(nameof(workingDirectory));

        // The Java worker's worker.config.json declares language "java", which matches the
        // workload id, so no runtime override is needed (unlike the Go worker's "native").
        _workerReference = FunctionsWorkerReference.FromWorkload("java");
    }

    // Internal seams so tests can stub the Maven discovery/build without spawning real processes.
    internal Func<string, CancellationToken, Task<string?>> ResolveMavenCommand { get; set; } = DefaultResolveMavenCommandAsync;

    internal Func<string, string, Action<string>, Action<string>, CancellationToken, Task<int>> RunMavenPackage { get; set; } = DefaultRunMavenPackageAsync;

    public override WorkingDirectory WorkingDirectory => _workingDirectory;

    public override string StackName => "java";

    public override string StackDisplayName => "Java";

    public override bool SupportsExtensionBundles => true;

    public override FunctionsWorkerReference WorkerReference => _workerReference;

    public override async Task PrepareForHostRunAsync(FunctionsProjectHostRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        string root = _workingDirectory.Info.FullName;

        if (!context.SkipBuild)
        {
            string? mavenCommand = await ResolveMavenCommand(root, cancellationToken).ConfigureAwait(false);
            if (mavenCommand is null)
            {
                throw new GracefulException(
                    "Could not find Apache Maven. Install Maven 3.6 or later (https://maven.apache.org/install.html) "
                    + "and ensure 'mvn' is on your PATH, or add a Maven wrapper ('mvnw') to the project.",
                    isUserError: true);
            }

            context.Reporter.ReportStatus("Running mvn clean package");
            int exitCode = await RunMavenPackage(
                root,
                mavenCommand,
                line => context.Reporter.WriteLog(line, FunctionsProjectReportSeverity.Info),
                line => context.Reporter.WriteLog(line, FunctionsProjectReportSeverity.Error),
                cancellationToken).ConfigureAwait(false);
            if (exitCode != 0)
            {
                throw new GracefulException(
                    $"'mvn clean package' failed (exit {exitCode}).",
                    isUserError: true);
            }
        }

        DirectoryInfo? stagedApp = LocateStagedApp(root);
        if (stagedApp is null)
        {
            throw new GracefulException(
                $"No built Functions app was found under '{StagedAppRootRelativePath}'. "
                + "Run 'mvn clean package' to build the project before starting the host.",
                isUserError: true);
        }

        // The Maven plugin stages a self-contained app (host.json, the function jar, and
        // generated function.json metadata) under target/azure-functions/<appName>. The host
        // runs from there, not the project root.
        context.StartupDirectory = stagedApp;
    }

    // Finds the staged Functions app produced by the Azure Functions Maven plugin. The plugin
    // writes it to target/azure-functions/<appName>; when several exist (e.g. a timestamped
    // appName across builds), the most recently written one wins.
    private static DirectoryInfo? LocateStagedApp(string root)
    {
        var stagedRoot = new DirectoryInfo(Path.Combine(root, "target", "azure-functions"));
        if (!stagedRoot.Exists)
        {
            return null;
        }

        return stagedRoot.EnumerateDirectories()
            .Where(dir => File.Exists(Path.Combine(dir.FullName, HostJsonFileName)))
            .OrderByDescending(dir => dir.LastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static Task<string?> DefaultResolveMavenCommandAsync(string root, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Prefer a project-local Maven wrapper when present.
        string wrapperName = OperatingSystem.IsWindows() ? "mvnw.cmd" : "mvnw";
        string wrapperPath = Path.Combine(root, wrapperName);
        if (File.Exists(wrapperPath))
        {
            return Task.FromResult<string?>(wrapperPath);
        }

        // Otherwise fall back to Maven on PATH.
        string[] candidates = OperatingSystem.IsWindows()
            ? ["mvn.cmd", "mvn.bat", "mvn.exe"]
            : ["mvn"];
        return Task.FromResult(FindExecutableOnPath(candidates));
    }

    private static string? FindExecutableOnPath(string[] candidates)
    {
        string? pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathValue))
        {
            return null;
        }

        foreach (string directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (string candidate in candidates)
            {
                string fullPath = Path.Combine(directory, candidate);
                if (File.Exists(fullPath))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static Task<int> DefaultRunMavenPackageAsync(
        string workingDirectory,
        string mavenCommand,
        Action<string> onOutputLine,
        Action<string> onErrorLine,
        CancellationToken cancellationToken)
        => RunProcessAsync(workingDirectory, mavenCommand, ["clean", "package", "-DskipTests"], onOutputLine, onErrorLine, cancellationToken);

    // Streams each line of stdout and stderr to the callbacks as the process produces them,
    // waits for exit, and returns the exit code. Mirrors the Go stack's live-streaming runner.
    private static async Task<int> RunProcessAsync(
        string workingDirectory,
        string fileName,
        string[] arguments,
        Action<string> onOutputLine,
        Action<string> onErrorLine,
        CancellationToken cancellationToken)
    {
        bool wrapInCmd = OperatingSystem.IsWindows() && IsBatchScript(fileName);

        var psi = new ProcessStartInfo
        {
            // On Windows, Maven's launcher is a batch script (mvn.cmd / mvnw.cmd). CreateProcess can't
            // run a .cmd/.bat directly, so it must go through cmd.exe, otherwise it exits 1 with no
            // output. Real executables, and the shell-script launchers on Linux/macOS, start directly.
            FileName = wrapInCmd ? "cmd.exe" : fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (wrapInCmd)
        {
            psi.ArgumentList.Add("/c");
            psi.ArgumentList.Add(fileName);
        }

        foreach (string argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = psi };

        // stdout and stderr are delivered on separate thread-pool threads, so each stream gets its own
        // completion signal, and each line callback is invoked only from its own stream's thread.
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
            // On cancellation (Ctrl+C) or failure, don't leave the Maven build and its child JVM running.
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
            // Best effort: the process may have already exited between the check and the kill.
        }
    }

    internal static bool IsBatchScript(string fileName)
        => fileName.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)
        || fileName.EndsWith(".bat", StringComparison.OrdinalIgnoreCase);
}
