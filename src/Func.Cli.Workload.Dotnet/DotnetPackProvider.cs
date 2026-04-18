// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Workloads;

namespace Azure.Functions.Cli.Workload.Dotnet;

/// <summary>
/// Pack provider for .NET Functions projects. Runs 'dotnet publish' to produce
/// the deployment-ready output, then returns the publish directory for zipping
/// by the base PackCommand.
/// </summary>
public class DotnetPackProvider : IPackProvider
{
    private readonly IDotnetCliRunner _dotnetCli;

    public DotnetPackProvider(IDotnetCliRunner dotnetCli)
    {
        _dotnetCli = dotnetCli;
    }

    public string WorkerRuntime => "dotnet";

    public Task ValidateAsync(PackContext context, CancellationToken cancellationToken = default)
    {
        var projectFiles = Directory.EnumerateFiles(context.ProjectPath, "*.csproj")
            .Concat(Directory.EnumerateFiles(context.ProjectPath, "*.fsproj"))
            .ToList();

        if (!context.NoBuild && projectFiles.Count == 0)
        {
            throw new GracefulException(
                "No .csproj or .fsproj file found in the project directory.",
                "Ensure you are in the root of a .NET Functions project.");
        }

        return Task.CompletedTask;
    }

    public async Task<string> PrepareAsync(PackContext context, CancellationToken cancellationToken = default)
    {
        if (context.NoBuild)
        {
            // When --no-build, the project path itself is the packing root.
            // The user is expected to have already run 'dotnet publish'.
            return context.ProjectPath;
        }

        var publishOutput = Path.Combine(context.ProjectPath, "publish_output");

        // Clean the output directory if it exists from a previous run
        if (Directory.Exists(publishOutput))
        {
            Directory.Delete(publishOutput, recursive: true);
        }

        var result = await _dotnetCli.RunAsync(
            $"publish --output \"{publishOutput}\" --configuration Release",
            workingDirectory: context.ProjectPath,
            cancellationToken: cancellationToken);

        if (!result.IsSuccess)
        {
            // dotnet CLI writes some errors to stdout (e.g., restore failures)
            var details = !string.IsNullOrWhiteSpace(result.StandardError)
                ? result.StandardError.Trim()
                : result.StandardOutput.Trim();

            throw new GracefulException(
                "Failed to build the .NET project.",
                details);
        }

        return publishOutput;
    }

    public Task CleanupAsync(PackContext context, string packingRoot, CancellationToken cancellationToken = default)
    {
        // Clean up the temporary publish output directory (only if we created it)
        if (!context.NoBuild && Directory.Exists(packingRoot)
            && packingRoot.EndsWith("publish_output", StringComparison.OrdinalIgnoreCase))
        {
            Directory.Delete(packingRoot, recursive: true);
        }

        return Task.CompletedTask;
    }
}
