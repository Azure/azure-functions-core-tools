// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Microsoft.Extensions.Configuration;

namespace Azure.Functions.Cli.Hosting;

/// <summary>
/// Builds the production <see cref="IHostConfiguration"/> from a fixed
/// allowlist of process environment variables. Adding a new host-level
/// setting means appending its env-var name to <see cref="AllowedKeys"/>;
/// nothing else can ever feed into host configuration. That keeps the
/// "workload loading can only be redirected by an explicit env var"
/// guarantee enforceable in one place (and assertable in tests).
/// </summary>
internal static class HostConfigurationBuilder
{
    /// <summary>
    /// Environment variables that may participate in host configuration.
    /// Keys in the returned <see cref="IHostConfiguration"/> are the
    /// unmodified env-var names (e.g. <c>FUNC_CLI_WORKLOADS_HOME</c>) so the
    /// config key and the user-facing variable name are the same string.
    /// </summary>
    public static IReadOnlyList<string> AllowedKeys { get; } =
    [
        Constants.WorkloadsHomeEnvironmentVariable,
    ];

    public static IHostConfiguration Build()
    {
        var snapshot = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (string key in AllowedKeys)
        {
            snapshot[key] = System.Environment.GetEnvironmentVariable(key);
        }

        IConfiguration inner = new ConfigurationBuilder()
            .AddInMemoryCollection(snapshot)
            .Build();

        return new HostConfiguration(inner);
    }
}
