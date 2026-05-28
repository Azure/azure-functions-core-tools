// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Workloads.Catalog;

/// <summary>
/// Resolves the configured workload catalog source. The only configurable
/// input is the <see cref="Constants.WorkloadsSourceEnvironmentVariable"/>
/// environment variable; config files and command-line settings are
/// intentionally ignored so the workload feed can't be redirected by
/// anything other than an explicit process env var or a per-invocation
/// <c>--source</c> override.
/// </summary>
internal static class WorkloadSourceResolver
{
    /// <summary>
    /// Returns the env-var override if explicitly set to a non-whitespace
    /// value, otherwise <c>null</c> to fall back to the catalog default.
    /// </summary>
    public static string? Resolve()
    {
        string? configured = System.Environment.GetEnvironmentVariable(Constants.WorkloadsSourceEnvironmentVariable);

        return string.IsNullOrWhiteSpace(configured) ? null : configured;
    }
}
