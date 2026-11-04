// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Containers;
using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Workloads.Python;

internal sealed class PythonContainerArtifactProvider : IContainerArtifactProvider
{
    internal const string DefaultRuntimeVersion = "3.14";
    internal const string WorkerPackageId = "Microsoft.Azure.Functions.PythonWorker";
    internal const string WorkerPackageVersion = "4.46.0";
    internal const string WorkerPackageFeed =
        "https://azfunc.pkgs.visualstudio.com/e6a70c92-4128-439f-8012-382fe78d6396/_packaging/AzureFunctionsRelease/nuget/v3/index.json";
    internal const string WorkerPackageContentPath = "microsoft.azure.functions.pythonworker/4.46.0/tools";

    internal static readonly IReadOnlyList<string> SupportedRuntimeVersions = ["3.11", "3.12", "3.13", "3.14"];

    public bool Supports(FunctionsProject project, ContainerArtifactCapability capability) =>
        project is PythonFunctionsProject && capability == ContainerArtifactCapability.FlexByoc;

    public Task<ContainerArtifactResult> GenerateAsync(ContainerArtifactRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.Project is not PythonFunctionsProject)
        {
            throw new ArgumentException("The resolved project is not a Python project.", nameof(request));
        }

        string runtimeVersion = ResolveRuntimeVersion(request.Parameters.GetValueOrDefault(ContainerArtifactRequest.RuntimeVersionParameterName));
        string dockerfile = ReadTemplate("FlexByoc/Dockerfile")
            .Replace("{{BUILD_IMAGE}}", $"python:{runtimeVersion}-slim-bookworm", StringComparison.Ordinal)
            .Replace("{{RUNTIME_IMAGE}}", $"python:{runtimeVersion}-slim-bookworm", StringComparison.Ordinal)
            .Replace("{{RUNTIME_VERSION}}", runtimeVersion, StringComparison.Ordinal)
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
                $"Flex BYOC artifact generation does not support version '{requestedVersion}' for worker runtime 'Python'. " +
                $"Supported versions are: {string.Join(", ", SupportedRuntimeVersions)}.");
        }

        return version;
    }

    private static string ReadTemplate(string relativePath) =>
        ProjectFiles.ReadTemplate(typeof(PythonContainerArtifactProvider).Assembly, relativePath).ReplaceLineEndings("\n");
}
