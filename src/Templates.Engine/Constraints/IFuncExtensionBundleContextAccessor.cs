// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Bridges the project's resolved extension bundle from the CLI (which knows
/// it per invocation) to the engine-constructed <c>func-extension-bundle</c>
/// constraint (which cannot reach DI). The CLI sets <see cref="Current"/>
/// before listing/scaffolding; the constraint reads it during evaluation.
/// </summary>
internal interface IFuncExtensionBundleContextAccessor
{
    /// <summary>
    /// The bundle context for the current invocation, or <c>null</c> when none
    /// has been resolved (constraint treats the requirement as unsatisfiable).
    /// </summary>
    public FuncExtensionBundleContext? Current { get; set; }
}
