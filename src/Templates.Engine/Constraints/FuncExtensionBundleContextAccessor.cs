// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Default holder for the current invocation's extension-bundle context.
/// Registered as a singleton so the constraint and the CLI share one instance.
/// </summary>
internal sealed class FuncExtensionBundleContextAccessor : IFuncExtensionBundleContextAccessor
{
    /// <inheritdoc />
    public FuncExtensionBundleContext? Current { get; set; }
}
