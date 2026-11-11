// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Configuration;

namespace Azure.Functions.Cli.Templates.Engine;

internal sealed class TemplaterFactory(
    CliConfigurationPathsOptions configurationPaths,
    FuncTemplateConstraintEvaluator funcConstraintEvaluator) : ITemplaterFactory
{
    /// <summary>
    /// Sub-directory (under the func home) that holds the engine's settings,
    /// installed template packages, and caches.
    /// </summary>
    private const string SettingsDirectoryName = "templates";
    private readonly CliConfigurationPathsOptions _configurationPaths = configurationPaths ?? throw new ArgumentNullException(nameof(configurationPaths));
    private readonly FuncTemplateConstraintEvaluator _funcConstraintEvaluator =
        funcConstraintEvaluator ?? throw new ArgumentNullException(nameof(funcConstraintEvaluator));

    public Templater Create(TemplateEngineContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        string settingsLocation = Path.Combine(_configurationPaths.Home, SettingsDirectoryName);
        TemplateEngineSession session = new(context, settingsLocation);
        TemplateSelection selection = new(session, new TemplateEligibilityEvaluator(session, _funcConstraintEvaluator));
        return new Templater(session, selection);
    }
}
