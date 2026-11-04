// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Containers;
using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Workloads.PowerShell;

internal sealed class PowerShellContainerArtifactProvider : IContainerArtifactProvider
{
    internal const string DefaultRuntimeVersion = "7.6";
    internal const string PowerShellWorkerFeed =
        "https://azfunc.pkgs.visualstudio.com/e6a70c92-4128-439f-8012-382fe78d6396/_packaging/Microsoft.Azure.Functions.PowerShellWorker/nuget/v3/index.json";
    internal const string InfrastructureFeed = "https://pkgs.dev.azure.com/azfunc/public/_packaging/infra/nuget/v3/index.json";

    internal static readonly IReadOnlyList<string> SupportedRuntimeVersions = ["7.2", "7.4", "7.6"];

    public bool Supports(FunctionsProject project, ContainerArtifactCapability capability) =>
        project is PowerShellFunctionsProject && capability == ContainerArtifactCapability.FlexByoc;

    public Task<ContainerArtifactResult> GenerateAsync(ContainerArtifactRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.Project is not PowerShellFunctionsProject)
        {
            throw new ArgumentException("The resolved project is not a PowerShell project.", nameof(request));
        }

        string runtimeVersion = ResolveRuntimeVersion(request.Parameters.GetValueOrDefault(ContainerArtifactRequest.RuntimeVersionParameterName));
        (string workerVersion, string workerFeed) = ResolveWorkerPackage(runtimeVersion);
        string workerPackageId = $"Microsoft.Azure.Functions.PowerShellWorker.PS{runtimeVersion}";
        string workerContentPath =
            $"{workerPackageId.ToLowerInvariant()}/{workerVersion}/contentFiles/any/any/workers/powershell";
        string runtimeImage = runtimeVersion == "7.6"
            ? "mcr.microsoft.com/powershell:preview-7.6-ubuntu-24.04"
            : $"mcr.microsoft.com/powershell:{runtimeVersion}-ubuntu-22.04";
        string dockerfile = ReadTemplate("FlexByoc/Dockerfile")
            .Replace("{{RUNTIME_IMAGE}}", runtimeImage, StringComparison.Ordinal)
            .Replace("{{RUNTIME_VERSION}}", runtimeVersion, StringComparison.Ordinal)
            .Replace("{{WORKER_PACKAGE_ID}}", workerPackageId, StringComparison.Ordinal)
            .Replace("{{WORKER_PACKAGE_VERSION}}", workerVersion, StringComparison.Ordinal)
            .Replace("{{WORKER_PACKAGE_FEED}}", workerFeed, StringComparison.Ordinal)
            .Replace("{{WORKER_PACKAGE_CONTENT_PATH}}", workerContentPath, StringComparison.Ordinal);

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
                $"Flex BYOC artifact generation does not support version '{requestedVersion}' for worker runtime 'PowerShell'. " +
                $"Supported versions are: {string.Join(", ", SupportedRuntimeVersions)}.");
        }

        return version;
    }

    private static (string Version, string Feed) ResolveWorkerPackage(string runtimeVersion) => runtimeVersion switch
    {
        "7.2" => ("4.0.4025", PowerShellWorkerFeed),
        "7.4" => ("4.0.5361", InfrastructureFeed),
        "7.6" => ("4.0.5362", InfrastructureFeed),
        _ => throw new InvalidOperationException($"Unexpected PowerShell runtime version '{runtimeVersion}'."),
    };

    private static string ReadTemplate(string relativePath) =>
        ProjectFiles.ReadTemplate(typeof(PowerShellContainerArtifactProvider).Assembly, relativePath).ReplaceLineEndings("\n");
}
