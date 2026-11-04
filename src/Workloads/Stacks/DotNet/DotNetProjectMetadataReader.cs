// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Xml.Linq;

namespace Azure.Functions.Cli.Workloads.DotNet;

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
