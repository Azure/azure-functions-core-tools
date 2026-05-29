// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Abstractions.Common;

/// <summary>
/// Resolves the func CLI home directory (the on-disk root the CLI persists
/// configuration, profiles, caches, workloads, etc. under). The only
/// configurable input is the <see cref="Constants.FuncHomeEnvironmentVariable"/>
/// environment variable; everything else (config files, command-line options)
/// is intentionally ignored so the home cannot be redirected by anything
/// other than an explicit process env var.
/// </summary>
public static class FuncHomeResolver
{
    /// <summary>
    /// Returns the env-var override if explicitly set to a non-whitespace
    /// value, otherwise the default user-profile home
    /// (<c>~/<see cref="Constants.FuncHomeDirectoryName"/></c>). Result is
    /// normalised with <see cref="Path.GetFullPath(string)"/>.
    /// </summary>
    public static string Resolve()
    {
        string? configured = Environment.GetEnvironmentVariable(Constants.FuncHomeEnvironmentVariable);

        string home = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                Constants.FuncHomeDirectoryName)
            : configured;

        return Path.GetFullPath(home);
    }
}
