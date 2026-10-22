// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.PhysicalFileSystem;
using Microsoft.TemplateEngine.Edge;
using Microsoft.TemplateEngine.Abstractions.TemplatePackage;
using Microsoft.Extensions.Logging;

using OrchestratorComponents = Microsoft.TemplateEngine.Orchestrator.RunnableProjects.Components;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Template engine host that exposes one command's <see cref="TemplateEngineContext"/> as host parameters.
/// </summary>
internal class FuncTemplateEngineHost : DefaultTemplateEngineHost, ITemplateEngineHost
{
    /// <summary>
    /// Host identifier reported to <c>Microsoft.TemplateEngine</c>. Using a
    /// func-specific identifier (rather than the dotnet CLI's
    /// <c>dotnetcli</c>) keeps host-owned template config and constraints
    /// scoped to func.
    /// </summary>
    internal const string Identifier = "func";

    private const string WorkingDirectoryParameter = "WorkingDirectory";

    private readonly TemplateEngineContext _context;
    private readonly NativeTemplatePackageScope? _packageScope;
    private readonly List<NativeCohortPackageProviderFactory> _cohortFactories = [];
    private IReadOnlyList<(Type InterfaceType, IIdentifiedComponent Instance)>? _scopedComponents;

    public FuncTemplateEngineHost(TemplateEngineContext context, TemplateConstraintEvaluationSnapshot? snapshot = null,
        ILoggerFactory? loggerFactory = null)
        : base(Identifier, AssemblyCliVersionProvider.Instance.Version, CreateDefaults(context), CreateComponents(context, snapshot),
            loggerFactory: loggerFactory)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _packageScope = snapshot?.PackageScope;
        InvocationFileSystem = new(FileSystem);
    }

    internal TemplateInvocationFileSystem InvocationFileSystem { get; }

    internal IPhysicalFileSystem? PreflightFileSystem { get; set; }

    IPhysicalFileSystem ITemplateEngineHost.FileSystem => PreflightFileSystem ?? InvocationFileSystem;

    public override IReadOnlyList<(Type InterfaceType, IIdentifiedComponent Instance)> BuiltInComponents
    {
        get
        {
            if (_packageScope is null) return base.BuiltInComponents;
            if (_scopedComponents is not null) return _scopedComponents;
            List<(Type InterfaceType, IIdentifiedComponent Instance)> components = [];
            foreach ((Type type, IIdentifiedComponent instance) in base.BuiltInComponents)
            {
                if (instance is ITemplatePackageProviderFactory factory)
                {
                    NativeCohortPackageProviderFactory scoped = new(factory, _packageScope);
                    _cohortFactories.Add(scoped);
                    components.Add((type, scoped));
                }
                else
                {
                    components.Add((type, instance));
                }
            }
            _scopedComponents = components.AsReadOnly();
            return _scopedComponents;
        }
    }

    internal void ThrowIfCohortFailed()
    {
        if (_cohortFactories.Select(factory => factory.Failure).FirstOrDefault(failure => failure is not null) is { } failure)
        {
            throw new NativeTemplateCohortException(failure);
        }
    }

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

    private static IReadOnlyList<(Type, IIdentifiedComponent)> CreateComponents(TemplateEngineContext context, TemplateConstraintEvaluationSnapshot? snapshot)
    {
        ArgumentNullException.ThrowIfNull(context);
        return [..Components.AllComponents, ..OrchestratorComponents.AllComponents,
            ..FuncTemplateComponents.Create(snapshot ?? new([]), context.Bundle, context.EnableBundleBindingConstraints)];
    }

    private static Dictionary<string, string> CreateDefaults(TemplateEngineContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Dictionary<string, string> defaults = new(StringComparer.Ordinal)
        {
            [FuncTemplateEngineHostParameters.ItemOutputIntent] = context.ItemOutputIntentValue,
        };

        if (context.Project is { } project)
        {
            defaults[FuncTemplateEngineHostParameters.ProjectRoot] = DirectoryValue(project.RootDirectory);
            if (project.Stack is not null)
            {
                defaults[FuncTemplateEngineHostParameters.Stack] = project.Stack;
            }

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
