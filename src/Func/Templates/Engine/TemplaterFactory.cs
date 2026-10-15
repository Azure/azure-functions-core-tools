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

    public Templater Create(TemplateEngineContext context, TemplateConstraintEvaluationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(snapshot);
        string settingsLocation = Path.Combine(_configurationPaths.Home, SettingsDirectoryName);
        return new Templater(new TemplateEngineSession(context, settingsLocation, snapshot));
    }

    public Templater CreateProject(
        TemplateEngineContext context, TemplateConstraintEvaluationSnapshot snapshot, TemplateProjectSupport projectSupport)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(projectSupport);
        string settingsLocation = Path.Combine(_configurationPaths.Home, SettingsDirectoryName);
        return new(new TemplateEngineSession(context, settingsLocation, snapshot, projectSupport));
    }
}
