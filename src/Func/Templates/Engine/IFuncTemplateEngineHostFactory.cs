// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.TemplateEngine.Abstractions;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Creates func-owned TemplateEngine hosts.
/// </summary>
internal interface IFuncTemplateEngineHostFactory
{
    public string SettingsLocation { get; }

    public ITemplateEngineHost CreateHost(IReadOnlyDictionary<string, string>? hostParameters = null);
}
