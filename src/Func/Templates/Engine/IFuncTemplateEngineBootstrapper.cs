// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.TemplateEngine.Abstractions;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Builds a fresh <c>Microsoft.TemplateEngine</c> environment for a single
/// <c>func new</c> invocation, carrying the resolved project context
/// (stack, language, bundle) as host params. A new environment is produced per
/// call so per-invocation context never leaks between invocations.
/// </summary>
internal interface IFuncTemplateEngineBootstrapper
{
    /// <summary>
    /// Creates a new <see cref="IEngineEnvironmentSettings"/> whose host is
    /// stamped with <paramref name="context"/> as host params and rooted in the
    /// func-owned settings location. The caller owns the returned instance and
    /// must dispose it.
    /// </summary>
    public IEngineEnvironmentSettings Create(FuncTemplateEngineContext context);
}
