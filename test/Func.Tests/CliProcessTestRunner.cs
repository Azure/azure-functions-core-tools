// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;
using System.Reflection;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Common;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.Tests;

/// <summary>
/// Runs the built CLI in a separate process with isolated user and cache state.
/// </summary>
internal sealed class CliProcessTestRunner(ITestOutputHelper output)
{
    internal const string UnusedSource = "http://127.0.0.1:1/v3/index.json";
    internal const string StateDirectoryName = "process-state";
    internal static readonly TimeSpan ExecutionTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan _cleanupTimeout = TimeSpan.FromSeconds(10);

    private readonly ITestOutputHelper _output = output ?? throw new ArgumentNullException(nameof(output));

    internal async Task<CliResult> RunAsync(
        string directory, string home, string workloadsHome, string project, IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(home);
        ArgumentException.ThrowIfNullOrWhiteSpace(workloadsHome);
        ArgumentException.ThrowIfNullOrWhiteSpace(project);
        ArgumentNullException.ThrowIfNull(args);

        string testAssembly = typeof(CliProcessTestRunner).Assembly.Location;
        Assembly cliAssembly = typeof(FuncRootCommand).Assembly;
        ProcessStartInfo startInfo = new()
        {
            FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            WorkingDirectory = project,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("exec");
        startInfo.ArgumentList.Add("--runtimeconfig");
        startInfo.ArgumentList.Add(Path.ChangeExtension(testAssembly, ".runtimeconfig.json"));
        startInfo.ArgumentList.Add("--depsfile");
        startInfo.ArgumentList.Add(Path.ChangeExtension(testAssembly, ".deps.json"));
        startInfo.ArgumentList.Add(cliAssembly.Location);
        foreach (string arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        // Preserve only host/runtime discovery. Do not inherit CLI options, credentials, hooks, or user caches.
        startInfo.Environment.Clear();
        string[] inheritedKeys =
        [
            "PATH", "SystemRoot", "WINDIR", "ProgramFiles", "ProgramFiles(x86)", "ProgramW6432",
            "DOTNET_ROOT", "DOTNET_ROOT_X64", "DOTNET_ROOT_X86", "DOTNET_ROOT_ARM64", "DOTNET_ROOT(x86)",
            "LD_LIBRARY_PATH", "DYLD_LIBRARY_PATH",
        ];
        foreach (string key in inheritedKeys)
        {
            if (Environment.GetEnvironmentVariable(key) is { } value)
            {
                startInfo.Environment[key] = value;
            }
        }

        string[] isolatedKeys =
        [
            "HOME", "USERPROFILE", "APPDATA", "LOCALAPPDATA", "DOTNET_CLI_HOME",
            "XDG_CONFIG_HOME", "XDG_CACHE_HOME", "XDG_DATA_HOME", "XDG_STATE_HOME", "XDG_RUNTIME_DIR",
            "NUGET_PACKAGES", "NUGET_HTTP_CACHE_PATH", "NUGET_PLUGINS_CACHE_PATH", "NUGET_SCRATCH",
            "TEMP", "TMP", "TMPDIR",
        ];
        foreach (string key in isolatedKeys)
        {
            string isolatedPath = Path.Combine(directory, StateDirectoryName, key.ToLowerInvariant());
            Directory.CreateDirectory(isolatedPath);
            startInfo.Environment[key] = isolatedPath;
        }

        startInfo.Environment[Abstractions.Common.Constants.FuncHomeEnvironmentVariable] = home;
        startInfo.Environment[Constants.WorkloadsHomeEnvironmentVariable] = workloadsHome;
        startInfo.Environment[Constants.WorkloadsSourceEnvironmentVariable] = UnusedSource;
        startInfo.Environment[Constants.ProfilesCdnBaseUrlEnvironmentVariable] = "http://127.0.0.1:1/";
        startInfo.Environment[Constants.TelemetryOptOutEnvVar] = "1";
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        startInfo.Environment["NO_COLOR"] = "1";

        _output.WriteLine("CLI assembly: {0}", cliAssembly.Location);
        _output.WriteLine("CLI version/source: {0}; module: {1}",
            AssemblyCliVersionProvider.Instance.InformationalVersion, cliAssembly.ManifestModule.ModuleVersionId);
        _output.WriteLine("Command: func {0}; host: {1}; working directory: {2}", string.Join(" ", args), startInfo.FileName, project);
        _output.WriteLine("Isolated root: {0}; CLI home: {1}; workload home: {2}", directory, home, workloadsHome);

        var duration = Stopwatch.StartNew();
        try
        {
            using var execution = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            execution.CancelAfter(ExecutionTimeout);
            using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start the CLI test process.");
            process.StandardInput.Close();
            using StreamReader standardOutput = process.StandardOutput;
            using StreamReader standardError = process.StandardError;
            using CancellationTokenSource streams = new();
            Task<string> stdout = standardOutput.ReadToEndAsync(streams.Token);
            Task<string> stderr = standardError.ReadToEndAsync(streams.Token);
            try
            {
                await process.WaitForExitAsync(execution.Token);
                await Task.WhenAll(stdout, stderr).WaitAsync(execution.Token);
                return new CliResult(process.ExitCode, await stdout, await stderr, duration.Elapsed);
            }
            catch (OperationCanceledException ex) when (execution.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"CLI execution exceeded {ExecutionTimeout.TotalSeconds} seconds.", ex);
            }
            finally
            {
                using CancellationTokenSource cleanup = new(_cleanupTimeout);
                try
                {
                    if (!process.HasExited)
                    {
                        try
                        {
                            process.Kill(entireProcessTree: true);
                        }
                        catch (InvalidOperationException) when (process.HasExited)
                        {
                            // The owned process exited between the check and kill.
                        }

                        await process.WaitForExitAsync(cleanup.Token);
                    }

                    await Task.WhenAll(stdout, stderr).WaitAsync(cleanup.Token);
                }
                catch (OperationCanceledException ex) when (cleanup.IsCancellationRequested)
                {
                    throw new TimeoutException($"CLI process/stream cleanup exceeded {_cleanupTimeout.TotalSeconds} seconds.", ex);
                }
                finally
                {
                    streams.Cancel();
                    _output.WriteLine("stdout: {0}{1}stderr: {2}",
                        stdout.IsCompletedSuccessfully ? await stdout : $"<capture incomplete: {stdout.Status}>",
                        Environment.NewLine,
                        stderr.IsCompletedSuccessfully ? await stderr : $"<capture incomplete: {stderr.Status}>");
                }
            }
        }
        finally
        {
            duration.Stop();
            _output.WriteLine("CLI duration (including cleanup): {0:F0} ms; execution limit: {1} s; cleanup limit: {2} s.",
                duration.Elapsed.TotalMilliseconds, ExecutionTimeout.TotalSeconds, _cleanupTimeout.TotalSeconds);
        }
    }

    internal sealed record CliResult(int ExitCode, string StandardOutput, string StandardError, TimeSpan Duration);
}
