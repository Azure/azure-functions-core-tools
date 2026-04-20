// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.IO.Compression;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Workloads;

namespace Azure.Functions.Cli.Commands;

/// <summary>
/// Packages an Azure Functions project into a zip ready for deployment.
/// Delegates to workload-provided pack providers for runtime-specific
/// build and validation logic.
/// </summary>
public class PackCommand : BaseCommand
{
    public static readonly Option<string?> OutputOption = new("--output", "-o")
    {
        Description = "The directory to place the output zip file in"
    };

    public static readonly Option<bool> NoBuildOption = new("--no-build")
    {
        Description = "Skip building the project before packaging"
    };

    private readonly IInteractionService _interaction;
    private readonly IWorkloadManager? _workloadManager;

    public PackCommand(IInteractionService interaction, IWorkloadManager? workloadManager = null)
        : base("pack", "Package the function app into a zip ready for deployment.")
    {
        _interaction = interaction;
        _workloadManager = workloadManager;

        AddPathArgument();
        Options.Add(OutputOption);
        Options.Add(NoBuildOption);

        RegisterWorkloadOptions();
    }

    protected override async Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        ApplyPath(parseResult);

        var projectPath = Directory.GetCurrentDirectory();

        // Verify this is a Functions project
        if (!File.Exists(Path.Combine(projectPath, "host.json")))
        {
            _interaction.WriteError("No Azure Functions project found. Run func init first.");
            return 1;
        }

        var outputPath = parseResult.GetValue(OutputOption);
        var noBuild = parseResult.GetValue(NoBuildOption);

        // Detect the runtime
        var detectedRuntime = ProjectDetector.DetectRuntime(projectPath);
        if (detectedRuntime is null)
        {
            _interaction.WriteError("Could not detect the worker runtime for this project.");
            _interaction.WriteMarkupLine("[grey]Ensure the project contains the expected project files (e.g., .csproj, package.json).[/]");
            return 1;
        }

        // Find the matching pack provider
        var providers = _workloadManager?.GetAllPackProviders() ?? [];
        var provider = providers.FirstOrDefault(p =>
            p.WorkerRuntime.Equals(detectedRuntime, StringComparison.OrdinalIgnoreCase));

        if (provider is null)
        {
            _interaction.WriteError($"No pack provider for runtime '{detectedRuntime}'.");
            _interaction.WriteMarkupLine(
                $"[grey]Install the workload:[/] [white]func workload install {detectedRuntime}[/]");
            return 1;
        }

        var context = new PackContext(
            ProjectPath: projectPath,
            OutputPath: outputPath,
            NoBuild: noBuild);

        // Validate
        await provider.ValidateAsync(context, cancellationToken);

        // Build / prepare — returns the directory to zip
        string packingRoot = "";
        await _interaction.StatusAsync(
            noBuild ? "Preparing package..." : "Building and preparing package...",
            async ct =>
            {
                packingRoot = await provider.PrepareAsync(context, ct);
            },
            cancellationToken);

        // Zip the output
        var zipPath = ResolveOutputZipPath(projectPath, outputPath);
        await _interaction.StatusAsync(
            "Creating deployment package...",
            async ct =>
            {
                await CreateZipAsync(packingRoot, zipPath, ct);
            },
            cancellationToken);

        // Cleanup
        await provider.CleanupAsync(context, packingRoot, cancellationToken);

        var fileInfo = new FileInfo(zipPath);
        var sizeMb = fileInfo.Length / (1024.0 * 1024.0);
        _interaction.WriteSuccess($"Package created: {zipPath} ({sizeMb:F1} MB)");

        return 0;
    }

    /// <summary>
    /// Registers workload-contributed options for the pack command.
    /// </summary>
    private void RegisterWorkloadOptions()
    {
        var providers = _workloadManager?.GetAllPackProviders() ?? [];
        var registeredOptions = new HashSet<string>(
            Options.Select(o => o.Name));

        foreach (var provider in providers)
        {
            foreach (var option in provider.GetPackOptions())
            {
                if (registeredOptions.Add(option.Name))
                {
                    Options.Add(option);
                }
            }
        }
    }

    /// <summary>
    /// Resolves the output zip file path. The --output option specifies the
    /// directory to place the zip in; the zip file name is always derived
    /// from the project folder name.
    /// </summary>
    private static string ResolveOutputZipPath(string projectPath, string? outputDir)
    {
        var projectName = Path.GetFileName(projectPath);
        var zipFileName = $"{projectName}.zip";

        if (!string.IsNullOrEmpty(outputDir))
        {
            var resolvedDir = Path.IsPathRooted(outputDir)
                ? outputDir
                : Path.Combine(projectPath, outputDir);

            Directory.CreateDirectory(resolvedDir);
            return Path.Combine(resolvedDir, zipFileName);
        }

        return Path.Combine(projectPath, zipFileName);
    }

    /// <summary>
    /// Creates a zip archive from the specified directory.
    /// </summary>
    private static Task CreateZipAsync(string sourceDirectory, string zipPath, CancellationToken cancellationToken)
    {
        // Remove existing zip if present
        if (File.Exists(zipPath))
        {
            File.Delete(zipPath);
        }

        ZipFile.CreateFromDirectory(sourceDirectory, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
        return Task.CompletedTask;
    }
}
