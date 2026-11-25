// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Creates the command-scoped <see cref="Templater"/> for a resolved <see cref="TemplateEngineContext"/>.
/// </summary>
internal interface ITemplaterFactory
{
    /// <summary>
    /// Gets the shared engine settings, packages, and cache directory.
    /// </summary>
    public string HivePath { get; }

    /// <summary>
    /// Creates a templater bound to <paramref name="context"/>. The caller disposes it when the command is finished.
    /// </summary>
    public Templater Create(TemplateEngineContext context);

    /// <summary>
    /// Creates a templater with an isolated settings directory and no external package providers.
    /// </summary>
    public Templater CreateIsolated(TemplateEngineContext context, string settingsLocation);
}
