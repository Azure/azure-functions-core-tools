// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Bridges the current invocation's project directory from the scaffolder to
/// the engine-constructed <c>msbuild:</c> bind source (which cannot reach DI).
/// The scaffolder sets <see cref="Current"/> before instantiating a template;
/// the bind source reads it to locate the project file whose MSBuild
/// properties answer <c>msbuild:&lt;Property&gt;</c> bindings.
/// </summary>
internal interface IFuncProjectDirectoryAccessor
{
    /// <summary>
    /// The absolute project directory for the current invocation, or
    /// <c>null</c> when none has been set.
    /// </summary>
    public string? Current { get; set; }
}
