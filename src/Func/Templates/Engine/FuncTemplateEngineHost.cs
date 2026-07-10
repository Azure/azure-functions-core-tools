// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Edge;

using OrchestratorComponents = Microsoft.TemplateEngine.Orchestrator.RunnableProjects.Components;

namespace Azure.Functions.Cli.Templates.Engine;

internal class FuncTemplateEngineHost(Dictionary<string, string>? defaults)
    : DefaultTemplateEngineHost(Identifier, AssemblyCliVersionProvider.Instance.Version, defaults, _builtIns)
{
    /// <summary>
    /// Host identifier reported to <c>Microsoft.TemplateEngine</c>. Using a
    /// func-specific identifier (rather than the dotnet CLI's
    /// <c>dotnetcli</c>) keeps host-owned template config and constraints
    /// scoped to func.
    /// </summary>
    internal const string Identifier = "func";

    private static readonly IReadOnlyList<(Type, IIdentifiedComponent)> _builtIns =
    [
        ..Components.AllComponents,
        ..OrchestratorComponents.AllComponents,
        ..FuncTemplateComponents.AllComponents,
    ];
}
