// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Templates.Engine;

internal sealed class TemplaterFactory(CliConfigurationPathsOptions configurationPaths, IFileSystem fileSystem) : ITemplaterFactory
{
    /// <summary>
    /// Sub-directory (under the func home) that holds the engine's settings,
    /// installed template packages, and caches.
    /// </summary>
    private const string SettingsDirectoryName = "templates";

    private readonly CliConfigurationPathsOptions _configurationPaths = configurationPaths ?? throw new ArgumentNullException(nameof(configurationPaths));
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

    public Templater Create(TemplateEngineContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        string settingsLocation = Path.Combine(_configurationPaths.Home, SettingsDirectoryName);
        TemplateEngineSession session = new(context, settingsLocation);
        return new Templater(session, new(session, _fileSystem));
    }

    public Templater Create(TemplateEngineContext context, TemplateConstraintEvaluationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(snapshot);
        string settingsLocation = Path.Combine(_configurationPaths.Home, SettingsDirectoryName);
        TemplateEngineSession session = new(context, settingsLocation, snapshot);
        return new Templater(session, new(session, _fileSystem));
    }
}
