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

    public override string? Language => Path.GetExtension(ProjectFilePath).Equals(".fsproj", StringComparison.OrdinalIgnoreCase)
        ? "F#"
        : "C#";

    public override async Task PrepareForHostRunAsync(FunctionsProjectHostRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        string projectDir = Path.GetDirectoryName(ProjectFilePath)
            ?? throw new GracefulException($"Could not determine directory for project file '{ProjectFilePath}'.", isUserError: true);

        // Both paths run `dotnet build` and read an MSBuild target result as JSON; only the requested
        // target differs. The default build runs the "Build" target (compiles, then reports the actual
        // output even when targets adjust OutputPath/OutDir dynamically); --no-build runs "GetTargetPath"
        // to resolve the existing output without compiling. The output JSON shape is the same, so a single
        // code path handles both.

        string targetName = context.SkipBuild ? "GetTargetPath" : "Build";

        context.Reporter.ReportStatus(context.SkipBuild ? "Resolving .NET build output" : "Running dotnet build");

        context.StartupDirectory = await BuildAndGetOutputDirectoryAsync(projectDir, targetName, context.SkipBuild, context.Reporter, cancellationToken);
    }

    private async Task<DirectoryInfo> BuildAndGetOutputDirectoryAsync(string projectDir, string targetName, bool requireAssemblyExists, IFunctionsProjectHostRunReporter reporter, CancellationToken cancellationToken)
    {
        // Stream the build output live (stdout as informational, stderr as error) while MSBuild writes the
        // structured target result to a temp file via --getResultOutputFile. In this mode the human-readable
        // build console output (including compiler errors) goes to stdout, leaving stdout free of the JSON we
        // need for path resolution.
        string resultFile = Path.Combine(Path.GetTempPath(), $"func-dotnet-build-{Guid.NewGuid():N}.json");

        try
        {
            try
            {
                await _dotnetCli.RunStreamingAsync(
                    ["build", ProjectFilePath, $"--getTargetResult:{targetName}", $"--getResultOutputFile:{resultFile}"],
                    projectDir,
                    line => reporter.WriteLog(line, FunctionsProjectReportSeverity.Info),
                    line => reporter.WriteLog(line, FunctionsProjectReportSeverity.Error),
                    cancellationToken);
            }
            catch (DotnetCliException ex)
            {
                // The build output has already been streamed live through the callbacks above, so point the
                // user there instead of repeating it in the message.
                throw new GracefulException($"'dotnet build' failed (exit {ex.ExitCode}).", isUserError: true);
            }

            string json = File.Exists(resultFile)
                ? await File.ReadAllTextAsync(resultFile, cancellationToken)
                : string.Empty;

            return ParseTargetResult(json.Trim(), targetName, requireAssemblyExists);
        }
        finally
        {
            TryDeleteFile(resultFile);
        }
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

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
