// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using System.Text.Json.Nodes;
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

        // Both paths run `dotnet build` and read an MSBuild target result as JSON; only the requested
        // target differs. The default build runs the "Build" target (compiles, then reports the actual
        // output even when targets adjust OutputPath/OutDir dynamically); --no-build runs "GetTargetPath"
        // to resolve the existing output without compiling. The output JSON shape is the same, so a single
        // code path handles both.

        string targetName = context.SkipBuild ? "GetTargetPath" : "Build";

        context.StartupDirectory = await BuildAndGetOutputDirectoryAsync(projectDir, targetName, context.SkipBuild, cancellationToken);
    }

    private async Task<DirectoryInfo> BuildAndGetOutputDirectoryAsync(string projectDir, string targetName, bool requireAssemblyExists, CancellationToken cancellationToken)
    {
        string json;
        try
        {
            json = await _dotnetCli.RunWithOutputAsync(
                ["build", ProjectFilePath, $"--getTargetResult:{targetName}"],
                projectDir,
                cancellationToken);
        }
        catch (DotnetCliException ex)
        {
            string detail = string.IsNullOrWhiteSpace(ex.StandardError) ? "see build output above." : ex.StandardError.Trim();

            throw new GracefulException($"'dotnet build' failed (exit {ex.ExitCode}). {detail}", isUserError: true);
        }

        return ParseTargetResult(json.Trim(), targetName, requireAssemblyExists);
    }

    internal DirectoryInfo ParseTargetResult(string json, string targetName, bool requireAssemblyExists = false)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new GracefulException(
                $"Could not determine output directory for project '{Path.GetFileName(ProjectFilePath)}'. The '{targetName}' target produced no output.",
                isUserError: true);
        }

        try
        {
            var root = JsonNode.Parse(json);
            JsonArray? items = root?["TargetResults"]?[targetName]?["Items"]?.AsArray();

            if (items is { Count: > 0 })
            {
                JsonNode firstItem = items[0]!;
                string assemblyPath =
                    firstItem["FullPath"]?.GetValue<string>()
                    ?? firstItem["Identity"]?.GetValue<string>()
                    ?? string.Empty;

                if (!string.IsNullOrWhiteSpace(assemblyPath))
                {
                    // With --no-build we only resolve the target path; nothing compiled it. If the
                    // assembly isn't actually present, fail here with an actionable message instead of
                    // letting the host fail later with a more cryptic "worker/app not found" error.
                    if (requireAssemblyExists && !File.Exists(assemblyPath))
                    {
                        throw new GracefulException(
                            $"Could not find the build output for project '{Path.GetFileName(ProjectFilePath)}' at '{assemblyPath}'. Build the project before running with --no-build, or omit --no-build to build automatically.",
                            isUserError: true);
                    }

                    return new DirectoryInfo(Path.GetDirectoryName(assemblyPath)!);
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
