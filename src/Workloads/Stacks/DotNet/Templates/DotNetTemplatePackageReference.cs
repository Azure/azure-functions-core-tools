// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.RegularExpressions;
using NuGet.Versioning;

#if FUNC_HOST_DOTNET_TEMPLATES
namespace Azure.Functions.Cli.Templates.DotNet.Shared;
#else
namespace Azure.Functions.Cli.Workloads.DotNet;
#endif

/// <summary>
/// Describes a pinned native package-reference action for a C# or F# project.
/// </summary>
internal sealed record DotNetTemplatePackageReference
{
    public DotNetTemplatePackageReference(string packageId, string version, string projectFileExtension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFileExtension);
        if (!Regex.IsMatch(packageId, @"\A[A-Za-z0-9_][A-Za-z0-9_.-]{0,99}\z", RegexOptions.CultureInvariant))
        {
            throw new ArgumentException("A native package reference requires a literal NuGet package ID.", nameof(packageId));
        }

        if (!NuGetVersion.TryParse(version, out _))
        {
            throw new ArgumentException("A native package reference requires an exact NuGet version.", nameof(version));
        }

        if (projectFileExtension is not (".csproj" or ".fsproj"))
        {
            throw new ArgumentException("A native package reference requires a C# or F# project extension.", nameof(projectFileExtension));
        }

        PackageId = packageId;
        Version = version;
        ProjectFileExtension = projectFileExtension;
    }

    public string PackageId { get; }

    public string Version { get; }

    public string ProjectFileExtension { get; }
}
