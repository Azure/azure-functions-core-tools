// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using static Azure.Functions.Cli.Projects.ProjectCreationResults;

namespace Azure.Functions.Cli.Workloads.PowerShell;

/// <summary>
/// Creates PowerShell Functions projects from PowerShell-specific fingerprints.
/// </summary>
internal sealed class PowerShellProjectFactory : IFunctionsProjectFactory
{
    public Task<ProjectCreationResult> TryCreateProjectAsync(ProjectCreationContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        DirectoryInfo workingDirectory = context.WorkingDirectory.Info;
        if (!workingDirectory.Exists)
        {
            return Task.FromResult(NotCreated("directory does not exist"));
        }

        string? reason = TryGetReason(workingDirectory);
        if (reason is null)
        {
            return Task.FromResult(NotCreated("no PowerShell project fingerprint found"));
        }

        FunctionsProject project = new PowerShellFunctionsProject(context.WorkingDirectory);
        return Task.FromResult(Created(project, reason));
    }

    private static string? TryGetReason(DirectoryInfo workingDirectory)
    {
        string root = workingDirectory.FullName;

        // profile.ps1 is the strongest signal for a PowerShell Functions project.
        if (File.Exists(Path.Combine(root, "profile.ps1")))
        {
            return "found profile.ps1";
        }

        // requirements.psd1 declares managed dependencies for PowerShell Functions.
        if (File.Exists(Path.Combine(root, "requirements.psd1")))
        {
            return "found requirements.psd1";
        }

        // Fallback: any *.ps1 at the project root. Filter by exact extension
        // because on Windows the 3-char pattern also matches .ps1xml files.
        if (Directory.EnumerateFiles(root, "*.ps1", SearchOption.TopDirectoryOnly)
                .Any(f => Path.GetExtension(f).Equals(".ps1", StringComparison.OrdinalIgnoreCase)))
        {
            return "found *.ps1 file";
        }

        // A Modules folder is a common PowerShell Functions convention.
        if (Directory.Exists(Path.Combine(root, "Modules")))
        {
            return "found Modules folder";
        }

        return null;
    }
}
