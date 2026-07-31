// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Edge.Settings;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Process-lifetime holder of the initialized templating engine. The first
/// template scan is expensive, so the engine environment and package manager
/// are built once and kept alive for the whole CLI invocation. Resolve as a
/// singleton.
/// </summary>
internal interface IFuncTemplateEngineSession
{
    /// <summary>
    /// The engine environment (host, paths, components) every read/scaffold
    /// path runs against.
    /// </summary>
    public IEngineEnvironmentSettings Settings { get; }

    /// <summary>
    /// The engine's package manager — the source of truth for installed
    /// packages and the built template cache.
    /// </summary>
    public TemplatePackageManager PackageManager { get; }
}
