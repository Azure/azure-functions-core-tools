// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Edge;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Creates func-owned TemplateEngine environments from resolved command context.
/// </summary>
internal sealed class FuncTemplateEngineBootstrapper(IFuncTemplateEngineHostFactory hostFactory) : IFuncTemplateEngineBootstrapper
{
    private readonly IFuncTemplateEngineHostFactory _hostFactory =
        hostFactory ?? throw new ArgumentNullException(nameof(hostFactory));

    public IEngineEnvironmentSettings Create(FuncTemplateEngineContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ITemplateEngineHost host = _hostFactory.CreateHost(BuildHostParameters(context));
        return new EngineEnvironmentSettings(host, settingsLocation: _hostFactory.SettingsLocation);
    }

    private static Dictionary<string, string> BuildHostParameters(FuncTemplateEngineContext context)
    {
        Dictionary<string, string> hostParameters = new(StringComparer.Ordinal);

        AddIfPresent(hostParameters, FuncTemplateEngineHostParameters.Stack, context.Stack);
        AddIfPresent(hostParameters, FuncTemplateEngineHostParameters.Language, context.Language);
        AddIfPresent(hostParameters, FuncTemplateEngineHostParameters.BundleId, context.BundleId);
        AddIfPresent(hostParameters, FuncTemplateEngineHostParameters.BundleVersion, context.BundleVersion);

        return hostParameters;
    }

    private static void AddIfPresent(Dictionary<string, string> hostParameters, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            hostParameters[key] = value;
        }
    }
}
