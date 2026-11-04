// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Containers;
using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Workloads.Node;

internal sealed class NodeContainerArtifactProvider : IContainerArtifactProvider
{
    internal const string DefaultRuntimeVersion = "26";
    internal const string WorkerPackageId = "Microsoft.Azure.Functions.NodeJsWorker";
    internal const string WorkerPackageVersion = "3.16.0";
    internal const string WorkerPackageFeed =
        "https://azfunc.pkgs.visualstudio.com/e6a70c92-4128-439f-8012-382fe78d6396/_packaging/AzureFunctionsRelease/nuget/v3/index.json";
    internal const string WorkerPackageContentPath =
        "microsoft.azure.functions.nodejsworker/3.16.0/contentFiles/any/any/workers/node";

    internal static readonly IReadOnlyList<string> SupportedRuntimeVersions = ["18", "20", "22", "24", "26"];

    public bool Supports(FunctionsProject project, ContainerArtifactCapability capability) =>
        project is NodeFunctionsProject && capability == ContainerArtifactCapability.FlexByoc;

    public Task<ContainerArtifactResult> GenerateAsync(ContainerArtifactRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.Project is not NodeFunctionsProject project)
        {
            throw new ArgumentException("The resolved project is not a Node.js project.", nameof(request));
        }

        string runtimeVersion = ResolveRuntimeVersion(request.Parameters.GetValueOrDefault(ContainerArtifactRequest.RuntimeVersionParameterName));
        string language = ResolveLanguage(project.Language);
        string dockerfile = ReadTemplate($"FlexByoc/Dockerfile.{language}")
            .Replace("{{BUILD_IMAGE}}", $"node:{runtimeVersion}-bookworm-slim", StringComparison.Ordinal)
            .Replace("{{RUNTIME_IMAGE}}", $"node:{runtimeVersion}-bookworm-slim", StringComparison.Ordinal)
            .Replace("{{WORKER_PACKAGE_ID}}", WorkerPackageId, StringComparison.Ordinal)
            .Replace("{{WORKER_PACKAGE_VERSION}}", WorkerPackageVersion, StringComparison.Ordinal)
            .Replace("{{WORKER_PACKAGE_FEED}}", WorkerPackageFeed, StringComparison.Ordinal)
            .Replace("{{WORKER_PACKAGE_CONTENT_PATH}}", WorkerPackageContentPath, StringComparison.Ordinal);

        return Task.FromResult(new ContainerArtifactResult(
        [
            new("Dockerfile", dockerfile),
            new("entrypoint.sh", ReadTemplate("FlexByoc/entrypoint.sh").ReplaceLineEndings("\n")),
            new(".dockerignore", ReadTemplate("FlexByoc/dockerignore")),
        ]));
    }

    internal static string ResolveRuntimeVersion(string? requestedVersion)
    {
        string version = string.IsNullOrWhiteSpace(requestedVersion) ? DefaultRuntimeVersion : requestedVersion.Trim();
        if (!SupportedRuntimeVersions.Contains(version, StringComparer.OrdinalIgnoreCase))
        {
            throw new ContainerArtifactProviderException(
                $"Flex BYOC artifact generation does not support version '{requestedVersion}' for worker runtime 'Node.js'. " +
                $"Supported versions are: {string.Join(", ", SupportedRuntimeVersions)}.");
        }

        return version;
    }

    private static string ResolveLanguage(string language)
    {
        if (language.Equals("JavaScript", StringComparison.OrdinalIgnoreCase))
        {
            return "javascript";
        }

        if (language.Equals("TypeScript", StringComparison.OrdinalIgnoreCase))
        {
            return "typescript";
        }

        throw new ContainerArtifactProviderException(
            "Flex BYOC artifact generation requires a Node.js language of 'javascript' or 'typescript'.");
    }

    private static string ReadTemplate(string relativePath) =>
        ProjectFiles.ReadTemplate(typeof(NodeContainerArtifactProvider).Assembly, relativePath).ReplaceLineEndings("\n");
}
