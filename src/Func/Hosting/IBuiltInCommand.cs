// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Hosting;

/// <summary>
/// Marker interface applied to top-level commands shipped by the CLI itself
/// (as opposed to commands contributed by an installed workload). Used by
/// <see cref="Parser"/> to build the reserved-name set: workload-contributed
/// commands whose name collides with a built-in are skipped at startup with
/// a warning instead of being grafted into the parse tree.
/// </summary>
internal interface IBuiltInCommand
{
}
