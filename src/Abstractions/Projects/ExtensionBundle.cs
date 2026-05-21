// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Projects;

/// <summary>
/// Shared metadata for the Functions extension bundle. Used by script-based
/// workloads (Python, Node, ...) that don't compile in their own extensions.
/// </summary>
public static class ExtensionBundle
{
    /// <summary>
    /// NuGet id for the GA extension bundle written by <c>func init</c> templates.
    /// </summary>
    public const string DefaultId = "Microsoft.Azure.Functions.ExtensionBundle";

    /// <summary>
    /// Default version range pulled by <c>func init</c> templates.
    /// </summary>
    public const string DefaultVersionRange = "[4.*, 5.0.0)";
}
