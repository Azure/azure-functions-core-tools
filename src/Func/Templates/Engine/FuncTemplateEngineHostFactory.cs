// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Abstractions.Common;
using Azure.Functions.Cli.Common;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Edge;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Production <see cref="IFuncTemplateEngineHostFactory"/>. Builds a
/// <see cref="DefaultTemplateEngineHost"/> stamped with the <c>func</c> host
/// identifier and the running CLI version, and roots the engine settings
/// location under the func home so it is isolated from the dotnet CLI hive.
/// </summary>
internal sealed class FuncTemplateEngineHostFactory : IFuncTemplateEngineHostFactory
{
    /// <summary>
    /// Host identifier reported to <c>Microsoft.TemplateEngine</c>. Using a
    /// func-specific identifier (rather than the dotnet CLI's
    /// <c>dotnetcli</c>) keeps host-owned template config and constraints
    /// scoped to func.
    /// </summary>
    internal const string HostIdentifier = "func";

    /// <summary>
    /// Sub-directory (under the func home) that holds the engine's settings,
    /// installed template packages, and caches.
    /// </summary>
    internal const string SettingsDirectoryName = "template-engine";

    private readonly string _version;

    public FuncTemplateEngineHostFactory(ICliVersionProvider versionProvider)
    {
        ArgumentNullException.ThrowIfNull(versionProvider);

        _version = versionProvider.Version;
        SettingsLocation = Path.GetFullPath(
            Path.Combine(FuncHomeResolver.Resolve(), SettingsDirectoryName));
    }

    public string SettingsLocation { get; }

    public ITemplateEngineHost CreateHost()
        => new DefaultTemplateEngineHost(HostIdentifier, _version);
}
