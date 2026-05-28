// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.DotNet;

/// <summary>
/// Provides the filesystem paths for the dotnet **item-template** hive used
/// by <see cref="DotNetEngineProvider"/> at <c>func new</c> time. Mirrors
/// the stack workload's <c>TemplateHivePathProvider</c> shape but uses a
/// distinct env var and directory so the project-template hive (used by
/// <c>func init</c>) and the item-template hive (used by <c>func new</c>)
/// can be managed independently.
/// </summary>
internal interface IItemTemplateHivePathProvider
{
    /// <summary>
    /// Root directory of the custom item-template hive.
    /// </summary>
    public string HivePath { get; }

    /// <summary>
    /// Sentinel file written after a successful install; presence + mtime
    /// drives the freshness check.
    /// </summary>
    public string TimestampPath { get; }
}
