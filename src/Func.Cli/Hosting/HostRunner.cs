// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Console;

namespace Azure.Functions.Cli.Hosting;

/// <summary>
/// Launches the Azure Functions host as a child process.
/// Delegates host resolution to IHostResolver for version management.
/// </summary>
public class HostRunner : IHostRunner
{
    private readonly IInteractionService _interaction;
    private readonly IHostResolver _hostResolver;
    private readonly IHostManager? _hostManager;

    public HostRunner(IInteractionService interaction, IHostResolver hostResolver, IHostManager? hostManager = null)
    {
        _interaction = interaction;
        _hostResolver = hostResolver;
        _hostManager = hostManager;
    }

    public int Start(HostConfiguration config, CancellationToken cancellationToken = default)
    {
        // For .NET projects, build first and redirect script root to build output
        var effectiveScriptRoot = config.ScriptRoot;
        if (!config.NoBuild && IsDotNetProject(config.ScriptRoot))
        {
            if (!BuildDotNetProject(config.ScriptRoot))
            {
                return 1;
            }

            var buildOutput = FindDotNetBuildOutput(config.ScriptRoot);
            if (buildOutput is not null)
            {
                effectiveScriptRoot = buildOutput;
            }
        }
        else if (config.NoBuild && IsDotNetProject(config.ScriptRoot))
        {
            var buildOutput = FindDotNetBuildOutput(config.ScriptRoot);
            if (buildOutput is not null)
            {
                effectiveScriptRoot = buildOutput;
            }
        }

        if (effectiveScriptRoot != config.ScriptRoot)
        {
            config.UpdateScriptRoot(effectiveScriptRoot);
        }

        var resolution = _hostResolver.Resolve(config.ScriptRoot, config.HostVersion);

        // If no host found, offer to auto-install the recommended version
        if (resolution is null && _hostManager is not null && string.IsNullOrEmpty(config.HostVersion))
        {
            resolution = TryAutoInstallHost(config.ScriptRoot);
        }

        if (resolution is null)
        {
            return 1;
        }

        var environment = config.BuildEnvironment();

        // Check if Azurite is needed and offer to start it
        if (AzuriteManager.RequiresAzurite(environment) && !AzuriteManager.IsRunning())
        {
            var azurite = new AzuriteManager(_interaction);
            azurite.EnsureRunningAsync(cancellationToken).GetAwaiter().GetResult();
        }

        // If python3 was auto-detected, patch the worker.config.json in the host directory
        if (config.PythonExecutablePath is not null)
        {
            PatchPythonWorkerConfig(resolution.HostPath, config.PythonExecutablePath);
        }

        PrintStartupBanner(config, resolution);

        var startInfo = BuildProcessStartInfo(resolution.HostPath, config, environment);

        var outputHandler = new HostOutputHandler(_interaction, config.Port, config.Verbose);
        using var runner = new ProcessRunner(startInfo, _interaction, config.Verbose,
            outputFilter: outputHandler.ProcessLine);
        return runner.Run();
    }

    private HostResolution? TryAutoInstallHost(string scriptRoot)
    {
        var version = KnownHostVersions.RecommendedVersion;
        _interaction.WriteBlankLine();
        _interaction.WriteMarkupLine(
            $"[yellow]No host runtime found. Installing recommended version {version}...[/]");
        _interaction.WriteBlankLine();

        var progress = new Spectre.Console.Progress(Spectre.Console.AnsiConsole.Console);
        string? hostPath = null;

        progress.Start(ctx =>
        {
            var task = ctx.AddTask($"Installing host {version}", maxValue: 100);
            var reporter = new Progress<HostInstallProgress>(p =>
            {
                task.Description = p.Phase;
                if (p.Percentage.HasValue) task.Value = p.Percentage.Value;
            });

            hostPath = _hostManager!.InstallAsync(version, reporter, CancellationToken.None)
                .GetAwaiter().GetResult();
            task.Value = 100;
        });

        if (hostPath is null)
        {
            _interaction.WriteError("Auto-install failed. Install manually: func host install <version>");
            return null;
        }

        _hostManager!.SetDefaultVersion(version);
        _interaction.WriteMarkupLine($"[green]✓[/] Host {version} installed and set as default.");
        _interaction.WriteBlankLine();

        return _hostResolver.Resolve(scriptRoot, requestedVersion: null);
    }

    private static ProcessStartInfo BuildProcessStartInfo(
        string hostPath,
        HostConfiguration config,
        Dictionary<string, string> environment)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = config.ScriptRoot,
            UseShellExecute = false,
            RedirectStandardError = false,
        };

        startInfo.ArgumentList.Add("exec");

        var hostDir = Path.GetDirectoryName(hostPath) ?? "";

        var depsFile = Path.Combine(hostDir, NuGetHostManager.HostDepsFileName);
        if (File.Exists(depsFile))
        {
            startInfo.ArgumentList.Add("--depsfile");
            startInfo.ArgumentList.Add(depsFile);
        }

        var runtimeConfig = Path.Combine(hostDir, NuGetHostManager.HostRuntimeConfigFileName);
        if (File.Exists(runtimeConfig))
        {
            startInfo.ArgumentList.Add("--runtimeconfig");
            startInfo.ArgumentList.Add(runtimeConfig);
        }

        startInfo.ArgumentList.Add(hostPath);

        foreach (var (key, value) in environment)
        {
            startInfo.Environment[key] = value;
        }

        var workersDir = Path.Combine(hostDir, "workers");
        if (!Directory.Exists(workersDir))
        {
            Directory.CreateDirectory(workersDir);
        }

        return startInfo;
    }

    private void PrintStartupBanner(HostConfiguration config, HostResolution resolution)
    {
        _interaction.WriteBlankLine();
        _interaction.WriteMarkupLine($"[bold blue]{Constants.ProductName}[/]");
        _interaction.WriteBlankLine();

        var versionDisplay = resolution.Version ?? "unknown";
        _interaction.WriteMarkupLine($"[grey]Host version:        {Spectre.Console.Markup.Escape(versionDisplay)} ({Spectre.Console.Markup.Escape(resolution.Source)})[/]");
        _interaction.WriteMarkupLine($"[grey]Function app path:   {Spectre.Console.Markup.Escape(config.ScriptRoot)}[/]");

        if (config.EnableAuth)
        {
            _interaction.WriteMarkupLine("[yellow]Authentication:      Enabled (function keys required)[/]");
        }

        _interaction.WriteBlankLine();

        if (config.FunctionsFilter is { Length: > 0 })
        {
            _interaction.WriteMarkupLine($"[yellow]Functions filter:[/] [grey]{string.Join(", ", config.FunctionsFilter)}[/]");
        }

        if (!string.IsNullOrEmpty(config.CorsOrigins))
        {
            _interaction.WriteMarkupLine($"[yellow]CORS origins:[/] [grey]{Spectre.Console.Markup.Escape(config.CorsOrigins)}[/]");
        }

        _interaction.WriteMarkupLine($"Listening on [green]http://localhost:{config.Port}[/]");
        _interaction.WriteBlankLine();
    }

    private static void PatchPythonWorkerConfig(string hostPath, string pythonExecutable)
    {
        var hostDir = Path.GetDirectoryName(hostPath) ?? "";
        var workerConfigPath = Path.Combine(hostDir, "workers", "python", "worker.config.json");

        if (!File.Exists(workerConfigPath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(workerConfigPath);
            var patched = System.Text.RegularExpressions.Regex.Replace(
                json,
                @"""defaultExecutablePath""\s*:\s*""[^""]*""",
                $@"""defaultExecutablePath"":""{pythonExecutable}""");

            if (patched != json)
            {
                File.WriteAllText(workerConfigPath, patched);
            }
        }
        catch (IOException)
        {
            // Best effort
        }
    }

    private static bool IsDotNetProject(string scriptRoot)
    {
        return Directory.EnumerateFiles(scriptRoot, "*.csproj").Any() ||
               Directory.EnumerateFiles(scriptRoot, "*.fsproj").Any();
    }

    private bool BuildDotNetProject(string scriptRoot)
    {
        _interaction.WriteMarkupLine("[grey]Building .NET project...[/]");

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = scriptRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("build");
        startInfo.ArgumentList.Add("--nologo");

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                _interaction.WriteError("Failed to start 'dotnet build'.");
                return false;
            }

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                _interaction.WriteError("Build failed.");
                if (!string.IsNullOrWhiteSpace(stdout))
                {
                    System.Console.Error.Write(stdout);
                }
                if (!string.IsNullOrWhiteSpace(stderr))
                {
                    System.Console.Error.Write(stderr);
                }
                return false;
            }

            _interaction.WriteMarkupLine("[green]✓[/] Build succeeded.");
            return true;
        }
        catch (Exception ex)
        {
            _interaction.WriteError($"Failed to build .NET project: {ex.Message}");
            return false;
        }
    }

    private static string? FindDotNetBuildOutput(string scriptRoot)
    {
        // Ask MSBuild for the actual output path (respects Directory.Build.props, ArtifactsPath, etc.)
        var msbuildOutput = QueryMSBuildProperty(scriptRoot, "TargetDir");
        if (!string.IsNullOrEmpty(msbuildOutput) && Directory.Exists(msbuildOutput))
        {
            return msbuildOutput;
        }

        // Fallback: search for .azurefunctions marker in bin/
        var binDir = Path.Combine(scriptRoot, "bin");
        if (Directory.Exists(binDir))
        {
            try
            {
                var candidates = Directory.GetDirectories(binDir, ".azurefunctions", SearchOption.AllDirectories);
                if (candidates.Length > 0)
                {
                    return Path.GetDirectoryName(candidates[0]);
                }
            }
            catch (IOException)
            {
                // Best effort
            }
        }

        return null;
    }

    private static string? QueryMSBuildProperty(string scriptRoot, string propertyName)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                WorkingDirectory = scriptRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("msbuild");
            startInfo.ArgumentList.Add($"--getProperty:{propertyName}");

            using var process = Process.Start(startInfo);
            if (process is null) return null;

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();

            return process.ExitCode == 0 && !string.IsNullOrEmpty(output) ? output : null;
        }
        catch
        {
            return null;
        }
    }
}
