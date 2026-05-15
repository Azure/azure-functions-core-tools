// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Resolution;

/// <summary>
/// Reads the per-project <c>.func/config.json</c> file.
/// </summary>
internal interface IFuncProjectConfigReader
{
    /// <summary>
    /// Returns the parsed config, or <c>null</c> when the file is missing,
    /// malformed, or empty. Never throws.
    /// </summary>
    public FuncProjectConfig? Read(DirectoryInfo directory);
}
