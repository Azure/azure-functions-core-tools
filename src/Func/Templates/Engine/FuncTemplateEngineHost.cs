// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Edge;

using OrchestratorComponents = Microsoft.TemplateEngine.Orchestrator.RunnableProjects.Components;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Template engine host that exposes one command's <see cref="TemplateEngineContext"/> as host parameters.
/// </summary>
internal class FuncTemplateEngineHost(TemplateEngineContext context)
    : DefaultTemplateEngineHost(Identifier, AssemblyCliVersionProvider.Instance.Version, CreateDefaults(context), _builtIns)
{
    /// <summary>
    /// Host identifier reported to <c>Microsoft.TemplateEngine</c>. Using a
    /// func-specific identifier (rather than the dotnet CLI's
    /// <c>dotnetcli</c>) keeps host-owned template config and constraints
    /// scoped to func.
    /// </summary>
    internal const string Identifier = "func";

    private const string WorkingDirectoryParameter = "WorkingDirectory";

    private static readonly IReadOnlyList<(Type, IIdentifiedComponent)> _builtIns =
    [
        ..Components.AllComponents,
        ..OrchestratorComponents.AllComponents,
        ..FuncTemplateComponents.AllComponents,
    ];

    private readonly TemplateEngineContext _context = context ?? throw new ArgumentNullException(nameof(context));

    public override bool TryGetHostParamDefault(string paramName, out string? value)
    {
        // The base host answers WorkingDirectory with the process current directory before it consults defaults.
        if (string.Equals(paramName, WorkingDirectoryParameter, StringComparison.Ordinal))
        {
            value = DirectoryValue(_context.CommandDirectory);
            return true;
        }

        return base.TryGetHostParamDefault(paramName, out value);
    }

    private static Dictionary<string, string> CreateDefaults(TemplateEngineContext context)
    {
        Dictionary<string, string> defaults = new(StringComparer.Ordinal);

        if (context.Project is { } project)
        {
            defaults[FuncTemplateEngineHostParameters.ProjectRoot] = DirectoryValue(project.RootDirectory);
            defaults[FuncTemplateEngineHostParameters.Stack] = project.Stack;
            if (project.Language is not null)
            {
                defaults[FuncTemplateEngineHostParameters.Language] = project.Language;
            }
        }

        if (context.Bundle is { } bundle)
        {
            defaults[FuncTemplateEngineHostParameters.BundleId] = bundle.Id;
            defaults[FuncTemplateEngineHostParameters.BundleVersion] = bundle.Version;
        }

        return defaults;
    }

    // Trim so the value doesn't depend on whether the user typed a trailing separator.
    private static string DirectoryValue(WorkingDirectory directory) => Path.TrimEndingDirectorySeparator(directory.Info.FullName);
}
