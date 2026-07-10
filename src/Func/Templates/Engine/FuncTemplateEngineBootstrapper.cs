// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Edge;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Production <see cref="IFuncTemplateEngineBootstrapper"/>. Per invocation it
/// asks the <see cref="IFuncTemplateEngineHostFactory"/> for a func host
/// stamped with the resolved project context as host params, then wraps it in a
/// fresh <see cref="EngineEnvironmentSettings"/> pointed at the func-owned
/// settings location. Component registration (RunnableProjects + func
/// constraints) is layered on separately.
/// </summary>
internal sealed class FuncTemplateEngineBootstrapper : IFuncTemplateEngineBootstrapper
{
    private readonly IFuncTemplateEngineHostFactory _hostFactory;

    public FuncTemplateEngineBootstrapper(IFuncTemplateEngineHostFactory hostFactory)
    {
        _hostFactory = hostFactory ?? throw new ArgumentNullException(nameof(hostFactory));
    }

    public IEngineEnvironmentSettings Create(FuncTemplateEngineContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ITemplateEngineHost host = _hostFactory.CreateHost(BuildHostParams(context));

        return new EngineEnvironmentSettings(host, settingsLocation: _hostFactory.SettingsLocation);
    }

    private static Dictionary<string, string> BuildHostParams(FuncTemplateEngineContext context)
    {
        var hostParams = new Dictionary<string, string>(StringComparer.Ordinal);

        AddIfPresent(hostParams, FuncTemplateEngineHostParameters.Stack, context.Stack);
        AddIfPresent(hostParams, FuncTemplateEngineHostParameters.Language, context.Language);
        AddIfPresent(hostParams, FuncTemplateEngineHostParameters.Bundle, context.Bundle);

        return hostParams;
    }

    private static void AddIfPresent(Dictionary<string, string> hostParams, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            hostParams[key] = value;
        }
    }
}
