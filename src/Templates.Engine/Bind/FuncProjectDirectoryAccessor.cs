// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Default holder for the current invocation's project directory. Registered
/// as a singleton so the scaffolder and the <c>msbuild:</c> bind source share
/// one instance.
/// </summary>
internal sealed class FuncProjectDirectoryAccessor : IFuncProjectDirectoryAccessor
{
    /// <inheritdoc />
    public string? Current { get; set; }
}
