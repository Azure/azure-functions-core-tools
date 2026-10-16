// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

#if FUNC_HOST_DOTNET_TEMPLATES
namespace Azure.Functions.Cli.Templates.DotNet.Shared;
#else
namespace Azure.Functions.Cli.Workloads.DotNet;
#endif

/// <summary>
/// Executes qualified .NET dependency actions against an explicitly resolved project, without dispatching template commands.
/// </summary>
internal sealed class DotNetTemplateDependencyExecutor(IDotnetCliRunner dotnetCli)
{
    private readonly IDotnetCliRunner _dotnetCli = dotnetCli ?? throw new ArgumentNullException(nameof(dotnetCli));

    /// <summary>
    /// Adds the exact native package reference, preserving the SDK's implicit restore during package addition.
    /// </summary>
    /// <exception cref="ArgumentException">The target is not an absolute matching C# or F# project path.</exception>
    /// <exception cref="ArgumentNullException">The package reference is null.</exception>
    /// <exception cref="DotnetCliException">Package addition or its implicit restore failed.</exception>
    public Task AddPackageReferenceAsync(string projectFilePath, DotNetTemplatePackageReference reference, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reference);
        string target = ValidateProjectPath(projectFilePath);
        if (!string.Equals(Path.GetExtension(target), reference.ProjectFileExtension, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The resolved project does not match the native action's project extension.", nameof(projectFilePath));
        }

        cancellationToken.ThrowIfCancellationRequested();
        return _dotnetCli.RunAsync(["add", target, "package", reference.PackageId, "--version", reference.Version],
            Path.GetDirectoryName(target), cancellationToken);
    }

    /// <summary>
    /// Restores the exact project selected by the qualified dispatcher.
    /// </summary>
    /// <exception cref="ArgumentException">The target is not an absolute C# or F# project path.</exception>
    /// <exception cref="DotnetCliException">Restore failed.</exception>
    public Task RestoreAsync(string projectFilePath, CancellationToken cancellationToken)
    {
        string target = ValidateProjectPath(projectFilePath);
        cancellationToken.ThrowIfCancellationRequested();
        return _dotnetCli.RunAsync(["restore", target], Path.GetDirectoryName(target), cancellationToken);
    }

    internal static string ValidateProjectPath(string projectFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFilePath);
        if (!Path.IsPathFullyQualified(projectFilePath)
            || !(Path.GetExtension(projectFilePath).Equals(".csproj", StringComparison.OrdinalIgnoreCase)
                || Path.GetExtension(projectFilePath).Equals(".fsproj", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("Native .NET dependency actions require an absolute C# or F# project path.", nameof(projectFilePath));
        }

        return Path.GetFullPath(projectFilePath);
    }
}
