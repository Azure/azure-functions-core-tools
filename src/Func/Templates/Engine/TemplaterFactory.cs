// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Templates.Engine;

internal sealed class TemplaterFactory(
    CliConfigurationPathsOptions configurationPaths,
    InstalledProjectStackCatalog projectStacks) : ITemplaterFactory
{
    /// <summary>
    /// Sub-directory (under the func home) that holds the engine's settings,
    /// installed template packages, and caches.
    /// </summary>
    private const string SettingsDirectoryName = "templates";

    private readonly CliConfigurationPathsOptions _configurationPaths = configurationPaths ?? throw new ArgumentNullException(nameof(configurationPaths));
    private readonly InstalledProjectStackCatalog _projectStacks = projectStacks ?? throw new ArgumentNullException(nameof(projectStacks));

    public Templater Create(TemplateEngineContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        string settingsLocation = Path.Combine(_configurationPaths.Home, SettingsDirectoryName);
        return new Templater(new TemplateEngineSession(context, settingsLocation), _projectStacks);
    }
}
