// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Containers;
using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Workloads.Java;

internal sealed class JavaContainerArtifactProvider : IContainerArtifactProvider
{
    internal const string DefaultRuntimeVersion = "25";
    internal const string WorkerPackageId = "Microsoft.Azure.Functions.JavaWorker";
    internal const string WorkerPackageVersion = "2.19.5";
    internal const string WorkerPackageFeed =
        "https://azfunc.pkgs.visualstudio.com/e6a70c92-4128-439f-8012-382fe78d6396/_packaging/AzureFunctionsRelease/nuget/v3/index.json";
    internal const string WorkerPackageContentPath =
        "microsoft.azure.functions.javaworker/2.19.5/contentFiles/any/any/workers/java";

    internal static readonly IReadOnlyList<string> SupportedRuntimeVersions = ["21", "25"];

    public bool Supports(FunctionsProject project, ContainerArtifactCapability capability) =>
        project is JavaFunctionsProject && capability == ContainerArtifactCapability.FlexByoc;

    public Task<ContainerArtifactResult> GenerateAsync(ContainerArtifactRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.Project is not JavaFunctionsProject)
        {
            throw new ArgumentException("The resolved project is not a Java project.", nameof(request));
        }

        string runtimeVersion = ResolveRuntimeVersion(request.Parameters.GetValueOrDefault(ContainerArtifactRequest.RuntimeVersionParameterName));
        string dockerfile = ReadTemplate("FlexByoc/Dockerfile")
            .Replace("{{BUILD_IMAGE}}", $"maven:3.9-eclipse-temurin-{runtimeVersion}", StringComparison.Ordinal)
            .Replace("{{RUNTIME_IMAGE}}", $"mcr.microsoft.com/openjdk/jdk:{runtimeVersion}-ubuntu", StringComparison.Ordinal)
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
                $"Flex BYOC artifact generation does not support version '{requestedVersion}' for worker runtime 'Java'. " +
                $"Supported versions are: {string.Join(", ", SupportedRuntimeVersions)}.");
        }

        return version;
    }

    private static string ReadTemplate(string relativePath) =>
        ProjectFiles.ReadTemplate(typeof(JavaContainerArtifactProvider).Assembly, relativePath).ReplaceLineEndings("\n");
}
