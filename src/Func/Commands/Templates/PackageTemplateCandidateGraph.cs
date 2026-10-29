// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;

namespace Azure.Functions.Cli.Commands.Templates;

/// <summary>
/// Exposes only package-backed init/new for explicit internal callers, never the public parser.
/// </summary>
internal sealed class PackageTemplateCandidateGraph(PackageTemplateInitCommand init, PackageTemplateNewCommand @new)
{
    private readonly PackageTemplateInitCommand _init = init ?? throw new ArgumentNullException(nameof(init));
    private readonly PackageTemplateNewCommand _new = @new ?? throw new ArgumentNullException(nameof(@new));

    public RootCommand Create() => new("Azure Functions CLI isolated package candidate") { _init, _new };
}
