// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Configuration;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Creates templaters using the configured template engine components and settings location.
/// </summary>
/// <param name="configurationPaths">The service to resolve configuration paths from.</param>
/// <param name="components">The template engine components to use.</param>
internal sealed class TemplaterFactory(
    CliConfigurationPathsOptions configurationPaths, IEnumerable<ITemplateComponent> components) : ITemplaterFactory
{
    /// <summary>
    /// Sub-directory (under the func home) that holds the engine's settings,
    /// installed template packages, and caches.
    /// </summary>
    private const string SettingsDirectoryName = "templates";

    private readonly CliConfigurationPathsOptions _configurationPaths = configurationPaths
        ?? throw new ArgumentNullException(nameof(configurationPaths));
    private readonly IEnumerable<ITemplateComponent> _components = components
        ?? throw new ArgumentNullException(nameof(components));

    public Templater Create(TemplateEngineContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        string settingsLocation = Path.Combine(_configurationPaths.Home, SettingsDirectoryName);
        return new Templater(new TemplateEngineSession(context, settingsLocation, _components));
    }
}
