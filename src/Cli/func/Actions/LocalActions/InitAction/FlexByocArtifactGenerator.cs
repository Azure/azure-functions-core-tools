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
                [WorkerRuntime.Python] = new(StringComparer.OrdinalIgnoreCase) { "3.11", "3.12", "3.13", "3.14", "3.15" },
                [WorkerRuntime.Node] = new(StringComparer.OrdinalIgnoreCase) { "18", "20", "22", "24", "26" },
                [WorkerRuntime.Java] = new(StringComparer.OrdinalIgnoreCase) { "21", "25" },
                [WorkerRuntime.Powershell] = new(StringComparer.OrdinalIgnoreCase) { "7.2", "7.4", "7.6" },
            };

        internal static async Task<FlexByocTemplateArtifacts> GetTemplateArtifactsAsync(
            WorkerRuntime workerRuntime,
            string runtimeVersion,
            string language)
        {
            var artifactNames = ResolveArtifactNames(workerRuntime, runtimeVersion, language);
            return new FlexByocTemplateArtifacts(
                await LoadRequiredArtifactAsync(artifactNames.Dockerfile),
                await LoadRequiredArtifactAsync(artifactNames.Entrypoint),
                await LoadRequiredArtifactAsync(artifactNames.DockerIgnore));
        }

        private static FlexByocArtifactNames ResolveArtifactNames(
            WorkerRuntime workerRuntime,
            string runtimeVersion,
            string language)
        {
            if (!_supportedVersions.TryGetValue(workerRuntime, out HashSet<string> supportedVersions))
            {
                throw new CliException(
                    $"Flex BYOC artifact generation is not supported for worker runtime '{workerRuntime.GetDisplayString()}'.");
            }

            string normalizedVersion = NormalizeRuntimeVersion(workerRuntime, runtimeVersion);
            if (!supportedVersions.Contains(normalizedVersion))
            {
                throw new CliException(
                    $"Flex BYOC artifact generation does not support version '{runtimeVersion}' for worker runtime '{workerRuntime.GetDisplayString()}'. Supported versions are: {string.Join(", ", supportedVersions)}.");
            }

            string templateName = workerRuntime switch
            {
                WorkerRuntime.DotnetIsolated => $"dotnetIsolated.{normalizedVersion}",
                WorkerRuntime.Node => $"node.{NormalizeNodeLanguage(language)}.{normalizedVersion}",
                WorkerRuntime.Python => $"python.{normalizedVersion}",
                WorkerRuntime.Java => $"java.{normalizedVersion}",
                WorkerRuntime.Powershell => $"powershell.{normalizedVersion}",
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

        private sealed record FlexByocArtifactNames(
            string Dockerfile,
            string Entrypoint,
            string DockerIgnore);
    }
}
