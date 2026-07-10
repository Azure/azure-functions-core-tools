// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Abstractions.Common;
using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Projects;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Edge;
using Microsoft.TemplateEngine.Edge.Settings;
using Microsoft.TemplateEngine.Edge.Template;

namespace Azure.Functions.Cli.Templates.Engine;

internal class Templater
{

    /// <summary>
    /// Sub-directory (under the func home) that holds the engine's settings,
    /// installed template packages, and caches.
    /// </summary>
    internal const string SettingsDirectoryName = "templates";

    private readonly IEngineEnvironmentSettings _settings;
    private readonly TemplateCreator _creator;
    private readonly TemplatePackageManager _packageManager;

    public Templater(IEngineEnvironmentSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        _creator = new(settings);
        _packageManager = new(settings);
    }

    /// <summary>
    /// The engine environment the host is bootstrapped with (settings location,
    /// registered components, host params). Exposed for host wiring and tests.
    /// </summary>
    public IEngineEnvironmentSettings Settings => _settings;

    public TemplateCreator Creator => _creator;

    public TemplatePackageManager PackageManager => _packageManager;

    public static Templater Create(
        ProjectResolutionResult? projectResolution = null,
        ExtensionBundleResolution? bundleResolution = null,
        string? settingsLocation = null)
    {
        projectResolution ??= ProjectResolutionResults.NotResolved("No project resolution context provided");
        bundleResolution ??= new ExtensionBundleResolution.NotResolved("No bundle resolution context provided");
        settingsLocation ??= Path.Combine(FuncHomeResolver.Resolve(), SettingsDirectoryName);

        Dictionary<string, string>? hostParams = null;
        if (projectResolution is ProjectResolutionResult.Resolved resolved)
        {
            string stack = resolved.Project.StackName.ToLowerInvariant();
            string language = resolved.Project.Language.ToLowerInvariant();
            hostParams = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [FuncTemplateEngineHostParameters.Stack] = stack,
                [FuncTemplateEngineHostParameters.Language] = language,
            };

            if (resolved.Project.SupportsExtensionBundles && bundleResolution is ExtensionBundleResolution.Resolved bundle)
            {
                hostParams[FuncTemplateEngineHostParameters.Bundle] = bundle.Version;
                hostParams[FuncTemplateEngineHostParameters.BundleChannel] = bundle.BundleId;
            }
        }

        FuncTemplateEngineHost host = new(hostParams);
        EngineEnvironmentSettings settings = new(host, settingsLocation: settingsLocation);
        Templater templater = new(settings);
        return templater;
    }

    /// <summary>
    /// Enumerates the templates installed in the func hive.
    /// </summary>
    public Task<IReadOnlyList<ITemplateInfo>> GetTemplatesAsync(CancellationToken cancellationToken = default)
    {
        return _packageManager.GetTemplatesAsync(cancellationToken);
    }
}
