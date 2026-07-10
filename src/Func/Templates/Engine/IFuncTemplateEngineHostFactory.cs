// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.TemplateEngine.Abstractions;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Creates the func-owned <see cref="ITemplateEngineHost"/> used to host
/// <c>Microsoft.TemplateEngine</c> in-process, and exposes the func-owned
/// settings location the engine persists its state under. The location is
/// isolated from the dotnet CLI hive (<c>~/.templateengine/dotnetcli</c>) so
/// <c>func new</c> never reads from or writes to the <c>dotnet new</c> cache.
/// </summary>
internal interface IFuncTemplateEngineHostFactory
{
    /// <summary>
    /// Absolute path to the func-owned template-engine settings directory.
    /// Rooted under the func home (<c>~/.azure-functions</c> by default) and
    /// never <c>~/.templateengine/dotnetcli</c>.
    /// </summary>
    public string SettingsLocation { get; }

    /// <summary>
    /// Creates a fresh <see cref="ITemplateEngineHost"/> with host identifier
    /// <c>func</c>. Construction performs no filesystem work and does not touch
    /// the dotnet CLI hive.
    /// </summary>
    /// <param name="hostParams">
    /// Optional host-param defaults (e.g. resolved project context) surfaced
    /// through <see cref="ITemplateEngineHost.TryGetHostParamDefault(string, out string?)"/>.
    /// </param>
    public ITemplateEngineHost CreateHost(IReadOnlyDictionary<string, string>? hostParams = null);
}
