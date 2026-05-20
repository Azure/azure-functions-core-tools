// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Workloads.Storage;

/// <summary>
/// Resolves the workload root directory. The only configurable input is
/// the <see cref="Constants.WorkloadsHomeEnvironmentVariable"/> environment
/// variable; everything else (config files, command-line options) is
/// intentionally ignored so workload assembly loading can't be redirected
/// by anything other than an explicit process env var.
/// </summary>
internal static class WorkloadHomeResolver
{
    /// <summary>
    /// Returns the env-var override if explicitly set to a non-whitespace
    /// value, otherwise the default user-profile home. Result is normalised
    /// with <see cref="Path.GetFullPath(string)"/>.
    /// </summary>
    public static string Resolve()
    {
        string? configured = System.Environment.GetEnvironmentVariable(
            Constants.WorkloadsHomeEnvironmentVariable);

        string home = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile),
                Constants.FuncHomeDirectoryName)
            : configured;

        return Path.GetFullPath(home);
    }
}
