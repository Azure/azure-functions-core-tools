// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Reflection;

namespace Azure.Functions.Cli.Workload.Dotnet;

/// <summary>
/// Reads bundled template pack versions from assembly metadata.
/// These are injected at build time by Templates.props via AssemblyMetadataAttribute.
/// </summary>
internal static class BundledTemplateVersions
{
    private const string FallbackVersion = "4.0.5337";

    public static string ItemTemplatesVersion { get; } = ReadMetadata("WorkerItemTemplatesVersion");
    public static string ProjectTemplatesVersion { get; } = ReadMetadata("WorkerProjectTemplatesVersion");

    private static string ReadMetadata(string key)
    {
        var attr = typeof(BundledTemplateVersions).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == key);

        return attr?.Value ?? FallbackVersion;
    }
}
