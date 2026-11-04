// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Xml;
using System.Xml.Linq;
using Azure.Functions.Cli.Containers;
using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Workloads.DotNet;

internal sealed class DotNetContainerArtifactProvider(IDotNetProjectMetadataReader projectMetadataReader) : IContainerArtifactProvider
{
    internal const string DefaultRuntimeVersion = "11";

    internal static readonly IReadOnlyList<string> SupportedRuntimeVersions = ["8", "9", "10", "11"];

    private readonly IDotNetProjectMetadataReader _projectMetadataReader =
        projectMetadataReader ?? throw new ArgumentNullException(nameof(projectMetadataReader));

    public bool Supports(FunctionsProject project, ContainerArtifactCapability capability) =>
        project is DotNetSourceProject && capability == ContainerArtifactCapability.FlexByoc;

    public Task<ContainerArtifactResult> GenerateAsync(ContainerArtifactRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.Project is not DotNetSourceProject project)
        {
            throw new ArgumentException("The resolved project is not a .NET source project.", nameof(request));
        }

        string runtimeVersion = ResolveRuntimeVersion(GetRequestedRuntimeVersion(request));
        string projectFileName = Path.GetFileName(project.ProjectFilePath);
        string assemblyName;
        try
        {
            assemblyName = _projectMetadataReader.ReadAssemblyName(project.ProjectFilePath);
        }
        catch (IOException ex)
        {
            throw new ContainerArtifactProviderException($"Unable to read .NET project file '{projectFileName}'.", ex);
        }
        catch (XmlException ex)
        {
            throw new ContainerArtifactProviderException($"Unable to read .NET project file '{projectFileName}'.", ex);
        }

        string dockerfile = ReadTemplate("FlexByoc/Dockerfile")
            .Replace("{{BUILD_IMAGE}}", $"mcr.microsoft.com/dotnet/sdk:{runtimeVersion}.0-noble", StringComparison.Ordinal)
            .Replace("{{RUNTIME_IMAGE}}", $"mcr.microsoft.com/dotnet/aspnet:{runtimeVersion}.0-noble", StringComparison.Ordinal)
            .Replace("{{PROJECT_FILE}}", $"'./{projectFileName}'", StringComparison.Ordinal);
        string entrypoint = ReadTemplate("FlexByoc/entrypoint.sh")
            .Replace("{{APPLICATION_DLL}}", $"'/home/site/wwwroot/{assemblyName}.dll'", StringComparison.Ordinal)
            .ReplaceLineEndings("\n");

        return Task.FromResult(new ContainerArtifactResult(
        [
            new("Dockerfile", dockerfile),
            new("entrypoint.sh", entrypoint),
            new(".dockerignore", ReadTemplate("FlexByoc/dockerignore")),
        ]));
    }

    internal static string ResolveRuntimeVersion(string? requestedVersion)
    {
        string originalVersion = string.IsNullOrWhiteSpace(requestedVersion) ? DefaultRuntimeVersion : requestedVersion;
        string normalizedVersion = NormalizeRuntimeVersion(originalVersion);
        if (!SupportedRuntimeVersions.Contains(normalizedVersion, StringComparer.OrdinalIgnoreCase))
        {
            throw new ContainerArtifactProviderException(
                $"Flex BYOC artifact generation does not support version '{originalVersion}' for worker runtime '.NET isolated'. " +
                $"Supported versions are: {string.Join(", ", SupportedRuntimeVersions)}.");
        }

        return normalizedVersion;
    }

    private static string? GetRequestedRuntimeVersion(ContainerArtifactRequest request) =>
        request.Parameters.GetValueOrDefault(ContainerArtifactRequest.RuntimeVersionParameterName);

    private static string NormalizeRuntimeVersion(string runtimeVersion)
    {
        string normalizedVersion = runtimeVersion.Trim();
        if (normalizedVersion.StartsWith("net", StringComparison.OrdinalIgnoreCase))
        {
            normalizedVersion = normalizedVersion[3..];
        }

        if (Version.TryParse(normalizedVersion, out Version? version)
            && version.Minor == 0
            && version.Build <= 0
            && version.Revision <= 0)
        {
            return version.Major.ToString();
        }

        return normalizedVersion;
    }

    private static string ReadTemplate(string relativePath) =>
        ProjectFiles.ReadTemplate(typeof(DotNetContainerArtifactProvider).Assembly, relativePath).ReplaceLineEndings("\n");
}

internal interface IDotNetProjectMetadataReader
{
    public string ReadAssemblyName(string projectFilePath);
}

internal sealed class DotNetProjectMetadataReader : IDotNetProjectMetadataReader
{
    public string ReadAssemblyName(string projectFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFilePath);

        var project = XDocument.Load(projectFilePath);
        string? configuredName = project
            .Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "AssemblyName")
            ?.Value
            .Trim();

        return string.IsNullOrWhiteSpace(configuredName)
            ? Path.GetFileNameWithoutExtension(projectFilePath)
            : configuredName;
    }
}
