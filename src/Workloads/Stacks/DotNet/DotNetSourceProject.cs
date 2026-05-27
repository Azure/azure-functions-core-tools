// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

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
            ?? throw new InvalidOperationException(
                $"Could not determine directory for project file '{ProjectFilePath}'.");

        if (!context.SkipBuild)
        {
            await BuildAsync(projectDir, cancellationToken);
        }

        DirectoryInfo outputDirectory = await GetOutputDirectoryAsync(projectDir, cancellationToken);
        context.StartupDirectory = outputDirectory;
    }

    private async Task<DirectoryInfo> GetOutputDirectoryAsync(string projectDir, CancellationToken cancellationToken)
    {
        string output = await _dotnetCli.RunWithOutputAsync(
            ["msbuild", ProjectFilePath, "--getProperty:OutputPath"],
            projectDir,
            cancellationToken);

        string outputPath = output.Trim();
        if (string.IsNullOrEmpty(outputPath))
        {
            throw new InvalidOperationException(
                $"Could not determine OutputPath for project '{Path.GetFileName(ProjectFilePath)}'.");
        }

        // OutputPath may be relative to the project directory.
        string fullPath = Path.IsPathRooted(outputPath)
            ? outputPath
            : Path.GetFullPath(outputPath, projectDir);

        return new DirectoryInfo(fullPath);
    }

    private async Task BuildAsync(string projectDir, CancellationToken cancellationToken)
    {
        await _dotnetCli.RunAsync(
            ["build", ProjectFilePath],
            projectDir,
            cancellationToken);
    }
}
