// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Extensions;
using Azure.Functions.Cli.Helpers;

namespace Azure.Functions.Cli.Actions.LocalActions
{
    internal static class FlexByocArtifactGenerator
    {
        private static readonly IReadOnlyDictionary<WorkerRuntime, HashSet<string>> _supportedVersions =
            new Dictionary<WorkerRuntime, HashSet<string>>
            {
                [WorkerRuntime.DotnetIsolated] = new(StringComparer.OrdinalIgnoreCase) { "8", "9", "10", "11" },
                [WorkerRuntime.Python] = new(StringComparer.OrdinalIgnoreCase) { "3.11", "3.12", "3.13", "3.14" },
                [WorkerRuntime.Node] = new(StringComparer.OrdinalIgnoreCase) { "18", "20", "22", "24", "26" },
                [WorkerRuntime.Java] = new(StringComparer.OrdinalIgnoreCase) { "21", "25" },
                [WorkerRuntime.Powershell] = new(StringComparer.OrdinalIgnoreCase) { "7.2", "7.4", "7.6" },
            };

        private static readonly IReadOnlyDictionary<WorkerRuntime, string> _defaultVersions =
            new Dictionary<WorkerRuntime, string>
            {
                [WorkerRuntime.DotnetIsolated] = "11",
                [WorkerRuntime.Python] = "3.14",
                [WorkerRuntime.Node] = "26",
                [WorkerRuntime.Java] = "25",
                [WorkerRuntime.Powershell] = "7.6",
            };

        internal static async Task<FlexByocTemplateArtifacts> GetTemplateArtifactsAsync(
            WorkerRuntime workerRuntime,
            string runtimeVersion,
            string language)
        {
            string normalizedVersion = ResolveRuntimeVersion(workerRuntime, runtimeVersion);
            var artifactNames = ResolveArtifactNames(workerRuntime, normalizedVersion, language);
            string dockerfile = await LoadRequiredArtifactAsync(artifactNames.Dockerfile);

            return new FlexByocTemplateArtifacts(
                ResolveDockerfileImages(dockerfile, workerRuntime, normalizedVersion),
                await LoadRequiredArtifactAsync(artifactNames.Entrypoint),
                await LoadRequiredArtifactAsync(artifactNames.DockerIgnore));
        }

        internal static string ResolveRuntimeVersion(WorkerRuntime workerRuntime, string runtimeVersion)
        {
            if (!_defaultVersions.TryGetValue(workerRuntime, out string defaultVersion) ||
                !_supportedVersions.TryGetValue(workerRuntime, out HashSet<string> supportedVersions))
            {
                throw new CliException(
                    $"Flex BYOC artifact generation is not supported for worker runtime '{workerRuntime.GetDisplayString()}'.");
            }

            string requestedVersion = string.IsNullOrWhiteSpace(runtimeVersion) ? defaultVersion : runtimeVersion;
            string normalizedVersion = NormalizeRuntimeVersion(workerRuntime, requestedVersion);
            if (!supportedVersions.Contains(normalizedVersion))
            {
                throw new CliException(
                    $"Flex BYOC artifact generation does not support version '{requestedVersion}' for worker runtime '{workerRuntime.GetDisplayString()}'. Supported versions are: {string.Join(", ", supportedVersions)}.");
            }

            return normalizedVersion;
        }

        internal static void ValidateSelection(
            WorkerRuntime workerRuntime,
            string runtimeVersion,
            string language)
        {
            _ = ResolveArtifactNames(workerRuntime, runtimeVersion, language);
        }

        private static FlexByocArtifactNames ResolveArtifactNames(
            WorkerRuntime workerRuntime,
            string runtimeVersion,
            string language)
        {
            _ = ResolveRuntimeVersion(workerRuntime, runtimeVersion);

            string templateName = workerRuntime switch
            {
                WorkerRuntime.DotnetIsolated => "dotnetIsolated",
                WorkerRuntime.Node => $"node.{NormalizeNodeLanguage(language)}",
                WorkerRuntime.Python => "python",
                WorkerRuntime.Java => "java",
                WorkerRuntime.Powershell => "powershell",
                _ => throw new InvalidOperationException(),
            };

            string entrypointName = workerRuntime switch
            {
                WorkerRuntime.DotnetIsolated => "dotnetIsolated",
                WorkerRuntime.Node => "node",
                WorkerRuntime.Python => "python",
                WorkerRuntime.Java => "java",
                WorkerRuntime.Powershell => "powershell",
                _ => throw new InvalidOperationException(),
            };

            return new FlexByocArtifactNames(
                $"Dockerfile.flexByoc.{templateName}",
                $"entrypoint.flexByoc.{entrypointName}",
                "dockerignore.flexByoc");
        }

        private static string ResolveDockerfileImages(
            string dockerfile,
            WorkerRuntime workerRuntime,
            string runtimeVersion)
        {
            FlexByocDockerImages images = workerRuntime switch
            {
                WorkerRuntime.DotnetIsolated => new(
                    $"mcr.microsoft.com/dotnet/sdk:{runtimeVersion}.0-noble",
                    $"mcr.microsoft.com/dotnet/aspnet:{runtimeVersion}.0-noble"),
                WorkerRuntime.Node => new(
                    $"node:{runtimeVersion}-bookworm-slim",
                    $"node:{runtimeVersion}-bookworm-slim"),
                WorkerRuntime.Python => new(
                    $"python:{runtimeVersion}-slim-bookworm",
                    $"python:{runtimeVersion}-slim-bookworm"),
                WorkerRuntime.Java => new(
                    $"maven:3.9-eclipse-temurin-{runtimeVersion}",
                    $"mcr.microsoft.com/openjdk/jdk:{runtimeVersion}-ubuntu"),
                WorkerRuntime.Powershell => new(
                    string.Empty,
                    ResolvePowerShellRuntimeImage(runtimeVersion)),
                _ => throw new InvalidOperationException(),
            };

            return dockerfile
                .Replace("{{BUILD_IMAGE}}", images.BuildImage)
                .Replace("{{RUNTIME_IMAGE}}", images.RuntimeImage)
                .Replace("{{RUNTIME_VERSION}}", runtimeVersion)
                .ReplaceWorkerPackageArguments(ResolveWorkerPackage(workerRuntime, runtimeVersion));
        }

        private static string ResolvePowerShellRuntimeImage(string runtimeVersion)
        {
            return runtimeVersion == "7.6"
                ? "mcr.microsoft.com/powershell:preview-7.6-ubuntu-24.04"
                : $"mcr.microsoft.com/powershell:{runtimeVersion}-ubuntu-22.04";
        }

        private static FlexByocWorkerPackage ResolveWorkerPackage(
            WorkerRuntime workerRuntime,
            string runtimeVersion)
        {
            const string releaseFeed = "https://azfunc.pkgs.visualstudio.com/e6a70c92-4128-439f-8012-382fe78d6396/_packaging/AzureFunctionsRelease/nuget/v3/index.json";
            const string powershellFeed = "https://azfunc.pkgs.visualstudio.com/e6a70c92-4128-439f-8012-382fe78d6396/_packaging/Microsoft.Azure.Functions.PowerShellWorker/nuget/v3/index.json";
            const string infraFeed = "https://pkgs.dev.azure.com/azfunc/public/_packaging/infra/nuget/v3/index.json";

            return workerRuntime switch
            {
                WorkerRuntime.DotnetIsolated => FlexByocWorkerPackage.Empty,
                WorkerRuntime.Python => new(
                    "Microsoft.Azure.Functions.PythonWorker",
                    "4.46.0",
                    releaseFeed,
                    "microsoft.azure.functions.pythonworker/4.46.0/tools"),
                WorkerRuntime.Node => new(
                    "Microsoft.Azure.Functions.NodeJsWorker",
                    "3.16.0",
                    releaseFeed,
                    "microsoft.azure.functions.nodejsworker/3.16.0/contentFiles/any/any/workers/node"),
                WorkerRuntime.Java => new(
                    "Microsoft.Azure.Functions.JavaWorker",
                    "2.19.5",
                    releaseFeed,
                    "microsoft.azure.functions.javaworker/2.19.5/contentFiles/any/any/workers/java"),
                WorkerRuntime.Powershell => ResolvePowerShellWorkerPackage(runtimeVersion, powershellFeed, infraFeed),
                _ => throw new InvalidOperationException(),
            };
        }

        private static FlexByocWorkerPackage ResolvePowerShellWorkerPackage(
            string runtimeVersion,
            string powershellFeed,
            string infraFeed)
        {
            string packageVersion = runtimeVersion switch
            {
                "7.2" => "4.0.4025",
                "7.4" => "4.0.5361",
                "7.6" => "4.0.5362",
                _ => throw new InvalidOperationException(),
            };

            string packageId = $"Microsoft.Azure.Functions.PowerShellWorker.PS{runtimeVersion}";
            return new(
                packageId,
                packageVersion,
                runtimeVersion == "7.2" ? powershellFeed : infraFeed,
                $"{packageId.ToLowerInvariant()}/{packageVersion}/contentFiles/any/any/workers/powershell");
        }

        private static string NormalizeRuntimeVersion(WorkerRuntime workerRuntime, string runtimeVersion)
        {
            if (string.IsNullOrWhiteSpace(runtimeVersion))
            {
                return string.Empty;
            }

            string normalizedVersion = runtimeVersion.Trim();
            if (workerRuntime == WorkerRuntime.DotnetIsolated)
            {
                if (normalizedVersion.StartsWith("net", StringComparison.OrdinalIgnoreCase))
                {
                    normalizedVersion = normalizedVersion[3..];
                }

                if (Version.TryParse(normalizedVersion, out Version version) &&
                    version.Minor == 0 &&
                    version.Build <= 0 &&
                    version.Revision <= 0)
                {
                    return version.Major.ToString();
                }
            }

            return normalizedVersion;
        }

        private static string NormalizeNodeLanguage(string language)
        {
            if (Constants.Languages.JavaScript.Equals(language, StringComparison.OrdinalIgnoreCase))
            {
                return Constants.Languages.JavaScript;
            }

            if (Constants.Languages.TypeScript.Equals(language, StringComparison.OrdinalIgnoreCase))
            {
                return Constants.Languages.TypeScript;
            }

            throw new CliException(
                $"Flex BYOC artifact generation requires a Node.js language of '{Constants.Languages.JavaScript}' or '{Constants.Languages.TypeScript}'.");
        }

        private static async Task<string> LoadRequiredArtifactAsync(string resourceName)
        {
            try
            {
                return await StaticResources.GetValue(resourceName);
            }
            catch (Exception ex)
            {
                throw new CliException(
                    $"Flex BYOC template artifact '{resourceName}' was not present.",
                    ex);
            }
        }

        private static string ReplaceWorkerPackageArguments(
            this string dockerfile,
            FlexByocWorkerPackage package)
        {
            return dockerfile
                .Replace("{{WORKER_PACKAGE_ID}}", package.Id)
                .Replace("{{WORKER_PACKAGE_VERSION}}", package.Version)
                .Replace("{{WORKER_PACKAGE_FEED}}", package.Feed)
                .Replace("{{WORKER_PACKAGE_CONTENT_PATH}}", package.ContentPath);
        }

        private sealed record FlexByocArtifactNames(
            string Dockerfile,
            string Entrypoint,
            string DockerIgnore);

        private sealed record FlexByocDockerImages(
            string BuildImage,
            string RuntimeImage);

        private sealed record FlexByocWorkerPackage(
            string Id,
            string Version,
            string Feed,
            string ContentPath)
        {
            internal static FlexByocWorkerPackage Empty { get; } = new(
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty);
        }
    }
}
