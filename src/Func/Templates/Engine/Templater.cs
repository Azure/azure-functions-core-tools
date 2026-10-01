// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Abstractions.Common;
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

    public static Templater Create(TemplateEngineContext context, string? settingsLocation = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        settingsLocation ??= Path.Combine(FuncHomeResolver.Resolve(), SettingsDirectoryName);

        FuncTemplateEngineHost host = new(context);
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
