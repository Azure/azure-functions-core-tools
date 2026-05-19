// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Microsoft.Extensions.Options;

namespace Azure.Functions.Cli.Workloads.Storage;

/// <summary>
/// Populates <see cref="WorkloadPathsOptions.Home"/> from the
/// <see cref="Constants.WorkloadsHomeEnvironmentVariable"/> environment
/// variable (when explicitly set to a non-whitespace value), otherwise the
/// default user-profile path. This is the single source of truth for
/// workload-home resolution so the boot path and the
/// <see cref="IOptions{TOptions}"/> pipeline produce the same answer.
/// </summary>
internal sealed class WorkloadPathsOptionsSetup(IEnvironmentVariables environment) : IConfigureOptions<WorkloadPathsOptions>
{
    private readonly IEnvironmentVariables _environment =
        environment ?? throw new ArgumentNullException(nameof(environment));

    /// <inheritdoc />
    public void Configure(WorkloadPathsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Home = Resolve(_environment);
    }

    /// <summary>
    /// Returns the env-var override if explicitly set to a non-whitespace
    /// value, otherwise the default user-profile home. Exposed so the boot
    /// path (which runs before DI builds) can resolve Home the same way as
    /// the options pipeline.
    /// </summary>
    internal static string Resolve(IEnvironmentVariables environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        string? configured = environment.Get(Constants.WorkloadsHomeEnvironmentVariable);
        string home = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile),
                Constants.FuncHomeDirectoryName)
            : configured;

        return Path.GetFullPath(home);
    }
}
