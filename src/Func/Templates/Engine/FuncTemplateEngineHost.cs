// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Edge;

using OrchestratorComponents = Microsoft.TemplateEngine.Orchestrator.RunnableProjects.Components;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Hosts TemplateEngine with func-owned identity and components.
/// </summary>
internal sealed class FuncTemplateEngineHost(string version, Dictionary<string, string>? defaults)
    : DefaultTemplateEngineHost(Identifier, version, defaults, _builtInComponents)
{
    internal const string Identifier = "func";

    private static readonly IReadOnlyList<(Type Type, IIdentifiedComponent Instance)> _builtInComponents =
    [
        .. Components.AllComponents,
        .. OrchestratorComponents.AllComponents,
        .. FuncTemplateComponents.AllComponents,
    ];
}
