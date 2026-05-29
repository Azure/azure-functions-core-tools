// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Workloads.DotNet;

/// <summary>
/// A .NET Functions project detected from source (has a .csproj/.fsproj).
/// Requires building before host startup.
/// </summary>
internal sealed class DotNetSourceProject(WorkingDirectory workingDirectory, string projectFilePath, IDotnetCliRunner dotnetCli) : DotNetProject(workingDirectory)
{
    private readonly IDotnetCliRunner _dotnetCli = dotnetCli ?? throw new ArgumentNullException(nameof(dotnetCli));

    public string ProjectFilePath { get; } = projectFilePath ?? throw new ArgumentNullException(nameof(projectFilePath));

    public override async Task PrepareForHostRunAsync(FunctionsProjectHostRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        string projectDir = Path.GetDirectoryName(ProjectFilePath)
            ?? throw new GracefulException(
                $"Could not determine directory for project file '{ProjectFilePath}'.",
                isUserError: true);

        DirectoryInfo outputDirectory;

        if (!context.SkipBuild)
        {
            // Build and resolve output in one shot so the output path reflects the actual build result,
            // even when targets modify OutputPath/OutDir dynamically during the build.
            outputDirectory = await BuildAndGetOutputDirectoryAsync(projectDir, cancellationToken);
        }
        else
        {
            // No build requested; resolve the target path from the existing project state.
            outputDirectory = await GetTargetPathAsync(projectDir, cancellationToken);
        }

        context.StartupDirectory = outputDirectory;
    }

    private async Task<DirectoryInfo> BuildAndGetOutputDirectoryAsync(string projectDir, CancellationToken cancellationToken)
    {
        string json;
        try
        {
            json = await _dotnetCli.RunWithOutputAsync(
                ["build", ProjectFilePath, "--getTargetResult:Build"],
                projectDir,
                cancellationToken);
        }
        catch (DotnetCliException ex)
        {
            string detail = string.IsNullOrWhiteSpace(ex.StandardError) ? "see build output above." : ex.StandardError.Trim();
            throw new GracefulException(
                $"'dotnet build' failed (exit {ex.ExitCode}). {detail}",
                isUserError: true);
        }

        return ParseTargetResult(json.Trim(), "Build");
    }

    private async Task<DirectoryInfo> GetTargetPathAsync(string projectDir, CancellationToken cancellationToken)
    {
        string json = await _dotnetCli.RunWithOutputAsync(
            ["build", ProjectFilePath, "--getTargetResult:GetTargetPath"],
            projectDir,
            cancellationToken);

        return ParseTargetResult(json.Trim(), "GetTargetPath");
    }

    internal DirectoryInfo ParseTargetResult(string json, string targetName)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new GracefulException(
                $"Could not determine output directory for project '{Path.GetFileName(ProjectFilePath)}'. The '{targetName}' target produced no output.",
                isUserError: true);
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            if (root.TryGetProperty("TargetResults", out JsonElement targetResults)
                && targetResults.TryGetProperty(targetName, out JsonElement target)
                && target.TryGetProperty("Items", out JsonElement items)
                && items.GetArrayLength() > 0)
            {
                JsonElement firstItem = items[0];

                // Both Build and GetTargetPath targets return the assembly's full path as the item identity.
                string assemblyPath = firstItem.TryGetProperty("FullPath", out JsonElement fullPathElement)
                    ? fullPathElement.GetString() ?? string.Empty
                    : firstItem.GetProperty("Identity").GetString() ?? string.Empty;

                if (!string.IsNullOrWhiteSpace(assemblyPath))
                {
                    string dir = Path.GetDirectoryName(assemblyPath)!;
                    return new DirectoryInfo(dir);
                }
            }
        }
        catch (JsonException ex)
        {
            throw new GracefulException(
                $"Could not parse '{targetName}' target result for project '{Path.GetFileName(ProjectFilePath)}'. The output was not valid JSON.",
                ex,
                isUserError: true);
        }

        throw new GracefulException(
            $"Could not determine output directory for project '{Path.GetFileName(ProjectFilePath)}'. The '{targetName}' target result did not contain expected output paths.",
            isUserError: true);
    }
}
