// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Abstractions.Common;

/// <summary>
/// Common constants used across the Azure Functions CLI.
/// </summary>
public static class Constants
{
    /// <summary>
    /// Directory name (under the user profile) the func CLI persists state in.
    /// </summary>
    public const string FuncHomeDirectoryName = ".azure-functions";

    /// <summary>
    /// Environment variable that, when explicitly set to a non-empty value,
    /// overrides the default func CLI home directory (the on-disk root the
    /// CLI persists configuration, profiles, caches, workloads, etc. under).
    /// When unset, the home defaults to
    /// <c>~/<see cref="FuncHomeDirectoryName"/></c>. Read directly (not
    /// through <c>IConfiguration</c>) so the home cannot be redirected by
    /// host config, global config, or project <c>.func/config.json</c>.
    /// </summary>
    public const string FuncHomeEnvironmentVariable = "FUNC_CLI_HOME";
}
