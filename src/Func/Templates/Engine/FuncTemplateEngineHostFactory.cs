// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Abstractions.Common;
using Azure.Functions.Cli.Common;
using Microsoft.TemplateEngine.Abstractions;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Creates func-owned TemplateEngine hosts and identifies their shared settings location.
/// </summary>
internal sealed class FuncTemplateEngineHostFactory(ICliVersionProvider versionProvider) : IFuncTemplateEngineHostFactory
{
    internal const string SettingsDirectoryName = "templates";

    private readonly ICliVersionProvider _versionProvider =
        versionProvider ?? throw new ArgumentNullException(nameof(versionProvider));

    public string SettingsLocation { get; } =
        Path.GetFullPath(Path.Combine(FuncHomeResolver.Resolve(), SettingsDirectoryName));

    public ITemplateEngineHost CreateHost(IReadOnlyDictionary<string, string>? hostParameters = null)
    {
        Dictionary<string, string>? defaults = hostParameters is { Count: > 0 }
            ? new Dictionary<string, string>(hostParameters, StringComparer.Ordinal)
            : null;

        return new FuncTemplateEngineHost(_versionProvider.Version, defaults);
    }
}
