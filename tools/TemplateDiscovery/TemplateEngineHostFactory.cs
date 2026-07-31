// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
//
// Vendored and adapted from the .NET Templating engine:
//   repo:   https://github.com/dotnet/templating
//   source: src/Tools/Microsoft.TemplateSearch.TemplateDiscovery/TemplateEngineHostHelper.cs
//   version: 10.0.302
// See README.md in this directory for the full provenance note and list of adaptations.

using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Edge;

namespace Azure.Functions.Cli.TemplateDiscovery;

/// <summary>
/// Builds the in-process template engine host used to scan candidate packages. The host mirrors the
/// engine the CLI itself loads, so the index reflects what <c>func new</c> can actually render.
/// </summary>
internal sealed class TemplateEngineHostFactory
{
    private const string DefaultHostVersion = "1.0.0";

    private static readonly Dictionary<string, string> _defaultPreferences = new() { ["prefs:language"] = "C#" };

    public DefaultTemplateEngineHost CreateHost(string hostIdentifier)
    {
        ArgumentException.ThrowIfNullOrEmpty(hostIdentifier);

        var builtIns = new List<(Type, IIdentifiedComponent)>();
        builtIns.AddRange(Components.AllComponents);
        builtIns.AddRange(Microsoft.TemplateEngine.Orchestrator.RunnableProjects.Components.AllComponents);

        // "dotnetcli" fallback so host-specific template config files are honoured, same as upstream.
        return new DefaultTemplateEngineHost(hostIdentifier, DefaultHostVersion, _defaultPreferences, builtIns, ["dotnetcli"]);
    }
}
