// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Configuration;
using Microsoft.TemplateEngine.Abstractions.TemplatePackage;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Creates templaters using the configured template engine components and settings location.
/// </summary>
/// <param name="configurationPaths">The service to resolve configuration paths from.</param>
/// <param name="componentProvider">The provider that builds template engine components.</param>
internal sealed class TemplaterFactory(
    CliConfigurationPathsOptions configurationPaths, IFuncTemplateComponentProvider componentProvider)
    : ITemplaterFactory
{
    /// <summary>
    /// Sub-directory (under the func home) that holds the engine's settings,
    /// installed template packages, and caches.
    /// </summary>
    private const string SettingsDirectoryName = "templates";

    private readonly CliConfigurationPathsOptions _configurationPaths = configurationPaths
        ?? throw new ArgumentNullException(nameof(configurationPaths));
    private readonly IFuncTemplateComponentProvider _componentProvider = componentProvider
        ?? throw new ArgumentNullException(nameof(componentProvider));

    public string HivePath => Path.Combine(_configurationPaths.Home, SettingsDirectoryName);

    public Templater Create(TemplateEngineContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new Templater(context, HivePath, _componentProvider.Get());
    }

    public Templater CreateIsolated(TemplateEngineContext context, string settingsLocation)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsLocation);
        HashSet<Guid> builtInIds = [.. Microsoft.TemplateEngine.Edge.Components.AllComponents.Select(component => component.Instance.Id)];
        ICoreTemplateComponent[] components = [.. _componentProvider.Get()
            .Where(component => component.Instance is not ITemplatePackageProviderFactory || builtInIds.Contains(component.Instance.Id))];
        return new Templater(context, settingsLocation, components);
    }
}
