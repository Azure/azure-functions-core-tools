// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Hosting;
using Microsoft.Extensions.Options;

namespace Azure.Functions.Cli.Workloads.Storage;

/// <summary>
/// Populates <see cref="WorkloadPathsOptions.Home"/> from
/// <see cref="IHostConfiguration"/>, reading the
/// <see cref="Constants.WorkloadsHomeEnvironmentVariable"/> key. When that
/// value is missing or whitespace, falls back to the default user-profile
/// path. The single source of truth for workload-home resolution so the boot
/// path and the <see cref="IOptions{TOptions}"/> pipeline produce the same
/// answer.
/// </summary>
internal sealed class WorkloadPathsOptionsSetup(IHostConfiguration hostConfiguration) : IConfigureOptions<WorkloadPathsOptions>
{
    private readonly IHostConfiguration _hostConfiguration =
        hostConfiguration ?? throw new ArgumentNullException(nameof(hostConfiguration));

    /// <inheritdoc />
    public void Configure(WorkloadPathsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Home = Resolve(_hostConfiguration);
    }

    /// <summary>
    /// Returns the host-configured override if explicitly set to a
    /// non-whitespace value, otherwise the default user-profile home. Exposed
    /// so the boot path (which runs before DI builds) can resolve Home the
    /// same way as the options pipeline.
    /// </summary>
    internal static string Resolve(IHostConfiguration hostConfiguration)
    {
        ArgumentNullException.ThrowIfNull(hostConfiguration);

        string? configured = hostConfiguration[Constants.WorkloadsHomeEnvironmentVariable];
        string home = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile),
                Constants.FuncHomeDirectoryName)
            : configured;

        return Path.GetFullPath(home);
    }
}
