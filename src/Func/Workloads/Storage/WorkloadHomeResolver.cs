// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using AbstractionsCommon = Azure.Functions.Cli.Abstractions.Common;

namespace Azure.Functions.Cli.Workloads.Storage;

/// <summary>
/// Resolves the workload root directory. The only configurable inputs are
/// the <see cref="Constants.WorkloadsHomeEnvironmentVariable"/> and
/// <see cref="Abstractions.Common.Constants.FuncHomeEnvironmentVariable"/>
/// environment variables; everything else (config files, command-line
/// options) is intentionally ignored so workload assembly loading can't be
/// redirected by anything other than an explicit process env var.
/// </summary>
internal static class WorkloadHomeResolver
{
    /// <summary>
    /// Returns <see cref="Constants.WorkloadsHomeEnvironmentVariable"/> if
    /// explicitly set to a non-whitespace value; otherwise falls back to the
    /// general func CLI home (which itself honors
    /// <see cref="Abstractions.Common.Constants.FuncHomeEnvironmentVariable"/>
    /// and defaults to the user-profile path). Result is normalised with
    /// <see cref="Path.GetFullPath(string)"/>.
    /// </summary>
    public static string Resolve()
    {
        string? configured = Environment.GetEnvironmentVariable(Constants.WorkloadsHomeEnvironmentVariable);

        return string.IsNullOrWhiteSpace(configured)
            ? AbstractionsCommon.FuncHomeResolver.Resolve()
            : Path.GetFullPath(configured);
    }
}
