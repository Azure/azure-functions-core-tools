// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Reflection;

namespace Azure.Functions.Cli.Templates.Integration;

internal interface ITemplatePackageResources
{
    public Stream OpenRead(string resourceName);
}

/// <summary>
/// Opens embedded archive bytes without requiring a loose file beside the CLI.
/// </summary>
internal sealed class TemplatePackageResources(Assembly assembly) : ITemplatePackageResources
{
    private readonly Assembly _assembly = assembly ?? throw new ArgumentNullException(nameof(assembly));

    public Stream OpenRead(string resourceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);
        return _assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"The bound template resource '{resourceName}' is missing. Use a correctly built qualification candidate.", resourceName);
    }
}
