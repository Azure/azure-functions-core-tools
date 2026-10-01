// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Configuration;

namespace Azure.Functions.Cli.Templates.Engine;

internal sealed class TemplaterFactory(CliConfigurationPathsOptions configurationPaths) : ITemplaterFactory
{
    /// <summary>
    /// Sub-directory (under the func home) that holds the engine's settings,
    /// installed template packages, and caches.
    /// </summary>
    private const string SettingsDirectoryName = "templates";

    private readonly CliConfigurationPathsOptions _configurationPaths = configurationPaths ?? throw new ArgumentNullException(nameof(configurationPaths));

    public Templater Create(TemplateEngineContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        string settingsLocation = Path.Combine(_configurationPaths.Home, SettingsDirectoryName);
        return new Templater(new TemplateEngineSession(context, settingsLocation));
    }
}
