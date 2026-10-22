// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Workloads.Node;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Templates.Node;

/// <summary>
/// Retains existing project preparation until the owning workload completion contract is available.
/// </summary>
internal sealed class NodeTemplateWorkflowProvider(IFileSystem fileSystem, NodeNpmInstaller npmInstaller) : INativeProjectWorkflowProvider
{
    private readonly NodeProjectPreparation _preparation = new(fileSystem ?? throw new ArgumentNullException(nameof(fileSystem)),
        npmInstaller ?? throw new ArgumentNullException(nameof(npmInstaller)));

    public string Stack => "node";

    public INativeProjectPreparation Preparation => _preparation;

    public bool Handles(TemplateCatalogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.Type == TemplateType.Project
            && entry.Package is { Identifier: "Azure.Functions.Templates.Node.Basic" } package
            && NuGetVersion.TryParse(package.Version, out _)
            && (entry.Identity, entry.Language) is ("Azure.Functions.Node.Basic.JavaScript", "JavaScript")
                or ("Azure.Functions.Node.Basic.TypeScript", "TypeScript");
    }
}
